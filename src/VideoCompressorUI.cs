using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace VideoToolsUI {
    public class Program {
        [STAThread]
        public static void Main(string[] args) {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                try {
                    File.WriteAllText(System.IO.Path.Combine(baseDir, "crash.log"), e.ExceptionObject.ToString());
                } catch { }
            };

            try {
                var app = new Application();
                var mainWindow = new MainWindow(args);
                app.Run(mainWindow);
            } catch (Exception ex) {
                try {
                    File.WriteAllText(System.IO.Path.Combine(baseDir, "crash.log"), ex.ToString());
                } catch { }
                MessageBox.Show("Fatal Error launching Video Compressor Pro:\n\n" + ex.ToString(),
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class VideoItem {
        public string FilePath { get; set; }
        public string FileName { get { return System.IO.Path.GetFileName(FilePath); } }
        public double SizeMB { get; set; }
        public double DurationSeconds { get; set; }
        public string DurationText { get; set; }
        public string Resolution { get; set; }
        public string Status { get; set; }

        public VideoItem() {
            Status = "Ready";
        }
    }

    public class LicenseInfo {
        public string Key { get; set; }
        public string Tier { get; set; }
        public string Status { get; set; }
        public DateTime Expires { get; set; }
        public string Hwid { get; set; }
        public string CustomerName { get; set; }

        public bool IsValid {
            get {
                return string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase) &&
                       DateTime.UtcNow <= Expires;
            }
        }

        public LicenseInfo() {
            Key = "";
            Tier = "Free";
            Status = "unlicensed";
            Expires = DateTime.MinValue;
            Hwid = "";
            CustomerName = "";
        }
    }

    public static class LicenseManager {
        // Change this URL to your Secret GitHub Gist raw URL when ready:
        public static string OnlineLicenseUrl = "https://gist.githubusercontent.com/raw/licenses.json";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VCP_Lic_Salt_2026!#");

        public static string GetMachineHWID() {
            try {
                string raw = Environment.MachineName + "|" + Environment.ProcessorCount + "|" + Environment.OSVersion.VersionString;
                using (var sha = SHA256.Create()) {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                    string hex = BitConverter.ToString(hash).Replace("-", "");
                    return "HWID-" + hex.Substring(0, 4) + "-" + hex.Substring(4, 4) + "-" + hex.Substring(8, 4);
                }
            } catch {
                return "HWID-DEFAULT-0001";
            }
        }

        private static string GetLicenseFilePath() {
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoTools");
            if (!Directory.Exists(folder)) {
                try { Directory.CreateDirectory(folder); } catch { }
            }
            return System.IO.Path.Combine(folder, "license.dat");
        }

        public static LicenseInfo LoadLocalLicense() {
            string path = GetLicenseFilePath();
            if (!File.Exists(path)) return null;

            try {
                byte[] encrypted = File.ReadAllBytes(path);
                byte[] raw = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                string text = Encoding.UTF8.GetString(raw);
                string[] parts = text.Split('|');
                if (parts.Length >= 5) {
                    var lic = new LicenseInfo();
                    lic.Key = parts[0];
                    lic.Tier = parts[1];
                    lic.Status = parts[2];
                    DateTime dt;
                    if (DateTime.TryParse(parts[3], null, DateTimeStyles.RoundtripKind, out dt)) {
                        lic.Expires = dt;
                    }
                    lic.Hwid = parts[4];
                    if (parts.Length >= 6) lic.CustomerName = parts[5];

                    // Verify HWID matches this computer
                    string currentHwid = GetMachineHWID();
                    if (!string.Equals(lic.Hwid, "ANY", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(lic.Hwid, currentHwid, StringComparison.OrdinalIgnoreCase)) {
                        return null; // Bound to a different computer
                    }

                    return lic;
                }
            } catch { }

            return null;
        }

        public static void SaveLocalLicense(LicenseInfo lic) {
            try {
                string text = string.Format("{0}|{1}|{2}|{3}|{4}|{5}",
                    lic.Key, lic.Tier, lic.Status, lic.Expires.ToString("o"), lic.Hwid, lic.CustomerName);
                byte[] raw = Encoding.UTF8.GetBytes(text);
                byte[] encrypted = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(GetLicenseFilePath(), encrypted);
            } catch { }
        }

        public static void ClearLocalLicense() {
            try {
                string path = GetLicenseFilePath();
                if (File.Exists(path)) File.Delete(path);
            } catch { }
        }

        public static async Task<LicenseInfo> VerifyKeyAsync(string inputKey) {
            if (string.IsNullOrEmpty(inputKey)) {
                throw new ArgumentException("Please enter a valid license key.");
            }

            string cleanKey = inputKey.Trim().ToUpperInvariant();
            string myHwid = GetMachineHWID();

            // 1. Built-in Master Demo / Developer Keys for immediate testing
            if (cleanKey == "VCP-PRO-TRIAL-2026") {
                return new LicenseInfo {
                    Key = cleanKey,
                    Tier = "Pro Subscription",
                    Status = "active",
                    Expires = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                    Hwid = myHwid,
                    CustomerName = "Trial Customer"
                };
            }

            if (cleanKey == "VCP-LIFETIME-VIP") {
                return new LicenseInfo {
                    Key = cleanKey,
                    Tier = "Lifetime Enterprise",
                    Status = "active",
                    Expires = new DateTime(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Hwid = myHwid,
                    CustomerName = "VIP Subscriber"
                };
            }

            // 2. Online verification against GitHub Gist or custom URL
            try {
                using (var wc = new WebClient()) {
                    wc.Headers[HttpRequestHeader.UserAgent] = "VideoCompressorPro/1.0";
                    string json = await wc.DownloadStringTaskAsync(new Uri(OnlineLicenseUrl));

                    // Lightweight parser for key block
                    if (!json.Contains(cleanKey)) {
                        throw new Exception("License key not found in activation database.");
                    }

                    // Extract block for cleanKey
                    int keyIdx = json.IndexOf(cleanKey);
                    int blockStart = json.IndexOf('{', keyIdx);
                    int blockEnd = json.IndexOf('}', blockStart);
                    if (blockStart < 0 || blockEnd < 0) {
                        throw new Exception("Invalid license configuration format.");
                    }

                    string block = json.Substring(blockStart, blockEnd - blockStart + 1);

                    string status = ExtractJsonVal(block, "status");
                    string tier = ExtractJsonVal(block, "tier");
                    string expiresStr = ExtractJsonVal(block, "expires");
                    string hwid = ExtractJsonVal(block, "hwid");
                    string name = ExtractJsonVal(block, "name");

                    if (!string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)) {
                        throw new Exception(string.Format("License is {0}. Please renew your subscription.", status.ToUpperInvariant()));
                    }

                    DateTime expDate;
                    if (!DateTime.TryParse(expiresStr, out expDate) || DateTime.UtcNow > expDate) {
                        throw new Exception("Subscription expired on " + expiresStr + ".");
                    }

                    if (!string.IsNullOrEmpty(hwid) &&
                        !string.Equals(hwid, "ANY", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(hwid, myHwid, StringComparison.OrdinalIgnoreCase)) {
                        throw new Exception("License is locked to another computer (" + hwid + ").");
                    }

                    return new LicenseInfo {
                        Key = cleanKey,
                        Tier = string.IsNullOrEmpty(tier) ? "Pro" : tier,
                        Status = "active",
                        Expires = expDate,
                        Hwid = string.IsNullOrEmpty(hwid) || hwid == "ANY" ? myHwid : hwid,
                        CustomerName = name
                    };
                }
            } catch (WebException wex) {
                throw new Exception("Could not connect to license server. Check internet connection: " + wex.Message);
            }
        }

        private static string ExtractJsonVal(string json, string field) {
            var match = Regex.Match(json, "\"" + field + "\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "";
        }

        public static void ExecuteCleanErase() {
            try {
                // 1. Delete local license
                ClearLocalLicense();

                // 2. Remove Desktop shortcut
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string lnk = System.IO.Path.Combine(desktop, "Video Compressor Pro.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);

                // 3. Remove Registry context menu associations
                string[] videoExts = new string[] {
                    ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".flv",
                    ".m4v", ".ts", ".mts", ".m2ts", ".3gp", ".3g2", ".mpeg",
                    ".mpg", ".vob", ".ogv", ".divx", ".asf", ".f4v", "video"
                };

                using (var root = Registry.CurrentUser.OpenSubKey("Software\\Classes\\SystemFileAssociations", true)) {
                    if (root != null) {
                        foreach (var ext in videoExts) {
                            try {
                                using (var extKey = root.OpenSubKey(ext + "\\shell", true)) {
                                    if (extKey != null) {
                                        extKey.DeleteSubKeyTree("VideoCompressorPro", false);
                                    }
                                }
                            } catch { }
                        }
                    }
                }

                // 4. Detached background process to wipe the entire installation directory after process exits
                string installDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoTools");
                string cmdArgs = string.Format("/c ping 127.0.0.1 -n 2 > nul & rmdir /s /q \"{0}\"", installDir);

                Process.Start(new ProcessStartInfo {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true
                });

                // Exit immediately so directory is unlocked for deletion
                Environment.Exit(0);
            } catch (Exception ex) {
                MessageBox.Show("Error during uninstall: " + ex.Message);
            }
        }
    }

    public class MainWindow : Window {
        private string ffmpegPath = "";
        private string ffprobePath = "";
        private List<VideoItem> videoQueue = new List<VideoItem>();
        private VideoItem currentVideo = null;
        private CancellationTokenSource cts = null;
        private Process activeProcess = null;

        // Licensing
        private LicenseInfo activeLicense = null;
        private DispatcherTimer heartbeatTimer;

        // License Overlay Controls
        private Grid mainAppPanel;
        private Grid licenseOverlay;
        private Border badgeLicense;
        private TextBlock txtLicenseBadge;
        private TextBlock txtMachineHwid;
        private Button btnCopyHwid;
        private TextBox txtLicenseKey;
        private Button btnActivateLicense;
        private TextBlock txtLicenseStatus;
        private TextBlock txtLicenseDetails;
        private Button btnCloseLicense;
        private Button btnEraseApp;

        // UI Controls (looked up from XAML)
        private Border dropZone;
        private TextBlock dropZoneTitle;
        private TextBlock dropZoneSub;
        private StackPanel fileInfoCard;
        private TextBlock txtFileName;
        private TextBlock badgeSize;
        private TextBlock badgeDuration;
        private TextBlock badgeRes;
        private Button btnBrowse;
        private Button btnChangeFile;

        // Mode Navigation
        private RadioButton tabSize;
        private RadioButton tabFormat;
        private RadioButton tabAudio;
        private StackPanel panelSize;
        private StackPanel panelFormat;
        private StackPanel panelAudio;

        // Size Mode Controls
        private Slider sliderTargetSize;
        private TextBlock txtTargetSizeVal;
        private Button btnPreset8;
        private Button btnPreset10;
        private Button btnPreset25;
        private Button btnPreset50;
        private Button btnPreset100;
        private RadioButton rbTurboFast;
        private RadioButton rbPrecision;
        private ComboBox cmbResolution;

        // Format Mode Controls
        private ComboBox cmbFormat;
        private ComboBox cmbCodec;
        private Slider sliderQuality;
        private TextBlock txtQualityVal;

        // Audio Mode Controls
        private ComboBox cmbAudioFmt;
        private ComboBox cmbAudioBitrate;

        // Action & Progress
        private Button btnStart;
        private Border progressCard;
        private ProgressBar progressBar;
        private TextBlock txtProgressPercent;
        private TextBlock txtProgressStatus;
        private TextBlock txtProgressDetails;
        private Button btnCancel;

        // Result Card
        private Border resultCard;
        private TextBlock txtResultTitle;
        private TextBlock txtResultDetails;
        private TextBlock txtResultSavings;
        private Button btnOpenFolder;
        private Button btnPlayVideo;
        private string lastOutputFile = "";

        // Status bar
        private TextBlock txtEngineStatus;

        public MainWindow(string[] initialArgs) {
            InitializeComponent();
            LocateFFmpeg();
            InitializeLicensing();

            if (initialArgs != null && initialArgs.Length > 0 && activeLicense != null && activeLicense.IsValid) {
                LoadFile(initialArgs[0]);
            }
        }

        private void InitializeLicensing() {
            string hwid = LicenseManager.GetMachineHWID();
            txtMachineHwid.Text = hwid;

            activeLicense = LicenseManager.LoadLocalLicense();
            if (activeLicense != null && activeLicense.IsValid) {
                SetLicenseActiveState(activeLicense);
            } else {
                SetLicenseLockedState();
            }

            // Heartbeat: checks every 6 hours while running
            heartbeatTimer = new DispatcherTimer();
            heartbeatTimer.Interval = TimeSpan.FromHours(6);
            heartbeatTimer.Tick += async (s, e) => {
                if (activeLicense != null && !string.IsNullOrEmpty(activeLicense.Key)) {
                    try {
                        var updated = await LicenseManager.VerifyKeyAsync(activeLicense.Key);
                        activeLicense = updated;
                        LicenseManager.SaveLocalLicense(updated);
                    } catch (Exception ex) {
                        // If revoked or expired online
                        if (ex.Message.Contains("revoked") || ex.Message.Contains("expired")) {
                            LicenseManager.ClearLocalLicense();
                            activeLicense = null;
                            SetLicenseLockedState();
                            MessageBox.Show("Your subscription status has changed:\n\n" + ex.Message,
                                "Subscription Inactive", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
            };
            heartbeatTimer.Start();
        }

        private void SetLicenseActiveState(LicenseInfo lic) {
            activeLicense = lic;
            txtLicenseBadge.Text = "PRO ACTIVE";
            badgeLicense.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));

            txtLicenseStatus.Text = "✅ Subscription Active (" + lic.Tier + ")";
            txtLicenseStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
            txtLicenseDetails.Text = string.Format("Licensed to: {0}\nKey: {1}\nExpires: {2:yyyy-MM-dd HH:mm} UTC",
                string.IsNullOrEmpty(lic.CustomerName) ? "Pro Customer" : lic.CustomerName,
                lic.Key, lic.Expires);

            btnCloseLicense.Visibility = Visibility.Visible;
            licenseOverlay.Visibility = Visibility.Collapsed;
            mainAppPanel.Visibility = Visibility.Visible;
        }

        private void SetLicenseLockedState() {
            activeLicense = null;
            txtLicenseBadge.Text = "ACTIVATE PRO";
            badgeLicense.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));

            txtLicenseStatus.Text = "🔒 Subscription Inactive — License Required";
            txtLicenseStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
            txtLicenseDetails.Text = "Enter your license key to unlock the application.\n(Tip for testing: enter VCP-PRO-TRIAL-2026)";

            btnCloseLicense.Visibility = Visibility.Collapsed;
            mainAppPanel.Visibility = Visibility.Collapsed;
            licenseOverlay.Visibility = Visibility.Visible;
        }

        private void InitializeComponent() {
            this.Title = "Video Compressor & Converter Pro";
            this.Width = 940;
            this.Height = 760;
            this.MinWidth = 860;
            this.MinHeight = 670;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B0F19"));
            this.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
            this.FontFamily = new FontFamily("Segoe UI, -apple-system, Roboto, Helvetica, Arial, sans-serif");

            // Build UI via XAML
            string xaml = GetXamlLayout();
            var content = (FrameworkElement)XamlReader.Parse(xaml);
            this.Content = content;

            // License Overlay Controls
            mainAppPanel = (Grid)content.FindName("MainAppPanel");
            licenseOverlay = (Grid)content.FindName("LicenseOverlay");
            badgeLicense = (Border)content.FindName("BadgeLicense");
            txtLicenseBadge = (TextBlock)content.FindName("TxtLicenseBadge");
            txtMachineHwid = (TextBlock)content.FindName("TxtMachineHwid");
            btnCopyHwid = (Button)content.FindName("BtnCopyHwid");
            txtLicenseKey = (TextBox)content.FindName("TxtLicenseKey");
            btnActivateLicense = (Button)content.FindName("BtnActivateLicense");
            txtLicenseStatus = (TextBlock)content.FindName("TxtLicenseStatus");
            txtLicenseDetails = (TextBlock)content.FindName("TxtLicenseDetails");
            btnCloseLicense = (Button)content.FindName("BtnCloseLicense");
            btnEraseApp = (Button)content.FindName("BtnEraseApp");

            // Core UI Controls
            dropZone = (Border)content.FindName("DropZone");
            dropZoneTitle = (TextBlock)content.FindName("DropZoneTitle");
            dropZoneSub = (TextBlock)content.FindName("DropZoneSub");
            fileInfoCard = (StackPanel)content.FindName("FileInfoCard");
            txtFileName = (TextBlock)content.FindName("TxtFileName");
            badgeSize = (TextBlock)content.FindName("BadgeSize");
            badgeDuration = (TextBlock)content.FindName("BadgeDuration");
            badgeRes = (TextBlock)content.FindName("BadgeRes");
            btnBrowse = (Button)content.FindName("BtnBrowse");
            btnChangeFile = (Button)content.FindName("BtnChangeFile");

            tabSize = (RadioButton)content.FindName("TabSize");
            tabFormat = (RadioButton)content.FindName("TabFormat");
            tabAudio = (RadioButton)content.FindName("TabAudio");
            panelSize = (StackPanel)content.FindName("PanelSize");
            panelFormat = (StackPanel)content.FindName("PanelFormat");
            panelAudio = (StackPanel)content.FindName("PanelAudio");

            sliderTargetSize = (Slider)content.FindName("SliderTargetSize");
            txtTargetSizeVal = (TextBlock)content.FindName("TxtTargetSizeVal");
            btnPreset8 = (Button)content.FindName("BtnPreset8");
            btnPreset10 = (Button)content.FindName("BtnPreset10");
            btnPreset25 = (Button)content.FindName("BtnPreset25");
            btnPreset50 = (Button)content.FindName("BtnPreset50");
            btnPreset100 = (Button)content.FindName("BtnPreset100");
            rbTurboFast = (RadioButton)content.FindName("RbTurboFast");
            rbPrecision = (RadioButton)content.FindName("RbPrecision");
            cmbResolution = (ComboBox)content.FindName("CmbResolution");

            cmbFormat = (ComboBox)content.FindName("CmbFormat");
            cmbCodec = (ComboBox)content.FindName("CmbCodec");
            sliderQuality = (Slider)content.FindName("SliderQuality");
            txtQualityVal = (TextBlock)content.FindName("TxtQualityVal");

            cmbAudioFmt = (ComboBox)content.FindName("CmbAudioFmt");
            cmbAudioBitrate = (ComboBox)content.FindName("CmbAudioBitrate");

            btnStart = (Button)content.FindName("BtnStart");
            progressCard = (Border)content.FindName("ProgressCard");
            progressBar = (ProgressBar)content.FindName("ProgressBar");
            txtProgressPercent = (TextBlock)content.FindName("TxtProgressPercent");
            txtProgressStatus = (TextBlock)content.FindName("TxtProgressStatus");
            txtProgressDetails = (TextBlock)content.FindName("TxtProgressDetails");
            btnCancel = (Button)content.FindName("BtnCancel");

            resultCard = (Border)content.FindName("ResultCard");
            txtResultTitle = (TextBlock)content.FindName("TxtResultTitle");
            txtResultDetails = (TextBlock)content.FindName("TxtResultDetails");
            txtResultSavings = (TextBlock)content.FindName("TxtResultSavings");
            btnOpenFolder = (Button)content.FindName("BtnOpenFolder");
            btnPlayVideo = (Button)content.FindName("BtnPlayVideo");

            txtEngineStatus = (TextBlock)content.FindName("TxtEngineStatus");

            // Attach License Events
            badgeLicense.MouseDown += (s, e) => {
                if (licenseOverlay.Visibility == Visibility.Visible) {
                    if (activeLicense != null && activeLicense.IsValid) {
                        licenseOverlay.Visibility = Visibility.Collapsed;
                        mainAppPanel.Visibility = Visibility.Visible;
                    }
                } else {
                    licenseOverlay.Visibility = Visibility.Visible;
                    mainAppPanel.Visibility = Visibility.Collapsed;
                }
            };

            btnCopyHwid.Click += (s, e) => {
                try {
                    Clipboard.SetText(txtMachineHwid.Text);
                    btnCopyHwid.Content = "✓ Copied!";
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    t.Tick += (ts, te) => { btnCopyHwid.Content = "📋 Copy HWID"; t.Stop(); };
                    t.Start();
                } catch { }
            };

            btnActivateLicense.Click += async (s, e) => {
                string key = txtLicenseKey.Text.Trim();
                if (string.IsNullOrEmpty(key)) {
                    txtLicenseStatus.Text = "⚠️ Please enter a license key.";
                    txtLicenseStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
                    return;
                }

                btnActivateLicense.IsEnabled = false;
                btnActivateLicense.Content = "⏳ Verifying...";
                txtLicenseStatus.Text = "Checking subscription status...";
                txtLicenseStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));

                try {
                    var lic = await LicenseManager.VerifyKeyAsync(key);
                    LicenseManager.SaveLocalLicense(lic);
                    SetLicenseActiveState(lic);
                    MessageBox.Show("🎉 Subscription Activated Successfully!\n\nTier: " + lic.Tier + "\nExpires: " + lic.Expires.ToString("yyyy-MM-dd"),
                        "Activation Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    txtLicenseStatus.Text = "❌ " + ex.Message;
                    txtLicenseStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                } finally {
                    btnActivateLicense.IsEnabled = true;
                    btnActivateLicense.Content = "⚡ Activate License";
                }
            };

            btnCloseLicense.Click += (s, e) => {
                if (activeLicense != null && activeLicense.IsValid) {
                    licenseOverlay.Visibility = Visibility.Collapsed;
                    mainAppPanel.Visibility = Visibility.Visible;
                }
            };

            btnEraseApp.Click += (s, e) => {
                var res = MessageBox.Show(
                    "⚠️ DANGER: COMPLETE UNINSTALL & SELF-CLEAN\n\n" +
                    "This will permanently erase:\n" +
                    " • Video Compressor Pro application & all downloaded tools\n" +
                    " • All saved license data on this PC\n" +
                    " • Desktop shortcuts & Windows context menu entries\n\n" +
                    "Are you completely sure you want to erase everything from this PC?",
                    "Confirm Clean Erase",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (res == MessageBoxResult.Yes) {
                    LicenseManager.ExecuteCleanErase();
                }
            };

            // Attach App Drag & Drop Events
            this.AllowDrop = true;
            this.DragOver += (s, e) => {
                if (activeLicense == null || !activeLicense.IsValid) {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
                    e.Effects = DragDropEffects.Copy;
                    dropZone.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1"));
                } else {
                    e.Effects = DragDropEffects.None;
                }
                e.Handled = true;
            };
            this.DragLeave += (s, e) => {
                dropZone.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
            };
            this.Drop += (s, e) => {
                dropZone.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                if (activeLicense == null || !activeLicense.IsValid) return;

                if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0) {
                        LoadFile(files[0]);
                    }
                }
            };

            btnBrowse.Click += (s, e) => BrowseVideo();
            btnChangeFile.Click += (s, e) => BrowseVideo();

            // Tabs
            tabSize.Checked += (s, e) => SwitchTab(1);
            tabFormat.Checked += (s, e) => SwitchTab(2);
            tabAudio.Checked += (s, e) => SwitchTab(3);

            // Size presets
            btnPreset8.Click += (s, e) => SetTargetSize(8);
            btnPreset10.Click += (s, e) => SetTargetSize(10);
            btnPreset25.Click += (s, e) => SetTargetSize(25);
            btnPreset50.Click += (s, e) => SetTargetSize(50);
            btnPreset100.Click += (s, e) => SetTargetSize(100);

            sliderTargetSize.ValueChanged += (s, e) => {
                txtTargetSizeVal.Text = string.Format("{0:0.0} MB", sliderTargetSize.Value);
            };

            sliderQuality.ValueChanged += (s, e) => {
                txtQualityVal.Text = string.Format("CRF {0} ({1})", (int)sliderQuality.Value,
                    sliderQuality.Value <= 22 ? "High Quality" : (sliderQuality.Value <= 26 ? "Balanced" : "High Compression"));
            };

            btnStart.Click += async (s, e) => await StartProcessing();
            btnCancel.Click += (s, e) => CancelProcessing();

            btnOpenFolder.Click += (s, e) => {
                if (!string.IsNullOrEmpty(lastOutputFile) && File.Exists(lastOutputFile)) {
                    Process.Start("explorer.exe", string.Format("/select,\"{0}\"", lastOutputFile));
                }
            };

            btnPlayVideo.Click += (s, e) => {
                if (!string.IsNullOrEmpty(lastOutputFile) && File.Exists(lastOutputFile)) {
                    Process.Start(new ProcessStartInfo(lastOutputFile) { UseShellExecute = true });
                }
            };
        }

        private void SwitchTab(int tabIndex) {
            panelSize.Visibility = (tabIndex == 1) ? Visibility.Visible : Visibility.Collapsed;
            panelFormat.Visibility = (tabIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
            panelAudio.Visibility = (tabIndex == 3) ? Visibility.Visible : Visibility.Collapsed;

            if (tabIndex == 1) btnStart.Content = "⚡ Compress Video";
            else if (tabIndex == 2) btnStart.Content = "🔄 Convert Video";
            else btnStart.Content = "🎵 Extract Audio";
        }

        private void SetTargetSize(double mb) {
            sliderTargetSize.Value = mb;
        }

        private void BrowseVideo() {
            var ofd = new OpenFileDialog {
                Title = "Select Video File",
                Filter = "Video Files|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.wmv;*.flv;*.ts;*.m4v|All Files|*.*"
            };
            if (ofd.ShowDialog() == true) {
                LoadFile(ofd.FileName);
            }
        }

        public async void LoadFile(string path) {
            if (!File.Exists(path)) return;

            currentVideo = new VideoItem { FilePath = path };
            txtFileName.Text = currentVideo.FileName;

            FileInfo fi = new FileInfo(path);
            currentVideo.SizeMB = fi.Length / (1024.0 * 1024.0);
            badgeSize.Text = string.Format("{0:0.0} MB", currentVideo.SizeMB);

            dropZone.Visibility = Visibility.Collapsed;
            fileInfoCard.Visibility = Visibility.Visible;
            btnStart.IsEnabled = true;

            // Probe duration & resolution
            await ProbeVideoAsync(path);

            // Auto-adjust target size slider recommendation
            if (currentVideo.SizeMB > 15) {
                double target = Math.Max(8.0, Math.Round(currentVideo.SizeMB * 0.45));
                sliderTargetSize.Value = Math.Min(target, 100.0);
            }
        }

        private async Task ProbeVideoAsync(string path) {
            if (string.IsNullOrEmpty(ffprobePath) || !File.Exists(ffprobePath)) {
                badgeDuration.Text = "Ready";
                badgeRes.Text = "Standard";
                return;
            }

            try {
                var psi = new ProcessStartInfo {
                    FileName = ffprobePath,
                    Arguments = string.Format("-v error -select_streams v:0 -show_entries stream=width,height,duration -show_entries format=duration -of default=noprint_wrappers=1 \"{0}\"", path),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi)) {
                    string output = await proc.StandardOutput.ReadToEndAsync();
                    proc.WaitForExit();

                    double dur = 0;
                    int width = 0;
                    int height = 0;

                    foreach (var line in output.Split('\n')) {
                        var parts = line.Trim().Split('=');
                        if (parts.Length == 2) {
                            if (parts[0] == "duration" && dur == 0) {
                                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out dur);
                            } else if (parts[0] == "width") {
                                int.TryParse(parts[1], out width);
                            } else if (parts[0] == "height") {
                                int.TryParse(parts[1], out height);
                            }
                        }
                    }

                    currentVideo.DurationSeconds = dur;
                    if (dur > 0) {
                        TimeSpan ts = TimeSpan.FromSeconds(dur);
                        currentVideo.DurationText = ts.Hours > 0
                            ? string.Format("{0:D2}:{1:D2}:{2:D2}", ts.Hours, ts.Minutes, ts.Seconds)
                            : string.Format("{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
                        badgeDuration.Text = currentVideo.DurationText;
                    } else {
                        badgeDuration.Text = "N/A";
                    }

                    if (width > 0 && height > 0) {
                        currentVideo.Resolution = string.Format("{0}x{1}", width, height);
                        badgeRes.Text = currentVideo.Resolution;
                    }
                }
            } catch {
                badgeDuration.Text = "Detected";
                badgeRes.Text = "Auto";
            }
        }

        private void LocateFFmpeg() {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] candidates = new string[] {
                System.IO.Path.Combine(baseDir, "bin", "ffmpeg.exe"),
                System.IO.Path.Combine(baseDir, "ffmpeg.exe"),
                System.IO.Path.Combine(localApp, "VideoTools", "bin", "ffmpeg.exe"),
                @"d:\Apps\VideoTools\bin\ffmpeg.exe",
                @"d:\Apps\ffmpeg\bin\ffmpeg.exe"
            };

            foreach (var c in candidates) {
                if (File.Exists(c)) {
                    ffmpegPath = c;
                    break;
                }
            }

            if (string.IsNullOrEmpty(ffmpegPath)) {
                // Check system PATH
                try {
                    var proc = Process.Start(new ProcessStartInfo("where.exe", "ffmpeg") {
                        UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
                    });
                    string outStr = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();
                    if (!string.IsNullOrEmpty(outStr)) {
                        string first = outStr.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                        if (File.Exists(first)) ffmpegPath = first;
                    }
                } catch { }
            }

            if (!string.IsNullOrEmpty(ffmpegPath)) {
                ffprobePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ffmpegPath), "ffprobe.exe");
                txtEngineStatus.Text = "● FFmpeg Engine Ready";
                txtEngineStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
            } else {
                txtEngineStatus.Text = "⚠️ FFmpeg Missing";
                txtEngineStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
            }
        }

        private async Task StartProcessing() {
            if (activeLicense == null || !activeLicense.IsValid) {
                MessageBox.Show("Active subscription required to compress or convert videos.\nPlease activate your license.",
                    "Subscription Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetLicenseLockedState();
                return;
            }

            if (currentVideo == null || !File.Exists(currentVideo.FilePath)) {
                MessageBox.Show("Please select or drop a valid video file first.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath)) {
                MessageBox.Show("FFmpeg conversion engine was not found on this computer.\nPlease run Setup.bat to install FFmpeg.", "Engine Missing", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            btnStart.IsEnabled = false;
            progressCard.Visibility = Visibility.Visible;
            resultCard.Visibility = Visibility.Collapsed;
            progressBar.Value = 0;
            txtProgressPercent.Text = "0%";
            txtProgressStatus.Text = "Initializing conversion...";
            txtProgressDetails.Text = "Preparing multi-threaded encoding engine...";

            cts = new CancellationTokenSource();

            string inputFile = currentVideo.FilePath;
            string inputDir = System.IO.Path.GetDirectoryName(inputFile);
            string inputBase = System.IO.Path.GetFileNameWithoutExtension(inputFile);

            try {
                if (tabSize.IsChecked == true) {
                    double targetMB = sliderTargetSize.Value;
                    bool isTurbo = rbTurboFast.IsChecked == true;
                    string resOpt = ((ComboBoxItem)cmbResolution.SelectedItem).Content.ToString();
                    string outputFile = System.IO.Path.Combine(inputDir, string.Format("{0}_compressed_{1:0}MB.mp4", inputBase, targetMB));

                    await ProcessTargetSize(inputFile, outputFile, targetMB, isTurbo, resOpt, cts.Token);
                } else if (tabFormat.IsChecked == true) {
                    string fmt = ((ComboBoxItem)cmbFormat.SelectedItem).Content.ToString().ToLower().Split(' ')[0];
                    string codec = ((ComboBoxItem)cmbCodec.SelectedItem).Content.ToString();
                    int crf = (int)sliderQuality.Value;
                    string outputFile = System.IO.Path.Combine(inputDir, string.Format("{0}_converted.{1}", inputBase, fmt));

                    await ProcessFormatConvert(inputFile, outputFile, fmt, codec, crf, cts.Token);
                } else {
                    string fmt = ((ComboBoxItem)cmbAudioFmt.SelectedItem).Content.ToString().ToLower().Split(' ')[0];
                    string bitrate = ((ComboBoxItem)cmbAudioBitrate.SelectedItem).Content.ToString().Split(' ')[0];
                    string outputFile = System.IO.Path.Combine(inputDir, string.Format("{0}_audio.{1}", inputBase, fmt));

                    await ProcessExtractAudio(inputFile, outputFile, fmt, bitrate, cts.Token);
                }
            } catch (OperationCanceledException) {
                txtProgressStatus.Text = "Operation Cancelled.";
                txtProgressDetails.Text = "Cancelled by user.";
            } catch (Exception ex) {
                txtProgressStatus.Text = "Error during processing!";
                txtProgressDetails.Text = ex.Message;
                MessageBox.Show("Error during compression:\n\n" + ex.Message, "Encoding Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                btnStart.IsEnabled = true;
                if (activeProcess != null) {
                    try { if (!activeProcess.HasExited) activeProcess.Kill(); } catch { }
                    activeProcess = null;
                }
            }
        }

        private void CancelProcessing() {
            if (cts != null) cts.Cancel();
            if (activeProcess != null) {
                try {
                    if (!activeProcess.HasExited) activeProcess.Kill();
                } catch { }
            }
        }

        private async Task ProcessTargetSize(string inputFile, string outputFile, double targetMB, bool isTurbo, string resOpt, CancellationToken token) {
            double duration = currentVideo.DurationSeconds > 0 ? currentVideo.DurationSeconds : 60.0;
            double targetBits = targetMB * 8.0 * 1024.0 * 1024.0;
            double totalKbps = (targetBits / duration) / 1024.0;

            int audioKbps = 96;
            if (totalKbps > 1500) audioKbps = 128;
            if (totalKbps < 300) audioKbps = 64;

            int videoKbps = (int)(totalKbps - audioKbps);
            if (videoKbps < 100) videoKbps = 100;

            string scaleFilter = "";
            if (resOpt.Contains("720p")) scaleFilter = "-vf \"scale='min(1280,iw)':-2\"";
            else if (resOpt.Contains("480p")) scaleFilter = "-vf \"scale='min(854,iw)':-2\"";
            else if (resOpt.Contains("1080p")) scaleFilter = "-vf \"scale='min(1920,iw)':-2\"";

            if (isTurbo) {
                txtProgressStatus.Text = "Running Turbo Fast Encoding (Single Pass)...";
                int maxrate = (int)(videoKbps * 1.25);
                int bufsize = (int)(videoKbps * 2.0);

                string args = string.Format("-y -hide_banner -threads 0 -i \"{0}\" {1} -c:v libx264 -preset veryfast -b:v {2}k -maxrate {3}k -bufsize {4}k -c:a aac -b:a {5}k -movflags +faststart \"{6}\"",
                    inputFile, scaleFilter, videoKbps, maxrate, bufsize, audioKbps, outputFile);

                await RunFFmpegAsync(args, duration, 1, 1, token);
            } else {
                string passLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pass_" + Guid.NewGuid().ToString("N"));

                txtProgressStatus.Text = "Pass 1/2: Analyzing video motion...";
                string pass1Args = string.Format("-y -hide_banner -threads 0 -i \"{0}\" {1} -c:v libx264 -preset faster -b:v {2}k -pass 1 -passlogfile \"{3}\" -an -f null NUL",
                    inputFile, scaleFilter, videoKbps, passLog);
                await RunFFmpegAsync(pass1Args, duration, 1, 2, token);

                txtProgressStatus.Text = "Pass 2/2: Encoding final video...";
                string pass2Args = string.Format("-y -hide_banner -threads 0 -i \"{0}\" {1} -c:v libx264 -preset faster -b:v {2}k -pass 2 -passlogfile \"{3}\" -c:a aac -b:a {4}k -movflags +faststart \"{5}\"",
                    inputFile, scaleFilter, videoKbps, passLog, audioKbps, outputFile);
                await RunFFmpegAsync(pass2Args, duration, 2, 2, token);

                try {
                    foreach (var f in Directory.GetFiles(System.IO.Path.GetTempPath(), System.IO.Path.GetFileName(passLog) + "*")) {
                        File.Delete(f);
                    }
                } catch { }
            }

            ShowCompletionResult(outputFile);
        }

        private async Task ProcessFormatConvert(string inputFile, string outputFile, string fmt, string codec, int crf, CancellationToken token) {
            double duration = currentVideo.DurationSeconds > 0 ? currentVideo.DurationSeconds : 60.0;
            string codecArgs = "";

            if (codec.Contains("HEVC")) {
                txtProgressStatus.Text = "Encoding HEVC / H.265 (High-Efficiency Archival)...";
                codecArgs = string.Format("-c:v libx265 -crf {0} -preset fast -tag:v hvc1 -c:a aac -b:a 160k -movflags +faststart", crf);
            } else if (codec.Contains("AV1")) {
                txtProgressStatus.Text = "Encoding AV1 (Next-Generation Compression)...";
                codecArgs = string.Format("-c:v libsvtav1 -crf {0} -preset 8 -pix_fmt yuv420p10le -c:a libopus -b:a 128k", crf);
            } else if (fmt == "webm") {
                txtProgressStatus.Text = "Encoding WebM (VP9 + Opus)...";
                codecArgs = string.Format("-c:v libvpx-vp9 -crf {0} -b:v 0 -cpu-used 4 -deadline good -c:a libopus -b:a 128k", crf);
            } else {
                txtProgressStatus.Text = "Encoding Universal H.264 MP4...";
                codecArgs = string.Format("-c:v libx264 -preset faster -crf {0} -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart", crf);
            }

            string args = string.Format("-y -hide_banner -threads 0 -i \"{0}\" {1} \"{2}\"", inputFile, codecArgs, outputFile);
            await RunFFmpegAsync(args, duration, 1, 1, token);
            ShowCompletionResult(outputFile);
        }

        private async Task ProcessExtractAudio(string inputFile, string outputFile, string fmt, string bitrate, CancellationToken token) {
            double duration = currentVideo.DurationSeconds > 0 ? currentVideo.DurationSeconds : 60.0;
            txtProgressStatus.Text = "Extracting audio track (" + fmt.ToUpper() + ")...";

            string audioArgs = "";
            if (fmt == "mp3") {
                audioArgs = string.Format("-vn -c:a libmp3lame -b:a {0}", bitrate);
            } else if (fmt == "aac") {
                audioArgs = string.Format("-vn -c:a aac -b:a {0}", bitrate);
            } else if (fmt == "flac") {
                audioArgs = "-vn -c:a flac";
            }

            string args = string.Format("-y -hide_banner -i \"{0}\" {1} \"{2}\"", inputFile, audioArgs, outputFile);
            await RunFFmpegAsync(args, duration, 1, 1, token);
            ShowCompletionResult(outputFile);
        }

        private async Task RunFFmpegAsync(string args, double totalDuration, int currentPass, int totalPasses, CancellationToken token) {
            var psi = new ProcessStartInfo {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var tcs = new TaskCompletionSource<bool>();
            activeProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            Regex timeRegex = new Regex(@"time=(\d+):(\d+):(\d+\.\d+)");
            Regex speedRegex = new Regex(@"speed=\s*([\d\.]+x)");

            activeProcess.ErrorDataReceived += (s, e) => {
                if (string.IsNullOrEmpty(e.Data)) return;

                var match = timeRegex.Match(e.Data);
                if (match.Success) {
                    double hours = double.Parse(match.Groups[1].Value);
                    double mins = double.Parse(match.Groups[2].Value);
                    double secs = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                    double curSecs = (hours * 3600) + (mins * 60) + secs;

                    double passFraction = totalDuration > 0 ? Math.Min(1.0, curSecs / totalDuration) : 0;
                    double overallPercent = (((currentPass - 1) + passFraction) / totalPasses) * 100.0;

                    string speed = "1.0x";
                    var spMatch = speedRegex.Match(e.Data);
                    if (spMatch.Success) speed = spMatch.Groups[1].Value;

                    Dispatcher.Invoke(() => {
                        progressBar.Value = Math.Max(0, Math.Min(100, overallPercent));
                        txtProgressPercent.Text = string.Format("{0:0}%", progressBar.Value);
                        txtProgressDetails.Text = string.Format("Speed: {0}  •  Processed: {1:0.0}s / {2:0.0}s", speed, curSecs, totalDuration);
                    });
                }
            };

            activeProcess.Exited += (s, e) => {
                if (activeProcess.ExitCode == 0) tcs.TrySetResult(true);
                else tcs.TrySetException(new Exception("FFmpeg process terminated with error code " + activeProcess.ExitCode));
            };

            activeProcess.Start();
            activeProcess.BeginErrorReadLine();

            using (token.Register(() => {
                try { if (!activeProcess.HasExited) activeProcess.Kill(); } catch { }
                tcs.TrySetCanceled();
            })) {
                await tcs.Task;
            }
        }

        private void ShowCompletionResult(string outputFile) {
            lastOutputFile = outputFile;
            progressCard.Visibility = Visibility.Collapsed;

            if (File.Exists(outputFile)) {
                FileInfo fi = new FileInfo(outputFile);
                double finalMB = fi.Length / (1024.0 * 1024.0);
                double origMB = currentVideo != null ? currentVideo.SizeMB : finalMB;
                double savedMB = origMB - finalMB;
                int savedPercent = origMB > 0 ? (int)((savedMB / origMB) * 100) : 0;

                txtResultTitle.Text = "🎉 Process Completed Successfully!";
                txtResultDetails.Text = string.Format("File saved to: {0}", outputFile);
                txtResultSavings.Text = string.Format("Original: {0:0.0} MB  ➔  New Size: {1:0.0} MB   (Saved {2}% • {3:0.0} MB saved)",
                    origMB, finalMB, savedPercent, savedMB);

                resultCard.Visibility = Visibility.Visible;
            }
        }

        private string GetXamlLayout() {
            return @"<Grid xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
      xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
      Margin=""28"">
    <Grid.RowDefinitions>
        <RowDefinition Height=""Auto""/>
        <RowDefinition Height=""*""/>
    </Grid.RowDefinitions>

    <!-- Header -->
    <Grid Grid.Row=""0"" Margin=""0,0,0,20"">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width=""*""/>
            <ColumnDefinition Width=""Auto""/>
        </Grid.ColumnDefinitions>
        <StackPanel Grid.Column=""0"">
            <StackPanel Orientation=""Horizontal"">
                <TextBlock Text=""🎬"" FontSize=""26"" VerticalAlignment=""Center"" Margin=""0,0,10,0""/>
                <TextBlock Text=""Video Compressor &amp; Converter"" FontSize=""24"" FontWeight=""Bold"" Foreground=""#FFFFFF"" VerticalAlignment=""Center""/>
                <Border x:Name=""BadgeLicense"" Background=""#10B981"" CornerRadius=""4"" Padding=""8,3"" Margin=""12,0,0,0"" VerticalAlignment=""Center"" Cursor=""Hand"">
                    <TextBlock x:Name=""TxtLicenseBadge"" Text=""PRO ACTIVE"" FontSize=""11"" FontWeight=""Bold"" Foreground=""#FFFFFF""/>
                </Border>
            </StackPanel>
            <TextBlock Text=""Hardware-accelerated, high-quality video optimization — 100% offline &amp; private.""
                       FontSize=""13"" Foreground=""#94A3B8"" Margin=""36,4,0,0""/>
        </StackPanel>

        <Border Grid.Column=""1"" Background=""#1E293B"" BorderBrush=""#334155"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,6"" VerticalAlignment=""Center"">
            <TextBlock x:Name=""TxtEngineStatus"" Text=""● Detecting FFmpeg..."" FontSize=""12"" Foreground=""#94A3B8"" FontWeight=""SemiBold""/>
        </Border>
    </Grid>

    <!-- MAIN APP PANEL (Visible when license active) -->
    <Grid x:Name=""MainAppPanel"" Grid.Row=""1"">
        <Grid.RowDefinitions>
            <RowDefinition Height=""Auto""/>
            <RowDefinition Height=""*""/>
            <RowDefinition Height=""Auto""/>
            <RowDefinition Height=""Auto""/>
        </Grid.RowDefinitions>

        <!-- Drop Zone / Selected File Card -->
        <Grid Grid.Row=""0"" Margin=""0,0,0,16"">
            <Border x:Name=""DropZone"" Background=""#131B2E"" BorderBrush=""#334155"" BorderThickness=""2"" CornerRadius=""12"" Padding=""24,20"">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""Auto""/>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""Auto""/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column=""0"" Text=""📥"" FontSize=""36"" VerticalAlignment=""Center"" Margin=""0,0,16,0""/>
                    <StackPanel Grid.Column=""1"" VerticalAlignment=""Center"">
                        <TextBlock x:Name=""DropZoneTitle"" Text=""Drag &amp; Drop Video Files Here"" FontSize=""16"" FontWeight=""SemiBold"" Foreground=""#F8FAFC""/>
                        <TextBlock x:Name=""DropZoneSub"" Text=""Supports MP4, MKV, MOV, WebM, AVI, WMV, FLV, and TS files"" FontSize=""12"" Foreground=""#64748B"" Margin=""0,3,0,0""/>
                    </StackPanel>
                    <Button x:Name=""BtnBrowse"" Grid.Column=""2"" Content=""Browse Video..."" Background=""#4F46E5"" Foreground=""#FFFFFF"" FontWeight=""SemiBold""
                            FontSize=""13"" Padding=""16,8"" BorderThickness=""0"" Cursor=""Hand"" VerticalAlignment=""Center""/>
                </Grid>
            </Border>

            <StackPanel x:Name=""FileInfoCard"" Visibility=""Collapsed"">
                <Border Background=""#1E293B"" BorderBrush=""#6366F1"" BorderThickness=""2,0,0,0"" CornerRadius=""8"" Padding=""16,12"">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""*""/>
                            <ColumnDefinition Width=""Auto""/>
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column=""0"">
                            <TextBlock x:Name=""TxtFileName"" Text=""video.mp4"" FontSize=""15"" FontWeight=""Bold"" Foreground=""#FFFFFF""/>
                            <StackPanel Orientation=""Horizontal"" Margin=""0,8,0,0"">
                                <Border Background=""#0F172A"" CornerRadius=""4"" Padding=""8,3"" Margin=""0,0,8,0"">
                                    <StackPanel Orientation=""Horizontal"">
                                        <TextBlock Text=""📦 Size: "" Foreground=""#94A3B8"" FontSize=""12""/>
                                        <TextBlock x:Name=""BadgeSize"" Text=""0 MB"" Foreground=""#F8FAFC"" FontSize=""12"" FontWeight=""Bold""/>
                                    </StackPanel>
                                </Border>
                                <Border Background=""#0F172A"" CornerRadius=""4"" Padding=""8,3"" Margin=""0,0,8,0"">
                                    <StackPanel Orientation=""Horizontal"">
                                        <TextBlock Text=""⏱ Duration: "" Foreground=""#94A3B8"" FontSize=""12""/>
                                        <TextBlock x:Name=""BadgeDuration"" Text=""--:--"" Foreground=""#60A5FA"" FontSize=""12"" FontWeight=""Bold""/>
                                    </StackPanel>
                                </Border>
                                <Border Background=""#0F172A"" CornerRadius=""4"" Padding=""8,3"">
                                    <StackPanel Orientation=""Horizontal"">
                                        <TextBlock Text=""📐 Resolution: "" Foreground=""#94A3B8"" FontSize=""12""/>
                                        <TextBlock x:Name=""BadgeRes"" Text=""1080p"" Foreground=""#34D399"" FontSize=""12"" FontWeight=""Bold""/>
                                    </StackPanel>
                                </Border>
                            </StackPanel>
                        </StackPanel>
                        <Button x:Name=""BtnChangeFile"" Grid.Column=""1"" Content=""Change Video"" Background=""#334155"" Foreground=""#F8FAFC""
                                FontSize=""12"" Padding=""12,6"" BorderThickness=""0"" Cursor=""Hand"" VerticalAlignment=""Center""/>
                    </Grid>
                </Border>
            </StackPanel>
        </Grid>

        <!-- Mode Configuration -->
        <Border Grid.Row=""1"" Background=""#131B2E"" BorderBrush=""#1E293B"" BorderThickness=""1"" CornerRadius=""12"" Padding=""20"" Margin=""0,0,0,16"">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height=""Auto""/>
                    <RowDefinition Height=""*""/>
                </Grid.RowDefinitions>

                <StackPanel Grid.Row=""0"" Orientation=""Horizontal"" Margin=""0,0,0,18"">
                    <RadioButton x:Name=""TabSize"" Content=""🎯 Target File Size"" IsChecked=""True"" GroupName=""Mode"" Margin=""0,0,18,0"" FontSize=""14"" FontWeight=""SemiBold"" Cursor=""Hand"" Foreground=""#F8FAFC""/>
                    <RadioButton x:Name=""TabFormat"" Content=""🔄 Convert Format"" GroupName=""Mode"" Margin=""0,0,18,0"" FontSize=""14"" FontWeight=""SemiBold"" Cursor=""Hand"" Foreground=""#94A3B8""/>
                    <RadioButton x:Name=""TabAudio"" Content=""🎵 Extract Audio"" GroupName=""Mode"" FontSize=""14"" FontWeight=""SemiBold"" Cursor=""Hand"" Foreground=""#94A3B8""/>
                </StackPanel>

                <!-- Mode 1: Target Size -->
                <StackPanel x:Name=""PanelSize"" Grid.Row=""1"">
                    <Grid Margin=""0,0,0,8"">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""*""/>
                            <ColumnDefinition Width=""Auto""/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column=""0"" Text=""Target File Size:"" FontSize=""13"" Foreground=""#E2E8F0"" FontWeight=""SemiBold""/>
                        <TextBlock x:Name=""TxtTargetSizeVal"" Grid.Column=""1"" Text=""25.0 MB"" FontSize=""15"" FontWeight=""Bold"" Foreground=""#60A5FA""/>
                    </Grid>
                    <Slider x:Name=""SliderTargetSize"" Minimum=""5"" Maximum=""100"" Value=""25"" TickFrequency=""5"" IsSnapToTickEnabled=""False"" Margin=""0,0,0,12"" Cursor=""Hand""/>

                    <StackPanel Orientation=""Horizontal"" Margin=""0,0,0,18"">
                        <TextBlock Text=""Quick Presets:"" FontSize=""12"" Foreground=""#94A3B8"" VerticalAlignment=""Center"" Margin=""0,0,10,0""/>
                        <Button x:Name=""BtnPreset8"" Content=""8 MB (Discord)"" Margin=""0,0,6,0"" Padding=""8,4"" FontSize=""11"" Background=""#1E293B"" Foreground=""#E2E8F0"" BorderThickness=""0"" Cursor=""Hand""/>
                        <Button x:Name=""BtnPreset10"" Content=""10 MB (Discord Nitro)"" Margin=""0,0,6,0"" Padding=""8,4"" FontSize=""11"" Background=""#1E293B"" Foreground=""#E2E8F0"" BorderThickness=""0"" Cursor=""Hand""/>
                        <Button x:Name=""BtnPreset25"" Content=""25 MB (Email/Slack)"" Margin=""0,0,6,0"" Padding=""8,4"" FontSize=""11"" Background=""#1E293B"" Foreground=""#E2E8F0"" BorderThickness=""0"" Cursor=""Hand""/>
                        <Button x:Name=""BtnPreset50"" Content=""50 MB"" Margin=""0,0,6,0"" Padding=""8,4"" FontSize=""11"" Background=""#1E293B"" Foreground=""#E2E8F0"" BorderThickness=""0"" Cursor=""Hand""/>
                        <Button x:Name=""BtnPreset100"" Content=""100 MB"" Padding=""8,4"" FontSize=""11"" Background=""#1E293B"" Foreground=""#E2E8F0"" BorderThickness=""0"" Cursor=""Hand""/>
                    </StackPanel>

                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""*""/>
                            <ColumnDefinition Width=""*""/>
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column=""0"" Margin=""0,0,12,0"">
                            <TextBlock Text=""Compression Strategy:"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,6""/>
                            <StackPanel Orientation=""Horizontal"">
                                <RadioButton x:Name=""RbTurboFast"" Content=""Turbo Fast (1-Pass)"" IsChecked=""True"" GroupName=""Strategy"" Margin=""0,0,12,0"" FontSize=""12"" Foreground=""#E2E8F0""/>
                                <RadioButton x:Name=""RbPrecision"" Content=""High Precision (2-Pass)"" GroupName=""Strategy"" FontSize=""12"" Foreground=""#E2E8F0""/>
                            </StackPanel>
                        </StackPanel>
                        <StackPanel Grid.Column=""1"" Margin=""12,0,0,0"">
                            <TextBlock Text=""Resolution Adjustment:"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,6""/>
                            <ComboBox x:Name=""CmbResolution"" SelectedIndex=""0"" Padding=""8,5"">
                                <ComboBoxItem Content=""Keep Original Resolution""/>
                                <ComboBoxItem Content=""Downscale to 1080p (FHD)""/>
                                <ComboBoxItem Content=""Downscale to 720p (HD)""/>
                                <ComboBoxItem Content=""Downscale to 480p (SD)""/>
                            </ComboBox>
                        </StackPanel>
                    </Grid>
                </StackPanel>

                <!-- Mode 2: Format Convert -->
                <StackPanel x:Name=""PanelFormat"" Grid.Row=""1"" Visibility=""Collapsed"">
                    <Grid Margin=""0,0,0,16"">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""*""/>
                            <ColumnDefinition Width=""*""/>
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column=""0"" Margin=""0,0,12,0"">
                            <TextBlock Text=""Target Container Format:"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,6""/>
                            <ComboBox x:Name=""CmbFormat"" SelectedIndex=""0"" Padding=""8,5"">
                                <ComboBoxItem Content=""MP4 (Universal Compatibility)""/>
                                <ComboBoxItem Content=""MKV (Matroska Media)""/>
                                <ComboBoxItem Content=""WebM (Web Optimized)""/>
                                <ComboBoxItem Content=""MOV (Apple QuickTime)""/>
                                <ComboBoxItem Content=""AVI""/>
                            </ComboBox>
                        </StackPanel>
                        <StackPanel Grid.Column=""1"" Margin=""12,0,0,0"">
                            <TextBlock Text=""Video Codec:"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,6""/>
                            <ComboBox x:Name=""CmbCodec"" SelectedIndex=""0"" Padding=""8,5"">
                                <ComboBoxItem Content=""H.264 / AVC (Most Compatible)""/>
                                <ComboBoxItem Content=""HEVC / H.265 (50% Better Compression)""/>
                                <ComboBoxItem Content=""AV1 (Next-Generation Codec)""/>
                            </ComboBox>
                        </StackPanel>
                    </Grid>

                    <Grid Margin=""0,0,0,8"">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""*""/>
                            <ColumnDefinition Width=""Auto""/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column=""0"" Text=""Encoding Quality (CRF):"" FontSize=""13"" Foreground=""#E2E8F0"" FontWeight=""SemiBold""/>
                        <TextBlock x:Name=""TxtQualityVal"" Grid.Column=""1"" Text=""CRF 24 (High Quality)"" FontSize=""14"" FontWeight=""Bold"" Foreground=""#60A5FA""/>
                    </Grid>
                    <Slider x:Name=""SliderQuality"" Minimum=""18"" Maximum=""34"" Value=""24"" TickFrequency=""1"" IsSnapToTickEnabled=""True"" Cursor=""Hand""/>
                </StackPanel>

                <!-- Mode 3: Audio Extraction -->
                <StackPanel x:Name=""PanelAudio"" Grid.Row=""1"" Visibility=""Collapsed"">
                    <Grid Margin=""0,0,0,16"">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""*""/>
                            <ColumnDefinition Width=""*""/>
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column=""0"" Margin=""0,0,12,0"">
                            <TextBlock Text=""Audio Format:"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,6""/>
                            <ComboBox x:Name=""CmbAudioFmt"" SelectedIndex=""0"" Padding=""8,5"">
                                <ComboBoxItem Content=""MP3""/>
                                <ComboBoxItem Content=""AAC""/>
                                <ComboBoxItem Content=""FLAC (Lossless)""/>
                            </ComboBox>
                        </StackPanel>
                        <StackPanel Grid.Column=""1"" Margin=""12,0,0,0"">
                            <TextBlock Text=""Audio Bitrate:"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,6""/>
                            <ComboBox x:Name=""CmbAudioBitrate"" SelectedIndex=""0"" Padding=""8,5"">
                                <ComboBoxItem Content=""320k (Studio Quality)""/>
                                <ComboBoxItem Content=""256k (High Quality)""/>
                                <ComboBoxItem Content=""192k (Standard)""/>
                                <ComboBoxItem Content=""128k (Compact)""/>
                            </ComboBox>
                        </StackPanel>
                    </Grid>
                </StackPanel>
            </Grid>
        </Border>

        <!-- Progress Card -->
        <Border x:Name=""ProgressCard"" Grid.Row=""2"" Background=""#1E293B"" BorderBrush=""#334155"" BorderThickness=""1"" CornerRadius=""8"" Padding=""16"" Margin=""0,0,0,14"" Visibility=""Collapsed"">
            <StackPanel>
                <Grid Margin=""0,0,0,8"">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""Auto""/>
                    </Grid.ColumnDefinitions>
                    <TextBlock x:Name=""TxtProgressStatus"" Grid.Column=""0"" Text=""Processing..."" FontSize=""13"" FontWeight=""SemiBold"" Foreground=""#F8FAFC""/>
                    <TextBlock x:Name=""TxtProgressPercent"" Grid.Column=""1"" Text=""0%"" FontSize=""15"" FontWeight=""Bold"" Foreground=""#818CF8""/>
                </Grid>
                <ProgressBar x:Name=""ProgressBar"" Height=""8"" Minimum=""0"" Maximum=""100"" Value=""0"" Foreground=""#6366F1"" Background=""#0F172A"" Margin=""0,0,0,8""/>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""Auto""/>
                    </Grid.ColumnDefinitions>
                    <TextBlock x:Name=""TxtProgressDetails"" Grid.Column=""0"" Text=""Starting..."" FontSize=""11"" Foreground=""#94A3B8""/>
                    <Button x:Name=""BtnCancel"" Grid.Column=""1"" Content=""Cancel"" Background=""#DC2626"" Foreground=""#FFFFFF"" FontSize=""11"" Padding=""10,3"" BorderThickness=""0"" Cursor=""Hand""/>
                </Grid>
            </StackPanel>
        </Border>

        <!-- Result Card -->
        <Border x:Name=""ResultCard"" Grid.Row=""2"" Background=""#064E3B"" BorderBrush=""#059669"" BorderThickness=""1"" CornerRadius=""8"" Padding=""16"" Margin=""0,0,0,14"" Visibility=""Collapsed"">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width=""*""/>
                    <ColumnDefinition Width=""Auto""/>
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column=""0"">
                    <TextBlock x:Name=""TxtResultTitle"" Text=""🎉 Compression Complete!"" FontSize=""15"" FontWeight=""Bold"" Foreground=""#A7F3D0""/>
                    <TextBlock x:Name=""TxtResultSavings"" Text=""Original: 100 MB -> New Size: 24 MB"" FontSize=""13"" FontWeight=""SemiBold"" Foreground=""#FFFFFF"" Margin=""0,4,0,2""/>
                    <TextBlock x:Name=""TxtResultDetails"" Text=""Saved to: output.mp4"" FontSize=""11"" Foreground=""#D1FAE5"" TextTrimming=""CharacterEllipsis""/>
                </StackPanel>
                <StackPanel Grid.Column=""1"" Orientation=""Horizontal"" VerticalAlignment=""Center"">
                    <Button x:Name=""BtnPlayVideo"" Content=""▶ Play Video"" Background=""#10B981"" Foreground=""#FFFFFF"" FontWeight=""SemiBold"" FontSize=""12"" Padding=""12,6"" Margin=""0,0,8,0"" BorderThickness=""0"" Cursor=""Hand""/>
                    <Button x:Name=""BtnOpenFolder"" Content=""📂 Open Folder"" Background=""#047857"" Foreground=""#FFFFFF"" FontWeight=""SemiBold"" FontSize=""12"" Padding=""12,6"" BorderThickness=""0"" Cursor=""Hand""/>
                </StackPanel>
            </Grid>
        </Border>

        <!-- Footer Action -->
        <Grid Grid.Row=""3"">
            <Button x:Name=""BtnStart"" Content=""⚡ Compress Video"" Background=""#4F46E5"" Foreground=""#FFFFFF""
                    FontSize=""16"" FontWeight=""Bold"" Height=""46"" BorderThickness=""0"" Cursor=""Hand"" IsEnabled=""False""/>
        </Grid>
    </Grid>

    <!-- LICENSE / ACTIVATION / UNINSTALL OVERLAY -->
    <Grid x:Name=""LicenseOverlay"" Grid.Row=""1"" Visibility=""Collapsed"">
        <Border Background=""#131B2E"" BorderBrush=""#334155"" BorderThickness=""1"" CornerRadius=""14"" Padding=""28"" VerticalAlignment=""Center"" MaxWidth=""680"">
            <StackPanel>
                <TextBlock Text=""🔑 Subscription &amp; License Management"" FontSize=""19"" FontWeight=""Bold"" Foreground=""#FFFFFF""/>
                <TextBlock Text=""Activate your license or manage this software installation on this PC."" FontSize=""13"" Foreground=""#94A3B8"" Margin=""0,4,0,18""/>

                <!-- Machine HWID -->
                <TextBlock Text=""Machine Hardware ID (HWID):"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,4""/>
                <Grid Margin=""0,0,0,16"">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""Auto""/>
                    </Grid.ColumnDefinitions>
                    <Border Grid.Column=""0"" Background=""#0F172A"" BorderBrush=""#1E293B"" BorderThickness=""1"" CornerRadius=""6"" Padding=""12,8"" Margin=""0,0,8,0"">
                        <TextBlock x:Name=""TxtMachineHwid"" Text=""HWID-0000-0000-0000"" FontFamily=""Consolas"" FontSize=""13"" Foreground=""#38BDF8"" FontWeight=""Bold"" VerticalAlignment=""Center""/>
                    </Border>
                    <Button x:Name=""BtnCopyHwid"" Grid.Column=""1"" Content=""📋 Copy HWID"" Background=""#1E293B"" Foreground=""#E2E8F0"" Padding=""14,8"" BorderThickness=""0"" Cursor=""Hand""/>
                </Grid>

                <!-- License Key Input -->
                <TextBlock Text=""Subscription License Key:"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,4""/>
                <Grid Margin=""0,0,0,14"">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""Auto""/>
                    </Grid.ColumnDefinitions>
                    <TextBox x:Name=""TxtLicenseKey"" Grid.Column=""0"" FontSize=""14"" FontFamily=""Consolas"" Padding=""10,8"" Background=""#0F172A"" Foreground=""#F8FAFC"" BorderBrush=""#334155"" Margin=""0,0,8,0""/>
                    <Button x:Name=""BtnActivateLicense"" Grid.Column=""1"" Content=""⚡ Activate License"" Background=""#4F46E5"" Foreground=""#FFFFFF"" FontWeight=""Bold"" FontSize=""13"" Padding=""16,8"" BorderThickness=""0"" Cursor=""Hand""/>
                </Grid>

                <!-- Status & Details -->
                <Border Background=""#0F172A"" BorderBrush=""#1E293B"" BorderThickness=""1"" CornerRadius=""8"" Padding=""14"" Margin=""0,0,0,18"">
                    <StackPanel>
                        <TextBlock x:Name=""TxtLicenseStatus"" Text=""Please enter your license key to unlock."" FontSize=""13"" FontWeight=""Bold"" Foreground=""#34D399""/>
                        <TextBlock x:Name=""TxtLicenseDetails"" Text="""" FontSize=""12"" Foreground=""#94A3B8"" Margin=""0,4,0,0""/>
                    </StackPanel>
                </Border>

                <!-- Buttons: Back to App and Clean Erase -->
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""Auto""/>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""Auto""/>
                    </Grid.ColumnDefinitions>

                    <Button x:Name=""BtnCloseLicense"" Grid.Column=""0"" Content=""← Back to Application"" Background=""#334155"" Foreground=""#FFFFFF"" FontWeight=""SemiBold"" FontSize=""12"" Padding=""14,7"" BorderThickness=""0"" Cursor=""Hand""/>
                    <Button x:Name=""BtnEraseApp"" Grid.Column=""2"" Content=""🗑️ Completely Erase &amp; Wipe App"" Background=""#7F1D1D"" Foreground=""#FCA5A5"" FontWeight=""SemiBold"" FontSize=""12"" Padding=""14,7"" BorderThickness=""0"" Cursor=""Hand""/>
                </Grid>
            </StackPanel>
        </Border>
    </Grid>
</Grid>";
        }
    }
}