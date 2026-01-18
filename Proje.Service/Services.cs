using Microsoft.Playwright;
using Proje.Core.Helpers;
using Proje.Core.Interfaces;
using Proje.Entities.Entities;
using Proje.Enums;
using Proje.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using static System.Net.Mime.MediaTypeNames;

namespace Proje.Service
{
    public class WebAutomationService : IWebAutomationService, IDisposable
    {
        private IPlaywright _playwright;
        private IBrowser _browser;
        private IPage _page;
        private IBrowserContext _context;
        private readonly LoginCredentials _credentials;
        private readonly BrowserConfig _browserConfig;

        public WebAutomationService(LoginCredentials credentials, BrowserConfig browserConfig)
        {
            _credentials = credentials;
            _browserConfig = browserConfig;
        }

        public async Task InitializeAsync()
        {
            try
            {
                LoggerHelper.LogInformation("Playwright başlatılıyor...");
                _playwright = await Playwright.CreateAsync();

                LoggerHelper.LogInformation("Browser başlatılıyor...");
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = _browserConfig.Headless,
                    Timeout = _browserConfig.TimeoutSeconds * 1000
                });

                LoggerHelper.LogInformation("Context oluşturuluyor...");
                _context = await _browser.NewContextAsync(new BrowserNewContextOptions
                {
                    HttpCredentials = new HttpCredentials
                    {
                        Username = _credentials.BasicAuthUsername,
                        Password = _credentials.BasicAuthPassword
                    },
                    UserAgent = _browserConfig.UserAgent
                });

