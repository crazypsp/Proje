using Microsoft.Playwright;
using Proje.Core.Helpers;
using Proje.Core.Interfaces;
using Proje.Entities.Entities;
using Proje.Enums;
using Proje.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

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
        private int _currentRow = 16; // 16. satırdan başlayarak

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

                // Google Sheets servisini başlat
                await InitializeGoogleSheetsAsync();

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

                // 1. Dosya yolunu belirle (birkaç alternatif)
                string credentialFilePath = null;

                // Öncelikli: Proje.Service klasörü altındaki credentials klasöründen oku
                string projectCredentialsPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "..", "..", "..", "..", // Proje.Service klasörüne çık
                    "credentials",
                    "google-service-account.json"
                );

                credentialFilePath = Path.GetFullPath(projectCredentialsPath);

                // Alternatif: Çıktı dizinindeki credentials klasöründen oku
                if (!File.Exists(credentialFilePath))
                {
                    credentialFilePath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "credentials",
                        "google-service-account.json"
                    );
                }

                // Alternatif 2: Kullanıcının belirttiği tam yolu kullan
                if (!File.Exists(credentialFilePath))
                {
                    credentialFilePath = @"C:\Users\yusuf\source\repos\Proje\Proje.Service\credentials\google-service-account.json";
                }

                // 2. Dosyanın var olduğunu kontrol et
                if (!File.Exists(credentialFilePath))
                {
                    throw new FileNotFoundException(
                        $"Google servis hesabı anahtarı bulunamadı!\n" +
                        $"Aranan yol: {credentialFilePath}\n" +
                        $"Çalışma dizini: {Environment.CurrentDirectory}\n" +
                        $"Uygulama taban dizini: {AppDomain.CurrentDomain.BaseDirectory}"
                    );
                }

                LoggerHelper.LogInformation($"Google kimlik bilgileri yükleniyor: {credentialFilePath}");
                LoggerHelper.LogInformation($"Dosya boyutu: {new FileInfo(credentialFilePath).Length} bytes");

                // 3. JSON dosyasından kimlik bilgilerini yükle
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
            catch (FileNotFoundException ex)
            {
                LoggerHelper.LogError(ex, "Kimlik bilgileri dosyası bulunamadı!");

                // Ek bilgi: Olası dosya konumlarını kontrol et
                LoggerHelper.LogInformation($"Mevcut çalışma dizini: {Environment.CurrentDirectory}");
                LoggerHelper.LogInformation($"Uygulama taban dizini: {AppDomain.CurrentDomain.BaseDirectory}");

                // Dosya arama
                var searchPaths = new[]
                {
            @"credentials\google-service-account.json",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "credentials", "google-service-account.json"),
            Path.Combine(Environment.CurrentDirectory, "credentials", "google-service-account.json"),
            @"C:\Users\yusuf\source\repos\Proje\Proje.Service\credentials\google-service-account.json"
        };

                foreach (var path in searchPaths)
                {
                    LoggerHelper.LogInformation($"Kontrol ediliyor: {path} -> {File.Exists(path)}");
                }

                throw;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Google Sheets servisi başlatma hatası");

                // Ek hata detayları
                if (ex.InnerException != null)
                {
                    LoggerHelper.LogError(ex.InnerException, "İç hata detayı:");
                }

                throw;
            }
        }

        private async Task ListProtectedRangesAsync()
        {
            try
            {
                LoggerHelper.LogInformation("Sayfadaki korumalar kontrol ediliyor...");

                var spreadsheet = await _sheetsService.Spreadsheets.Get(SpreadsheetId).ExecuteAsync();
                var sheet = spreadsheet.Sheets.FirstOrDefault(s => s.Properties.Title == SheetName);

                if (sheet == null)
                {
                    //LoggerHelper.LogError($"'{SheetName}' sayfası bulunamadı!");
                    return;
                }

                LoggerHelper.LogInformation($"Sayfa ID: {sheet.Properties.SheetId}");
                LoggerHelper.LogInformation($"Sayfa başlığı: {sheet.Properties.Title}");

                // Sayfadaki tüm korumalı aralıkları al
                var protectedRanges = sheet.ProtectedRanges;

                if (protectedRanges == null || protectedRanges.Count == 0)
                {
                    LoggerHelper.LogInformation("Sayfada korumalı aralık bulunamadı.");
                }
                else
                {
                    LoggerHelper.LogInformation($"Sayfada {protectedRanges.Count} adet korumalı aralık bulundu:");

                    foreach (var protectedRange in protectedRanges)
                    {
                        LoggerHelper.LogInformation($"Koruma ID: {protectedRange.ProtectedRangeId}");
                        LoggerHelper.LogInformation($"Açıklama: {protectedRange.Description}");

                        if (protectedRange.Range != null)
                        {
                            LoggerHelper.LogInformation($"Aralık: Sayfa ID: {protectedRange.Range.SheetId}, " +
                                $"Başlangıç Satır: {protectedRange.Range.StartRowIndex}, " +
                                $"Bitiş Satır: {protectedRange.Range.EndRowIndex}, " +
                                $"Başlangıç Sütun: {protectedRange.Range.StartColumnIndex}, " +
                                $"Bitiş Sütun: {protectedRange.Range.EndColumnIndex}");

                            // A16 hücresinin korunup korunmadığını kontrol et
                            bool isA16Protected = IsCellProtected(16, 0, protectedRange); // A16 = satır 16, sütun A (0)
                            if (isA16Protected)
                            {
                                //LoggerHelper.LogError($"A16 hücresi bu koruma ile korunuyor: {protectedRange.Description}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Koruma kontrol hatası");
            }
        }

        private bool IsCellProtected(int row, int column, ProtectedRange protectedRange)
        {
            if (protectedRange.Range == null) return false;

            // Not: Google Sheets API'de satır ve sütun indeksleri 0 tabanlı
            // row = 16 → indeks 15 (16. satır)
            // column = 0 → A sütunu

            bool rowInRange = (protectedRange.Range.StartRowIndex == null ||
                               row >= protectedRange.Range.StartRowIndex) &&
                              (protectedRange.Range.EndRowIndex == null ||
                               row < protectedRange.Range.EndRowIndex);

            bool columnInRange = (protectedRange.Range.StartColumnIndex == null ||
                                  column >= protectedRange.Range.StartColumnIndex) &&
                                 (protectedRange.Range.EndColumnIndex == null ||
                                  column < protectedRange.Range.EndColumnIndex);

            return rowInRange && columnInRange;
        }

        private async Task<int> FindFirstEmptyRowInColumnAAsync()
        {
            try
            {
                var range = $"{SheetName}!A:A";
                var request = _sheetsService.Spreadsheets.Values.Get(SpreadsheetId, range);
                var response = await request.ExecuteAsync();

                // Boş olan ilk satırı bul (0 tabanlı değil, 1 tabanlı satır numarası)
                int firstEmptyRow = response.Values?.Count + 1 ?? 1;

                // Eğer satır 1-15 arası doluysa ve 16 boşsa, 16'yı döndür
                if (firstEmptyRow < 16)
                {
                    // 16. satırı kontrol et
                    var checkRange = $"{SheetName}!A16:A16";
                    var checkRequest = _sheetsService.Spreadsheets.Values.Get(SpreadsheetId, checkRange);
                    var checkResponse = await checkRequest.ExecuteAsync();

                    if (checkResponse.Values == null || checkResponse.Values.Count == 0)
                    {
                        return 16;
                    }
                    else
                    {
                        return firstEmptyRow;
                    }
                }

                return firstEmptyRow;
            }
            catch
            {
                return 16;
            }
        }

        private async Task TryAlternativeColumnAsync(Transaction transaction, string column)
        {
            try
            {
                LoggerHelper.LogInformation($"Alternatif sütun deneyimi: {column}");

                var range = $"{SheetName}!{column}{_currentRow}";
                var valueRange = new ValueRange
                {
                    Values = new List<IList<object>> { new List<object> { "ALT_TEST_" + DateTime.Now.ToString("HHmmss") } }
                };

                var request = _sheetsService.Spreadsheets.Values.Update(
                    valueRange, SpreadsheetId, range);
                request.ValueInputOption =
                    SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

                var response = await request.ExecuteAsync();
                LoggerHelper.LogInformation($"{column} sütunu yazılabilir! Güncellenen hücreler: {response.UpdatedCells}");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, $"{column} sütunu da korumalı!");
            }
        }


        private async Task WriteTransactionToGoogleSheetAsync(Transaction transaction)
        {
            // SADECE ONAYLANMIŞ İŞLEMLERİ YAZ
            if (transaction.Status != "Onaylandı")
            {
                LoggerHelper.LogInformation($"İşlem {transaction.TransactionNo} onaylanmamış ({transaction.Status}). Atlanıyor.");
                return;
            }

            try
            {
                // 1. _sheetsService kontrolü
                if (_sheetsService == null)
                {
                    await InitializeGoogleSheetsAsync();
                }

                // 2. Boş satır bul (B sütununa göre)
                int emptyRow = await FindFirstEmptyRowInColumnBAsync();
                _currentRow = Math.Max(emptyRow, 16);

                LoggerHelper.LogInformation($"Onaylanmış işlem yazılıyor: {transaction.TransactionNo}, Satır: {_currentRow}");

                // 3. SADECE BELİRLİ SÜTUNLARA YAZ (B, C, D, E, F, H)
                // G sütunu atlanacak (H sütunu için yer açmak için)

                // Range: B sütunundan H sütununa kadar
                var range = $"{SheetName}!B{_currentRow}:H{_currentRow}";
                var valueRange = new ValueRange();

                // 7 sütunluk değer listesi (B, C, D, E, F, G, H)
                // G sütunu boş bırakılacak
                var values = new List<IList<object>>
        {
            new List<object>
            {
                // B: Son Onay Tarihi
                transaction.LastApprovalDate?.ToString("dd/MM/yyyy HH:mm:ss") ?? DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                
                // C: IBAN Sahibi
                transaction.AccountHolder ?? "",
                
                // D: Banka Adı
                transaction.BankName ?? "",
                
                // E: IBAN
                transaction.IBAN ?? "",
                
                // F: Sonuç Tutar
                transaction.ResultAmount.ToString("N2"),
                
                // G: BOŞ (atlanacak)
                "",
                
                // H: İşlem ID
                transaction.TransactionId ?? transaction.TransactionNo ?? ""
            }
        };

                valueRange.Values = values;

                // 4. Doğrudan güncelleme yap (Append değil, Update)
                var updateRequest = _sheetsService.Spreadsheets.Values.Update(
                    valueRange, SpreadsheetId, range);
                updateRequest.ValueInputOption =
                    SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

                var response = await updateRequest.ExecuteAsync();
                LoggerHelper.LogInformation($"✅ İşlem {transaction.TransactionNo} başarıyla yazıldı. Satır: {_currentRow}, Sütunlar: B-H");
                LoggerHelper.LogInformation($"   Güncellenen hücreler: {response.UpdatedCells}");

                _currentRow++;
            }
            catch (Google.GoogleApiException ex) when (ex.Message.Contains("protected cell"))
            {
                // Korumalı hücre hatası - hangi sütunun korumalı olduğunu bul
                LoggerHelper.LogError(ex, $"KORUMALI HÜCRE HATASI! Lütfen Google Sheets'te kontrol edin:");
                LoggerHelper.LogError(ex, $"   - 'İşlem' sayfasındaki B{_currentRow}:H{_currentRow} aralığı");
                LoggerHelper.LogError(ex, $"   - 'Veri' > 'Korumalı sayfalar ve aralıklar' menüsü");
                LoggerHelper.LogError(ex, $"   - Servis hesabı izinleri: coreapi@heroic-bucksaw-484811-a2.iam.gserviceaccount.com");

                throw;
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Google Sheets'e yazma hatası");
                throw;
            }
        }

        // B sütunundaki ilk boş satırı bulan metod
        // B sütunundaki ilk boş satırı bulan metod
        private async Task<int> FindFirstEmptyRowInColumnBAsync()
        {
            try
            {
                var range = $"{SheetName}!B:B";
                var request = _sheetsService.Spreadsheets.Values.Get(SpreadsheetId, range);
                var response = await request.ExecuteAsync();

                // Boş olan ilk satırı bul (1 tabanlı)
                int firstEmptyRow = response.Values?.Count + 1 ?? 1;

                // Eğer 16'dan küçükse, 16. satır boş mu kontrol et
                if (firstEmptyRow < 16)
                {
                    var checkRange = $"{SheetName}!B16:B16";
                    var checkRequest = _sheetsService.Spreadsheets.Values.Get(SpreadsheetId, checkRange);
                    var checkResponse = await checkRequest.ExecuteAsync();

                    if (checkResponse.Values == null || checkResponse.Values.Count == 0)
                    {
                        return 16;
                    }
                    else
                    {
                        // 16 doluysa, bir sonraki boş satırı bul
                        for (int row = 17; row <= 1000; row++)
                        {
                            var rowRange = $"{SheetName}!B{row}:B{row}";
                            var rowRequest = _sheetsService.Spreadsheets.Values.Get(SpreadsheetId, rowRange);
                            var rowResponse = await rowRequest.ExecuteAsync();

                            if (rowResponse.Values == null || rowResponse.Values.Count == 0)
                            {
                                return row;
                            }
                        }
                        return 1001; // Tüm satırlar dolu
                    }
                }

                return firstEmptyRow;
            }
            catch
            {
                return 16; // Varsayılan olarak 16 döndür
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

                await _page.ClickAsync("a[href*='/marjin/transaction-history']");
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(3000);

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

                                // SADECE ONAYLANMIŞ İŞLEMLERİN DETAYINI AL VE GOOGLE SHEETS'E YAZ
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

                                            // MODAL DETAYLARI ALINDI, ŞİMDİ GOOGLE SHEETS'E YAZ
                                            await WriteTransactionToGoogleSheetAsync(transaction);

                                            await CloseModalAsync();
                                            await Task.Delay(500);
                                        }
                                        else
                                        {
                                            // Detay butonu yoksa bile, temel bilgilerle Google Sheets'e yaz
                                            await WriteTransactionToGoogleSheetAsync(transaction);
                                        }
                                    }
                                    catch (Exception modalEx)
                                    {
                                        LoggerHelper.LogError(modalEx, $"{transaction.TransactionNo} detay alma hatası");
                                        await TryCloseModalAsync();

                                        // Hata olsa bile, eldeki bilgilerle Google Sheets'e yazmayı dene
                                        await WriteTransactionToGoogleSheetAsync(transaction);
                                    }
                                }
                                else
                                {
                                    LoggerHelper.LogInformation($"{transaction.TransactionNo} onaylanmamış ({transaction.Status}). Google Sheets'e yazılmayacak.");
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
                LoggerHelper.LogInformation($"Onaylanmış işlemler Google Sheets'e yazıldı.");
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

                // 1. İşlem No
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
                await ExtractPaymentDetailsAsync(modal, transaction);
                await ExtractCustomerInfoAsync(modal, transaction);
                await ExtractBankAccountInfoAsync(modal, transaction);
                await ExtractOtherInfoAsync(modal, transaction);
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

        private async Task WriteModalToTextFileAsync(IElementHandle modal, Transaction transaction)
        {
            try
            {
                string modalFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ModalLogs");
                if (!Directory.Exists(modalFolder))
                    Directory.CreateDirectory(modalFolder);

                var modalText = await modal.InnerTextAsync();
                string fileName = $"Modal_{transaction.TransactionNo}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string filePath = Path.Combine(modalFolder, fileName);

                using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    writer.WriteLine("=".PadRight(80, '='));
                    writer.WriteLine($"İŞLEM MODAL DETAYLARI - {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
                    writer.WriteLine("=".PadRight(80, '='));
                    writer.WriteLine();

                    writer.WriteLine($"İşlem No: {transaction.TransactionNo}");
                    writer.WriteLine($"External Ref No: {transaction.ExternalRefNo}");
                    writer.WriteLine($"Customer Ref No: {transaction.CustomerRefNo}");
                    writer.WriteLine($"Müşteri: {transaction.CustomerName}");
                    writer.WriteLine($"Talep Tutarı: {transaction.RequestedAmount:N2} ₺");
                    writer.WriteLine($"Sonuç Tutarı: {transaction.ResultAmount:N2} ₺");
                    writer.WriteLine($"Durum: {transaction.Status}");
                    writer.WriteLine();

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
                // Hata olsa bile devam et
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

        public void Dispose()
        {
            _page?.CloseAsync();
            _browser?.CloseAsync();
            _playwright?.Dispose();
        }
    }
}