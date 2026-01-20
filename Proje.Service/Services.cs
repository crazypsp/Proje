using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Playwright;
using Proje.Core.Helpers;
using Proje.Core.Interfaces;
using Proje.Entities.Entities;
using Proje.Models;

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
        private SheetsService _sheetsService;
        private const string SpreadsheetId = "1RstouLb99LwTTzyavcJi-j1B6E49tu9gNtOxpcrQywY";
        private const string SheetName = "İşlem";
        private int _currentRow = 16;
        private CancellationTokenSource _processingCts;
        private int _processedTransactionCount = 0;
        private HashSet<string> _processedTransactionIds = new HashSet<string>();
        private HashSet<string> _existingTransactionIds = new HashSet<string>();

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
                    Timeout = _browserConfig.TimeoutSeconds * 1000,
                    Args = new[] { "--disable-blink-features=AutomationControlled" }
                });

                LoggerHelper.LogInformation("Context oluşturuluyor...");
                _context = await _browser.NewContextAsync(new BrowserNewContextOptions
                {
                    HttpCredentials = new HttpCredentials
                    {
                        Username = _credentials.BasicAuthUsername,
                        Password = _credentials.BasicAuthPassword
                    },
                    UserAgent = _browserConfig.UserAgent,
                    ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                    IgnoreHTTPSErrors = true
                });

                // Anti-bot önlemlerini atlatmak için
                await _context.AddInitScriptAsync(@"
                    Object.defineProperty(navigator, 'webdriver', {
                        get: () => undefined
                    });
                    Object.defineProperty(navigator, 'plugins', {
                        get: () => [1, 2, 3, 4, 5]
                    });
                    Object.defineProperty(navigator, 'languages', {
                        get: () => ['tr-TR', 'tr', 'en-US', 'en']
                    });
                ");

                _page = await _context.NewPageAsync();
                await _page.SetViewportSizeAsync(1920, 1080);

                // Sayfa hata yönetimi
                _page.PageError += (_, e) => LoggerHelper.LogError(null, $"Sayfa hatası: {e}");
                _page.Crash += (_, _) => LoggerHelper.LogError(null, "Sayfa çöktü!");

                await InitializeGoogleSheetsAsync();

                // Excel'deki mevcut işlem ID'lerini yükle
                await LoadExistingTransactionIdsAsync();

                LoggerHelper.LogInformation("Web otomasyon servisi hazır!");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Playwright başlatma hatası");
                throw;
            }
        }

        private async Task InitializeGoogleSheetsAsync()
        {
            try
            {
                LoggerHelper.LogInformation("Google Sheets servisi başlatılıyor...");

                string credentialFilePath = GetCredentialsFilePath();

                if (!File.Exists(credentialFilePath))
                {
                    throw new FileNotFoundException($"Google servis hesabı anahtarı bulunamadı!\nAranan yol: {credentialFilePath}");
                }

                LoggerHelper.LogInformation($"Google kimlik bilgileri yükleniyor: {credentialFilePath}");

                GoogleCredential credential;
                using (var stream = new FileStream(credentialFilePath, FileMode.Open, FileAccess.Read))
                {
                    credential = GoogleCredential.FromStream(stream)
                        .CreateScoped(SheetsService.Scope.Spreadsheets);
                }

                _sheetsService = new SheetsService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Proje",
                });

                LoggerHelper.LogInformation("Google Sheets servisi başlatıldı.");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Google Sheets servisi başlatma hatası");
                throw;
            }
        }

        private async Task LoadExistingTransactionIdsAsync()
        {
            try
            {
                if (_sheetsService == null)
                {
                    await InitializeGoogleSheetsAsync();
                }

                // H sütunundaki tüm ID'leri oku (16. satırdan itibaren)
                var range = $"{SheetName}!H16:H";
                var request = _sheetsService.Spreadsheets.Values.Get(SpreadsheetId, range);
                var response = await request.ExecuteAsync();

                if (response.Values != null)
                {
                    foreach (var row in response.Values)
                    {
                        if (row.Count > 0 && !string.IsNullOrWhiteSpace(row[0].ToString()))
                        {
                            _existingTransactionIds.Add(row[0].ToString().Trim());
                        }
                    }
                    LoggerHelper.LogInformation($"Google Sheets'ten {_existingTransactionIds.Count} adet mevcut işlem ID'si yüklendi.");
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Mevcut işlem ID'lerini yükleme hatası");
            }
        }

        private string GetCredentialsFilePath()
        {
            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "credentials", "google-service-account.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "credentials", "google-service-account.json"),
                @"C:\Users\yusuf\source\repos\Proje\Proje.Service\credentials\google-service-account.json"
            };

            foreach (var path in searchPaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            return searchPaths[0];
        }

        private async Task WriteTransactionToGoogleSheetAsync(Transaction transaction, string transactionType)
        {
            try
            {
                // 1. İşlem onaylandı mı kontrol et
                if (transaction.Status != "Onaylandı")
                {
                    LoggerHelper.LogInformation($"İşlem {transaction.TransactionNo} onaylanmamış ({transaction.Status}). Atlanıyor.");
                    return;
                }

                // 2. İşlem ID kontrolü (bellekte)
                string transactionId = transaction.TransactionId ?? transaction.TransactionNo;
                if (string.IsNullOrEmpty(transactionId))
                {
                    LoggerHelper.LogWarning($"İşlem ID bulunamadı: {transaction.TransactionNo}");
                    return;
                }

                // 3. Daha önce işlenmiş mi?
                if (_processedTransactionIds.Contains(transactionId))
                {
                    LoggerHelper.LogInformation($"İşlem {transactionId} zaten işlenmiş. Atlanıyor.");
                    return;
                }

                // 4. Excel'de zaten var mı?
                if (_existingTransactionIds.Contains(transactionId))
                {
                    LoggerHelper.LogInformation($"İşlem ID {transactionId} Google Sheets'te zaten mevcut. Atlanıyor.");
                    return;
                }

                // 5. Tarihi GG.AA.YYYY formatına çevir
                string transactionDate = "01.01.2001"; // Varsayılan tarih

                // HTML'den Son Onay Tarihini al
                if (!string.IsNullOrEmpty(transaction.LastApprovalDateFormatted))
                {
                    // "20.01.2026 22:00:42" formatından sadece tarihi al
                    var dateParts = transaction.LastApprovalDateFormatted.Split(' ');
                    if (dateParts.Length > 0)
                    {
                        transactionDate = dateParts[0];
                    }
                }

                // 6. Tutarı işlem türüne göre ayarla
                decimal amount = transaction.ResultAmount;
                if (transactionType == "Çekim")
                {
                    amount = -amount;
                }

                // 7. Boş satır bul
                int emptyRow = await FindFirstEmptyRowFromRow16Async();
                _currentRow = emptyRow;

                LoggerHelper.LogInformation($"Onaylanmış işlem yazılıyor: {transaction.TransactionNo}, Satır: {_currentRow}, Tür: {transactionType}");

                // 8. Google Sheets'e yaz
                var range = $"{SheetName}!B{_currentRow}:H{_currentRow}";
                var valueRange = new ValueRange
                {
                    Values = new List<IList<object>>
                    {
                        new List<object>
                        {
                            transactionDate, // B sütunu: İşlem Tarihi (GG.AA.YYYY)
                            transaction.FullName ?? "", // C sütunu: İsim Soyisim
                            transaction.BankName ?? "", // D sütunu: Banka
                            transaction.AccountHolder ?? "", // E sütunu: IBAN Sahibi
                            amount.ToString("N2", CultureInfo.InvariantCulture), // F sütunu: İşlem Tutarı
                            "", // G sütunu: Boş bırakıldı
                            transactionId // H sütunu: İşlem ID (Key)
                        }
                    }
                };

                var updateRequest = _sheetsService.Spreadsheets.Values.Update(valueRange, SpreadsheetId, range);
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

                var response = await updateRequest.ExecuteAsync();

                // 9. Başarılı kayıtları güncelle
                _processedTransactionIds.Add(transactionId);
                _existingTransactionIds.Add(transactionId);
                _currentRow++;
                _processedTransactionCount++;

                LoggerHelper.LogInformation($"✅ İşlem {transactionId} başarıyla yazıldı. Satır: {_currentRow - 1}, Tutar: {amount:N2}");
            }
            catch (Google.GoogleApiException ex) when (ex.Message.Contains("protected cell"))
            {
                LoggerHelper.LogError(ex, $"KORUMALI HÜCRE HATASI! B{_currentRow}:H{_currentRow} aralığı korumalı.");
                throw;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Google Sheets'e yazma hatası");
                throw;
            }
        }

        private async Task<int> FindFirstEmptyRowFromRow16Async()
        {
            try
            {
                if (_sheetsService == null)
                {
                    await InitializeGoogleSheetsAsync();
                }

                // B sütununu 16. satırdan itibaren oku
                var range = $"{SheetName}!B16:B";
                var request = _sheetsService.Spreadsheets.Values.Get(SpreadsheetId, range);
                var response = await request.ExecuteAsync();

                if (response.Values == null)
                {
                    return 16; // Hiç veri yoksa 16. satır boştur
                }

                for (int i = 0; i < response.Values.Count; i++)
                {
                    var row = response.Values[i];
                    if (row.Count == 0 || string.IsNullOrWhiteSpace(row[0].ToString()))
                    {
                        return 16 + i; // Boş satırın gerçek satır numarası
                    }
                }

                // Tüm satırlar doluysa, son satırdan sonraki ilk satır
                return 16 + response.Values.Count;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Boş satır bulma hatası");
                return 16; // Hata durumunda 16. satıra yaz
            }
        }

        public async Task<bool> LoginAsync()
        {
            try
            {
                LoggerHelper.LogInformation("Login işlemi başlatılıyor...");

                // Önce ana sayfaya git
                await _page.GotoAsync("https://online.powerhavale.com/marjin/employee/69",
                    new PageGotoOptions
                    {
                        Timeout = 30000,
                        WaitUntil = WaitUntilState.NetworkIdle,
                        Referer = "https://online.powerhavale.com/"
                    });

                // Login sayfasına git
                await _page.GotoAsync(_credentials.LoginUrl,
                    new PageGotoOptions
                    {
                        Timeout = 30000,
                        WaitUntil = WaitUntilState.NetworkIdle
                    });

                // Email inputunu bul ve doldur
                var emailInput = await _page.WaitForSelectorAsync("input[name='email'], input[type='email']",
                    new PageWaitForSelectorOptions { Timeout = 10000 });
                await emailInput.FillAsync(_credentials.FormUsername);
                await Task.Delay(500);

                // Password inputunu bul ve doldur
                var passwordInput = await _page.WaitForSelectorAsync("input[name='password'], input[type='password']",
                    new PageWaitForSelectorOptions { Timeout = 10000 });
                await passwordInput.FillAsync(_credentials.FormPassword);
                await Task.Delay(500);

                // Submit butonunu bul ve tıkla
                var submitButton = await _page.WaitForSelectorAsync("button[type='submit'], button:has-text('Giriş'), button:has-text('Login')",
                    new PageWaitForSelectorOptions { Timeout = 10000 });
                await submitButton.ClickAsync();

                // Giriş başarısını bekle
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(3000);

                // Başarı kontrolü
                var currentUrl = _page.Url;
                var content = await _page.ContentAsync();

                bool isLoginSuccess = !content.Contains("401 Authorization Required") &&
                                     !content.Contains("Hatalı") &&
                                     !content.Contains("Yanlış") &&
                                     !currentUrl.Contains("login") &&
                                     !currentUrl.Contains("auth") &&
                                     (content.Contains("Dashboard") || content.Contains("Genel Bakış") || content.Contains("İşlem Geçmişi"));

                if (isLoginSuccess)
                {
                    LoggerHelper.LogInformation($"✅ Giriş başarılı! Yönlendirilen sayfa: {currentUrl}");
                    return true;
                }

                LoggerHelper.LogWarning("Giriş başarısız!");
                return false;
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

                // Yan menüdeki "İşlem Geçmişi" linkini bul
                var historyLink = await _page.WaitForSelectorAsync(
                    "a[href*='transaction-history'], " +
                    "a:has-text('İşlem Geçmişi'), " +
                    "a:has-text('Transaction History')",
                    new PageWaitForSelectorOptions { Timeout = 10000 });

                if (historyLink != null)
                {
                    await historyLink.ClickAsync();
                    await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await Task.Delay(3000);

                    // Sayfa başlığını kontrol et
                    var pageTitle = await _page.TitleAsync();
                    var content = await _page.ContentAsync();

                    if (pageTitle.Contains("İşlem Geçmişi") ||
                        content.Contains("İşlem Geçmişi") ||
                        content.Contains("transaction-history"))
                    {
                        LoggerHelper.LogInformation("✅ İşlem Geçmişi sayfasına ulaşıldı!");
                        await Task.Delay(2000);
                        return true;
                    }
                }

                // Alternatif: URL'den direkt git
                await _page.GotoAsync("https://online.powerhavale.com/marjin/transaction-history",
                    new PageGotoOptions
                    {
                        Timeout = 30000,
                        WaitUntil = WaitUntilState.NetworkIdle
                    });

                await Task.Delay(3000);
                return true;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "İşlem Geçmişi sayfasına yönlendirme hatası");
                return false;
            }
        }

        private async Task ApplyFiltersAsync(string status = "Onaylandı", string transactionType = "Yatırım")
        {
            try
            {
                LoggerHelper.LogInformation($"Filtreler uygulanıyor: Durum={status}, İşlem Türü={transactionType}");

                // Filtre butonlarının yüklenmesini bekle
                await Task.Delay(2000);

                // 1. DURUM FİLTRESİNİ BUL VE UYGULA
                await ApplyStatusFilterAsync(status);
                await Task.Delay(1000);

                // 2. İŞLEM TÜRÜ FİLTRESİNİ BUL VE UYGULA
                await ApplyTransactionTypeFilterAsync(transactionType);
                await Task.Delay(1000);

                // 3. ARA BUTONUNU BUL VE TIKLA
                await ClickSearchButtonAsync();

                LoggerHelper.LogInformation($"✅ Filtreler başarıyla uygulandı: {status}, {transactionType}");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Filtre uygulama hatası");
                throw;
            }
        }

        private async Task ApplyStatusFilterAsync(string status)
        {
            try
            {
                // Tüm combobox butonlarını bul
                var comboboxes = await _page.QuerySelectorAllAsync(
                    "button[role='combobox'][data-slot='select-trigger'], " +
                    "button[role='combobox']");

                foreach (var combobox in comboboxes)
                {
                    try
                    {
                        // Combobox içindeki metni kontrol et
                        var span = await combobox.QuerySelectorAsync("span[data-slot='select-value']");
                        if (span != null)
                        {
                            var currentText = await span.InnerTextAsync();

                            // Eğer bu combobox'ın metni "Tümü", "Onaylandı", "Beklemede", "Reddedildi" vs ise bu durum combobox'ıdır
                            if (currentText == "Tümü" ||
                                currentText == "Onaylandı" ||
                                currentText == "Beklemede" ||
                                currentText == "Reddedildi")
                            {
                                LoggerHelper.LogInformation($"Durum combobox'ı bulundu: {currentText} -> {status} olarak değiştiriliyor");

                                await combobox.ClickAsync();
                                await Task.Delay(800);

                                // Option listesini bekle
                                await _page.WaitForSelectorAsync("[role='option'], [data-radix-select-viewport]",
                                    new PageWaitForSelectorOptions { Timeout = 3000 });
                                await Task.Delay(500);

                                // İstenen option'ı bul ve tıkla
                                var option = await _page.QuerySelectorAsync(
                                    $"[role='option']:has-text('{status}'), " +
                                    $"[data-radix-select-viewport] :text('{status}')");

                                if (option != null)
                                {
                                    await option.ClickAsync();
                                    LoggerHelper.LogInformation($"Durum '{status}' olarak ayarlandı.");
                                }
                                else
                                {
                                    LoggerHelper.LogWarning($"'{status}' seçeneği bulunamadı!");
                                    await _page.Keyboard.PressAsync("Escape");
                                }

                                await Task.Delay(500);
                                return; // İlk bulduğumuz durum combobox'ı ile işlem yap
                            }
                        }
                    }
                    catch { continue; }
                }

                LoggerHelper.LogWarning("Durum filtresi combobox'ı bulunamadı!");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Durum filtresi ayarlama hatası");
                throw;
            }
        }

        private async Task ApplyTransactionTypeFilterAsync(string transactionType)
        {
            try
            {
                // Tüm combobox butonlarını bul
                var comboboxes = await _page.QuerySelectorAllAsync(
                    "button[role='combobox'][data-slot='select-trigger'], " +
                    "button[role='combobox']");

                foreach (var combobox in comboboxes)
                {
                    try
                    {
                        // Combobox içindeki metni kontrol et
                        var span = await combobox.QuerySelectorAsync("span[data-slot='select-value']");
                        if (span != null)
                        {
                            var currentText = await span.InnerTextAsync();

                            // Eğer bu combobox'ın metni "Yatırım", "Çekim" ise bu işlem türü combobox'ıdır
                            if (currentText == "Yatırım" ||
                                currentText == "Çekim" ||
                                currentText.Contains("Yatırım") ||
                                currentText.Contains("Çekim"))
                            {
                                LoggerHelper.LogInformation($"İşlem türü combobox'ı bulundu: {currentText} -> {transactionType} olarak değiştiriliyor");

                                await combobox.ClickAsync();
                                await Task.Delay(800);

                                // Option listesini bekle
                                await _page.WaitForSelectorAsync("[role='option'], [data-radix-select-viewport]",
                                    new PageWaitForSelectorOptions { Timeout = 3000 });
                                await Task.Delay(500);

                                // İstenen option'ı bul ve tıkla
                                var option = await _page.QuerySelectorAsync(
                                    $"[role='option']:has-text('{transactionType}'), " +
                                    $"[data-radix-select-viewport] :text('{transactionType}')");

                                if (option != null)
                                {
                                    await option.ClickAsync();
                                    LoggerHelper.LogInformation($"İşlem türü '{transactionType}' olarak ayarlandı.");
                                }
                                else
                                {
                                    LoggerHelper.LogWarning($"'{transactionType}' seçeneği bulunamadı!");
                                    await _page.Keyboard.PressAsync("Escape");
                                }

                                await Task.Delay(500);
                                return; // İlk bulduğumuz işlem türü combobox'ı ile işlem yap
                            }
                        }
                    }
                    catch { continue; }
                }

                LoggerHelper.LogWarning("İşlem türü filtresi combobox'ı bulunamadı!");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "İşlem türü filtresi ayarlama hatası");
                throw;
            }
        }

        private async Task ClickSearchButtonAsync()
        {
            try
            {
                // Ara butonunu bul (funnel icon'lu buton)
                var searchButton = await _page.WaitForSelectorAsync(
                    "button:has(svg.lucide-funnel), " +
                    "button:has-text('Ara'), " +
                    "[data-slot='button']:has(svg.lucide-funnel)",
                    new PageWaitForSelectorOptions { Timeout = 5000 });

                if (searchButton != null && await searchButton.IsVisibleAsync())
                {
                    LoggerHelper.LogInformation("Ara butonuna tıklanıyor...");
                    await searchButton.ClickAsync();

                    // Filtrelerin uygulanmasını bekle
                    await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await Task.Delay(3000);

                    // Tablonun güncellendiğini kontrol et
                    var tableRows = await _page.QuerySelectorAllAsync("tbody tr[data-slot='table-row']");
                    LoggerHelper.LogInformation($"✅ Filtreler uygulandı. {tableRows.Count} satır bulundu.");
                }
                else
                {
                    LoggerHelper.LogWarning("Ara butonu bulunamadı! Enter tuşunu deneyelim...");
                    await _page.Keyboard.PressAsync("Enter");
                    await Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Ara butonuna tıklama hatası");
            }
        }

        public async Task<List<Transaction>> ExtractTransactionsWithFilterAsync(
    string status = "Onaylandı",
    string transactionType = "Yatırım",
    bool autoPaginate = false,
    bool onlyNew = false)
        {
            var allTransactions = new List<Transaction>();
            int currentPage = 1;
            int maxPages = autoPaginate ? 100 : 1;

            try
            {
                LoggerHelper.LogInformation($"Filtreli işlem çekme başlatılıyor: {status}, {transactionType}, Sadece Yeni: {onlyNew}");

                // 1. Önce sayfanın tamamen yüklendiğinden emin ol
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(2000);

                // 2. Filtreleri uygula
                await ApplyFiltersAsync(status, transactionType);
                await Task.Delay(2000);

                while (currentPage <= maxPages)
                {
                    try
                    {
                        LoggerHelper.LogInformation($"Sayfa {currentPage} işleniyor...");

                        // 3. Tablonun yüklenmesini bekle
                        await WaitForTableToLoadAsync();
                        await Task.Delay(1000);

                        // 4. Tablodaki satırları al
                        var rows = await _page.QuerySelectorAllAsync(
                            "tbody tr[data-slot='table-row'], " +
                            "tbody tr");

                        if (rows.Count == 0)
                        {
                            LoggerHelper.LogInformation("Sayfada işlem bulunamadı.");
                            break;
                        }

                        LoggerHelper.LogInformation($"{rows.Count} adet satır bulundu.");

                        // 5. Her satırı işle
                        int newTransactions = 0;
                        foreach (var row in rows)
                        {
                            try
                            {
                                var transaction = await ExtractTransactionFromRowAsync(row);
                                if (transaction != null && transaction.Status == status)
                                {
                                    // Transaction ID oluştur
                                    string transactionId = transaction.TransactionId ?? transaction.TransactionNo;

                                    // onlyNew=true ise sadece daha önce işlenmemiş olanları al
                                    if (onlyNew)
                                    {
                                        if (_processedTransactionIds.Contains(transactionId) ||
                                            _existingTransactionIds.Contains(transactionId))
                                        {
                                            LoggerHelper.LogInformation($"İşlem {transactionId} zaten işlenmiş, atlanıyor.");
                                            continue;
                                        }
                                    }
                                    else
                                    {
                                        // onlyNew=false ise sadece bu oturumda işlenmemiş olanları kontrol et
                                        if (_processedTransactionIds.Contains(transactionId))
                                        {
                                            LoggerHelper.LogInformation($"İşlem {transactionId} bu oturumda işlenmiş, atlanıyor.");
                                            continue;
                                        }
                                    }

                                    // Modal detayları al (eğer buton varsa)
                                    var detailButton = await row.QuerySelectorAsync(
                                        "button[data-slot='sheet-trigger'], " +
                                        "button:has-text('Detaylı Görüntüle'), " +
                                        "td:nth-child(7) button");

                                    if (detailButton != null && await detailButton.IsVisibleAsync())
                                    {
                                        await detailButton.ClickAsync();
                                        await Task.Delay(2000);
                                        await ExtractModalDetailsAsync(transaction);
                                        await CloseModalAsync();
                                    }

                                    // Google Sheets'e yaz
                                    await WriteTransactionToGoogleSheetAsync(transaction, transactionType);
                                    allTransactions.Add(transaction);
                                    newTransactions++;
                                    LoggerHelper.LogInformation($"✅ YENİ İşlem eklendi: {transactionId}");
                                }
                            }
                            catch (Exception ex)
                            {
                                LoggerHelper.LogError(ex, "Satır işleme hatası");
                            }
                        }

                        LoggerHelper.LogInformation($"Sayfa {currentPage}: {newTransactions} yeni işlem eklendi.");

                        // 6. Sayfalar arası geçiş yapılacak mı?
                        if (autoPaginate && currentPage < maxPages)
                        {
                            if (!await NavigateToNextPageAsync(currentPage))
                            {
                                LoggerHelper.LogInformation("Son sayfaya ulaşıldı veya sayfa geçişi başarısız.");
                                break;
                            }
                            currentPage++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.LogError(ex, $"Sayfa {currentPage} işleme hatası");
                        break;
                    }
                }

                LoggerHelper.LogInformation($"✅ {allTransactions.Count} adet YENİ işlem başarıyla çekildi! {currentPage} sayfa işlendi.");
                return allTransactions;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Filtreli işlem çekme hatası");
                return allTransactions;
            }
        }

        public async Task<List<Transaction>> ExtractTransactionsAsync(int pageCount = 10)
        {
            return await ExtractTransactionsWithFilterAsync("Onaylandı", "Yatırım", false);
        }

        private async Task<bool> NavigateToNextPageAsync(int currentPage)
        {
            try
            {
                LoggerHelper.LogInformation($"Sonraki sayfaya geçiliyor... (Mevcut: {currentPage})");

                // HTML'deki pagination yapısını bul
                var pagination = await _page.QuerySelectorAsync(
                    "nav[role='navigation'][aria-label='pagination'], " +
                    "[data-slot='pagination']");

                if (pagination == null)
                {
                    LoggerHelper.LogWarning("Pagination yapısı bulunamadı.");
                    return false;
                }

                // "Sonraki" butonunu bul
                var nextButton = await pagination.QuerySelectorAsync(
                    "a:has-text('Sonraki'), " +
                    "button:has-text('Sonraki'), " +
                    "[data-slot='pagination-link']:has-text('Sonraki'), " +
                    "[aria-label*='next'], " +
                    "[aria-label*='Next']");

                if (nextButton != null && await nextButton.IsVisibleAsync())
                {
                    // Butonun disabled olup olmadığını kontrol et
                    var isDisabled = await nextButton.EvaluateAsync<bool>(@"
                        element => {
                            if (element.disabled) return true;
                            if (element.getAttribute('disabled') !== null) return true;
                            if (element.getAttribute('aria-disabled') === 'true') return true;
                            if (element.classList.contains('disabled')) return true;
                            if (element.classList.contains('pointer-events-none')) return true;
                            if (window.getComputedStyle(element).pointerEvents === 'none') return true;
                            if (window.getComputedStyle(element).opacity === '0.5') return true;
                            return false;
                        }
                    ");

                    if (!isDisabled)
                    {
                        LoggerHelper.LogInformation("Sonraki sayfa butonu bulundu, tıklanıyor...");
                        await nextButton.ClickAsync();

                        // Yeni sayfanın yüklenmesini bekle
                        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                        await Task.Delay(3000);

                        // Tablonun yeni verilerle dolmasını bekle
                        await WaitForTableToLoadAsync();

                        LoggerHelper.LogInformation("✅ Sayfa başarıyla değişti.");
                        return true;
                    }
                    else
                    {
                        LoggerHelper.LogInformation("Sonraki sayfa butonu devre dışı (muhtemelen son sayfadayız).");
                        return false;
                    }
                }
                else
                {
                    LoggerHelper.LogWarning("Sonraki sayfa butonu bulunamadı veya görünür değil.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Sonraki sayfaya geçme hatası");
                return false;
            }
        }

        private async Task WaitForTableToLoadAsync(int timeoutSeconds = 10)
        {
            try
            {
                LoggerHelper.LogInformation("Tablo yüklenmesi bekleniyor...");

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
                {
                    // Tablo body'sini kontrol et
                    var tbody = await _page.QuerySelectorAsync(
                        "tbody[data-slot='table-body'], " +
                        "tbody");

                    if (tbody != null)
                    {
                        // Satırları kontrol et
                        var rows = await tbody.QuerySelectorAllAsync(
                            "tr[data-slot='table-row'], " +
                            "tr");

                        if (rows.Count > 0)
                        {
                            LoggerHelper.LogInformation($"✅ Tablo yüklendi. {rows.Count} satır bulundu.");
                            return;
                        }
                    }

                    await Task.Delay(500);
                }

                LoggerHelper.LogWarning($"Tablo yüklenmesi {timeoutSeconds} saniye içinde tamamlanamadı.");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Tablo yüklenmesi beklenirken hata");
            }
        }

        public async Task ProcessTransactionsCycleAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    LoggerHelper.LogInformation("=== YENİ İŞLEM DÖNGÜSÜ BAŞLATILIYOR ===");

                    // İşlenen işlem listesini temizle (isteğe bağlı)
                    _processedTransactionCount = 0;

                    // 1. Yatırım işlemlerini işle
                    LoggerHelper.LogInformation("=== YATIRIM İŞLEMLERİ İŞLENİYOR ===");
                    var yatirimTransactions = await ExtractTransactionsWithFilterAsync(
                        status: "Onaylandı",
                        transactionType: "Yatırım",
                        autoPaginate: true);

                    LoggerHelper.LogInformation($"{yatirimTransactions.Count} adet Yatırım işlemi işlendi.");

                    // 2. Çekim işlemlerini işle
                    LoggerHelper.LogInformation("=== ÇEKİM İŞLEMLERİ İŞLENİYOR ===");
                    var cekimTransactions = await ExtractTransactionsWithFilterAsync(
                        status: "Onaylandı",
                        transactionType: "Çekim",
                        autoPaginate: true);

                    LoggerHelper.LogInformation($"{cekimTransactions.Count} adet Çekim işlemi işlendi.");

                    // 3. Toplam işlem sayısını logla
                    int totalTransactions = yatirimTransactions.Count + cekimTransactions.Count;
                    LoggerHelper.LogInformation($"=== TOPLAM İŞLENEN İŞLEM: {totalTransactions} ===");

                    // 4. İşlem sayısını sıfırla (bir sonraki döngü için)
                    _processedTransactionCount = 0;

                    // 5. 5 dakika bekle
                    LoggerHelper.LogInformation("5 dakika bekleniyor...");
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                }
                catch (Exception ex) when (!(ex is TaskCanceledException))
                {
                    LoggerHelper.LogError(ex, "İşlem döngüsünde hata. 1 dakika sonra tekrar denenecek.");
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                }
            }
        }

        public void StartContinuousProcessing()
        {
            _processingCts = new CancellationTokenSource();
            var processingTask = ProcessTransactionsCycleAsync(_processingCts.Token);
            LoggerHelper.LogInformation("Sürekli işlem döngüsü başlatıldı.");
        }

        public void StopContinuousProcessing()
        {
            _processingCts?.Cancel();
            LoggerHelper.LogInformation("Sürekli işlem döngüsü durduruldu.");
        }

        private async Task<Transaction> ExtractTransactionFromRowAsync(IElementHandle row)
        {
            try
            {
                var transaction = new Transaction();

                // 1. İşlem No (3 buton: yeşil, turuncu, mavi)
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

                // 3. Tutar Bilgileri
                var amountElements = await row.QuerySelectorAllAsync("td:nth-child(3) button");
                if (amountElements.Count >= 2)
                {
                    var requestedAmountText = await ExtractTextFromButtonAsync(amountElements[0]);
                    transaction.RequestedAmount = ParseAmount(requestedAmountText);

                    var resultAmountText = await ExtractTextFromButtonAsync(amountElements[1]);
                    transaction.ResultAmount = ParseAmount(resultAmountText);
                }

                // 4. Personel Bilgileri
                var employeeCell = await row.QuerySelectorAsync("td:nth-child(4)");
                if (employeeCell != null)
                {
                    var employeeText = await employeeCell.InnerTextAsync();
                    if (!string.IsNullOrWhiteSpace(employeeText))
                    {
                        var lines = employeeText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        transaction.EmployeeName = lines.Length > 0 ? lines[0].Trim() : "";
                        transaction.EmployeeRole = lines.Length > 1 ? lines[1].Trim() : "";
                    }
                }

                // 5. Durum
                var statusCell = await row.QuerySelectorAsync("td:nth-child(5)");
                if (statusCell != null)
                {
                    var statusBadge = await statusCell.QuerySelectorAsync(
                        "[data-slot='badge'], " +
                        ".badge, " +
                        "span.badge");

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
                var textElement = await button.QuerySelectorAsync("p, span, div");
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
                    .Replace(".", "")
                    .Trim();

                if (cleanText.Contains(","))
                {
                    cleanText = cleanText.Replace(",", ".");
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
                        transaction.LastApprovalDate = ParseTurkishDateTime(dateStr);
                    }
                    else if (line.Contains("Güncelleme:"))
                    {
                        // İsteğe bağlı: güncelleme tarihini kaydet
                    }
                    else if (line.Contains("Reddedildi:"))
                    {
                        var dateStr = line.Replace("Reddedildi:", "").Trim();
                        if (dateStr != "-")
                        {
                            transaction.LastRejectionDate = ParseTurkishDateTime(dateStr);
                        }
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
                var formats = new[] { "dd/MM/yyyy HH:mm:ss", "dd.MM.yyyy HH:mm:ss", "dd/MM/yyyy", "dd.MM.yyyy" };
                return DateTime.ParseExact(dateTimeStr.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None);
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
                var modal = await _page.QuerySelectorAsync(
                    "[role='dialog'], " +
                    "[data-slot='sheet-content'], " +
                    ".modal, " +
                    ".dialog");

                if (modal == null)
                {
                    LoggerHelper.LogWarning("Modal bulunamadı!");
                    return;
                }

                // Modal içeriğini HTML olarak al
                var modalHtml = await modal.InnerHTMLAsync();

                // HTML'den gerekli verileri parse et
                ParseModalHtml(modalHtml, transaction);

                LoggerHelper.LogInformation($"{transaction.TransactionNo} modal detayları alındı.");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Modal detay alma hatası");
            }
        }

        private void ParseModalHtml(string modalHtml, Transaction transaction)
        {
            try
            {
                // 1. İşlem ID (H sütunu)
                var transactionIdMatch = System.Text.RegularExpressions.Regex.Match(
                    modalHtml,
                    @"İşlem ID.*?font-medium text-primary text-xs.*?<div>([^<]+)</div>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                if (transactionIdMatch.Success)
                {
                    transaction.TransactionId = transactionIdMatch.Groups[1].Value.Trim();
                }

                // 2. İsim Soyisim (C sütunu)
                var fullNameMatch = System.Text.RegularExpressions.Regex.Match(
                    modalHtml,
                    @"İsim Soyisim.*?font-medium text-primary text-xs.*?<div>([^<]+)</div>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                if (fullNameMatch.Success)
                {
                    transaction.FullName = fullNameMatch.Groups[1].Value.Trim();
                }

                // 3. Banka (D sütunu) - img'den sonraki div'deki metin
                var bankMatch = System.Text.RegularExpressions.Regex.Match(
                    modalHtml,
                    @"<img[^>]+>.*?<div class=""flex flex-col gap-1 text-right mr-8"">.*?<div></div>.*?<div>([^<]+)</div>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                if (bankMatch.Success)
                {
                    transaction.BankName = bankMatch.Groups[1].Value.Trim();
                }

                // 4. IBAN Sahibi (E sütunu)
                var ibanHolderMatch = System.Text.RegularExpressions.Regex.Match(
                    modalHtml,
                    @"IBAN Sahibi.*?font-medium text-primary text-xs.*?<div>([^<]+)</div>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                if (ibanHolderMatch.Success)
                {
                    transaction.AccountHolder = ibanHolderMatch.Groups[1].Value.Trim();
                }

                // 5. Sonuç Tutarı (F sütunu)
                var amountMatch = System.Text.RegularExpressions.Regex.Match(
                    modalHtml,
                    @"Sonuç Tutarı.*?font-medium text-primary text-xs.*?<div>([^<]+)</div>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                if (amountMatch.Success)
                {
                    var amountText = amountMatch.Groups[1].Value.Trim();
                    if (decimal.TryParse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal resultAmount))
                    {
                        transaction.ResultAmount = resultAmount;
                    }
                }

                // 6. Son Onay Tarihi (B sütunu) - GG.AA.YYYY formatında
                var lastApprovalMatch = System.Text.RegularExpressions.Regex.Match(
                    modalHtml,
                    @"Son Onay Tarihi.*?font-medium text-primary text-xs.*?<div>([^<]+)</div>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                if (lastApprovalMatch.Success)
                {
                    transaction.LastApprovalDateFormatted = lastApprovalMatch.Groups[1].Value.Trim();
                }

                // 7. IBAN
                var ibanMatch = System.Text.RegularExpressions.Regex.Match(modalHtml, @"TR\d{24}");
                if (ibanMatch.Success)
                {
                    transaction.IBAN = ibanMatch.Value;
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Modal HTML parse hatası");
            }
        }

        private async Task CloseModalAsync()
        {
            try
            {
                var closeButton = await _page.QuerySelectorAsync(
                    "button[aria-label='Close'], " +
                    "button[data-slot='close-button'], " +
                    ".close-button, " +
                    "[class*='close'], " +
                    "button:has(svg[aria-label='Close']), " +
                    "button:has-text('×'), " +
                    "button:has(svg.lucide-x)");

                if (closeButton != null && await closeButton.IsVisibleAsync())
                {
                    await closeButton.ClickAsync();
                }
                else
                {
                    await _page.Keyboard.PressAsync("Escape");
                }

                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Modal kapatma hatası");
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                LoggerHelper.LogInformation("Bağlantı testi başlatılıyor...");

                // 1. İnternet bağlantısı testi
                bool internetConnected = false;

                using (var ping = new Ping())
                {
                    try
                    {
                        var reply = await ping.SendPingAsync("8.8.8.8", 3000);
                        internetConnected = reply.Status == IPStatus.Success;
                    }
                    catch { }
                }

                if (!internetConnected)
                {
                    using (var ping = new Ping())
                    {
                        try
                        {
                            var reply = await ping.SendPingAsync("1.1.1.1", 3000);
                            internetConnected = reply.Status == IPStatus.Success;
                        }
                        catch { }
                    }
                }

                if (!internetConnected)
                {
                    LoggerHelper.LogWarning("İnternet bağlantısı başarısız!");
                    return false;
                }

                LoggerHelper.LogInformation("✓ İnternet bağlantısı başarılı");

                // 2. Hedef siteye bağlantı testi
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var authToken = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{_credentials.BasicAuthUsername}:{_credentials.BasicAuthPassword}"));

                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);

                try
                {
                    var testResponse = await httpClient.GetAsync(_credentials.LoginUrl);
                    var statusCode = (int)testResponse.StatusCode;

                    if (statusCode == 200 || statusCode == 401 || statusCode == 403)
                    {
                        LoggerHelper.LogInformation($"✓ Hedef siteye bağlantı başarılı. HTTP Durumu: {statusCode}");
                        return true;
                    }
                    else
                    {
                        LoggerHelper.LogWarning($"Hedef siteye bağlantı var ama HTTP Durumu: {statusCode}");
                        return false;
                    }
                }
                catch (HttpRequestException ex)
                {
                    LoggerHelper.LogError(ex, "Hedef siteye bağlantı başarısız");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Genel bağlantı testi hatası");
                return false;
            }
        }

        public async Task<bool> IsLoggedInAsync()
        {
            try
            {
                var currentUrl = _page.Url;
                var content = await _page.ContentAsync();

                return !currentUrl.Contains("login") &&
                       !currentUrl.Contains("auth") &&
                       !content.Contains("Giriş Yap") &&
                       !content.Contains("401") &&
                       !content.Contains("403");
            }
            catch
            {
                return false;
            }
        }

        public async Task<Transaction> GetTransactionDetailsAsync(string transactionId)
        {
            try
            {
                LoggerHelper.LogInformation($"İşlem detayları alınıyor: {transactionId}");

                // İşlem Geçmişi sayfasına git
                if (!await NavigateToTransactionHistoryAsync())
                {
                    LoggerHelper.LogWarning("İşlem Geçmişi sayfasına ulaşılamadı.");
                    return null;
                }

                // Arama alanına işlem ID'sini yaz
                var searchInput = await _page.QuerySelectorAsync("input[placeholder='Ara...']");
                if (searchInput != null)
                {
                    await searchInput.FillAsync(transactionId);
                    await Task.Delay(1000);

                    // Ara butonuna tıkla
                    await ClickSearchButtonAsync();
                }

                // Tablodaki satırları kontrol et
                var rows = await _page.QuerySelectorAllAsync("tbody tr[data-slot='table-row']");
                foreach (var row in rows)
                {
                    var transactionNoElements = await row.QuerySelectorAllAsync("td:nth-child(1) button");
                    if (transactionNoElements.Count > 0)
                    {
                        var currentTransactionId = await ExtractTextFromButtonAsync(transactionNoElements[0]);
                        if (currentTransactionId == transactionId)
                        {
                            var transaction = await ExtractTransactionFromRowAsync(row);
                            if (transaction != null && transaction.Status == "Onaylandı")
                            {
                                // Modal detayları al
                                var detailButton = await row.QuerySelectorAsync("td:nth-child(7) button[data-slot='sheet-trigger']");
                                if (detailButton != null)
                                {
                                    await detailButton.ClickAsync();
                                    await Task.Delay(1500);
                                    await ExtractModalDetailsAsync(transaction);
                                    await CloseModalAsync();
                                }
                                return transaction;
                            }
                        }
                    }
                }

                LoggerHelper.LogWarning($"İşlem bulunamadı: {transactionId}");
                return null;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, $"İşlem detayları alma hatası: {transactionId}");
                return null;
            }
        }

        public async Task<bool> TestPaginationAsync()
        {
            try
            {
                LoggerHelper.LogInformation("=== PAGINATION TESTİ BAŞLATILIYOR ===");

                // İşlem Geçmişi sayfasına git
                if (!await NavigateToTransactionHistoryAsync())
                    return false;

                // Filtreleri uygula
                await ApplyFiltersAsync("Onaylandı", "Yatırım");

                // 1. Sayfadaki satırları say
                var page1Rows = await _page.QuerySelectorAllAsync("tbody tr[data-slot='table-row']");
                LoggerHelper.LogInformation($"1. sayfada {page1Rows.Count} satır bulundu.");

                if (page1Rows.Count == 0)
                {
                    LoggerHelper.LogWarning("1. sayfada hiç satır yok!");
                    return false;
                }

                // İlk satırın içeriğini kaydet
                var firstRowPage1 = await page1Rows[0].InnerHTMLAsync();

                // 2. Sonraki sayfaya gitmeyi dene
                bool canNavigate = await NavigateToNextPageAsync(1);

                if (canNavigate)
                {
                    // 2. Sayfadaki satırları say
                    var page2Rows = await _page.QuerySelectorAllAsync("tbody tr[data-slot='table-row']");
                    LoggerHelper.LogInformation($"2. sayfada {page2Rows.Count} satır bulundu.");

                    if (page2Rows.Count > 0)
                    {
                        // İlk satırların içeriğini karşılaştır
                        var firstRowPage2 = await page2Rows[0].InnerHTMLAsync();

                        if (firstRowPage1 != firstRowPage2)
                        {
                            LoggerHelper.LogInformation("✅ PAGINATION ÇALIŞIYOR - Sayfalar farklı içeriğe sahip");
                            return true;
                        }
                        else
                        {
                            LoggerHelper.LogWarning("⚠️ PAGINATION ÇALIŞMIYOR - Sayfalar aynı içeriğe sahip");
                            return false;
                        }
                    }
                    else
                    {
                        LoggerHelper.LogWarning("2. sayfada hiç satır yok!");
                        return false;
                    }
                }
                else
                {
                    LoggerHelper.LogWarning("⚠️ PAGINATION ÇALIŞMIYOR - Sonraki sayfaya geçilemedi");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Pagination testi hatası");
                return false;
            }
        }

        public async Task<bool> ResetToDefaultViewAsync()
        {
            try
            {
                LoggerHelper.LogInformation("Sayfa varsayılan duruma getiriliyor...");

                // 1. Sayfayı yenile
                await _page.ReloadAsync(new PageReloadOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 15000
                });
                await Task.Delay(2000);

                // 2. Tüm açık modal veya açılır pencereleri kapat
                await CloseAllModalsAsync();

                // 3. Filtreleri temizle
                await ClearTransactionFiltersAsync();

                LoggerHelper.LogInformation("✅ Sayfa varsayılan duruma getirildi.");
                return true;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Sayfayı varsayılan duruma getirme hatası");
                return false;
            }
        }

        public async Task<bool> ClearTransactionFiltersAsync()
        {
            try
            {
                LoggerHelper.LogInformation("İşlem filtreleri temizleniyor...");

                // 1. Önce filtre bölümünün yüklenmesini bekle
                await Task.Delay(1000);

                // 2. Tüm combobox'ları bul ve "Tümü" yap
                var comboboxes = await _page.QuerySelectorAllAsync(
                    "button[role='combobox'][data-slot='select-trigger'], " +
                    "button[role='combobox']");

                LoggerHelper.LogInformation($"{comboboxes.Count} adet combobox bulundu.");

                foreach (var combobox in comboboxes)
                {
                    try
                    {
                        // Combobox'ın görünür olup olmadığını kontrol et
                        if (!await combobox.IsVisibleAsync())
                            continue;

                        await combobox.ClickAsync();
                        await Task.Delay(500);

                        // "Tümü" seçeneğini ara
                        var tumuOption = await _page.QuerySelectorAsync(
                            "[role='option']:has-text('Tümü'), " +
                            "[role='option']:has-text('Tüm Durumlar'), " +
                            "[role='option']:has-text('Hepsi'), " +
                            "[data-radix-select-viewport] :text('Tümü'), " +
                            "[data-radix-select-viewport] :text('Tüm Durumlar'), " +
                            "[data-radix-select-viewport] :text('Hepsi')");

                        if (tumuOption != null)
                        {
                            await tumuOption.ClickAsync();
                            LoggerHelper.LogInformation("Combobox 'Tümü' olarak ayarlandı.");
                        }
                        else
                        {
                            // "Tümü" bulunamazsa, ilk seçeneği seç veya Escape tuşuna bas
                            var firstOption = await _page.QuerySelectorAsync("[role='option']:first-child");
                            if (firstOption != null)
                            {
                                await firstOption.ClickAsync();
                            }
                            else
                            {
                                await _page.Keyboard.PressAsync("Escape");
                            }
                        }

                        await Task.Delay(300);
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.LogError(ex, "Combobox temizleme hatası");
                        // Hata durumunda Escape tuşuna bas
                        await _page.Keyboard.PressAsync("Escape");
                    }
                }

                // 3. Tarih filtrelerini temizle (eğer varsa)
                await ClearDateFiltersAsync();

                // 4. Arama inputlarını temizle
                var searchInputs = await _page.QuerySelectorAllAsync(
                    "input[placeholder*='Ara'], " +
                    "input[type='search'], " +
                    "input[placeholder*='Search']");

                foreach (var input in searchInputs)
                {
                    try
                    {
                        if (await input.IsVisibleAsync())
                        {
                            await input.FillAsync("");
                            await Task.Delay(200);
                        }
                    }
                    catch { }
                }

                // 5. Ara butonuna tıkla veya Enter tuşuna bas
                await ClickSearchButtonAsync();

                // 6. Filtrelerin temizlenmesini bekle
                await Task.Delay(1500);

                LoggerHelper.LogInformation("✅ İşlem filtreleri başarıyla temizlendi.");
                return true;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Filtre temizleme hatası");
                return false;
            }
        }

        private async Task CloseAllModalsAsync()
        {
            try
            {
                // Escape tuşuna basarak açık modal varsa kapat
                await _page.Keyboard.PressAsync("Escape");
                await Task.Delay(500);

                // Close butonlarını kontrol et
                var closeButtons = await _page.QuerySelectorAllAsync(
                    "button[aria-label='Close'], " +
                    "button[data-slot='close-button'], " +
                    ".close-button, " +
                    "button:has(svg.lucide-x)");

                foreach (var closeButton in closeButtons)
                {
                    try
                    {
                        if (await closeButton.IsVisibleAsync())
                        {
                            await closeButton.ClickAsync();
                            await Task.Delay(300);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private async Task ClearDateFiltersAsync()
        {
            try
            {
                // Tarih inputlarını bul
                var dateInputs = await _page.QuerySelectorAllAsync(
                    "input[type='date'], " +
                    "input[placeholder*='Tarih'], " +
                    "input[placeholder*='Date']");

                foreach (var input in dateInputs)
                {
                    try
                    {
                        if (await input.IsVisibleAsync())
                        {
                            await input.FillAsync("");
                            await Task.Delay(200);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
        public async Task<bool> ResetProcessedTransactionIdsAsync()
        {
            try
            {
                int count = _processedTransactionIds.Count;
                _processedTransactionIds.Clear();
                LoggerHelper.LogInformation($"{count} adet işlenmiş işlem ID'si temizlendi.");
                return true;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "İşlem ID'leri temizleme hatası");
                return false;
            }
        }
        public void Dispose()
        {
            try
            {
                StopContinuousProcessing();
                _page?.CloseAsync();
                _browser?.CloseAsync();
                _playwright?.Dispose();
                LoggerHelper.LogInformation("WebAutomationService başarıyla dispose edildi.");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Dispose sırasında hata");
            }
        }
    }
}