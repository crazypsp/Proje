using Proje.Business.Managers;
using Proje.Core.Helpers;
using Proje.Data.Services;
using Proje.Models;
using Proje.Service;
using System;
using System.Windows.Forms;

namespace Proje.Destop
{
    public partial class MainForm : Form
    {

        private TransactionManager _transactionManager;
        private WebAutomationService _webService;
        private ExcelService _excelService;

        public MainForm()
        {
            InitializeComponent();
            InitializeServices();
            SetupUI();
        }

        private void InitializeServices()
        {
            // Config dosyasından oku (appsettings.json)
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
            this.Size = new System.Drawing.Size(800, 600);

            // Progress Bar
            var progressBar = new ProgressBar
            {
                Dock = DockStyle.Bottom,
                Height = 20,
                Visible = false
            };
            Controls.Add(progressBar);

            // Buttons
            var btnStart = new Button
            {
                Text = "OTOMASYONU BAŞLAT",
                Font = new System.Drawing.Font("Arial", 12, System.Drawing.FontStyle.Bold),
                BackColor = System.Drawing.Color.Green,
                ForeColor = System.Drawing.Color.White,
                Size = new System.Drawing.Size(200, 50),
                Location = new System.Drawing.Point(300, 200)
            };
            btnStart.Click += async (s, e) => await StartAutomationAsync(progressBar);
            Controls.Add(btnStart);

            var btnExport = new Button
            {
                Text = "EXPORT ET",
                Font = new System.Drawing.Font("Arial", 10),
                BackColor = System.Drawing.Color.Blue,
                ForeColor = System.Drawing.Color.White,
                Size = new System.Drawing.Size(150, 40),
                Location = new System.Drawing.Point(325, 270)
            };
            btnExport.Click += (s, e) => ExportToExcel();
            Controls.Add(btnExport);

            // Status Label
            var lblStatus = new Label
            {
                Text = "Hazır",
                Font = new System.Drawing.Font("Arial", 10),
                ForeColor = System.Drawing.Color.DarkGreen,
                Size = new System.Drawing.Size(200, 30),
                Location = new System.Drawing.Point(300, 350),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter
            };
            Controls.Add(lblStatus);

            // Log TextBox
            var txtLog = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Bottom,
                Height = 150,
                ReadOnly = true
            };
            Controls.Add(txtLog);
        }

        private async Task StartAutomationAsync(ProgressBar progressBar)
        {
            try
            {
                // UI güncelleme
                this.Invoke(new Action(() =>
                {
                    progressBar.Visible = true;
                    progressBar.Value = 0;
                    UpdateStatus("Playwright başlatılıyor...");
                }));

                // 1. Web servisini başlat
                await _webService.InitializeAsync();

                this.Invoke(new Action(() =>
                {
                    progressBar.Value = 20;
                    UpdateStatus("Login işlemi yapılıyor...");
                }));

                // 2. Otomasyonu çalıştır
                var success = await _transactionManager.ExecuteFullAutomationAsync(
                    @"C:\PowerHavale\İşlemler.xlsx",
                    5); // 5 sayfa çek

                this.Invoke(new Action(() =>
                {
                    progressBar.Value = 100;
                    UpdateStatus(success ? "Başarıyla tamamlandı!" : "Hata oluştu!");
                    progressBar.Visible = false;

                    if (success)
                    {
                        MessageBox.Show("Otomasyon başarıyla tamamlandı! Excel dosyası oluşturuldu.",
                            "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("Otomasyon sırasında hata oluştu. Lütfen logları kontrol edin.",
                            "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }));
            }
            catch (Exception ex)
            {
                this.Invoke(new Action(() =>
                {
                    UpdateStatus($"Hata: {ex.Message}");
                    MessageBox.Show($"Kritik hata: {ex.Message}\n\nDetay: {ex.StackTrace}",
                        "Kritik Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
            }
        }

        private void ExportToExcel()
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "Excel Files|*.xlsx",
                Title = "Excel Dosyasını Kaydet",
                FileName = $"İşlemler_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            };

            if (saveDialog.ShowDialog() == DialogResult.OK)
            {
                // Manual export işlemi
                // (Kullanıcı daha önce çekilmiş verileri export edebilir)
            }
        }

        private void UpdateStatus(string message)
        {
            LoggerHelper.LogInformation(message);

            // UI'daki status label'ını güncelle
            foreach (Control control in Controls)
            {
                if (control is Label label && label.Text.StartsWith("Hazır"))
                {
                    label.Text = message;

                    if (message.Contains("Hata") || message.Contains("başarısız"))
                        label.ForeColor = System.Drawing.Color.Red;
                    else if (message.Contains("tamamlandı"))
                        label.ForeColor = System.Drawing.Color.DarkGreen;
                    else
                        label.ForeColor = System.Drawing.Color.Blue;

                    break;
                }
            }
        }
    }
}