                _page = await _context.NewPageAsync();
                LoggerHelper.LogInformation("Web otomasyon servisi hazır!");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Playwright başlatma hatası");
                throw;
            }
        }

        public async Task<bool> LoginAsync()
        {
            try
            {
                LoggerHelper.LogInformation("Login işlemi başlatılıyor...");

                // ADIM 1: 401 Basic Auth Geçişi
                await _page.GotoAsync("https://online.powerhavale.com/marjin/employee/69",
                    new PageGotoOptions { Timeout = 10000, WaitUntil = WaitUntilState.NetworkIdle });

                // ADIM 2: Login Sayfasına Git
                await _page.GotoAsync(_credentials.LoginUrl,
                    new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

                // ADIM 3: Login Formunu Doldur
                // HTML analizine göre input name'leri: "email" ve "password"
                await _page.FillAsync("input[name='email']", _credentials.FormUsername);
                await _page.FillAsync("input[name='password']", _credentials.FormPassword);

                // ADIM 4: Giriş Yap
                await _page.ClickAsync("button[type='submit']");

                // ADIM 5: Sayfanın Yüklenmesini Bekle
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(2000);

                // ADIM 6: Giriş Kontrolü
                var currentUrl = _page.Url;
                var content = await _page.ContentAsync();

                bool isLoginSuccess = !content.Contains("401 Authorization Required") &&
                                     !content.Contains("Hatalı") &&
                                     !content.Contains("Yanlış") &&
                                     !currentUrl.Contains("login");

                if (isLoginSuccess)
                {
                    LoggerHelper.LogInformation($"Giriş başarılı! Yönlendirilen sayfa: {currentUrl}");
                    return true;
                }
                else
                {
                    LoggerHelper.LogWarning("Giriş başarısız!");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Login işlemi sırasında hata");
                return false;
            }
        }

        public async Task<bool> NavigateToTransactionHistoryAsync()
        {
            try
            {
                LoggerHelper.LogInformation("İşlem Geçmişi sayfasına yönlendiriliyor...");

                // Sidebar'daki "İşlem Geçmişi" linkini bul ve tıkla
                await _page.ClickAsync("a[href*='/marjin/transaction-history']");

                // Sayfanın yüklenmesini bekle
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(3000);

                // Sayfa başlığını kontrol et
                var pageTitle = await _page.TitleAsync();
                if (pageTitle.Contains("İşlem Geçmişi") ||
                    await _page.ContentAsync().ContinueWith(t => t.Result.Contains("İşlem Geçmişi")))
                {
                    LoggerHelper.LogInformation("İşlem Geçmişi sayfasına ulaşıldı!");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "İşlem Geçmişi sayfasına yönlendirme hatası");
                return false;
            }
        }

        public async Task<List<Transaction>> ExtractTransactionsAsync(int pageCount = 10)
        {
            var transactions = new List<Transaction>();

            try
            {
                LoggerHelper.LogInformation($"Toplam {pageCount} sayfa işlem verisi çekiliyor...");

                for (int pageNum = 1; pageNum <= pageCount; pageNum++)
                {
                    LoggerHelper.LogInformation($"Sayfa {pageNum} çekiliyor...");

                    // Tablodaki tüm satırları al
                    var rows = await _page.QuerySelectorAllAsync("tbody tr[data-slot='table-row']");

                    int rowIndex = 0;
                    foreach (var row in rows)
                    {
                        try
                        {
                            var transaction = await ExtractTransactionFromRowAsync(row);
                            if (transaction != null)
                            {
                                transaction.PageNumber = pageNum;
                                transaction.RowIndex = rowIndex;
                                transaction.ExtractionDate = DateTime.Now;

                                // SADECE ONAYLANMIŞ İŞLEMLERİN DETAYINI AL
                                if (transaction.Status == "Onaylandı")
                                {
                                    try
                                    {
                                        LoggerHelper.LogInformation($"{transaction.TransactionNo} numaralı işlemin detayları alınıyor...");

                                        // Detay butonuna tıkla
                                        var detailButton = await row.QuerySelectorAsync("td:nth-child(7) button");
                                        if (detailButton != null)
                                        {
                                            await detailButton.ClickAsync();
                                            await Task.Delay(1500); // Modal'ın açılmasını bekle

                                            // Modal'dan ekstra bilgileri çek
                                            await ExtractModalDetailsAsync(transaction);
                                            transaction.HasModalDetails = true;

                                            // Modal'ı kapat (X butonuna bas)
                                            await CloseModalAsync();
                                            await Task.Delay(500); // Kapanmasını bekle
                                        }
                                    }
                                    catch (Exception modalEx)
                                    {
                                        LoggerHelper.LogError(modalEx, $"{transaction.TransactionNo} detay alma hatası");
                                        // Modal kapama hatası olsa bile diğer işlemlere devam et
                                        await TryCloseModalAsync();
                                    }
                                }

                                transactions.Add(transaction);
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggerHelper.LogError(ex, "Satır işleme hatası");
                        }
                        rowIndex++;
                    }

                    // Sonraki sayfaya git (eğer varsa)
                    if (pageNum < pageCount)
                    {
                        var nextButton = await _page.QuerySelectorAsync("button[aria-label='Next page']");
                        if (nextButton != null && await nextButton.IsVisibleAsync())
                        {
                            await nextButton.ClickAsync();
                            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                            await Task.Delay(2000);
                        }
                        else
                        {
                            LoggerHelper.LogInformation("Daha fazla sayfa bulunamadı.");
                            break;
                        }
                    }
                }

                LoggerHelper.LogInformation($"{transactions.Count} adet işlem başarıyla çekildi!");
                return transactions;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "İşlem verisi çekme hatası");
                return transactions;
            }
        }

        private async Task<Transaction> ExtractTransactionFromRowAsync(IElementHandle row)
        {
            try
            {
                var transaction = new Transaction();

                // 1. İşlem No (3 farklı renkli kutu)
                var transactionNoElements = await row.QuerySelectorAllAsync("td:nth-child(1) button");
                if (transactionNoElements.Count >= 3)
                {
                    // Yeşil kutu: İşlem No
                    transaction.TransactionNo = await ExtractTextFromButtonAsync(transactionNoElements[0]);

                    // Turuncu kutu: External Ref No
                    transaction.ExternalRefNo = await ExtractTextFromButtonAsync(transactionNoElements[1]);

                    // Mavi kutu: Customer Ref No
                    transaction.CustomerRefNo = await ExtractTextFromButtonAsync(transactionNoElements[2]);
                }

                // 2. Müşteri Bilgileri (2 button)
                var customerElements = await row.QuerySelectorAllAsync("td:nth-child(2) button");
                if (customerElements.Count >= 2)
                {
                    transaction.CustomerId = await ExtractTextFromButtonAsync(customerElements[0]);
                    transaction.CustomerName = await ExtractTextFromButtonAsync(customerElements[1]);
                }

                // 3. Tutar Bilgileri
                var amountElements = await row.QuerySelectorAllAsync("td:nth-child(3) button");
                if (amountElements.Count >= 2)
                {
                    // Talep Tutarı (İlk button)
                    var requestedAmountText = await ExtractTextFromButtonAsync(amountElements[0]);
                    transaction.RequestedAmount = ParseAmount(requestedAmountText);

                    // Sonuç Tutarı (İkinci button)
                    var resultAmountText = await ExtractTextFromButtonAsync(amountElements[1]);
                    transaction.ResultAmount = ParseAmount(resultAmountText);
                }

                // 4. Personel Bilgileri
                var employeeCell = await row.QuerySelectorAsync("td:nth-child(4)");
                if (employeeCell != null)
                {
                    // HTML'de sadece "pHantom" yazıyor
                    transaction.EmployeeName = "pHantom";
                    transaction.EmployeeRole = "Sistem";
                }

                // 5. Durum - GÜNCELLENDİ
                var statusCell = await row.QuerySelectorAsync("td:nth-child(5)");
                if (statusCell != null)
                {
                    // Badge içindeki metni al
                    var statusBadge = await statusCell.QuerySelectorAsync("[data-slot='badge']");
                    if (statusBadge != null)
                    {
                        transaction.Status = (await statusBadge.InnerTextAsync()).Trim();
                    }
                    else
                    {
                        // Eğer badge yoksa hücrenin içeriğini al
                        transaction.Status = (await statusCell.InnerTextAsync()).Trim();
                    }
                }

                // 6. Tarihler
                var datesCell = await row.QuerySelectorAsync("td:nth-child(6)");
                if (datesCell != null)
                {
                    await ParseDatesAsync(datesCell, transaction);
                }

                return transaction;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Satırdan veri çıkarma hatası");
                return null;
            }
        }

        private async Task<string> ExtractTextFromButtonAsync(IElementHandle button)
        {
            try
            {
                var textElement = await button.QuerySelectorAsync("p");
                if (textElement != null)
                {
                    return (await textElement.InnerTextAsync()).Trim();
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private decimal ParseAmount(string amountText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(amountText))
                    return 0;

                // "5.000,00 ₺" formatından sayıya çevir
                var cleanText = amountText
                    .Replace("₺", "")
                    .Replace("TL", "")
                    .Replace(" ", "")
                    .Trim();

                // Binlik ayracını (.) ve ondalık ayracını (,) düzelt
                if (cleanText.Contains(".") && cleanText.Contains(","))
                {
                    // "5.000,00" -> "5000.00"
                    cleanText = cleanText.Replace(".", "").Replace(",", ".");
                }

                return decimal.Parse(cleanText, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private async Task ParseDatesAsync(IElementHandle datesCell, Transaction transaction)
        {
            try
            {
                var dateText = await datesCell.InnerTextAsync();
                var lines = dateText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if (line.Contains("Oluşturulma:"))
                    {
                        var dateStr = line.Replace("Oluşturulma:", "").Trim();
                        transaction.CreatedDate = ParseTurkishDateTime(dateStr);
                    }
                    else if (line.Contains("Onay:"))
                    {
                        var dateStr = line.Replace("Onay:", "").Trim();
                        //if (dateStr != "-")
                            //transaction.ApprovalDate = ParseTurkishDateTime(dateStr);
                    }
                    else if (line.Contains("Güncelleme:"))
                    {
                        var dateStr = line.Replace("Güncelleme:", "").Trim();
                        //if (dateStr != "-")
                            //transaction.UpdateDate = ParseTurkishDateTime(dateStr);
                    }
                    else if (line.Contains("Reddedildi:"))
                    {
                        var dateStr = line.Replace("Reddedildi:", "").Trim();
                        //if (dateStr != "-")
                            //transaction.RejectionDate = ParseTurkishDateTime(dateStr);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Tarih parse hatası");
            }
        }

        private DateTime ParseTurkishDateTime(string dateTimeStr)
        {
            try
            {
                // "17/01/2026 21:20:21" formatı
                var formats = new[] { "dd/MM/yyyy HH:mm:ss", "dd.MM.yyyy HH:mm:ss" };
                return DateTime.ParseExact(dateTimeStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.None);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private async Task ExtractModalDetailsAsync(Transaction transaction)
        {
            try
            {
                // Modal'ın açıldığını kontrol et
                var modal = await _page.QuerySelectorAsync("[role='dialog'], .modal, [data-slot='sheet-content']");
                if (modal == null)
                {
                    LoggerHelper.LogWarning("Modal bulunamadı!");
                    return;
                }

                // Modal içeriğini bekle
                await Task.Delay(1500);

                // HTML'deki yapıya göre selector'ları güncelle
                // "Ödeme Tutarı" ve "Sonuç Tutarı" alanlarını al
                await ExtractPaymentDetailsAsync(modal, transaction);

                // "Müşteri Bilgileri" bölümünü al
                await ExtractCustomerInfoAsync(modal, transaction);

                // "Atanan Banka Hesabı" bölümünü al
                await ExtractBankAccountInfoAsync(modal, transaction);

                // "Diğer Bilgiler" bölümünü al
                await ExtractOtherInfoAsync(modal, transaction);

                // Modal'ın tüm içeriğini TXT dosyasına yaz
                await WriteModalToTextFileAsync(modal, transaction);

                LoggerHelper.LogInformation($"{transaction.TransactionNo} detayları alındı ve kaydedildi.");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Modal detay alma hatası");
            }
        }

        private async Task ExtractPaymentDetailsAsync(IElementHandle modal, Transaction transaction)
        {
            try
            {
                // Ödeme Tutarı
                var paymentAmountElement = await modal.QuerySelectorAsync("p:text('Ödeme Tutarı') + div .font-medium");
                if (paymentAmountElement != null)
                {
                    var paymentText = await paymentAmountElement.InnerTextAsync();
                    transaction.PaymentAmount = ParseAmount(paymentText.Split('\n')[0].Trim());
                }

                // Sonuç Tutarı
                var resultAmountElement = await modal.QuerySelectorAsync("p:text('Sonuç Tutarı') + div .font-medium");
                if (resultAmountElement != null)
                {
                    var resultText = await resultAmountElement.InnerTextAsync();
                    transaction.ResultAmount = ParseAmount(resultText.Trim());
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Ödeme detayları alma hatası");
            }
        }

        private async Task ExtractCustomerInfoAsync(IElementHandle modal, Transaction transaction)
        {
            try
            {
                // İşlem ID
                var transactionIdElement = await modal.QuerySelectorAsync("p:text('İşlem ID') + div .font-medium");
                if (transactionIdElement != null)
                {
                    transaction.TransactionId = (await transactionIdElement.InnerTextAsync()).Trim();
                }

                // Kullanıcı ID
                var userIdElement = await modal.QuerySelectorAsync("p:text('Kullanıcı ID') + div .font-medium");
                if (userIdElement != null)
                {
                    transaction.UserId = (await userIdElement.InnerTextAsync()).Trim();
                }

                // Kullanıcı Adı
                var usernameElement = await modal.QuerySelectorAsync("p:text('Kullanıcı Adı') + div .font-medium");
                if (usernameElement != null)
                {
                    transaction.Username = (await usernameElement.InnerTextAsync()).Trim();
                }

                // İsim Soyisim
                var fullNameElement = await modal.QuerySelectorAsync("p:text('İsim Soyisim') + div .font-medium");
                if (fullNameElement != null)
                {
                    transaction.FullName = (await fullNameElement.InnerTextAsync()).Trim();
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Müşteri bilgileri alma hatası");
            }
        }

        private async Task ExtractBankAccountInfoAsync(IElementHandle modal, Transaction transaction)
        {
            try
            {
                // Banka Adı
                var bankNameElement = await modal.QuerySelectorAsync("h3:text('Atanan Banka Hesabı') + div .flex-col div:nth-child(2)");
                if (bankNameElement != null)
                {
                    transaction.BankName = (await bankNameElement.InnerTextAsync()).Trim();
                }

                // IBAN Sahibi
                var ibanHolderElement = await modal.QuerySelectorAsync("p:text('IBAN Sahibi') + div .font-medium");
                if (ibanHolderElement != null)
                {
                    transaction.AccountHolder = (await ibanHolderElement.InnerTextAsync()).Trim();
                }

                // IBAN
                var ibanElement = await modal.QuerySelectorAsync("p:text('IBAN') + div .font-medium");
                if (ibanElement != null)
                {
                    transaction.IBAN = (await ibanElement.InnerTextAsync()).Trim();
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Banka hesap bilgileri alma hatası");
            }
        }

        private async Task ExtractOtherInfoAsync(IElementHandle modal, Transaction transaction)
        {
            try
            {
                // İşlem Oluşturma Tarihi
                var creationDateElement = await modal.QuerySelectorAsync("p:text('İşlem Oluşturma Tarihi') + div .font-medium");
                if (creationDateElement != null)
                {
                    var dateText = (await creationDateElement.InnerTextAsync()).Trim();
                    transaction.CreatedDate = ParseTurkishDateTime(dateText);
                }

                // İşleme Kabul Tarihi
                var acceptanceDateElement = await modal.QuerySelectorAsync("p:text('İşleme Kabul Tarihi') + div .font-medium");
                if (acceptanceDateElement != null)
                {
                    var dateText = (await acceptanceDateElement.InnerTextAsync()).Trim();
                    transaction.AcceptanceDate = ParseTurkishDateTime(dateText);
                }

                // Son Onay Tarihi
                var lastApprovalDateElement = await modal.QuerySelectorAsync("p:text('Son Onay Tarihi') + div .font-medium");
                if (lastApprovalDateElement != null)
                {
                    var dateText = (await lastApprovalDateElement.InnerTextAsync()).Trim();
                    transaction.LastApprovalDate = ParseTurkishDateTime(dateText);
                }

                // Son İptal/Red Tarihi
                var lastRejectionDateElement = await modal.QuerySelectorAsync("p:text('Son İptal/Red Tarihi') + div .font-medium");
                if (lastRejectionDateElement != null)
                {
                    var dateText = (await lastRejectionDateElement.InnerTextAsync()).Trim();
                    if (dateText != "-")
                        transaction.LastRejectionDate = ParseTurkishDateTime(dateText);
                }

                // Son Güncelleme Tarihi
                var lastUpdateDateElement = await modal.QuerySelectorAsync("p:text('Son Güncelleme Tarihi') + div .font-medium");
                if (lastUpdateDateElement != null)
                {
                    var dateText = (await lastUpdateDateElement.InnerTextAsync()).Trim();
                    transaction.LastUpdateDate = ParseTurkishDateTime(dateText);
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Diğer bilgiler alma hatası");
            }
        }

        private async Task WriteModalToTextFileAsync(IElementHandle modal, Transaction transaction)
        {
            try
            {
                // Modal klasörünü oluştur
                string modalFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ModalLogs");
                if (!Directory.Exists(modalFolder))
                    Directory.CreateDirectory(modalFolder);

                // Modal'ın tüm text içeriğini al
                var modalText = await modal.InnerTextAsync();

                // Dosya adını oluştur
                string fileName = $"Modal_{transaction.TransactionNo}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string filePath = Path.Combine(modalFolder, fileName);

                using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    writer.WriteLine("=".PadRight(80, '='));
                    writer.WriteLine($"İŞLEM MODAL DETAYLARI - {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
                    writer.WriteLine("=".PadRight(80, '='));
                    writer.WriteLine();

                    // Temel işlem bilgileri
                    writer.WriteLine($"İşlem No: {transaction.TransactionNo}");
                    writer.WriteLine($"External Ref No: {transaction.ExternalRefNo}");
                    writer.WriteLine($"Customer Ref No: {transaction.CustomerRefNo}");
                    writer.WriteLine($"Müşteri: {transaction.CustomerName}");
                    writer.WriteLine($"Talep Tutarı: {transaction.RequestedAmount:N2} ₺");
                    writer.WriteLine($"Sonuç Tutarı: {transaction.ResultAmount:N2} ₺");
                    writer.WriteLine($"Durum: {transaction.Status}");
                    writer.WriteLine();

                    // Modal'dan çıkarılan detaylar
                    writer.WriteLine("ÖDEME BİLGİLERİ:");
                    writer.WriteLine($"- Ödeme Tutarı: {transaction.PaymentAmount:N2} ₺");
                    writer.WriteLine($"- Sonuç Tutarı: {transaction.ResultAmount:N2} ₺");
                    writer.WriteLine();

                    writer.WriteLine("MÜŞTERİ BİLGİLERİ:");
                    writer.WriteLine($"- İşlem ID: {transaction.TransactionId}");
                    writer.WriteLine($"- Kullanıcı ID: {transaction.UserId}");
                    writer.WriteLine($"- Kullanıcı Adı: {transaction.Username}");
                    writer.WriteLine($"- İsim Soyisim: {transaction.FullName}");
                    writer.WriteLine();

                    writer.WriteLine("BANKA HESAP BİLGİLERİ:");
                    writer.WriteLine($"- Banka Adı: {transaction.BankName}");
                    writer.WriteLine($"- IBAN Sahibi: {transaction.AccountHolder}");
                    writer.WriteLine($"- IBAN: {transaction.IBAN}");
                    writer.WriteLine();

                    writer.WriteLine("DİĞER BİLGİLER:");
                    writer.WriteLine($"- İşlem Oluşturma: {transaction.CreatedDate:dd.MM.yyyy HH:mm:ss}");
                    writer.WriteLine($"- İşleme Kabul: {transaction.AcceptanceDate:dd.MM.yyyy HH:mm:ss}");
                    writer.WriteLine($"- Son Onay: {transaction.LastApprovalDate:dd.MM.yyyy HH:mm:ss}");
                    writer.WriteLine($"- Son Red/İptal: {(transaction.LastRejectionDate.HasValue ? transaction.LastRejectionDate.Value.ToString("dd.MM.yyyy HH:mm:ss") : "-")}");
                    writer.WriteLine($"- Son Güncelleme: {transaction.LastUpdateDate:dd.MM.yyyy HH:mm:ss}");
                    writer.WriteLine();

                    writer.WriteLine("-".PadRight(80, '-'));
                    writer.WriteLine("MODAL TAM METİN İÇERİĞİ:");
                    writer.WriteLine("-".PadRight(80, '-'));
                    writer.WriteLine(modalText);

                    writer.WriteLine();
                    writer.WriteLine("=".PadRight(80, '='));
                    writer.WriteLine($"Dosya Oluşturulma: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
                    writer.WriteLine($"Dosya Yolu: {filePath}");
                    writer.WriteLine("=".PadRight(80, '='));
                }

                LoggerHelper.LogInformation($"Modal verileri TXT dosyasına yazıldı: {filePath}");

                // Ayrıca günlük birleştirilmiş dosyaya da ekle
                await AppendToDailyLogFileAsync(transaction, modalText);
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "TXT dosyasına yazma hatası");
            }
        }

        private async Task AppendToDailyLogFileAsync(Transaction transaction, string modalText)
        {
            try
            {
                string dailyFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ModalLogs", "Daily");
                if (!Directory.Exists(dailyFolder))
                    Directory.CreateDirectory(dailyFolder);

                string dailyFileName = $"Onaylanmis_Islemler_{DateTime.Now:yyyyMMdd}.txt";
                string dailyFilePath = Path.Combine(dailyFolder, dailyFileName);

                using (StreamWriter writer = new StreamWriter(dailyFilePath, true, Encoding.UTF8))
                {
                    // İlk yazma ise başlık ekle
                    if (new FileInfo(dailyFilePath).Length == 0)
                    {
                        writer.WriteLine("=".PadRight(100, '='));
                        writer.WriteLine($"ONAYLANMIŞ İŞLEM LOGLARI - {DateTime.Now:dd.MM.yyyy}");
                        writer.WriteLine("=".PadRight(100, '='));
                        writer.WriteLine();
                    }

                    writer.WriteLine($"■ İŞLEM: {transaction.TransactionNo}");
                    writer.WriteLine($"  Müşteri: {transaction.CustomerName}");
                    writer.WriteLine($"  Tutar: {transaction.ResultAmount:N2} ₺");
                    writer.WriteLine($"  IBAN: {transaction.IBAN}");
                    writer.WriteLine($"  Banka: {transaction.BankName}");
                    writer.WriteLine($"  Tarih: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
                    writer.WriteLine($"  Dosya: Modal_{transaction.TransactionNo}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    writer.WriteLine();
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Günlük log dosyasına yazma hatası");
            }
        }

        private async Task CloseModalAsync()
        {
            try
            {
                // X butonunu bul (farklı selector'lar deneyelim)
                var closeButton = await _page.QuerySelectorAsync(@"
                    button[aria-label='Close'], 
                    button[data-slot='close-button'],
                    .close-button,
                    [class*='close'],
                    button:has(svg[aria-label='Close']),
                    button:has-text('×'),
                    button:has(svg.lucide-x)
                ");

                if (closeButton != null && await closeButton.IsVisibleAsync())
                {
                    await closeButton.ClickAsync();
                    LoggerHelper.LogInformation("Modal X butonu ile kapatıldı.");
                }
                else
                {
                    // Alternatif kapatma yöntemi: ESC tuşu veya overlay tıklama
                    await _page.Keyboard.PressAsync("Escape");
                    LoggerHelper.LogInformation("Modal ESC ile kapatıldı.");
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Modal kapatma hatası");
                // Başka bir kapatma yöntemi dene
                await TryCloseModalAsync();
            }
        }

        private async Task TryCloseModalAsync()
        {
            try
            {
                // Overlay'e tıkla (modal arka planı)
                var overlay = await _page.QuerySelectorAsync(".modal-backdrop, .overlay, [class*='backdrop']");
                if (overlay != null)
                {
                    await overlay.ClickAsync(new ElementHandleClickOptions { Force = true });
                }
                else
                {
                    // ESC tuşuna bas
                    await _page.Keyboard.PressAsync("Escape");
                }

                await Task.Delay(500);
            }
            catch
            {
                // Hata olsa bile devam et
            }
        }

        public async Task<Transaction> GetTransactionDetailsAsync(string transactionId)
        {
            // Detaylı işlem bilgisi için implementasyon
            throw new NotImplementedException();
        }

        public async Task<bool> IsLoggedInAsync()
        {
            try
            {
                var currentUrl = _page.Url;
                return !currentUrl.Contains("login") &&
                       !currentUrl.Contains("auth") &&
                       await _page.ContentAsync().ContinueWith(t => !t.Result.Contains("Giriş Yap"));
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _page?.CloseAsync();
            _browser?.CloseAsync();
            _playwright?.Dispose();
        }
    }
}