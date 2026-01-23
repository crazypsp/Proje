using Proje.Core.Helpers;
using Proje.Core.Interfaces;
using Proje.Entities.Entities;
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
using Proje.Models;
using System.Text.RegularExpressions;

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
        private DateTime? _lastDepositDate = null;
        private DateTime? _lastWithdrawalDate = null;
        private DateTime? _initialSelectedDate = null;
        private string _initialSortOrder = "Eskiden Yeniye";
        private bool _isLoggedIn = false;
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

                LoggerHelper.LogInformation("Google Sheets servisi başarıyla başlatıldı.");
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

                // I sütunundaki tüm ID'leri oku (16. satırdan itibaren)
                var range = $"{SheetName}!I16:I";
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
                string transactionDateFormatted = "01.01.2001";
                string transactionDateTimeFull = "01.01.2001 00:00:00";

                // HTML'den Son Onay Tarihini al
                if (!string.IsNullOrEmpty(transaction.LastApprovalDateFormatted))
                {
                    var dateParts = transaction.LastApprovalDateFormatted.Split(' ');
                    if (dateParts.Length > 0)
                    {
                        transactionDateFormatted = dateParts[0]; // GG.AA.YYYY (B sütunu)
                        transactionDateTimeFull = transaction.LastApprovalDateFormatted; // GG.AA.YYYY HH:mm:ss (C sütunu)
                    }
                }

                // 6. Tutarı işlem türüne göre ayarla ve ₺ simgesi ekle
                decimal amount = transaction.ResultAmount;
                string amountFormatted;

                if (transactionType == "Çekim")
                {
                    amountFormatted = $"-₺{amount:N2}";
                }
                else
                {
                    amountFormatted = $"₺{amount:N2}";
                }

                // 7. Boş satır bul
                int emptyRow = await FindFirstEmptyRowFromRow16Async();
                _currentRow = emptyRow;

                LoggerHelper.LogInformation($"Onaylanmış işlem yazılıyor: {transaction.TransactionNo}, Satır: {_currentRow}, Tür: {transactionType}, Tutar: {amountFormatted}");

                // 8. Google Sheets'e yaz - YENİ SÜTUN DÜZENİ
                // B: Son Onay Tarihi (GG.AA.YYYY)
                // C: Son Onay Tarihi (tam format: GG.AA.YYYY HH:mm:ss)
                // D: İsim Soyisim
                // E: Banka
                // F: İban Sahibi
                // G: Tutar (₺ simgesi ile)
                // H: Boş
                // I: İşlem ID

                var range = $"{SheetName}!B{_currentRow}:I{_currentRow}";
                var valueRange = new ValueRange
                {
                    Values = new List<IList<object>>
                    {
                        new List<object>
                        {
                            transactionDateFormatted, // B sütunu: Son Onay Tarihi (GG.AA.YYYY)
                            transactionDateTimeFull, // C sütunu: Son Onay Tarihi (tam format)
                            transaction.FullName ?? "", // D sütunu: İsim Soyisim
                            transaction.BankName ?? "", // E sütunu: Banka
                            transaction.AccountHolder ?? "", // F sütunu: IBAN Sahibi
                            amountFormatted, // G sütunu: İşlem Tutarı (₺ ile formatlı)
                            "", // H sütunu: Boş bırakıldı
                            transactionId // I sütunu: İşlem ID (Key)
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

                LoggerHelper.LogInformation($"✅ İşlem {transactionId} başarıyla yazıldı. Satır: {_currentRow - 1}, Tutar: {amountFormatted}");
            }
            catch (Google.GoogleApiException ex) when (ex.Message.Contains("protected cell"))
            {
                LoggerHelper.LogError(ex, $"KORUMALI HÜCRE HATASI! B{_currentRow}:I{_currentRow} aralığı korumalı.");
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

        // TAMAMEN YENİ - Resimdeki takvim yapısına göre GÜN SEÇİMİ
        public async Task ApplyDateFilterAsync(DateTime selectedDate)
        {
            try
            {
                LoggerHelper.LogInformation($"Tarih filtresi uygulanıyor: {selectedDate:dd.MM.yyyy HH:mm}");

                // 1. Tarih butonunu bul ve tıkla
                var datePickerButton = await _page.WaitForSelectorAsync(
                    "button[data-slot='popover-trigger']:has(svg.lucide-calendar), " +
                    "button:has-text('Oluşturma Tarihine Göre'), " +
                    "button:has-text('Tarih'), " +
                    "#date",
                    new PageWaitForSelectorOptions { Timeout = 5000 });

                if (datePickerButton == null)
                {
                    LoggerHelper.LogWarning("Tarih picker butonu bulunamadı!");
                    return;
                }

                LoggerHelper.LogInformation("Tarih picker butonu bulundu, tıklanıyor...");
                await datePickerButton.ClickAsync();
                await Task.Delay(2000);

                // 2. Takvim popup'ını bekle
                var datePickerPopup = await _page.WaitForSelectorAsync(
                    "[role='dialog'], " +
                    "[data-slot='popover-content'], " +
                    ".rdp-root",
                    new PageWaitForSelectorOptions { Timeout = 3000 });

                if (datePickerPopup == null)
                {
                    LoggerHelper.LogWarning("Tarih picker popup'ı açılamadı!");
                    await _page.Keyboard.PressAsync("Escape");
                    return;
                }

                LoggerHelper.LogInformation("Tarih picker açıldı.");

                // 3. TAKVİM TABLOSUNU BUL
                var calendarTable = await datePickerPopup.QuerySelectorAsync("table[role='grid']");

                if (calendarTable == null)
                {
                    calendarTable = await datePickerPopup.QuerySelectorAsync("table.rdp-table");
                }

                if (calendarTable == null)
                {
                    calendarTable = await datePickerPopup.QuerySelectorAsync("table");
                }

                if (calendarTable == null)
                {
                    LoggerHelper.LogWarning("Takvim tablosu bulunamadı!");
                    await _page.Keyboard.PressAsync("Escape");
                    return;
                }

                LoggerHelper.LogInformation("Takvim tablosu bulundu.");

                // 4. GÜNÜ SEÇ - ÇOK ÖNEMLİ: Resimdeki gibi tıklanabilir gün butonlarını bul
                int dayToSelect = selectedDate.Day;
                bool daySelected = false;

                // Önce tüm tıklanabilir gün butonlarını al
                var dayButtons = await calendarTable.QuerySelectorAllAsync(
                    "button[role='gridcell']:not([disabled]), " +
                    "td button:not([disabled]), " +
                    "button.rdp-day:not([disabled])");

                LoggerHelper.LogInformation($"{dayButtons.Count} adet tıklanabilir gün butonu bulundu.");

                foreach (var dayButton in dayButtons)
                {
                    try
                    {
                        var buttonText = await dayButton.InnerTextAsync();

                        // Sadece sayı olan butonları kontrol et (günler)
                        if (int.TryParse(buttonText.Trim(), out int day) && day == dayToSelect)
                        {
                            // Butonun görünür ve tıklanabilir olduğundan emin ol
                            if (await dayButton.IsVisibleAsync())
                            {
                                await dayButton.ClickAsync();
                                LoggerHelper.LogInformation($"✅ Gün seçildi: {dayToSelect}");
                                daySelected = true;
                                break;
                            }
                        }
                    }
                    catch { continue; }
                }

                if (!daySelected)
                {
                    // Alternatif: Tüm butonları dene
                    var allButtons = await calendarTable.QuerySelectorAllAsync("button");
                    foreach (var button in allButtons)
                    {
                        try
                        {
                            var buttonText = await button.InnerTextAsync();
                            if (int.TryParse(buttonText.Trim(), out int day) && day == dayToSelect)
                            {
                                if (await button.IsVisibleAsync())
                                {
                                    await button.ClickAsync();
                                    LoggerHelper.LogInformation($"✅ Gün seçildi (alternatif): {dayToSelect}");
                                    daySelected = true;
                                    break;
                                }
                            }
                        }
                        catch { continue; }
                    }
                }

                if (!daySelected)
                {
                    LoggerHelper.LogWarning($"Gün seçilemedi: {dayToSelect}. Escape tuşuna basılıyor...");
                    await _page.Keyboard.PressAsync("Escape");
                    return;
                }

                await Task.Delay(1000);

                // 5. SAAT INPUTLARINI BUL VE SEÇİLEN SAATİ DOLDUR - Main Form'dan gelen saat
                var timeInputs = await datePickerPopup.QuerySelectorAllAsync("input[type='time']");

                if (timeInputs.Count >= 2)
                {
                    // Başlangıç saati: Seçilen tarihin saat kısmı (DateTimePicker'dan)
                    var startTimeInput = timeInputs[0];
                    string startTime = selectedDate.ToString("HH:mm");
                    await startTimeInput.FillAsync(startTime);
                    LoggerHelper.LogInformation($"Başlangıç saati ayarlandı: {startTime}");

                    // Bitiş saati: 23:59
                    var endTimeInput = timeInputs[1];
                    await endTimeInput.FillAsync("23:59");
                    LoggerHelper.LogInformation("Bitiş saati ayarlandı: 23:59");
                }
                else if (timeInputs.Count == 1)
                {
                    // Sadece bir time input varsa
                    var timeInput = timeInputs[0];
                    string startTime = selectedDate.ToString("HH:mm");
                    await timeInput.FillAsync(startTime);
                    LoggerHelper.LogInformation($"Saati ayarlandı: {startTime}");
                }
                else
                {
                    LoggerHelper.LogInformation("Time input bulunamadı, sadece tarih seçildi.");
                }

                // 6. TAMAM/UYGULA butonuna tıkla veya Enter'a bas
                await Task.Delay(1000);

                // Önce "Uygula" butonunu ara
                var applyButton = await datePickerPopup.QuerySelectorAsync(
                    "button:has-text('Uygula'), " +
                    "button:has-text('Tamam'), " +
                    "button:has-text('Apply'), " +
                    "button:has-text('OK')");

                if (applyButton != null)
                {
                    await applyButton.ClickAsync();
                    LoggerHelper.LogInformation("Tarih filtresi uygulandı (Uygula butonu ile).");
                }
                else
                {
                    // Uygula butonu yoksa Enter tuşuna bas
                    await _page.Keyboard.PressAsync("Enter");
                    LoggerHelper.LogInformation("Tarih filtresi uygulandı (Enter ile).");
                }

                // 7. Filtrenin uygulanmasını bekle
                await Task.Delay(1500);

                LoggerHelper.LogInformation($"✅ Tarih filtresi başarıyla uygulandı: {selectedDate:dd.MM.yyyy HH:mm}");

            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Tarih filtresi uygulama hatası");
                try { await _page.Keyboard.PressAsync("Escape"); } catch { }
            }
        }

        // TÜM FİLTRELERİ UYGULA VE ARA BUTONUNA TIKLA
        private async Task ApplyFiltersAsync(
            string status = "Onaylandı",
            string transactionType = "Yatırım",
            DateTime? selectedDate = null,
            string sortOrder = "Eskiden Yeniye")
        {
            try
            {
                LoggerHelper.LogInformation($"Filtreler uygulanıyor: Durum={status}, İşlem Türü={transactionType}, Tarih={selectedDate}, Sıralama={sortOrder}");

                // 1. Önce tüm filtreleri temizle
                await ClearTransactionFiltersAsync();
                await Task.Delay(2000);

                // 2. Tarih filtresi uygula (eğer seçilmişse)
                if (selectedDate.HasValue)
                {
                    await ApplyDateFilterAsync(selectedDate.Value);
                    await Task.Delay(2000);
                }

                // 3. DURUM FİLTRESİNİ UYGULA
                await ApplyStatusFilterAsync(status);
                await Task.Delay(1500);

                // 4. İŞLEM TÜRÜ FİLTRESİNİ UYGULA
                await ApplyTransactionTypeFilterAsync(transactionType);
                await Task.Delay(1500);

                // 5. SIRALAMA FİLTRESİNİ UYGULA
                if (!string.IsNullOrEmpty(sortOrder))
                {
                    await ApplySortFilterAsync(sortOrder);
                    await Task.Delay(1500);
                }

                // 6. ARA BUTONUNU BUL VE TIKLA - EN ÖNEMLİ ADIM
                await ClickSearchButtonAsync();

                // 7. Tablonun yüklenmesini bekle
                await Task.Delay(3000);
                await WaitForTableToLoadAsync();

                LoggerHelper.LogInformation($"✅ Tüm filtreler başarıyla uygulandı ve ara butonuna tıklandı.");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Filtre uygulama hatası");
                throw;
            }
        }

        // SIRALAMA FİLTRESİ
        public async Task ApplySortFilterAsync(string sortOrder)
        {
            try
            {
                LoggerHelper.LogInformation($"Sıralama filtresi uygulanıyor: {sortOrder}");

                var sortCombobox = await _page.QuerySelectorAsync(
                    "button[role='combobox']:has(span[data-slot='select-value']:has-text('Yeniden Eskiye')), " +
                    "button[role='combobox']:has(span[data-slot='select-value']:has-text('Eskiden Yeniye'))");

                if (sortCombobox == null)
                {
                    var allComboboxes = await _page.QuerySelectorAllAsync("button[role='combobox'][data-slot='select-trigger']");
                    foreach (var combo in allComboboxes)
                    {
                        try
                        {
                            var span = await combo.QuerySelectorAsync("span[data-slot='select-value']");
                            if (span != null)
                            {
                                var text = await span.InnerTextAsync();
                                if (text == "Yeniden Eskiye" || text == "Eskiden Yeniye")
                                {
                                    sortCombobox = combo;
                                    break;
                                }
                            }
                        }
                        catch { continue; }
                    }
                }

                if (sortCombobox != null)
                {
                    var currentValueSpan = await sortCombobox.QuerySelectorAsync("span[data-slot='select-value']");
                    if (currentValueSpan != null)
                    {
                        var currentText = await currentValueSpan.InnerTextAsync();
                        if (currentText.Trim() == sortOrder)
                        {
                            LoggerHelper.LogInformation($"{sortOrder} zaten seçili!");
                            return;
                        }
                    }

                    await sortCombobox.ClickAsync();
                    await Task.Delay(1500);

                    var dropdownMenu = await _page.WaitForSelectorAsync(
                        "[role='listbox'][data-slot='select-content'], " +
                        "[data-slot='select-content'], " +
                        "[role='listbox']",
                        new PageWaitForSelectorOptions { Timeout = 3000 });

                    if (dropdownMenu != null)
                    {
                        var targetOption = await dropdownMenu.QuerySelectorAsync(
                            $"[role='option']:has-text('{sortOrder}'), " +
                            $"[data-slot='select-item']:has-text('{sortOrder}')");

                        if (targetOption != null)
                        {
                            await targetOption.ClickAsync();
                            LoggerHelper.LogInformation($"{sortOrder} seçildi.");
                        }
                        await Task.Delay(1000);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Sıralama filtresi ayarlama hatası");
            }
        }

        // İŞLEM TÜRÜ FİLTRESİ
        public async Task ApplyTransactionTypeFilterAsync(string transactionType)
        {
            try
            {
                LoggerHelper.LogInformation($"İşlem türü filtresi uygulanıyor: {transactionType}");

                var comboboxes = await _page.QuerySelectorAllAsync(
                    "button[role='combobox'][data-slot='select-trigger'], " +
                    "button[role='combobox']");

                foreach (var combobox in comboboxes)
                {
                    try
                    {
                        var span = await combobox.QuerySelectorAsync("span[data-slot='select-value']");
                        if (span != null)
                        {
                            var currentText = await span.InnerTextAsync();

                            if (currentText == "Yatırım" || currentText == "Çekim" || currentText.Contains("Hepsi"))
                            {
                                await combobox.ClickAsync();
                                await Task.Delay(1000);

                                var dropdownMenu = await _page.WaitForSelectorAsync(
                                    "[role='listbox'], " +
                                    "[data-slot='select-content']",
                                    new PageWaitForSelectorOptions { Timeout = 3000 });

                                if (dropdownMenu != null)
                                {
                                    var option = await dropdownMenu.QuerySelectorAsync(
                                        $"[role='option']:has-text('{transactionType}'), " +
                                        $"[data-slot='select-item']:has-text('{transactionType}')");

                                    if (option != null)
                                    {
                                        await option.ClickAsync();
                                        LoggerHelper.LogInformation($"İşlem türü '{transactionType}' olarak ayarlandı.");
                                    }
                                }
                                return;
                            }
                        }
                    }
                    catch { continue; }
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "İşlem türü filtresi ayarlama hatası");
            }
        }

        // DURUM FİLTRESİ
        private async Task ApplyStatusFilterAsync(string status)
        {
            try
            {
                var comboboxes = await _page.QuerySelectorAllAsync(
                    "button[role='combobox'][data-slot='select-trigger'], " +
                    "button[role='combobox']");

                foreach (var combobox in comboboxes)
                {
                    try
                    {
                        var span = await combobox.QuerySelectorAsync("span[data-slot='select-value']");
                        if (span != null)
                        {
                            var currentText = await span.InnerTextAsync();

                            if (currentText == "Tümü" || currentText == "Onaylandı" || currentText == "Hepsi")
                            {
                                await combobox.ClickAsync();
                                await Task.Delay(1000);

                                var dropdownMenu = await _page.WaitForSelectorAsync(
                                    "[role='listbox'], " +
                                    "[data-slot='select-content']",
                                    new PageWaitForSelectorOptions { Timeout = 3000 });

                                if (dropdownMenu != null)
                                {
                                    var option = await dropdownMenu.QuerySelectorAsync(
                                        $"[role='option']:has-text('{status}'), " +
                                        $"[data-slot='select-item']:has-text('{status}')");

                                    if (option != null)
                                    {
                                        await option.ClickAsync();
                                        LoggerHelper.LogInformation($"Durum '{status}' olarak ayarlandı.");
                                    }
                                }
                                return;
                            }
                        }
                    }
                    catch { continue; }
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Durum filtresi ayarlama hatası");
            }
        }

        // ARA BUTONUNA TIKLA - KESİN ÇÖZÜM
        private async Task ClickSearchButtonAsync()
        {
            try
            {
                LoggerHelper.LogInformation("Ara butonu aranıyor...");

                // TÜM OLASI ARA BUTONLARINI DENE
                var searchButtonSelectors = new[]
                {
                    "button:has(svg.lucide-funnel)",
                    "button:has-text('Ara')",
                    "[data-slot='button']:has(svg.lucide-funnel)",
                    "button:has-text('Filtrele')",
                    "button[type='submit']:has-text('Ara')",
                    "button[type='button']:has-text('Ara')",
                    "button.btn-primary:has-text('Ara')",
                    "button.bg-primary:has-text('Ara')"
                };

                IElementHandle searchButton = null;

                foreach (var selector in searchButtonSelectors)
                {
                    try
                    {
                        var button = await _page.QuerySelectorAsync(selector);
                        if (button != null && await button.IsVisibleAsync())
                        {
                            searchButton = button;
                            LoggerHelper.LogInformation($"Ara butonu bulundu: {selector}");
                            break;
                        }
                    }
                    catch { }
                }

                if (searchButton != null)
                {
                    LoggerHelper.LogInformation("Ara butonuna tıklanıyor...");
                    await searchButton.ClickAsync();

                    // Filtrelerin uygulanmasını bekle
                    await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await Task.Delay(4000);

                    // Tablonun güncellendiğini kontrol et
                    var tableRows = await _page.QuerySelectorAllAsync("tbody tr[data-slot='table-row']");
                    LoggerHelper.LogInformation($"✅ Ara butonuna tıklandı. {tableRows.Count} satır bulundu.");
                }
                else
                {
                    LoggerHelper.LogWarning("Ara butonu bulunamadı! Enter tuşuna basılıyor...");
                    await _page.Keyboard.PressAsync("Enter");
                    await Task.Delay(3000);
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Ara butonuna tıklama hatası");
            }
        }

        // İŞLEMLERİ FİLTRE İLE ÇEK
        public async Task<List<Transaction>> ExtractTransactionsWithFilterAsync(
            string status = "Onaylandı",
            string transactionType = "Yatırım",
            bool autoPaginate = false,
            DateTime? selectedDate = null,
            string sortOrder = "Eskiden Yeniye")
        {
            var allTransactions = new List<Transaction>();
            int currentPage = 1;
            int maxPages = autoPaginate ? 100 : 1;

            try
            {
                LoggerHelper.LogInformation($"Filtreli işlem çekme başlatılıyor: {status}, {transactionType}, Tarih: {selectedDate}, Sıralama: {sortOrder}");

                // 1. Önce sayfanın tamamen yüklendiğinden emin ol
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(2000);

                // 2. TÜM FİLTRELERİ UYGULA VE ARA BUTONUNA TIKLA
                await ApplyFiltersAsync(status, transactionType, selectedDate, sortOrder);
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
                                    transaction.TransactionType = transactionType;

                                    if (_processedTransactionIds.Contains(transaction.TransactionId ?? transaction.TransactionNo))
                                    {
                                        continue;
                                    }

                                    var detailButton = await row.QuerySelectorAsync(
                                        "button[data-slot='sheet-trigger'], " +
                                        "button:has-text('Detaylı Görüntüle'), " +
                                        "td:nth-child(7) button");

                                    if (detailButton != null && await detailButton.IsVisibleAsync())
                                    {
                                        await detailButton.ClickAsync();
                                        await Task.Delay(2000);
                                        await ExtractModalDetailsAsync(transaction, transactionType);
                                        await CloseModalAsync();
                                    }

                                    await WriteTransactionToGoogleSheetAsync(transaction, transactionType);
                                    allTransactions.Add(transaction);
                                    newTransactions++;
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

                LoggerHelper.LogInformation($"✅ {allTransactions.Count} adet işlem başarıyla çekildi!");
                return allTransactions;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Filtreli işlem çekme hatası");
                return allTransactions;
            }
        }

        // KALAN METODLAR (DEĞİŞMEDİ)
        public async Task<List<Transaction>> ExtractTransactionsAsync(int pageCount = 10)
        {
            return await ExtractTransactionsWithFilterAsync("Onaylandı", "Yatırım", false);
        }

        private async Task<bool> NavigateToNextPageAsync(int currentPage)
        {
            try
            {
                LoggerHelper.LogInformation($"Sonraki sayfaya geçiliyor... (Mevcut: {currentPage})");

                var pagination = await _page.QuerySelectorAsync(
                    "nav[role='navigation'][aria-label='pagination'], " +
                    "[data-slot='pagination']");

                if (pagination == null)
                {
                    return false;
                }

                var nextButton = await pagination.QuerySelectorAsync(
                    "a:has-text('Sonraki'), " +
                    "button:has-text('Sonraki'), " +
                    "[data-slot='pagination-link']:has-text('Sonraki'), " +
                    "[aria-label*='next']");

                if (nextButton != null && await nextButton.IsVisibleAsync())
                {
                    var isDisabled = await nextButton.EvaluateAsync<bool>(@"
                        element => {
                            if (element.disabled) return true;
                            if (element.getAttribute('disabled') !== null) return true;
                            if (element.getAttribute('aria-disabled') === 'true') return true;
                            return false;
                        }
                    ");

                    if (!isDisabled)
                    {
                        await nextButton.ClickAsync();
                        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                        await Task.Delay(3000);
                        await WaitForTableToLoadAsync();
                        return true;
                    }
                }
                return false;
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
                    var tbody = await _page.QuerySelectorAsync(
                        "tbody[data-slot='table-body'], " +
                        "tbody");

                    if (tbody != null)
                    {
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
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Tablo yüklenmesi beklenirken hata");
            }
        }

        
        // GÜNCELLENMİŞ: ProcessTransactionsCycleAsync - İş akışına uygun
        public async Task ProcessTransactionsCycleAsync(CancellationToken cancellationToken)
        {
            try
            {
                LoggerHelper.LogInformation("=== YENİ İŞLEM DÖNGÜSÜ BAŞLATILIYOR ===");

                // 1. Login durumunu kontrol et
                if (!await IsLoggedInAsync())
                {
                    LoggerHelper.LogInformation("Oturum kapalı, yeniden login yapılıyor...");
                    await LoginAsync();
                    await NavigateToTransactionHistoryAsync();
                }

                // 2. Yatırım işlemlerini işle
                LoggerHelper.LogInformation("=== YATIRIM İŞLEMLERİ İŞLENİYOR ===");
                var yatirimTransactions = await ExtractTransactionsWithFilterAsync(
                    status: "Onaylandı",
                    transactionType: "Yatırım",
                    autoPaginate: true,
                    selectedDate: _lastDepositDate ?? _initialSelectedDate,
                    sortOrder: _initialSortOrder);

                if (yatirimTransactions.Any())
                {
                    _lastDepositDate = yatirimTransactions.Max(t => t.LastApprovalDate);
                    LoggerHelper.LogInformation($"{yatirimTransactions.Count} adet Yatırım işlemi işlendi. Son tarih: {_lastDepositDate}");
                }

                // 3. Çekim işlemlerini işle
                LoggerHelper.LogInformation("=== ÇEKİM İŞLEMLERİ İŞLENİYOR ===");
                var cekimTransactions = await ExtractTransactionsWithFilterAsync(
                    status: "Onaylandı",
                    transactionType: "Çekim",
                    autoPaginate: true,
                    selectedDate: _lastWithdrawalDate ?? _initialSelectedDate,
                    sortOrder: _initialSortOrder);

                if (cekimTransactions.Any())
                {
                    _lastWithdrawalDate = cekimTransactions.Max(t => t.LastApprovalDate);
                    LoggerHelper.LogInformation($"{cekimTransactions.Count} adet Çekim işlemi işlendi. Son tarih: {_lastWithdrawalDate}");
                }

                int totalTransactions = yatirimTransactions.Count + cekimTransactions.Count;
                LoggerHelper.LogInformation($"=== TOPLAM İŞLENEN İŞLEM: {totalTransactions} ===");
            }
            catch (Exception ex) when (!(ex is TaskCanceledException))
            {
                LoggerHelper.LogError(ex, "İşlem döngüsünde hata");
                throw;
            }
        }

        // GÜNCELLENMİŞ: StartContinuousProcessing - Parametre alacak şekilde
        public void StartContinuousProcessing(DateTime selectedDate, string sortOrder)
        {
            try
            {
                _initialSelectedDate = selectedDate;
                _initialSortOrder = sortOrder;

                _processingCts = new CancellationTokenSource();
                var processingTask = ProcessTransactionsCycleAsync(_processingCts.Token);

                LoggerHelper.LogInformation($"Sürekli işlem döngüsü başlatıldı. Tarih: {selectedDate}, Sıralama: {sortOrder}");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Sürekli işlem başlatma hatası");
                throw;
            }
        }

        public void StopContinuousProcessing()
        {
            try
            {
                _processingCts?.Cancel();
                _processingCts?.Dispose();
                _processingCts = null;

                LoggerHelper.LogInformation("Sürekli işlem döngüsü durduruldu.");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Sürekli işlem durdurma hatası");
            }
        }

        private async Task<Transaction> ExtractTransactionFromRowAsync(IElementHandle row)
        {
            try
            {
                var transaction = new Transaction();

                var transactionNoElements = await row.QuerySelectorAllAsync("td:nth-child(1) button");
                if (transactionNoElements.Count >= 3)
                {
                    transaction.TransactionNo = await ExtractTextFromButtonAsync(transactionNoElements[0]);
                    transaction.ExternalRefNo = await ExtractTextFromButtonAsync(transactionNoElements[1]);
                    transaction.CustomerRefNo = await ExtractTextFromButtonAsync(transactionNoElements[2]);
                }

                var customerElements = await row.QuerySelectorAllAsync("td:nth-child(2) button");
                if (customerElements.Count >= 2)
                {
                    transaction.CustomerId = await ExtractTextFromButtonAsync(customerElements[0]);
                    transaction.CustomerName = await ExtractTextFromButtonAsync(customerElements[1]);
                }

                var amountElements = await row.QuerySelectorAllAsync("td:nth-child(3) button");
                if (amountElements.Count >= 2)
                {
                    var requestedAmountText = await ExtractTextFromButtonAsync(amountElements[0]);
                    transaction.RequestedAmount = ParseAmount(requestedAmountText);

                    var resultAmountText = await ExtractTextFromButtonAsync(amountElements[1]);
                    transaction.ResultAmount = ParseAmount(resultAmountText);
                }

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

        private async Task ExtractModalDetailsAsync(Transaction transaction, string transactionType)
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

                var modalHtml = await modal.InnerHTMLAsync();
                ParseModalHtml(modalHtml, transaction, transactionType);

                LoggerHelper.LogInformation($"{transaction.TransactionNo} modal detayları alındı. Tür: {transactionType}");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Modal detay alma hatası");
            }
        }

        // GÜNCELLENMİŞ: ParseModalHtml - Çekim modal bilgilerini doğru şekilde parse eder
        private void ParseModalHtml(string modalHtml, Transaction transaction, string transactionType)
        {
            try
            {
                // 1. İşlem ID (I sütunu)
                var transactionIdMatch = Regex.Match(
                    modalHtml,
                    @"İşlem ID.*?font-medium text-primary text-xs.*?<div>([^<]+)</div>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (transactionIdMatch.Success)
                {
                    transaction.TransactionId = transactionIdMatch.Groups[1].Value.Trim();
                    LoggerHelper.LogInformation($"İşlem ID bulundu: {transaction.TransactionId}");
                }

                // 2. İsim Soyisim (D sütununa)
                var fullNameMatch = Regex.Match(
                    modalHtml,
                    @"İsim Soyisim.*?font-medium text-primary text-xs.*?<div>([^<]+)</div>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (fullNameMatch.Success)
                {
                    transaction.FullName = fullNameMatch.Groups[1].Value.Trim();
                    LoggerHelper.LogInformation($"İsim Soyisim bulundu: {transaction.FullName}");
                }

                // 3. Banka Bilgisi - E sütununa
                var bankMatch = Regex.Match(
                    modalHtml,
                    @"<div class=""flex flex-col gap-1 text-right mr-8"">.*?<div></div>.*?<div>([^<]+)</div>",
                    RegexOptions.Singleline);

                if (bankMatch.Success)
                {
                    transaction.BankName = bankMatch.Groups[1].Value.Trim();
                    LoggerHelper.LogInformation($"Banka bulundu: {transaction.BankName}");
                }

                // 4. İban Sahibi - F sütununa
                var ibanHolderMatch = Regex.Match(
                    modalHtml,
                    @"IBAN Sahibi.*?font-medium text-primary text-xs.*?<div>([^<]+)</div>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (ibanHolderMatch.Success)
                {
                    transaction.AccountHolder = ibanHolderMatch.Groups[1].Value.Trim();
                    LoggerHelper.LogInformation($"IBAN Sahibi bulundu: {transaction.AccountHolder}");
                }

                // 5. Sonuç Tutarı - G sütununa (Çekim için -₺ olacak)
                var amountMatch = Regex.Match(
                    modalHtml,
                    @"Sonuç Tutarı.*?font-medium text-primary text-xs.*?<div>([^<]+)</div>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (amountMatch.Success)
                {
                    var amountText = amountMatch.Groups[1].Value.Trim();
                    if (decimal.TryParse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal resultAmount))
                    {
                        transaction.ResultAmount = resultAmount;
                        LoggerHelper.LogInformation($"Sonuç Tutarı bulundu: {resultAmount}");
                    }
                }

                // 6. Son Onay Tarihi - B ve C sütunları için
                var lastApprovalMatch = Regex.Match(
                    modalHtml,
                    @"Son Onay Tarihi.*?font-medium text-primary text-xs.*?<div>([^<]+)</div>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (lastApprovalMatch.Success)
                {
                    transaction.LastApprovalDateFormatted = lastApprovalMatch.Groups[1].Value.Trim();
                    LoggerHelper.LogInformation($"Son Onay Tarihi bulundu: {transaction.LastApprovalDateFormatted}");
                }

                // 7. IBAN
                var ibanMatch = Regex.Match(modalHtml, @"TR\d{24}");
                if (ibanMatch.Success)
                {
                    transaction.IBAN = ibanMatch.Value;
                    LoggerHelper.LogInformation($"IBAN bulundu: {transaction.IBAN}");
                }

                // Çekim için tutarı negatif yap (WriteTransactionToGoogleSheetAsync'da -₺ ekleniyor)
                if (transactionType == "Çekim" && transaction.ResultAmount > 0)
                {
                    // WriteTransactionToGoogleSheetAsync metodunda zaten -₺ eklenecek
                    LoggerHelper.LogInformation($"Çekim işlemi tutarı: {transaction.ResultAmount} (Excel'e -₺ olarak yazılacak)");
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
            // Önce bellekteki durumu kontrol et
            if (!_isLoggedIn) return false;

            // Sonra sayfayı kontrol et (çift kontrol)
            try
            {
                var currentUrl = _page.Url;
                return !currentUrl.Contains("login") && !currentUrl.Contains("auth");
            }
            catch
            {
                _isLoggedIn = false;
                return false;
            }
        }

        public async Task<Transaction> GetTransactionDetailsAsync(string transactionId)
        {
            try
            {
                LoggerHelper.LogInformation($"İşlem detayları alınıyor: {transactionId}");

                if (!await NavigateToTransactionHistoryAsync())
                {
                    LoggerHelper.LogWarning("İşlem Geçmişi sayfasına ulaşılamadı.");
                    return null;
                }

                var searchInput = await _page.QuerySelectorAsync("input[placeholder='Ara...']");
                if (searchInput != null)
                {
                    await searchInput.FillAsync(transactionId);
                    await Task.Delay(1000);
                    await ClickSearchButtonAsync();
                }

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
                                var detailButton = await row.QuerySelectorAsync("td:nth-child(7) button[data-slot='sheet-trigger']");
                                if (detailButton != null)
                                {
                                    await detailButton.ClickAsync();
                                    await Task.Delay(1500);
                                    await ExtractModalDetailsAsync(transaction, transaction.TransactionType ?? "Yatırım");
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

                if (!await NavigateToTransactionHistoryAsync())
                    return false;

                await ApplyFiltersAsync("Onaylandı", "Yatırım");

                var page1Rows = await _page.QuerySelectorAllAsync("tbody tr[data-slot='table-row']");
                LoggerHelper.LogInformation($"1. sayfada {page1Rows.Count} satır bulundu.");

                if (page1Rows.Count == 0)
                {
                    LoggerHelper.LogWarning("1. sayfada hiç satır yok!");
                    return false;
                }

                var firstRowPage1 = await page1Rows[0].InnerHTMLAsync();

                bool canNavigate = await NavigateToNextPageAsync(1);

                if (canNavigate)
                {
                    var page2Rows = await _page.QuerySelectorAllAsync("tbody tr[data-slot='table-row']");
                    LoggerHelper.LogInformation($"2. sayfada {page2Rows.Count} satır bulundu.");

                    if (page2Rows.Count > 0)
                    {
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

                await _page.ReloadAsync(new PageReloadOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 15000
                });
                await Task.Delay(2000);

                await CloseAllModalsAsync();

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

                await Task.Delay(1000);

                var comboboxes = await _page.QuerySelectorAllAsync(
                    "button[role='combobox'][data-slot='select-trigger'], " +
                    "button[role='combobox']");

                LoggerHelper.LogInformation($"{comboboxes.Count} adet combobox bulundu.");

                foreach (var combobox in comboboxes)
                {
                    try
                    {
                        if (!await combobox.IsVisibleAsync())
                            continue;

                        await combobox.ClickAsync();
                        await Task.Delay(500);

                        var tumuOption = await _page.QuerySelectorAsync(
                            "[role='option']:has-text('Tümü'), " +
                            "[role='option']:has-text('Hepsi'), " +
                            "[role='option']:has-text('Tüm Durumlar')");

                        if (tumuOption != null)
                        {
                            await tumuOption.ClickAsync();
                            LoggerHelper.LogInformation("Combobox 'Tümü/Hepsi' olarak ayarlandı.");
                        }
                        else
                        {
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
                        await _page.Keyboard.PressAsync("Escape");
                    }
                }

                await ClearDateFiltersAsync();

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

                await ClickSearchButtonAsync();
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
                await _page.Keyboard.PressAsync("Escape");
                await Task.Delay(500);

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
                var dateInputs = await _page.QuerySelectorAllAsync(
                    "input[type='date'], " +
                    "input[placeholder*='Tarih'], " +
                    "input[placeholder*='Date'], " +
                    "input[type='datetime-local']");

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