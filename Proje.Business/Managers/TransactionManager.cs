using Proje.Core.Helpers;
using Proje.Core.Interfaces;
using Proje.Data.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Proje.Business.Managers
{
    public class TransactionManager
    {
        private readonly IWebAutomationService _webService;
        private readonly ExcelService _excelService;
        private bool _isContinuousProcessingRunning = false;
        private CancellationTokenSource _cts;

        public TransactionManager(IWebAutomationService webService, ExcelService excelService)
        {
            _webService = webService;
            _excelService = excelService;
        }

        public async void StartContinuousProcessing(DateTime selectedDate, string sortOrder)
        {
            try
            {
                _cts = new CancellationTokenSource();
                _isContinuousProcessingRunning = true;

                // Sürekli işlem döngüsünü başlat
                await Task.Run(async () =>
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            // İŞ AKIŞI 3,4,5: Filtrele, çek, Excel'e yaz, tekrarla
                            await _webService.ProcessTransactionsCycleAsync(_cts.Token);

                            // 5 dakika bekle
                            await Task.Delay(TimeSpan.FromMinutes(5), _cts.Token);
                        }
                        catch (TaskCanceledException)
                        {
                            // Normal iptal durumu
                            break;
                        }
                        catch (Exception ex)
                        {
                            // Hata durumunda 1 dakika bekle ve tekrar dene
                            LoggerHelper.LogError(ex, "Sürekli işlem döngüsünde hata");
                            await Task.Delay(TimeSpan.FromMinutes(1), _cts.Token);
                        }
                    }
                }, _cts.Token);
            }
            catch (Exception ex)
            {
                _isContinuousProcessingRunning = false;
                throw;
            }
        }

        public void StopContinuousProcessing()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _isContinuousProcessingRunning = false;
        }

        public bool IsContinuousProcessingRunning()
        {
            return _isContinuousProcessingRunning;
        }
    }
}