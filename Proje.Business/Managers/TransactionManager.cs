using Proje.Core.Helpers;
using Proje.Core.Interfaces;
using Proje.Entities;
using Proje.Entities.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Proje.Business.Managers
{
    public class TransactionManager
    {
        private readonly IWebAutomationService _webService;
        private readonly IExcelService _excelService;

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
                    //LoggerHelper.LogError("Login başarısız!");
                    return false;
                }

                // 2. İşlem Geçmişi sayfasına git
                var navigated = await _webService.NavigateToTransactionHistoryAsync();
                if (!navigated)
                {
                    //LoggerHelper.LogError("İşlem Geçmişi sayfasına ulaşılamadı!");
                    return false;
                }

                // 3. İşlem verilerini çek
                var transactions = await _webService.ExtractTransactionsAsync(pageCount);
                if (transactions == null || transactions.Count == 0)
                {
                    LoggerHelper.LogWarning("Hiç işlem verisi çekilemedi!");
                    return false;
                }

                // 4. Excel'e yaz
                var excelSuccess = _excelService.WriteTransactionsToExcel(transactions, excelFilePath);
                if (!excelSuccess)
                {
                    //LoggerHelper.LogError("Excel'e yazma başarısız!");
                    return false;
                }

                LoggerHelper.LogInformation($"Tam otomasyon başarıyla tamamlandı! {transactions.Count} işlem kaydedildi.");
                return true;
            }
            catch (System.Exception ex)
            {
                LoggerHelper.LogError(ex, "Tam otomasyon sırasında hata");
                return false;
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
