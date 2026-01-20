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

        public TransactionManager(IWebAutomationService webService, IExcelService excelService)
        {
            _webService = webService;
            _excelService = excelService;
        }

        public async Task<bool> ExecuteFullAutomationAsync(string excelFilePath, int pageCount = 10)
        {
            try
            {
                LoggerHelper.LogInformation("Tam otomasyon başlatılıyor...");

                // 1. Login ol
                var isLoggedIn = await _webService.LoginAsync();
                if (!isLoggedIn)
                {
                    LoggerHelper.LogError(null, "Login başarısız!");
                    return false;
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

                // 4. Yatırım işlemlerini çek
                LoggerHelper.LogInformation("Yatırım işlemleri çekiliyor...");
                var depositTransactions = await _webService.ExtractTransactionsWithFilterAsync(
                    status: "Onaylandı",
                    transactionType: "Yatırım",
                    autoPaginate: true);

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
                    // Yine de devam etmeyi deneyelim
                }

                // Filtreleri temizle
                await _webService.ClearTransactionFiltersAsync();
                await Task.Delay(800);

                // 6. Çekim işlemlerini çek
                LoggerHelper.LogInformation("Çekim işlemleri çekiliyor...");
                var withdrawalTransactions = await _webService.ExtractTransactionsWithFilterAsync(
                    status: "Onaylandı",
                    transactionType: "Çekim",
                    autoPaginate: true);

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
                    LoggerHelper.LogInformation($"{depositTransactions.Count} adet Yatırım işlemi çekildi.");
                }

                if (withdrawalTransactions.Count > 0)
                {
                    allTransactions.AddRange(withdrawalTransactions);
                    LoggerHelper.LogInformation($"{withdrawalTransactions.Count} adet Çekim işlemi çekildi.");
                }

                if (allTransactions.Count == 0)
                {
                    LoggerHelper.LogWarning("Hiç işlem verisi çekilemedi!");
                    return false;
                }

                // 8. Excel'e yaz
                LoggerHelper.LogInformation($"Toplam {allTransactions.Count} işlem Excel'e yazılıyor...");
                var excelSuccess = _excelService.WriteTransactionsToExcel(allTransactions, excelFilePath);
                if (!excelSuccess)
                {
                    LoggerHelper.LogError(null, "Excel'e yazma başarısız!");
                    return false;
                }

                LoggerHelper.LogInformation($"Tam otomasyon başarıyla tamamlandı! {allTransactions.Count} işlem kaydedildi.");
                return true;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Tam otomasyon sırasında hata");
                return false;
            }
        }

        // Sürekli işlem döngüsünü başlat
        public async void StartContinuousProcessing(string excelFilePath = null, int intervalMinutes = 5)
        {
            lock (_lock)
            {
                if (_isContinuousProcessing)
                {
                    LoggerHelper.LogWarning("Sürekli işlem döngüsü zaten çalışıyor.");
                    return;
                }
                _isContinuousProcessing = true;
            }

            _cts = new CancellationTokenSource();

            // Default excel dosya yolu
            string filePath = excelFilePath ?? $"TransactionHistory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            LoggerHelper.LogInformation($"Sürekli işlem döngüsü başlatıldı. Her {intervalMinutes} dakikada bir çalışacak.");

            try
            {
                int cycleCount = 0;

                while (_isContinuousProcessing && !_cts.Token.IsCancellationRequested)
                {
                    cycleCount++;
                    LoggerHelper.LogInformation($"=== DÖNGÜ #{cycleCount} BAŞLATILIYOR ===");

                    try
                    {
                        // Excel dosya adını güncelle (her döngüde farklı dosya)
                        if (excelFilePath == null)
                        {
                            filePath = $"TransactionHistory_{DateTime.Now:yyyyMMdd_HHmmss}_Cycle{cycleCount}.xlsx";
                        }

                        bool success = await ExecuteFullAutomationAsync(filePath);

                        if (success)
                        {
                            LoggerHelper.LogInformation($"Döngü #{cycleCount} başarıyla tamamlandı. {intervalMinutes} dakika sonra tekrar...");
                        }
                        else
                        {
                            LoggerHelper.LogWarning($"Döngü #{cycleCount} başarısız oldu. {intervalMinutes} dakika sonra tekrar deneniyor...");
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.LogError(ex, $"Döngü #{cycleCount} sırasında beklenmeyen hata");
                    }

                    // Bekleme süresi (kullanıcı projeyi kapatana kadar)
                    for (int i = 0; i < intervalMinutes * 60 && _isContinuousProcessing; i++)
                    {
                        if (_cts.Token.IsCancellationRequested) break;

                        // Her saniye kontrol et
                        await Task.Delay(1000, _cts.Token);
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
                LoggerHelper.LogInformation("Sürekli işlem döngüsü tamamen durduruldu.");
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

        public async Task<List<Transaction>> GetTransactionsAsync(int pageCount = 5)
        {
            return await _webService.ExtractTransactionsAsync(pageCount);
        }

        public bool ExportToExcel(List<Transaction> transactions, string filePath)
        {
            return _excelService.WriteTransactionsToExcel(transactions, filePath);
        }
    }
}