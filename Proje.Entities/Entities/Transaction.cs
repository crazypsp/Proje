using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace Proje.Entities.Entities
{
    public class Transaction
    {
        public int Id { get; set; }

        // İşlem Numaraları
        public string TransactionNo { get; set; }          // Yeşil kutu: 1573990371
        public string ExternalRefNo { get; set; }          // Turuncu kutu: d517889b-5523-4ede-922e-9900bedf8e18
        public string CustomerRefNo { get; set; }          // Mavi kutu: c15f603b7dc3eeae3f8139f26423d561

        // Müşteri Bilgileri
        public string CustomerId { get; set; }             // Müşteri ID
        public string CustomerName { get; set; }           // Müşteri Adı

        // Tutar Bilgileri
        public decimal RequestedAmount { get; set; }       // Talep Tutarı
        public decimal ResultAmount { get; set; }          // Sonuç Tutarı

        // Personel Bilgileri
        public string EmployeeName { get; set; }           // Personel Adı
        public string EmployeeRole { get; set; }           // Personel Rolü

        // Durum
        public TransactionStatus Status { get; set; }

        // Tarihler
        public DateTime CreatedDate { get; set; }          // Oluşturulma
        public DateTime? ApprovalDate { get; set; }        // Onay
        public DateTime? UpdateDate { get; set; }          // Güncelleme
        public DateTime? RejectionDate { get; set; }       // Reddedildi

        // Ekstra Bilgiler
        public string BankName { get; set; }
        public string AccountNumber { get; set; }
        public string Description { get; set; }

        // Meta Bilgiler
        public DateTime ExtractionDate { get; set; }
        public int PageNumber { get; set; }
        public int RowIndex { get; set; }
    }
}
