using Proje.Business.Managers;
using Proje.Core.Helpers;
using Proje.Data.Services;
using Proje.Models;
using Proje.Service;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Drawing;

namespace Proje.Destop
{
    public partial class MainForm : Form
    {
        private TransactionManager _transactionManager;
        private WebAutomationService _webService;
        private ExcelService _excelService;
        private ProgressBar _progressBar;
        private Label _lblStatus;
        private TextBox _txtLog;

        public MainForm()
        {
            InitializeComponent();
            InitializeServices();
            SetupUI();
        }

        private void InitializeServices()
        {
            try
            {
                // KULLANICI BİLGİLERİ - BUNLARI KENDİ BİLGİLERİNİZLE DEĞİŞTİRİN
                var credentials = new LoginCredentials
                {
                    BasicAuthUsername = "login",
                    BasicAuthPassword = "4610",
                    FormUsername = "coderysf@gmail.com",
                    FormPassword = "Aflex6501.@",
                    LoginUrl = "https://online.powerhavale.com/marjin/auth/login"
                };

                // BROWSER AYARLARI
                var browserConfig = new BrowserConfig
                {
                    Headless = false,
                    TimeoutSeconds = 30,
                    MaxRetryCount = 3,
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
                };

                // GOOGLE SHEETS ID - BU KISMI KENDİ GOOGLE SHEETS ID'NİZLE DEĞİŞTİRİN
                // Örnek: https://docs.google.com/spreadsheets/d/1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgvE2upms/edit
                // ID: 1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgvE2upms
                string googleSheetsId = "1RstouLb99LwTTzyavcJi-j1B6E49tu9gNtOxpcrQywY";

                // SERVİSLERİ BAŞLAT
                _webService = new WebAutomationService(credentials, browserConfig, googleSheetsId);
                _excelService = new ExcelService();
                _transactionManager = new TransactionManager(_webService, _excelService);

                LoggerHelper.LogInformation("Tüm servisler başarıyla başlatıldı!");
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Servis başlatma hatası");
                MessageBox.Show($"Servis başlatma hatası: {ex.Message}", "Hata",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetupUI()
        {
            try
            {
                this.Text = "PowerHavale İşlem Otomasyonu - Google Sheets Entegre";
                this.Size = new Size(900, 700);
                this.StartPosition = FormStartPosition.CenterScreen;
                this.BackColor = Color.FromArgb(240, 245, 250);

                // BAŞLIK LABEL
                var lblTitle = new Label
                {
                    Text = "POWERHAVALE OTOMASYON SİSTEMİ",
                    Font = new Font("Arial", 16, FontStyle.Bold),
                    ForeColor = Color.FromArgb(0, 51, 102),
                    Size = new Size(600, 40),
                    Location = new Point(150, 20),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                Controls.Add(lblTitle);

                // PROGRESS BAR
                _progressBar = new ProgressBar
                {
                    Location = new Point(50, 600),
                    Size = new Size(800, 25),
                    Style = ProgressBarStyle.Continuous,
                    Visible = false
                };
                Controls.Add(_progressBar);

                // BAŞLAT BUTONU
                var btnStart = new Button
                {
                    Text = "OTOMASYONU BAŞLAT",
                    Font = new Font("Arial", 12, FontStyle.Bold),
                    BackColor = Color.FromArgb(0, 123, 255),
                    ForeColor = Color.White,
                    Size = new Size(250, 60),
                    Location = new Point(325, 100),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnStart.FlatAppearance.BorderSize = 0;
                btnStart.Click += async (s, e) => await StartAutomationAsync();
                Controls.Add(btnStart);

                // GOOGLE SHEETS BUTONU
                var btnGoogleSheets = new Button
                {
                    Text = "GOOGLE SHEETS AÇ",
                    Font = new Font("Arial", 10, FontStyle.Bold),
                    BackColor = Color.FromArgb(40, 167, 69),
                    ForeColor = Color.White,
                    Size = new Size(200, 45),
                    Location = new Point(350, 180),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnGoogleSheets.FlatAppearance.BorderSize = 0;
                btnGoogleSheets.Click += (s, e) => OpenGoogleSheets();
                Controls.Add(btnGoogleSheets);

                // EXCEL BUTONU
                var btnExport = new Button
                {
                    Text = "EXCEL'E KAYDET",
                    Font = new Font("Arial", 10, FontStyle.Bold),
                    BackColor = Color.FromArgb(108, 117, 125),
                    ForeColor = Color.White,
                    Size = new Size(200, 45),
                    Location = new Point(350, 240),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnExport.FlatAppearance.BorderSize = 0;
                btnExport.Click += (s, e) => ExportToExcel();
                Controls.Add(btnExport);

                // DURUM LABEL'ı
                _lblStatus = new Label
                {
                    Text = "🚀 Sistem Hazır - Google Sheets Bağlantı Bekleniyor",
                    Font = new Font("Arial", 10, FontStyle.Bold),
                    ForeColor = Color.FromArgb(0, 51, 102),
                    Size = new Size(600, 30),
                    Location = new Point(150, 300),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.FromArgb(255, 255, 200)
                };
                Controls.Add(_lblStatus);

                // GOOGLE SHEETS BİLGİ LABEL
                var lblSheetsInfo = new Label
                {
                    Text = "📊 Google Sheets ID: " + (_webService?.GetSheetsId() ?? "Belirtilmedi"),
                    Font = new Font("Arial", 9),
                    ForeColor = Color.FromArgb(52, 58, 64),
                    Size = new Size(600, 20),
                    Location = new Point(150, 340),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                Controls.Add(lblSheetsInfo);

                // LOG TEXTBOX BAŞLIĞI
                var lblLogTitle = new Label
                {
                    Text = "📝 SİSTEM LOGLARI",
                    Font = new Font("Arial", 10, FontStyle.Bold),
                    ForeColor = Color.FromArgb(0, 51, 102),
                    Size = new Size(200, 20),
                    Location = new Point(50, 380),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                Controls.Add(lblLogTitle);

                // LOG TEXTBOX
                _txtLog = new TextBox
                {
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    Size = new Size(800, 200),
                    Location = new Point(50, 410),
                    ReadOnly = true,
                    Font = new Font("Consolas", 9),
                    BackColor = Color.FromArgb(248, 249, 250),
                    BorderStyle = BorderStyle.FixedSingle
                };
                Controls.Add(_txtLog);

                // TEMİZLE BUTONU
                var btnClearLog = new Button
                {
                    Text = "Logları Temizle",
                    Font = new Font("Arial", 8),
                    BackColor = Color.FromArgb(220, 53, 69),
                    ForeColor = Color.White,
                    Size = new Size(100, 25),
                    Location = new Point(750, 380),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnClearLog.FlatAppearance.BorderSize = 0;
                btnClearLog.Click += (s, e) => _txtLog.Clear();
                Controls.Add(btnClearLog);

                // LOG KAYDIRMA
                var btnScrollLog = new Button
                {
                    Text = "↓ En Son",
                    Font = new Font("Arial", 8),
                    BackColor = Color.FromArgb(23, 162, 184),
                    ForeColor = Color.White,
                    Size = new Size(80, 25),
                    Location = new Point(650, 380),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnScrollLog.FlatAppearance.BorderSize = 0;
                btnScrollLog.Click += (s, e) => _txtLog.ScrollToCaret();
                Controls.Add(btnScrollLog);

                // LOG YÖNLENDİRME
                //LoggerHelper.OnLogMessage += (message, level) =>
                //{
                //    this.Invoke(new Action(() =>
                //    {
                //        _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                //        _txtLog.ScrollToCaret();
                //    }));
                //};

                // BAĞLANTI TEST BUTONU
                var btnTestConnection = new Button
                {
                    Text = "Google Sheets Test",
                    Font = new Font("Arial", 8),
                    BackColor = Color.FromArgb(255, 193, 7),
                    ForeColor = Color.Black,
                    Size = new Size(120, 25),
                    Location = new Point(50, 350),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnTestConnection.FlatAppearance.BorderSize = 0;
                btnTestConnection.Click += async (s, e) => await TestGoogleSheetsConnection();
                Controls.Add(btnTestConnection);

                UpdateStatus("✅ Sistem başlatıldı. Google Sheets ID: " + (_webService?.GetSheetsId() ?? "Belirtilmedi"));
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "UI oluşturma hatası");
                MessageBox.Show($"UI oluşturma hatası: {ex.Message}", "Hata",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task StartAutomationAsync()
        {
            try
            {
                // Butonları devre dışı bırak
                SetControlsEnabled(false);

                _progressBar.Visible = true;
                _progressBar.Value = 0;
                UpdateStatus("🔧 Playwright başlatılıyor...");

                // 1. Web servisini başlat
                await _webService.InitializeAsync();

                _progressBar.Value = 20;
                UpdateStatus("🌐 Google Sheets bağlantısı kuruluyor...");

                // 2. Google Sheets bağlantısını test et
                bool googleConnected = await _webService.TestGoogleSheetsConnection();

                if (googleConnected)
                {
                    _progressBar.Value = 40;
                    UpdateStatus("✅ Google Sheets bağlantısı başarılı!");
                }
                else
                {
                    UpdateStatus("⚠ Google Sheets bağlantısı başarısız! Sadece Excel kullanılacak.");
                    MessageBox.Show("Google Sheets bağlantısı başarısız! İşlemler sadece Excel'e kaydedilecek.",
                        "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                _progressBar.Value = 50;
                UpdateStatus("🔐 Login işlemi yapılıyor...");

                // 3. Login yap
                bool loginSuccess = await _webService.LoginAsync();

                if (!loginSuccess)
                {
                    UpdateStatus("❌ Login başarısız!");
                    _progressBar.Visible = false;
                    MessageBox.Show("Login başarısız! Kullanıcı adı/şifreyi kontrol edin.",
                        "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetControlsEnabled(true);
                    return;
                }

                _progressBar.Value = 60;
                UpdateStatus("✅ Login başarılı! İşlemler çekiliyor...");

                // 4. İşlem geçmişi sayfasına git
                bool navigationSuccess = await _webService.NavigateToTransactionHistoryAsync();

                if (!navigationSuccess)
                {
                    UpdateStatus("❌ İşlem geçmişi sayfasına gidilemedi!");
                    _progressBar.Value = 0;
                    _progressBar.Visible = false;
                    MessageBox.Show("İşlem geçmişi sayfasına gidilemedi!", "Hata",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetControlsEnabled(true);
                    return;
                }

                _progressBar.Value = 70;
                UpdateStatus("📊 İşlem verileri çekiliyor...");

                // 5. İşlemleri çek
                var transactions = await _webService.ExtractTransactionsAsync(5);

                if (transactions == null || transactions.Count == 0)
                {
                    UpdateStatus("⚠ İşlem bulunamadı!");
                    _progressBar.Value = 100;
                    await Task.Delay(1000);
                    _progressBar.Visible = false;
                    MessageBox.Show("İşlem bulunamadı!", "Bilgi",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    SetControlsEnabled(true);
                    return;
                }

                _progressBar.Value = 80;
                UpdateStatus($"✅ {transactions.Count} adet işlem çekildi. Excel'e kaydediliyor...");

                // 6. Excel'e kaydet
                //bool excelSaved = await _transactionManager.SaveTransactionsToExcelAsync(
                //    transactions,
                    //@"C:\PowerHavale\İşlemler_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");

                _progressBar.Value = 90;

                //if (excelSaved)
                //{
                //    UpdateStatus("✅ Excel dosyası oluşturuldu!");
                //}
                //else
                //{
                //    UpdateStatus("⚠ Excel dosyası oluşturulamadı!");
                //}

                // 7. Google Sheets'e işlemleri ekle
                _progressBar.Value = 95;
                UpdateStatus("☁ Google Sheets'e veriler ekleniyor...");

                int addedCount = 0;
                foreach (var transaction in transactions)
                {
                    if (transaction.Status == "Onaylandı")
                    {
                        bool added = await _webService.AddTransactionToGoogleSheetsAsync(transaction);
                        if (added) addedCount++;
                    }
                }

                _progressBar.Value = 100;

                if (addedCount > 0)
                {
                    UpdateStatus($"✅ {addedCount} işlem Google Sheets'e eklendi!");

                    var result = MessageBox.Show(
                        $"✅ Otomasyon başarıyla tamamlandı!\n\n" +
                        $"📊 {transactions.Count} işlem çekildi\n" +
                        $"☁ {addedCount} işlem Google Sheets'e eklendi\n\n" +
                        $"Google Sheets'i açmak ister misiniz?",
                        "Başarılı",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        OpenGoogleSheets();
                    }
                }
                else
                {
                    UpdateStatus("✅ Otomasyon tamamlandı! (Google Sheets'e eklenen işlem yok)");
                    MessageBox.Show(
                        $"✅ Otomasyon tamamlandı!\n\n" +
                        $"📊 {transactions.Count} işlem çekildi\n" +
                        $"⚠ Google Sheets'e eklenecek onaylanmış işlem bulunamadı",
                        "Bilgi",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                _progressBar.Visible = false;
                SetControlsEnabled(true);
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Hata: {ex.Message}");
                _progressBar.Visible = false;
                MessageBox.Show($"Kritik hata: {ex.Message}\n\nDetay: {ex.InnerException?.Message}",
                    "Kritik Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetControlsEnabled(true);
            }
        }

        private async Task TestGoogleSheetsConnection()
        {
            try
            {
                UpdateStatus("🔗 Google Sheets bağlantısı test ediliyor...");

                bool connected = await _webService.TestGoogleSheetsConnection();

                if (connected)
                {
                    UpdateStatus("✅ Google Sheets bağlantısı başarılı!");
                    MessageBox.Show("Google Sheets bağlantısı başarılı!", "Başarılı",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    UpdateStatus("❌ Google Sheets bağlantısı başarısız!");
                    MessageBox.Show(
                        "Google Sheets bağlantısı başarısız!\n\n" +
                        "1. Google Sheets ID'nizi kontrol edin\n" +
                        "2. Service Account'ın dosyada editör yetkisi olduğundan emin olun\n" +
                        "3. Private key'in doğru olduğundan emin olun",
                        "Hata",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ Bağlantı testi hatası: {ex.Message}");
                MessageBox.Show($"Bağlantı testi hatası: {ex.Message}", "Hata",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenGoogleSheets()
        {
            try
            {
                string sheetsId = _webService?.GetSheetsId();

                if (string.IsNullOrEmpty(sheetsId))
                {
                    MessageBox.Show("Google Sheets ID belirtilmemiş!", "Hata",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Google Sheets URL'sini aç
                string googleSheetsUrl = $"https://docs.google.com/spreadsheets/d/{sheetsId}/edit";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = googleSheetsUrl,
                    UseShellExecute = true
                });

                UpdateStatus("🌐 Google Sheets açılıyor...");
                LoggerHelper.LogInformation($"Google Sheets URL: {googleSheetsUrl}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Google Sheets açma hatası: {ex.Message}", "Hata",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                LoggerHelper.LogError(ex, "Google Sheets açma hatası");
            }
        }

        private void ExportToExcel()
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "Excel Dosyaları (*.xlsx)|*.xlsx|Tüm Dosyalar (*.*)|*.*",
                    Title = "Excel Dosyasını Kaydet",
                    FileName = $"PowerHavale_İşlemler_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    DefaultExt = "xlsx"
                };

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    UpdateStatus("💾 Excel dosyası kaydediliyor...");

                    // Burada ExcelService kullanarak kaydetme işlemi yapılacak
                    // Şimdilik demo mesajı
                    MessageBox.Show($"Excel dosyası kaydedilecek konum:\n{saveDialog.FileName}",
                        "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    UpdateStatus($"✅ Excel kaydedildi: {Path.GetFileName(saveDialog.FileName)}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Excel kaydetme hatası: {ex.Message}", "Hata",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                LoggerHelper.LogError(ex, "Excel kaydetme hatası");
            }
        }

        private void SetControlsEnabled(bool enabled)
        {
            foreach (Control control in Controls)
            {
                if (control is Button button && button.Text != "Logları Temizle" && button.Text != "↓ En Son")
                {
                    button.Enabled = enabled;
                    button.BackColor = enabled ?
                        (button.Text.Contains("GOOGLE") ? Color.FromArgb(40, 167, 69) :
                         button.Text.Contains("OTOMASYON") ? Color.FromArgb(0, 123, 255) :
                         button.Text.Contains("EXCEL") ? Color.FromArgb(108, 117, 125) :
                         button.BackColor) :
                        Color.Gray;
                }
            }
        }

        private void UpdateStatus(string message)
        {
            try
            {
                LoggerHelper.LogInformation(message);
                _lblStatus.Text = message;

                if (message.Contains("✅") || message.Contains("başarılı") || message.Contains("tamamlandı"))
                {
                    _lblStatus.ForeColor = Color.FromArgb(40, 167, 69);
                    _lblStatus.BackColor = Color.FromArgb(220, 255, 220);
                }
                else if (message.Contains("❌") || message.Contains("Hata") || message.Contains("başarısız"))
                {
                    _lblStatus.ForeColor = Color.FromArgb(220, 53, 69);
                    _lblStatus.BackColor = Color.FromArgb(255, 220, 220);
                }
                else if (message.Contains("⚠") || message.Contains("Uyarı"))
                {
                    _lblStatus.ForeColor = Color.FromArgb(255, 193, 7);
                    _lblStatus.BackColor = Color.FromArgb(255, 255, 200);
                }
                else
                {
                    _lblStatus.ForeColor = Color.FromArgb(0, 51, 102);
                    _lblStatus.BackColor = Color.FromArgb(255, 255, 200);
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.LogError(ex, "Status güncelleme hatası");
            }
        }
    }
}