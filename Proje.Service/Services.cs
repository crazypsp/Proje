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
using System.IO;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace Proje.Service
{
    public class WebAutomationService : IWebAutomationService, IDisposable
    {
        // === TÜM ALANLAR AYNI ===
        private IPlaywright _playwright;
        private IBrowser _browser;
        private IPage _page;
        private IBrowserContext _context;
        private readonly LoginCredentials _credentials;
        private readonly BrowserConfig _browserConfig;

        private SheetsService _sheetsService;
        private string _spreadsheetId;
        private bool _googleSheetsInitialized = false;

        public WebAutomationService(LoginCredentials credentials, BrowserConfig browserConfig, string spreadsheetId = null)
        {
            _credentials = credentials;
            _browserConfig = browserConfig;
            _spreadsheetId = spreadsheetId;
        }

        public async Task InitializeAsync()
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(
                new BrowserTypeLaunchOptions
                {
                    Headless = _browserConfig.Headless,
                    Timeout = _browserConfig.TimeoutSeconds * 1000
                });

            _context = await _browser.NewContextAsync();
            _page = await _context.NewPageAsync();

            if (!string.IsNullOrEmpty(_spreadsheetId))
                await InitializeGoogleSheetsAsync();
        }

        private async Task<bool> InitializeGoogleSheetsAsync()
        {
            try
            {
                if (_googleSheetsInitialized)
                    return true;

                var credential = GoogleCredential
                    .FromFile("Credentials/google-service-account.json")
                    .CreateScoped(SheetsService.Scope.Spreadsheets);

                _sheetsService = new SheetsService(
                    new BaseClientService.Initializer
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = "WebAutomationService"
                    });

                _googleSheetsInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Google Sheets init failed");
                return false;
            }
        }


        public async Task<bool> TestGoogleSheetsConnection()
        {
            try
            {
                if (!_googleSheetsInitialized)
                {
                    bool initialized = await InitializeGoogleSheetsAsync();
                    if (!initialized)
                    {
                        return false;
                    }
                }

                var request = _sheetsService.Spreadsheets.Get(_spreadsheetId);
                var spreadsheet = await request.ExecuteAsync();

                LoggerHelper.LogInformation($"Google Sheets bağlantısı başarılı!");
                LoggerHelper.LogInformation($"Dosya Adı: {spreadsheet.Properties.Title}");
                LoggerHelper.LogInformation($"URL: https://docs.google.com/spreadsheets/d/{_spreadsheetId}/edit");

                return true;
            }
            catch (Google.GoogleApiException ex)
            {
                if (ex.Error.Code == 404)
                {
                    //LoggerHelper.LogError($"Google Sheets bulunamadı: {_spreadsheetId}");
                }
                else if (ex.Error.Code == 403)
                {
                    //LoggerHelper.LogError("Erişim engellendi! Service Account'ın yetkisi yok.");
                    //LoggerHelper.LogError($"Lütfen şu e-postaya editör yetkisi verin: {SERVICE_ACCOUNT_EMAIL}");
                }
                else
                {
                    //LoggerHelper.LogError($"Google API hatası: {ex.Error.Message}");
                }
                return false;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Google Sheets bağlantı testi hatası");
                return false;
            }
        }

        public async Task<bool> LoginAsync()
        {
            try
            {
                LoggerHelper.LogInformation("Login işlemi başlatılıyor...");

                // ADIM 1: 401 Basic Auth Geçişi
                await _page.GotoAsync("https://online.powerhavale.com/marjin/employee/69",
                    new PageGotoOptions { Timeout = 15000, WaitUntil = WaitUntilState.NetworkIdle });

                // ADIM 2: Login Sayfasına Git
                await _page.GotoAsync(_credentials.LoginUrl,
                    new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

                // ADIM 3: Login Formunu Doldur
                await _page.FillAsync("input[name='email']", _credentials.FormUsername);
                await _page.FillAsync("input[name='password']", _credentials.FormPassword);

                // ADIM 4: Giriş Yap
                await _page.ClickAsync("button[type='submit']");

                // ADIM 5: Bekle
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(3000);

                // ADIM 6: Kontrol
                var currentUrl = _page.Url;
                var content = await _page.ContentAsync();

                bool isLoginSuccess = !content.Contains("401 Authorization Required") &&
                                     !content.Contains("Hatalı") &&
                                     !content.Contains("Yanlış") &&
                                     !currentUrl.Contains("login");

                if (isLoginSuccess)
                {
                    LoggerHelper.LogInformation($"Giriş başarılı! URL: {currentUrl}");
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

                // Sidebar'daki linki bul
                await _page.ClickAsync("a[href*='/marjin/transaction-history']");

                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(3000);

                var pageTitle = await _page.TitleAsync();
                if (pageTitle.Contains("İşlem Geçmişi") || (await _page.ContentAsync()).Contains("İşlem Geçmişi"))
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

                                if (transaction.Status == "Onaylandı")
                                {
                                    try
                                    {
                                        LoggerHelper.LogInformation($"{transaction.TransactionNo} numaralı işlemin detayları alınıyor...");

                                        var detailButton = await row.QuerySelectorAsync("td:nth-child(7) button");
                                        if (detailButton != null)
                                        {
                                            await detailButton.ClickAsync();
                                            await Task.Delay(1500);

                                            await ExtractModalDetailsAsync(transaction);
                                            transaction.HasModalDetails = true;

                                            await CloseModalAsync();
                                            await Task.Delay(500);
                                        }
                                    }
                                    catch (Exception modalEx)
                                    {
                                        LoggerHelper.LogError(modalEx, $"{transaction.TransactionNo} detay alma hatası");
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
                    transaction.TransactionNo = await ExtractTextFromButtonAsync(transactionNoElements[0]);
                    transaction.ExternalRefNo = await ExtractTextFromButtonAsync(transactionNoElements[1]);
                    transaction.CustomerRefNo = await ExtractTextFromButtonAsync(transactionNoElements[2]);
                }

                // 2. Müşteri Bilgileri
                var customerElements = await row.QuerySelectorAllAsync("td:nth-child(2) button");
                if (customerElements.Count >= 2)
                {
                    transaction.CustomerId = await ExtractTextFromButtonAsync(customerElements[0]);
                    transaction.CustomerName = await ExtractTextFromButtonAsync(customerElements[1]);
                }

                // 3. Tutarlar
                var amountElements = await row.QuerySelectorAllAsync("td:nth-child(3) button");
                if (amountElements.Count >= 2)
                {
                    var requestedAmountText = await ExtractTextFromButtonAsync(amountElements[0]);
                    transaction.RequestedAmount = ParseAmount(requestedAmountText);

                    var resultAmountText = await ExtractTextFromButtonAsync(amountElements[1]);
                    transaction.ResultAmount = ParseAmount(resultAmountText);
                }

                // 4. Personel
                var employeeCell = await row.QuerySelectorAsync("td:nth-child(4)");
                if (employeeCell != null)
                {
                    transaction.EmployeeName = "pHantom";
                    transaction.EmployeeRole = "Sistem";
                }

                // 5. Durum
                var statusCell = await row.QuerySelectorAsync("td:nth-child(5)");
                if (statusCell != null)
                {
                    var statusBadge = await statusCell.QuerySelectorAsync("[data-slot='badge']");
                    if (statusBadge != null)
                    {
                        transaction.Status = (await statusBadge.InnerTextAsync()).Trim();
                    }
                    else
                    {
                        transaction.Status = (await statusCell.InnerTextAsync()).Trim();
                    }
                }

                // 6. Tarihler
                var datesCell = await row.QuerySelectorAsync("td:nth-child(6)");
                if (datesCell != null)
                {
                    await ParseDatesAsync(datesCell, transaction);
                }

                // Google Sheets'e ekle (arka planda)
                if (transaction.Status == "Onaylandı")
                {
                    await AddTransactionToGoogleSheetsAsync(transaction);
                }

                return transaction;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Satırdan veri çıkarma hatası");
                return null;
            }
        }

        public async Task<bool> AddTransactionToGoogleSheetsAsync(Transaction transaction)
        {
            try
            {
                if (transaction.Status != "Onaylandı")
                    return false;

                if (!_googleSheetsInitialized)
                {
                    if (!await InitializeGoogleSheetsAsync())
                    {
                        await WriteToCsvBackupAsync(transaction);
                        return false;
                    }
                }

                // ✔ İŞLEM TABI
                // ✔ 16. SATIRDAN BAŞLAR
                const string range = "İşlem!A16";

                var values = new List<IList<object>>
                {
                    new List<object>
                    {
                        transaction.CreatedDate.ToString("dd.MM.yyyy HH:mm:ss"),
                        transaction.Username ?? transaction.CustomerName ?? "",
                        transaction.BankName ?? "",
                        transaction.AccountHolder ?? "",
                        transaction.ResultAmount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")),
                        transaction.TransactionId ?? transaction.TransactionNo ?? "",
                        transaction.CustomerRefNo ?? "",
                        transaction.ExternalRefNo ?? ""
                    }
                };

                var body = new ValueRange { Values = values };

                var request = _sheetsService.Spreadsheets.Values.Append(
                    body,
                    _spreadsheetId,
                    range);

                request.ValueInputOption =
                    SpreadsheetsResource.ValuesResource.AppendRequest
                        .ValueInputOptionEnum.USERENTERED;

                request.InsertDataOption =
                    SpreadsheetsResource.ValuesResource.AppendRequest
                        .InsertDataOptionEnum.INSERTROWS;

                await request.ExecuteAsync();

                LoggerHelper.LogInformation(
                    $"[Sheets] Eklendi: {transaction.TransactionNo}");

                return true;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "[Sheets] Yazma hatası");
                await WriteToCsvBackupAsync(transaction);
                return false;
            }
        }

        private async Task EnsureHeadersExistAsync(string sheetName)
        {
            try
            {
                var getRequest = _sheetsService.Spreadsheets.Values.Get(_spreadsheetId, $"{sheetName}!A1:H1");
                var existingHeaders = await getRequest.ExecuteAsync();

                if (existingHeaders.Values == null || existingHeaders.Values.Count == 0)
                {
                    var headers = new ValueRange
                    {
                        Values = new List<IList<object>>
                        {
                            new List<object>
                            {
                                "Tarih", "Kullanıcı", "Banka", "Hesap Sahibi",
                                "Tutar", "İşlem No", "Customer Ref", "External Ref"
                            }
                        }
                    };

                    var updateRequest = _sheetsService.Spreadsheets.Values.Update(
                        headers,
                        _spreadsheetId,
                        $"{sheetName}!A1:H1"
                    );

                    updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                    await updateRequest.ExecuteAsync();

                    LoggerHelper.LogInformation("[Google Sheets] Başlıklar eklendi");
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "[Google Sheets] Başlık kontrol hatası");
            }
        }

        private async Task WriteToCsvBackupAsync(Transaction transaction)
        {
            try
            {
                if (transaction.Status != "Onaylandı")
                    return;

                string csvFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backup");
                if (!Directory.Exists(csvFolder))
                    Directory.CreateDirectory(csvFolder);

                string fileName = $"Transactions_{DateTime.Now:yyyyMMdd}.csv";
                string filePath = Path.Combine(csvFolder, fileName);

                string csvLine = $"{DateTime.Now:dd.MM.yyyy HH:mm:ss}," +
                               $"\"{(transaction.Username ?? transaction.CustomerName ?? "").Replace("\"", "\"\"")}\"," +
                               $"\"{transaction.BankName?.Replace("\"", "\"\"") ?? ""}\"," +
                               $"\"{transaction.AccountHolder?.Replace("\"", "\"\"") ?? ""}\"," +
                               $"{transaction.ResultAmount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")).Replace(",", ".")}," +
                               $"\"{transaction.TransactionId ?? transaction.TransactionNo ?? ""}\"," +
                               $"\"{transaction.CustomerRefNo ?? ""}\"," +
                               $"\"{transaction.ExternalRefNo ?? ""}\"";

                if (!File.Exists(filePath))
                {
                    string header = "Tarih,Kullanıcı,Banka,Hesap Sahibi,Tutar,İşlem No,Customer Ref,External Ref";
                    await File.WriteAllTextAsync(filePath, header + Environment.NewLine + csvLine, Encoding.UTF8);
                }
                else
                {
                    await File.AppendAllTextAsync(filePath, Environment.NewLine + csvLine, Encoding.UTF8);
                }

                LoggerHelper.LogInformation($"[Backup] İşlem {transaction.TransactionNo} CSV'ye yedeklendi");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "[Backup] CSV yedekleme hatası");
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

                var cleanText = amountText
                    .Replace("₺", "")
                    .Replace("TL", "")
                    .Replace(" ", "")
                    .Trim();

                if (cleanText.Contains(".") && cleanText.Contains(","))
                {
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
                var modal = await _page.QuerySelectorAsync("[role='dialog'], .modal, [data-slot='sheet-content']");
                if (modal == null)
                {
                    LoggerHelper.LogWarning("Modal bulunamadı!");
                    return;
                }

                await Task.Delay(1500);

                // Modal içindeki bilgileri çek
                await ExtractPaymentDetailsAsync(modal, transaction);
                await ExtractCustomerInfoAsync(modal, transaction);
                await ExtractBankAccountInfoAsync(modal, transaction);
                await ExtractOtherInfoAsync(modal, transaction);

                LoggerHelper.LogInformation($"{transaction.TransactionNo} detayları alındı.");
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
                var paymentAmountElement = await modal.QuerySelectorAsync("p:text('Ödeme Tutarı') + div .font-medium");
                if (paymentAmountElement != null)
                {
                    var paymentText = await paymentAmountElement.InnerTextAsync();
                    transaction.PaymentAmount = ParseAmount(paymentText.Split('\n')[0].Trim());
                }

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
                var transactionIdElement = await modal.QuerySelectorAsync("p:text('İşlem ID') + div .font-medium");
                if (transactionIdElement != null)
                {
                    transaction.TransactionId = (await transactionIdElement.InnerTextAsync()).Trim();
                }

                var userIdElement = await modal.QuerySelectorAsync("p:text('Kullanıcı ID') + div .font-medium");
                if (userIdElement != null)
                {
                    transaction.UserId = (await userIdElement.InnerTextAsync()).Trim();
                }

                var usernameElement = await modal.QuerySelectorAsync("p:text('Kullanıcı Adı') + div .font-medium");
                if (usernameElement != null)
                {
                    transaction.Username = (await usernameElement.InnerTextAsync()).Trim();
                }

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
                var bankNameElement = await modal.QuerySelectorAsync("h3:text('Atanan Banka Hesabı') + div .flex-col div:nth-child(2)");
                if (bankNameElement != null)
                {
                    transaction.BankName = (await bankNameElement.InnerTextAsync()).Trim();
                }

                var ibanHolderElement = await modal.QuerySelectorAsync("p:text('IBAN Sahibi') + div .font-medium");
                if (ibanHolderElement != null)
                {
                    transaction.AccountHolder = (await ibanHolderElement.InnerTextAsync()).Trim();
                }

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
                var creationDateElement = await modal.QuerySelectorAsync("p:text('İşlem Oluşturma Tarihi') + div .font-medium");
                if (creationDateElement != null)
                {
                    var dateText = (await creationDateElement.InnerTextAsync()).Trim();
                    transaction.CreatedDate = ParseTurkishDateTime(dateText);
                }

                var acceptanceDateElement = await modal.QuerySelectorAsync("p:text('İşleme Kabul Tarihi') + div .font-medium");
                if (acceptanceDateElement != null)
                {
                    var dateText = (await acceptanceDateElement.InnerTextAsync()).Trim();
                    transaction.AcceptanceDate = ParseTurkishDateTime(dateText);
                }

                var lastApprovalDateElement = await modal.QuerySelectorAsync("p:text('Son Onay Tarihi') + div .font-medium");
                if (lastApprovalDateElement != null)
                {
                    var dateText = (await lastApprovalDateElement.InnerTextAsync()).Trim();
                    transaction.LastApprovalDate = ParseTurkishDateTime(dateText);
                }

                var lastRejectionDateElement = await modal.QuerySelectorAsync("p:text('Son İptal/Red Tarihi') + div .font-medium");
                if (lastRejectionDateElement != null)
                {
                    var dateText = (await lastRejectionDateElement.InnerTextAsync()).Trim();
                    if (dateText != "-")
                        transaction.LastRejectionDate = ParseTurkishDateTime(dateText);
                }

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

        private async Task CloseModalAsync()
        {
            try
            {
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
                    await _page.Keyboard.PressAsync("Escape");
                    LoggerHelper.LogInformation("Modal ESC ile kapatıldı.");
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Modal kapatma hatası");
                await TryCloseModalAsync();
            }
        }

        private async Task TryCloseModalAsync()
        {
            try
            {
                var overlay = await _page.QuerySelectorAsync(".modal-backdrop, .overlay, [class*='backdrop']");
                if (overlay != null)
                {
                    await overlay.ClickAsync(new ElementHandleClickOptions { Force = true });
                }
                else
                {
                    await _page.Keyboard.PressAsync("Escape");
                }

                await Task.Delay(500);
            }
            catch
            {
            }
        }

        public async Task<Transaction> GetTransactionDetailsAsync(string transactionId)
        {
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

        public string GetSheetsId() => _spreadsheetId;

        public void Dispose()
        {
            try
            {
                _page?.CloseAsync();
                _browser?.CloseAsync();
                _playwright?.Dispose();
                _sheetsService?.Dispose();
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Dispose hatası");
            }
        }
    }
}