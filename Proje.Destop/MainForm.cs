using Proje.Business.Managers;
using Proje.Core.Helpers;
using Proje.Data.Services;
using Proje.Models;
using Proje.Service;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Proje.Destop
{
    public partial class MainForm : Form
    {
        private TransactionManager _transactionManager;
        private WebAutomationService _webService;
        private ExcelService _excelService;

        // UI Kontrolleri
        private Button _btnConnectionTest, _btnStartAutomation, _btnRestart, _btnExit, _btnExport;
        private RichTextBox _rtbLog;
        private ProgressBar _progressBar;
        private Label _lblStatus;
        private Panel _controlPanel, _logPanel;
        private TableLayoutPanel _mainLayout;

        public MainForm()
        {
            InitializeComponent();
            InitializeServices();
            SetupUI();
            ApplyCorporateDesign();
        }

        private void InitializeServices()
        {
            // NOT: Hassas bilgileri config dosyasından almanız önerilir
            var credentials = new LoginCredentials
            {
                BasicAuthUsername = "login",
                BasicAuthPassword = "4610",
                FormUsername = "coderysf@gmail.com",
                FormPassword = "Aflex6501.@",
                LoginUrl = "https://online.powerhavale.com/marjin/auth/login"
            };

            var browserConfig = new BrowserConfig
            {
                Headless = false,
                TimeoutSeconds = 30,
                MaxRetryCount = 3
            };

            _webService = new WebAutomationService(credentials, browserConfig);
            _excelService = new ExcelService();
            _transactionManager = new TransactionManager(_webService, _excelService);
        }

        private void SetupUI()
        {
            this.Text = "PowerHavale İşlem Otomasyonu";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(900, 600);

            // Ana layout
            _mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10),
                BackColor = Color.White
            };

            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));  // Kontrol paneli
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));  // Log paneli
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));  // Alt butonlar

            // 1. KONTROL PANELİ (Bağlantı Testi ve Otomasyon Butonları)
            _controlPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(240, 244, 248)
            };

            var controlTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(20)
            };

            // Bağlantı Testi Butonu
            _btnConnectionTest = CreateButton("🔗 BAĞLANTI TESTİ",
                Color.FromArgb(52, 152, 219), Color.White);
            _btnConnectionTest.Click += async (s, e) => await TestConnectionAsync();

            // Otomasyon Başlat Butonu
            _btnStartAutomation = CreateButton("▶️ OTOMASYONU BAŞLAT",
                Color.FromArgb(46, 204, 113), Color.White);
            _btnStartAutomation.Click += async (s, e) => await StartAutomationAsync();

            // Export Butonu
            _btnExport = CreateButton("📊 EXCEL EXPORT",
                Color.FromArgb(155, 89, 182), Color.White);
            _btnExport.Click += (s, e) => ExportToExcel();

            // Durum Göstergesi
            _lblStatus = new Label
            {
                Text = "Sistem Hazır",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };

            controlTable.Controls.Add(_btnConnectionTest, 0, 0);
            controlTable.Controls.Add(_btnStartAutomation, 1, 0);
            controlTable.Controls.Add(_btnExport, 2, 0);
            controlTable.Controls.Add(_lblStatus, 3, 0);

            _controlPanel.Controls.Add(controlTable);

            // 2. LOG PANELİ (Gelişmiş Log Ekranı)
            _logPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle
            };

            var logHeader = new Label
            {
                Text = "İŞLEM LOG KAYITLARI",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(44, 62, 80),
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };

            _rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 9),
                BackColor = Color.FromArgb(25, 25, 25),
                ForeColor = Color.LightGray,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            _logPanel.Controls.Add(_rtbLog);
            _logPanel.Controls.Add(logHeader);

            // 3. ALT BUTON PANELİ (Yeniden Başlat/Kapat)
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(240, 244, 248)
            };

            var bottomTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(150, 10, 150, 10)
            };

            // Progress Bar
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Style = ProgressBarStyle.Continuous,
                ForeColor = Color.FromArgb(52, 152, 219)
            };

            // Yeniden Başlat Butonu
            _btnRestart = CreateButton("🔄 YENİDEN BAŞLAT",
                Color.FromArgb(241, 196, 15), Color.FromArgb(44, 62, 80));
            _btnRestart.Click += (s, e) => RestartApplication();

            // Çıkış Butonu
            _btnExit = CreateButton("⏹️ UYGULAMAYI KAPAT",
                Color.FromArgb(231, 76, 60), Color.White);
            _btnExit.Click += (s, e) => ExitApplication();

            bottomTable.Controls.Add(_progressBar, 0, 0);
            bottomTable.Controls.Add(_btnRestart, 1, 0);
            bottomTable.Controls.Add(_btnExit, 2, 0);

            bottomPanel.Controls.Add(bottomTable);

            // Ana panele ekle
            _mainLayout.Controls.Add(_controlPanel, 0, 0);
            _mainLayout.Controls.Add(_logPanel, 0, 1);
            _mainLayout.Controls.Add(bottomPanel, 0, 2);

            this.Controls.Add(_mainLayout);
        }

        private Button CreateButton(string text, Color backColor, Color foreColor)
        {
            return new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = backColor,
                ForeColor = foreColor,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                Height = 45,
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                Cursor = Cursors.Hand
            };
        }

        private void ApplyCorporateDesign()
        {
            // Kurumsal renk paleti
            this.BackColor = Color.White;

            // Buton hover efektleri
            foreach (Control control in GetAllControls(this))
            {
                if (control is Button btn)
                {
                    btn.MouseEnter += (s, e) =>
                    {
                        btn.BackColor = ControlPaint.Light(btn.BackColor, 0.2f);
                    };
                    btn.MouseLeave += (s, e) =>
                    {
                        btn.BackColor = GetOriginalButtonColor(btn.Text);
                    };
                }
            }
        }

        private IEnumerable<Control> GetAllControls(Control control)
        {
            var controls = new List<Control>();
            foreach (Control ctrl in control.Controls)
            {
                controls.Add(ctrl);
                controls.AddRange(GetAllControls(ctrl));
            }
            return controls;
        }

        private Color GetOriginalButtonColor(string buttonText)
        {
            return buttonText switch
            {
                string t when t.Contains("BAĞLANTI") => Color.FromArgb(52, 152, 219),
                string t when t.Contains("OTOMASYON") => Color.FromArgb(46, 204, 113),
                string t when t.Contains("EXPORT") => Color.FromArgb(155, 89, 182),
                string t when t.Contains("YENİDEN") => Color.FromArgb(241, 196, 15),
                string t when t.Contains("KAPAT") => Color.FromArgb(231, 76, 60),
                _ => Color.SteelBlue
            };
        }

        // 1. BAĞLANTI TESTİ METODU
        private async Task TestConnectionAsync()
        {
            try
            {
                AddLog("Bağlantı testi başlatılıyor...", LogType.Info);
                _btnConnectionTest.Enabled = false;
                _btnConnectionTest.Text = "TEST EDİLİYOR...";

                var isConnected = await _webService.TestConnectionAsync();

                if (isConnected)
                {
                    AddLog("✓ Bağlantı testi başarılı!", LogType.Success);
                    _lblStatus.Text = "Bağlantı Aktif";
                    _lblStatus.ForeColor = Color.FromArgb(46, 204, 113);
                    MessageBox.Show("Bağlantı testi başarılı:\n- İnternet bağlantısı ✓\n- Hedef siteye erişim ✓",
                        "Bağlantı Testi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    AddLog("✗ Bağlantı testi başarısız!", LogType.Error);
                    _lblStatus.Text = "Bağlantı Hatası";
                    _lblStatus.ForeColor = Color.Red;
                    MessageBox.Show("Bağlantı testi başarısız!\nLütfen:\n1. İnternet bağlantınızı kontrol edin\n2. PowerHavale sitesinin erişilebilir olduğundan emin olun",
                        "Bağlantı Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                AddLog($"Bağlantı hatası: {ex.Message}", LogType.Error);
            }
            finally
            {
                _btnConnectionTest.Enabled = true;
                _btnConnectionTest.Text = "🔗 BAĞLANTI TESTİ";
            }
        }

        // 2. OTOMASYON METODU (Güncellendi)
        private async Task StartAutomationAsync()
        {
            try
            {
                AddLog("Otomasyon başlatılıyor...", LogType.Info);
                _progressBar.Visible = true;
                _progressBar.Value = 0;
                _btnStartAutomation.Enabled = false;

                // Web servisini başlat
                await _webService.InitializeAsync();
                _progressBar.Value = 20;
                AddLog("Playwright başlatıldı", LogType.Info);

                // Login işlemi
                AddLog("Login işlemi yapılıyor...", LogType.Info);
                _progressBar.Value = 40;

                // Otomasyonu çalıştır
                var success = await _transactionManager.ExecuteFullAutomationAsync(
                    @"C:\PowerHavale\İşlemler.xlsx", 5);

                _progressBar.Value = 100;

                if (success)
                {
                    AddLog("✓ Otomasyon başarıyla tamamlandı!", LogType.Success);
                    _lblStatus.Text = "Otomasyon Tamamlandı";
                    _lblStatus.ForeColor = Color.FromArgb(46, 204, 113);
                }
                else
                {
                    AddLog("✗ Otomasyon sırasında hata oluştu!", LogType.Error);
                    _lblStatus.Text = "Otomasyon Hatası";
                    _lblStatus.ForeColor = Color.Red;
                }
            }
            catch (Exception ex)
            {
                AddLog($"Kritik hata: {ex.Message}", LogType.Error);
                MessageBox.Show($"Hata: {ex.Message}", "Otomasyon Hatası",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _progressBar.Visible = false;
                _btnStartAutomation.Enabled = true;
            }
        }

        // 3. LOG EKLEME METODU (Renkli ve formatlı)
        private void AddLog(string message, LogType logType = LogType.Info)
        {
            if (_rtbLog.InvokeRequired)
            {
                _rtbLog.Invoke(new Action(() => AddLog(message, logType)));
                return;
            }

            Color color = logType switch
            {
                LogType.Info => Color.LightBlue,
                LogType.Success => Color.LightGreen,
                LogType.Warning => Color.LightGoldenrodYellow,
                LogType.Error => Color.LightCoral,
                _ => Color.LightGray
            };

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string logEntry = $"[{timestamp}] {message}\n";

            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionLength = 0;
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText(logEntry);
            _rtbLog.SelectionColor = _rtbLog.ForeColor;
            _rtbLog.ScrollToCaret();

            // LoggerHelper'a da kaydet
            LoggerHelper.LogInformation($"[{logType}] {message}");
        }

        // 4. UYGULAMAYI YENİDEN BAŞLAT
        private void RestartApplication()
        {
            var result = MessageBox.Show("Uygulamayı yeniden başlatmak istediğinize emin misiniz?",
                "Yeniden Başlat", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                AddLog("Uygulama yeniden başlatılıyor...", LogType.Warning);
                Application.Restart();
                Environment.Exit(0);
            }
        }

        // 5. UYGULAMAYI KAPAT
        private void ExitApplication()
        {
            var result = MessageBox.Show("Uygulamayı kapatmak istediğinize emin misiniz?",
                "Çıkış", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                AddLog("Uygulama kapatılıyor...", LogType.Info);
                Application.Exit();
            }
        }

        private void ExportToExcel()
        {
            // Mevcut export kodunuz buraya
        }
    }

    public enum LogType
    {
        Info,
        Success,
        Warning,
        Error
    }
}