using Proje.Entities.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Proje.Core.Interfaces
{
    public interface IWebAutomationService
    {
        Task<bool> LoginAsync();
        Task<List<Transaction>> ExtractTransactionsAsync(int pageCount = 10);
        Task<Transaction> GetTransactionDetailsAsync(string transactionId);
        Task<bool> NavigateToTransactionHistoryAsync();
        Task<bool> IsLoggedInAsync();
    }
}
