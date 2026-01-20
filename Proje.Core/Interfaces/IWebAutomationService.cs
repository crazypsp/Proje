using Proje.Entities.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Proje.Core.Interfaces
{
    public interface IWebAutomationService
    {
        Task InitializeAsync();
        Task<bool> LoginAsync();
        Task<bool> NavigateToTransactionHistoryAsync();
        Task<List<Transaction>> ExtractTransactionsAsync(int pageCount = 10);
        Task<Transaction> GetTransactionDetailsAsync(string transactionId);
        Task<bool> IsLoggedInAsync();
        Task<bool> TestConnectionAsync();
        Task<bool> TestPaginationAsync();
        Task ProcessTransactionsCycleAsync(CancellationToken cancellationToken);
        Task<bool> ResetToDefaultViewAsync();
        Task<bool> ClearTransactionFiltersAsync();
        void StartContinuousProcessing();
        void StopContinuousProcessing();
        void Dispose();

        // Yeni method: Filtre uygulayarak işlemleri çek
        Task<List<Transaction>> ExtractTransactionsWithFilterAsync(string status = "Onaylandı", string transactionType = "Yatırım", bool autoPaginate = false, bool onlyNew = false);
        Task<bool> ResetProcessedTransactionIdsAsync();
    }
}
