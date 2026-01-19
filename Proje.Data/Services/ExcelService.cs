using OfficeOpenXml;
using Proje.Core.Helpers;
using Proje.Core.Interfaces;
using Proje.Entities.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Proje.Data.Services
{
    public class ExcelService : IExcelService
    {
        public bool CreateExcelTemplate(string filePath)
        {
            throw new NotImplementedException();
        }

        public List<Transaction> ReadTransactionsFromExcel(string filePath)
        {
            throw new NotImplementedException();
        }

        public bool UpdateTransactionInExcel(Transaction transaction, string filePath)
        {
            throw new NotImplementedException();
        }

        public bool WriteTransactionsToExcel(List<Transaction> transactions, string filePath)
        {
            try
            {
                //ExcelPackage.LicenseContext = System.ComponentModel.LicenseContext.NonCommercial;

                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var worksheet = package.Workbook.Worksheets["İşlemler"]
                        ?? package.Workbook.Worksheets.Add("İşlemler");

                    // Başlıklar
                    worksheet.Cells[1, 1].Value = "İşlem No";
                    worksheet.Cells[1, 2].Value = "External Ref No";
                    worksheet.Cells[1, 3].Value = "Müşteri Ref No";
                    worksheet.Cells[1, 4].Value = "Müşteri ID";
                    worksheet.Cells[1, 5].Value = "Müşteri Adı";
                    worksheet.Cells[1, 6].Value = "Talep Tutarı";
                    worksheet.Cells[1, 7].Value = "Sonuç Tutarı";
                    worksheet.Cells[1, 8].Value = "Personel Adı";
                    worksheet.Cells[1, 9].Value = "Personel Rolü";
                    worksheet.Cells[1, 10].Value = "Durum";
                    worksheet.Cells[1, 11].Value = "Oluşturulma Tarihi";
                    worksheet.Cells[1, 12].Value = "Onay Tarihi";
                    worksheet.Cells[1, 13].Value = "Güncelleme Tarihi";
                    worksheet.Cells[1, 14].Value = "Reddedilme Tarihi";
                    worksheet.Cells[1, 15].Value = "Banka Adı";
                    worksheet.Cells[1, 16].Value = "Hesap No";
                    worksheet.Cells[1, 17].Value = "Açıklama";
                    worksheet.Cells[1, 18].Value = "Çekim Tarihi";

                    // Veriler
                    for (int i = 0; i < transactions.Count; i++)
                    {
                        var transaction = transactions[i];
                        int row = i + 2; // 1. satır başlık

                        worksheet.Cells[row, 1].Value = transaction.TransactionNo;
                        worksheet.Cells[row, 2].Value = transaction.ExternalRefNo;
                        worksheet.Cells[row, 3].Value = transaction.CustomerRefNo;
                        worksheet.Cells[row, 4].Value = transaction.CustomerId;
                        worksheet.Cells[row, 5].Value = transaction.CustomerName;
                        worksheet.Cells[row, 6].Value = transaction.RequestedAmount;
                        worksheet.Cells[row, 7].Value = transaction.ResultAmount;
                        worksheet.Cells[row, 8].Value = transaction.EmployeeName;
                        worksheet.Cells[row, 9].Value = transaction.EmployeeRole;
                        worksheet.Cells[row, 10].Value = transaction.Status.ToString();
                        worksheet.Cells[row, 11].Value = transaction.CreatedDate;
                        //worksheet.Cells[row, 12].Value = transaction.ApprovalDate;
                        //worksheet.Cells[row, 13].Value = transaction.UpdateDate;
                        //worksheet.Cells[row, 14].Value = transaction.RejectionDate;
                        worksheet.Cells[row, 15].Value = transaction.BankName;
                        worksheet.Cells[row, 16].Value = transaction.AccountNumber;
                        worksheet.Cells[row, 17].Value = transaction.Description;
                        worksheet.Cells[row, 18].Value = transaction.ExtractionDate;

                        // Formatlama
                        worksheet.Cells[row, 6].Style.Numberformat.Format = "#,##0.00";
                        worksheet.Cells[row, 7].Style.Numberformat.Format = "#,##0.00";
                        worksheet.Cells[row, 11].Style.Numberformat.Format = "dd/MM/yyyy HH:mm:ss";
                        //worksheet.Cells[row, 12].Style.Numberformat.Format = "dd/MM/yyyy HH:mm:ss";
                        //worksheet.Cells[row, 13].Style.Numberformat.Format = "dd/MM/yyyy HH:mm:ss";
                        //worksheet.Cells[row, 14].Style.Numberformat.Format = "dd/MM/yyyy HH:mm:ss";
                        worksheet.Cells[row, 18].Style.Numberformat.Format = "dd/MM/yyyy HH:mm:ss";
                    }

                    // Otomatik genişlik ayarı
                    worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

                    package.Save();
                }

                return true;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Excel'e yazma hatası");
                return false;
            }
        }

        // Diğer metodlar...
    }
}
