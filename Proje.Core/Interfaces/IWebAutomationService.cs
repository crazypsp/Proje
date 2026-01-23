using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Proje.Entities.Entities;

namespace Proje.Core.Interfaces
{
    public interface IWebAutomationService
    {
        Task InitializeAsync();
        Task<bool> LoginAsync();
        Task<bool> NavigateToTransactionHistoryAsync();
        Task ApplyDateFilterAsync(DateTime selectedDate);
        Task ApplySortFilterAsync(string sortOrder);
        Task ApplyTransactionTypeFilterAsync(string transactionType);
        Task<List<Transaction>> ExtractTransactionsWithFilterAsync(
            string status = "Onaylandı",
            string transactionType = "Yatırım",
            bool autoPaginate = false,
            DateTime? selectedDate = null,
            string sortOrder = "Eskiden Yeniye");
        Task<List<Transaction>> ExtractTransactionsAsync(int pageCount = 10);
        void StartContinuousProcessing(DateTime selectedDate, string sortOrder);
        void StopContinuousProcessing();
        Task ProcessTransactionsCycleAsync(CancellationToken cancellationToken);
        Task<bool> TestConnectionAsync();
        Task<bool> IsLoggedInAsync();
        Task<Transaction> GetTransactionDetailsAsync(string transactionId);
        Task<bool> TestPaginationAsync();
        Task<bool> ResetToDefaultViewAsync();
        Task<bool> ClearTransactionFiltersAsync();
        void Dispose();
    }
}