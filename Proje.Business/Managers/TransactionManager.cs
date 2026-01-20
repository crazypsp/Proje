using Proje.Core.Helpers;
using Proje.Core.Interfaces;
using Proje.Entities;
using Proje.Entities.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace Proje.Business.Managers
{
    public class TransactionManager
    {
        private readonly IWebAutomationService _webService;
        private readonly IExcelService _excelService;
        private CancellationTokenSource _cts;
        private bool _isContinuousProcessing = false;
        private readonly object _lock = new object();
        private int _cycleCount = 0;

        public TransactionManager(IWebAutomationService webService, IExcelService excelService)
        {
            _webService = webService;
            _excelService = excelService;
        }

        public async Task<bool> ExecuteFullAutomationAsync(string excelFilePath, int pageCount = 10, bool resetTransactionMemory = true)
        {
            try
            {
                _cycleCount++;
                LoggerHelper.LogInformation($"=== DÖNGÜ #{_cycleCount} BAŞLATILIYOR ===");

                // Belleği temizle (opsiyonel)
                if (resetTransactionMemory)
                {
                    await _webService.ResetProcessedTransactionIdsAsync();
                }

                // 1. Login ol (gerekirse)
                var isLoggedIn = await _webService.IsLoggedInAsync();
                if (!isLoggedIn)
                {
                    LoggerHelper.LogInformation("Oturum açılmamış, login deneniyor...");
                    isLoggedIn = await _webService.LoginAsync();
                    if (!isLoggedIn)
                    {
                        LoggerHelper.LogError(null, "Login başarısız!");
                        return false;
                    }
                }

                // 2. İşlem Geçmişi sayfasına git
                var navigated = await _webService.NavigateToTransactionHistoryAsync();
                if (!navigated)
                {
                    LoggerHelper.LogError(null, "İşlem Geçmişi sayfasına ulaşılamadı!");
                    return false;
                }

                // 3. Önce sayfayı temizle ve varsayılan duruma getir
                await Task.Delay(1500);
                await _webService.ResetToDefaultViewAsync();

                // 4. Yatırım işlemlerini çek (SADECE YENİ OLANLARI)
                LoggerHelper.LogInformation("Yeni Yatırım işlemleri kontrol ediliyor...");
                var depositTransactions = await _webService.ExtractTransactionsWithFilterAsync(
                    status: "Onaylandı",
                    transactionType: "Yatırım",
                    autoPaginate: true,
                    onlyNew: true);

                if (depositTransactions == null)
                {
                    LoggerHelper.LogError(null, "Yatırım işlemleri çekilirken null döndü!");
                    return false;
                }

                // 5. Sayfayı tekrar temizle ve Çekim işlemleri için hazırla
                LoggerHelper.LogInformation("Sayfa Çekim işlemleri için hazırlanıyor...");
                await Task.Delay(1000);

                // Sayfayı tekrar işlem geçmişine getir
                var refreshed = await _webService.NavigateToTransactionHistoryAsync();
                if (!refreshed)
                {
                    LoggerHelper.LogWarning("İşlem Geçmişi sayfasına tekrar yönlendirilemedi!");
                }

                // Filtreleri temizle
                await _webService.ClearTransactionFiltersAsync();
                await Task.Delay(800);

                // 6. Çekim işlemlerini çek (SADECE YENİ OLANLARI)
                LoggerHelper.LogInformation("Yeni Çekim işlemleri kontrol ediliyor...");
                var withdrawalTransactions = await _webService.ExtractTransactionsWithFilterAsync(
                    status: "Onaylandı",
                    transactionType: "Çekim",
                    autoPaginate: true,
                    onlyNew: true);

                if (withdrawalTransactions == null)
                {
                    LoggerHelper.LogError(null, "Çekim işlemleri çekilirken null döndü!");
                    return false;
                }

                // 7. Tüm işlemleri birleştir
                var allTransactions = new List<Transaction>();
                if (depositTransactions.Count > 0)
                {
                    allTransactions.AddRange(depositTransactions);
                    LoggerHelper.LogInformation($"{depositTransactions.Count} adet YENİ Yatırım işlemi bulundu.");
                }

                if (withdrawalTransactions.Count > 0)
                {
                    allTransactions.AddRange(withdrawalTransactions);
                    LoggerHelper.LogInformation($"{withdrawalTransactions.Count} adet YENİ Çekim işlemi bulundu.");
                }

                if (allTransactions.Count == 0)
                {
                    LoggerHelper.LogInformation("Yeni işlem bulunamadı.");
                    return true; // Hata değil, sadece yeni işlem yok
                }

                // 8. Excel'e yaz
                LoggerHelper.LogInformation($"Toplam {allTransactions.Count} YENİ işlem Excel'e yazılıyor...");
                var excelSuccess = _excelService.WriteTransactionsToExcel(allTransactions, excelFilePath);
                if (!excelSuccess)
                {
                    LoggerHelper.LogError(null, "Excel'e yazma başarısız!");
                    return false;
                }

                LoggerHelper.LogInformation($"=== DÖNGÜ #{_cycleCount} BAŞARIYLA TAMAMLANDI ===");
                LoggerHelper.LogInformation($"{allTransactions.Count} yeni işlem kaydedildi.");
                return true;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Tam otomasyon sırasında hata");
                return false;
            }
        }

        // Sürekli işlem döngüsünü başlat (GÜNCELLENMİŞ)
        public async void StartContinuousProcessing(string excelFilePath = null, int intervalMinutes = 5, bool combineAllInOneFile = false)
        {
            lock (_lock)
            {
                if (_isContinuousProcessing)
                {
                    LoggerHelper.LogWarning("Sürekli işlem döngüsü zaten çalışıyor.");
                    return;
                }
                _isContinuousProcessing = true;
                _cycleCount = 0;
            }

            _cts = new CancellationTokenSource();

            // Default excel dosya yolu
            string filePath = excelFilePath ?? $"TransactionHistory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            LoggerHelper.LogInformation($"=== SÜREKLİ İŞLEM DÖNGÜSÜ BAŞLATILIYOR ===");
            LoggerHelper.LogInformation($"Her {intervalMinutes} dakikada bir yeni işlemler kontrol edilecek.");

            try
            {
                List<Transaction> allCycleTransactions = new List<Transaction>();

                while (_isContinuousProcessing && !_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        _cycleCount++;
                        LoggerHelper.LogInformation($"=== DÖNGÜ #{_cycleCount} BAŞLATILIYOR ===");

                        // Excel dosya adını güncelle (her döngüde farklı dosya veya aynı dosya)
                        if (excelFilePath == null && !combineAllInOneFile)
                        {
                            filePath = $"TransactionHistory_{DateTime.Now:yyyyMMdd_HHmmss}_Cycle{_cycleCount}.xlsx";
                        }

                        bool success = await ExecuteFullAutomationAsync(filePath, resetTransactionMemory: false);

                        if (success)
                        {
                            LoggerHelper.LogInformation($"Döngü #{_cycleCount} başarıyla tamamlandı.");
                        }
                        else
                        {
                            LoggerHelper.LogWarning($"Döngü #{_cycleCount} başarısız oldu.");
                        }

                        LoggerHelper.LogInformation($"{intervalMinutes} dakika sonra tekrar kontrol edilecek...");
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.LogError(ex, $"Döngü #{_cycleCount} sırasında beklenmeyen hata");
                    }

                    // Bekleme süresi (kullanıcı projeyi kapatana kadar)
                    try
                    {
                        for (int i = 0; i < intervalMinutes * 60 && _isContinuousProcessing; i++)
                        {
                            if (_cts.Token.IsCancellationRequested) break;

                            // Her saniye kontrol et
                            await Task.Delay(1000, _cts.Token);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Normal durdurma, devam et
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LoggerHelper.LogInformation("Sürekli işlem döngüsü iptal edildi.");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Sürekli işlem döngüsünde beklenmeyen hata");
            }
            finally
            {
                lock (_lock)
                {
                    _isContinuousProcessing = false;
                }
                LoggerHelper.LogInformation("=== SÜREKLİ İŞLEM DÖNGÜSÜ DURDURULDU ===");
            }
        }

        // Sürekli işlem döngüsünü durdur
        public void StopContinuousProcessing()
        {
            lock (_lock)
            {
                if (!_isContinuousProcessing)
                {
                    LoggerHelper.LogWarning("Sürekli işlem döngüsü zaten durdurulmuş.");
                    return;
                }

                LoggerHelper.LogInformation("Sürekli işlem döngüsü durduruluyor...");
                _isContinuousProcessing = false;
            }

            _cts?.Cancel();
            LoggerHelper.LogInformation("Sürekli işlem döngüsü durduruldu.");
        }

        // Sürekli işlem durumunu kontrol et
        public bool IsContinuousProcessingRunning()
        {
            lock (_lock)
            {
                return _isContinuousProcessing;
            }
        }

        // Belleği temizle
        public async Task ResetTransactionMemoryAsync()
        {
            await _webService.ResetProcessedTransactionIdsAsync();
            _cycleCount = 0;
            LoggerHelper.LogInformation("İşlem bellek temizlendi.");
        }

        public async Task<List<Transaction>> GetTransactionsAsync(int pageCount = 5)
        {
            return await _webService.ExtractTransactionsAsync(pageCount);
        }

        public bool ExportToExcel(List<Transaction> transactions, string filePath)
        {
            return _excelService.WriteTransactionsToExcel(transactions, filePath);
        }

        public int GetCurrentCycleCount()
        {
            return _cycleCount;
        }
    }
}