using Proje.Entities.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Proje.Core.Interfaces
{
    public interface IExcelService
    {
        bool WriteTransactionsToExcel(List<Transaction> transactions, string filePath);
        List<Transaction> ReadTransactionsFromExcel(string filePath);
        bool UpdateTransactionInExcel(Transaction transaction, string filePath);
        bool CreateExcelTemplate(string filePath);
    }
}
