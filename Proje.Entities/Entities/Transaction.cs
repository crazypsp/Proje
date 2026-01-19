using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public decimal PaymentAmount { get; set; }         // Ödeme Tutarı
        public decimal? Commission { get; set; }           // Komisyon

        // Personel Bilgileri
        public string EmployeeName { get; set; }           // Personel Adı
        public string EmployeeRole { get; set; }           // Personel Rolü

        // Durum
        public string Status { get; set; }                 // "Onaylandı", "Reddedildi", vs.

        // Modal Detayları - Müşteri Bilgileri
        public string TransactionId { get; set; }          // İşlem ID
        public string UserId { get; set; }                 // Kullanıcı ID
        public string Username { get; set; }               // Kullanıcı Adı
        public string FullName { get; set; }               // İsim Soyisim

        // Modal Detayları - Banka Hesabı
        public string BankName { get; set; }
        public string AccountNumber { get; set; }
        public string IBAN { get; set; }
        public string AccountHolder { get; set; }
        public string Description { get; set; }
        public string TransactionType { get; set; }

        // Modal Detayları - Tarihler
        public DateTime CreatedDate { get; set; }          // İşlem Oluşturma Tarihi
        public DateTime? AcceptanceDate { get; set; }      // İşleme Kabul Tarihi
        public DateTime? LastApprovalDate { get; set; }    // Son Onay Tarihi
        public DateTime? LastRejectionDate { get; set; }   // Son İptal/Red Tarihi
        public DateTime? LastUpdateDate { get; set; }      // Son Güncelleme Tarihi

        // Ek Bilgiler
        public bool HasModalDetails { get; set; }          // Modal detayları alındı mı?
        public string ErrorMessage { get; set; }           // Hata mesajı
        public string ModalFilePath { get; set; }          // TXT dosyasının yolu

        // Meta Bilgiler
        public DateTime ExtractionDate { get; set; }
        public int PageNumber { get; set; }
        public int RowIndex { get; set; }
        public TimeSpan ProcessingTime { get; set; }       // İşlem süresi
    }
}