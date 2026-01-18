using Microsoft.Playwright;
using Proje.Core.Helpers;
using Proje.Core.Interfaces;
using Proje.Entities.Entities;
using Proje.Enums;
using Proje.Models;
using System;
using System.Collections.Generic;
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

                    foreach (var row in rows)
                    {
                        try
                        {
                            var transaction = await ExtractTransactionFromRowAsync(row);
                            if (transaction != null)
                            {
                                transaction.PageNumber = pageNum;
                                transaction.ExtractionDate = DateTime.Now;
                                transactions.Add(transaction);
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggerHelper.LogError(ex, "Satır işleme hatası");
                        }
                    }

                    // Sonraki sayfaya git (eğer varsa)
                    if (pageNum < pageCount)
                    {
                        var nextButton = await _page.QuerySelectorAsync("button[aria-label='Next page']");
                        if (nextButton != null)
                        {
                            await nextButton.ClickAsync();
                            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                            await Task.Delay(2000);
                        }
                        else
                        {
                            break; // Daha fazla sayfa yok
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
                    var employeeText = await employeeCell.InnerTextAsync();
                    // HTML'de sadece "pHantom" yazıyor
                    transaction.EmployeeName = "pHantom";
                    transaction.EmployeeRole = "Sistem";
                }

                // 5. Durum
                var statusCell = await row.QuerySelectorAsync("td:nth-child(5)");
                if (statusCell != null)
                {
                    var statusText = await statusCell.InnerTextAsync();
                    //transaction.Status = ParseTransactionStatus(statusText);
                }

                // 6. Tarihler
                var datesCell = await row.QuerySelectorAsync("td:nth-child(6)");
                if (datesCell != null)
                {
                    await ParseDatesAsync(datesCell, transaction);
                }

                // 7. Detaylı bilgiler için "Detaylı Görüntüle" butonuna tıkla
                var detailButton = await row.QuerySelectorAsync("td:nth-child(7) button");
                if (detailButton != null)
                {
                    await detailButton.ClickAsync();
                    await Task.Delay(1000);

                    // Modal'dan ekstra bilgileri çek
                    await ExtractModalDetailsAsync(transaction);

                    // Modal'ı kapat
                    var closeButton = await _page.QuerySelectorAsync("button[aria-label='Close']");
                    if (closeButton != null)
                    {
                        await closeButton.ClickAsync();
                        await Task.Delay(500);
                    }
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
                    return await textElement.InnerTextAsync();
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
                if (string.IsNullOrEmpty(amountText))
                    return 0;

                // "5.000,00 ₺" formatından temizleme
                var cleanText = amountText.Replace("₺", "").Replace(" ", "").Trim();
                cleanText = cleanText.Replace(".", "").Replace(",", ".");

                if (decimal.TryParse(cleanText, out decimal amount))
                    return amount;

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private TransactionStatus ParseTransactionStatus(string statusText)
        {
            if (statusText.Contains("Onaylandı"))
                return TransactionStatus.Onaylandi;
            else if (statusText.Contains("Reddedildi"))
                return TransactionStatus.Reddedildi;
            else if (statusText.Contains("Beklemede"))
                return TransactionStatus.Beklemede;
            else
                return TransactionStatus.Iptal;
        }

        private async Task ParseDatesAsync(IElementHandle datesCell, Transaction transaction)
        {
            try
            {
                var dateSpans = await datesCell.QuerySelectorAllAsync("span");
                if (dateSpans.Count >= 8) // 4 tarih için 2'şer span (label + value)
                {
                    // Format: "Oluşturulma:" "17/01/2026 21:20:21"
                    for (int i = 0; i < dateSpans.Count; i += 2)
                    {
                        var label = await dateSpans[i].InnerTextAsync();
                        var value = await dateSpans[i + 1].InnerTextAsync();

                        if (DateTime.TryParseExact(value.Trim(), "dd/MM/yyyy HH:mm:ss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out DateTime dateValue))
                        {
                            if (label.Contains("Oluşturulma"))
                                transaction.CreatedDate = dateValue;
                            else if (label.Contains("Onay"))
                                transaction.ApprovalDate = dateValue;
                            else if (label.Contains("Güncelleme"))
                                transaction.UpdateDate = dateValue;
                            else if (label.Contains("Reddedildi") && value != "-")
                                transaction.RejectionDate = dateValue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Tarih parse etme hatası");
            }
        }

        private async Task ExtractModalDetailsAsync(Transaction transaction)
        {
            try
            {
                // Modal içeriğini al
                var modalContent = await _page.QuerySelectorAsync("[role='dialog']");
                if (modalContent != null)
                {
                    // Banka bilgilerini çek
                    var bankInfo = await modalContent.QuerySelectorAsync("//div[contains(text(),'Banka')]/following-sibling::div");
                    if (bankInfo != null)
                        transaction.BankName = await bankInfo.InnerTextAsync();

                    // Hesap No
                    var accountInfo = await modalContent.QuerySelectorAsync("//div[contains(text(),'Hesap No')]/following-sibling::div");
                    if (accountInfo != null)
                        transaction.AccountNumber = await accountInfo.InnerTextAsync();

                    // Açıklama
                    var descriptionInfo = await modalContent.QuerySelectorAsync("//div[contains(text(),'Açıklama')]/following-sibling::div");
                    if (descriptionInfo != null)
                        transaction.Description = await descriptionInfo.InnerTextAsync();
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Modal detay çekme hatası");
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
