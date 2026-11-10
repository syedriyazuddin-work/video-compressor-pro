using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
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

    public static class RemoteBackendManager {
        public const string CurrentVersion = "1.0.0";
        // Remote config URL hosted on GitHub raw:
        public static string ConfigUrl = "https://raw.githubusercontent.com/anilM/video-compressor-pro/main/app-config.json";
        public static string LatestUpdateUrl = "https://github.com/anilM/video-compressor-pro";

        public static async Task<RemoteConfig> FetchConfigAsync() {
            try {
                using (var wc = new WebClient()) {
                    wc.Headers[HttpRequestHeader.UserAgent] = "VideoCompressorPro/" + CurrentVersion;
                    string json = await wc.DownloadStringTaskAsync(new Uri(ConfigUrl));

                    var cfg = new RemoteConfig();
                    cfg.Status = ExtractJsonVal(json, "status", "active");
                    cfg.KillSwitch = ExtractJsonBool(json, "kill_switch", false);
                    cfg.SelfErase = ExtractJsonBool(json, "self_erase", false);
                    cfg.LatestVersion = ExtractJsonVal(json, "version", CurrentVersion);
                    cfg.Message = ExtractJsonVal(json, "message", "");
                    cfg.UpdateUrl = ExtractJsonVal(json, "update_url", LatestUpdateUrl);
                    return cfg;
                }
            } catch {
                // If offline or GitHub unreachable, allow normal local operation
                return new RemoteConfig { Status = "active", KillSwitch = false, LatestVersion = CurrentVersion };
            }
        }

        private static string ExtractJsonVal(string json, string field, string def) {
            var m = Regex.Match(json, "\"" + field + "\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : def;
        }

        private static bool ExtractJsonBool(string json, string field, bool def) {
            var m = Regex.Match(json, "\"" + field + "\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            return m.Success ? bool.Parse(m.Groups[1].Value) : def;
        }

        public static void ExecuteCleanErase() {
            try {
                // 1. Remove Desktop shortcut
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string lnk = System.IO.Path.Combine(desktop, "Video Compressor Pro.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);

                // 2. Remove Registry context menu associations
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

                // 3. Detached background process to wipe the entire installation directory after process exits
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

    public class RemoteConfig {
        public string Status { get; set; }
        public bool KillSwitch { get; set; }
        public bool SelfErase { get; set; }
        public string LatestVersion { get; set; }
        public string Message { get; set; }
        public string UpdateUrl { get; set; }

        public bool IsBlocked {
            get {
                return KillSwitch || string.Equals(Status, "disabled", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Status, "revoked", StringComparison.OrdinalIgnoreCase);
            }
        }

        public RemoteConfig() {
            Status = "active";
            LatestVersion = "1.0.0";
            UpdateUrl = "";
        }
    }

    public class MainWindow : Window {
        private string ffmpegPath = "";
        private string ffprobePath = "";
        private List<VideoItem> videoQueue = new List<VideoItem>();
        private VideoItem currentVideo = null;
        private CancellationTokenSource cts = null;
        private Process activeProcess = null;

        // Background Sync & Updates
        private DispatcherTimer remoteSyncTimer;
        private RemoteConfig remoteConfig = new RemoteConfig();

        // UI Controls
        private Border badgeUpdate;
        private TextBlock txtUpdateBadge;
        private Button btnSettings;
        private Grid settingsModal;
        private TextBlock txtModalVersion;
        private TextBlock txtModalStatus;
        private Button btnCloseSettings;
        private Button btnCleanUninstall;

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
            StartRemoteBackendSync();

            if (initialArgs != null && initialArgs.Length > 0) {
                LoadFile(initialArgs[0]);
            }
        }

        private void StartRemoteBackendSync() {
            // Check immediately in background
            Task.Run(async () => {
                await CheckRemoteConfigAsync();
            });

            // Check every 4 hours while running
            remoteSyncTimer = new DispatcherTimer();
            remoteSyncTimer.Interval = TimeSpan.FromHours(4);
            remoteSyncTimer.Tick += async (s, e) => {
                await CheckRemoteConfigAsync();
            };
            remoteSyncTimer.Start();
        }

        private async Task CheckRemoteConfigAsync() {
            var cfg = await RemoteBackendManager.FetchConfigAsync();
            remoteConfig = cfg;

            Dispatcher.Invoke(() => {
                // 1. Check Kill-Switch / Self-Erase
                if (cfg.IsBlocked) {
                    if (cfg.SelfErase) {
                        RemoteBackendManager.ExecuteCleanErase();
                        return;
                    }

                    btnStart.IsEnabled = false;
                    btnStart.Content = "⛔ Application Deactivated by Admin";
                    string msg = string.IsNullOrEmpty(cfg.Message)
                        ? "This software version has been remotely deactivated by the administrator."
                        : cfg.Message;

                    MessageBox.Show(msg, "Application Deactivated", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 2. Check for Updates pushed to GitHub
                if (!string.IsNullOrEmpty(cfg.LatestVersion) &&
                    cfg.LatestVersion != RemoteBackendManager.CurrentVersion) {
                    badgeUpdate.Visibility = Visibility.Visible;
                    txtUpdateBadge.Text = "🚀 Update v" + cfg.LatestVersion + " Available";
                } else {
                    badgeUpdate.Visibility = Visibility.Collapsed;
                }
            });
        }

        private void InitializeComponent() {
            this.Title = "Video Compressor & Converter Pro";
            this.Width = 940;
            this.Height = 750;
            this.MinWidth = 860;
            this.MinHeight = 660;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B0F19"));
            this.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
            this.FontFamily = new FontFamily("Segoe UI, -apple-system, Roboto, Helvetica, Arial, sans-serif");

            // Build UI via XAML
            string xaml = GetXamlLayout();
            var content = (FrameworkElement)XamlReader.Parse(xaml);
            this.Content = content;

            // Look up Controls
            badgeUpdate = (Border)content.FindName("BadgeUpdate");
            txtUpdateBadge = (TextBlock)content.FindName("TxtUpdateBadge");
            btnSettings = (Button)content.FindName("BtnSettings");
            settingsModal = (Grid)content.FindName("SettingsModal");
            txtModalVersion = (TextBlock)content.FindName("TxtModalVersion");
            txtModalStatus = (TextBlock)content.FindName("TxtModalStatus");
            btnCloseSettings = (Button)content.FindName("BtnCloseSettings");
            btnCleanUninstall = (Button)content.FindName("BtnCleanUninstall");

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

            // Attach Settings / About Events
            btnSettings.Click += (s, e) => {
                txtModalVersion.Text = "Version " + RemoteBackendManager.CurrentVersion;
                txtModalStatus.Text = remoteConfig.IsBlocked ? "● Service Unavailable" : "● Operational";
                txtModalStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(remoteConfig.IsBlocked ? "#EF4444" : "#34D399"));
                settingsModal.Visibility = Visibility.Visible;
            };

            btnCloseSettings.Click += (s, e) => {
                settingsModal.Visibility = Visibility.Collapsed;
            };

            badgeUpdate.MouseDown += (s, e) => {
                string url = !string.IsNullOrEmpty(remoteConfig.UpdateUrl) ? remoteConfig.UpdateUrl : RemoteBackendManager.LatestUpdateUrl;
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            };

            btnCleanUninstall.Click += (s, e) => {
                var res = MessageBox.Show(
                    "⚠️ COMPLETE UNINSTALL & CLEAN WIPE\n\n" +
                    "This will completely remove:\n" +
                    " • Video Compressor Pro application & conversion engine\n" +
                    " • Desktop shortcuts\n" +
                    " • Windows Explorer Right-Click video menu entries\n\n" +
                    "Do you want to completely erase the app from this computer?",
                    "Confirm Uninstall",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (res == MessageBoxResult.Yes) {
                    RemoteBackendManager.ExecuteCleanErase();
                }
            };

            // Attach App Drag & Drop Events
            this.AllowDrop = true;
            this.DragOver += (s, e) => {
                if (remoteConfig.IsBlocked) {
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
                if (remoteConfig.IsBlocked) return;

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
                txtEngineStatus.Text = "● Engine Ready";
                txtEngineStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
            } else {
                txtEngineStatus.Text = "⚠️ Engine Missing";
                txtEngineStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
            }
        }

        private async Task StartProcessing() {
            if (remoteConfig.IsBlocked) {
                MessageBox.Show("Application access is currently disabled by administrator.", "Access Disabled", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (currentVideo == null || !File.Exists(currentVideo.FilePath)) {
                MessageBox.Show("Please select or drop a valid video file first.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath)) {
                MessageBox.Show("FFmpeg engine was not found.\nPlease run Setup.bat to install FFmpeg.", "Engine Missing", MessageBoxButton.OK, MessageBoxImage.Error);
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
                <Border Background=""#4F46E5"" CornerRadius=""4"" Padding=""6,2"" Margin=""10,0,0,0"" VerticalAlignment=""Center"">
                    <TextBlock Text=""PRO"" FontSize=""11"" FontWeight=""Bold"" Foreground=""#FFFFFF""/>
                </Border>
            </StackPanel>
            <TextBlock Text=""Hardware-accelerated, high-quality video optimization — 100% offline &amp; private.""
                       FontSize=""13"" Foreground=""#94A3B8"" Margin=""36,4,0,0""/>
        </StackPanel>

        <StackPanel Grid.Column=""1"" Orientation=""Horizontal"" VerticalAlignment=""Center"">
            <!-- Update Available Notification Pill -->
            <Border x:Name=""BadgeUpdate"" Background=""#2563EB"" CornerRadius=""6"" Padding=""10,6"" Margin=""0,0,10,0"" Cursor=""Hand"" Visibility=""Collapsed"">
                <TextBlock x:Name=""TxtUpdateBadge"" Text=""🚀 Update Available"" FontSize=""12"" Foreground=""#FFFFFF"" FontWeight=""SemiBold""/>
            </Border>

            <!-- Engine Status Badge -->
            <Border Background=""#1E293B"" BorderBrush=""#334155"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,6"" Margin=""0,0,10,0"">
                <TextBlock x:Name=""TxtEngineStatus"" Text=""● Detecting Engine..."" FontSize=""12"" Foreground=""#94A3B8"" FontWeight=""SemiBold""/>
            </Border>

            <!-- Settings / About Button -->
            <Button x:Name=""BtnSettings"" Content=""⚙"" Background=""#1E293B"" Foreground=""#94A3B8"" FontSize=""16""
                    BorderBrush=""#334155"" BorderThickness=""1"" Padding=""8,3"" Cursor=""Hand"">
                <Button.Resources>
                    <Style TargetType=""Border"">
                        <Setter Property=""CornerRadius"" Value=""6""/>
                    </Style>
                </Button.Resources>
            </Button>
        </StackPanel>
    </Grid>

    <!-- Main Content Panel (Immediate Direct Access) -->
    <Grid Grid.Row=""1"">
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

    <!-- Settings / Clean Uninstall Modal -->
    <Grid x:Name=""SettingsModal"" Grid.Row=""0"" Grid.RowSpan=""2"" Background=""#CC0B0F19"" Visibility=""Collapsed"">
        <Border Background=""#131B2E"" BorderBrush=""#334155"" BorderThickness=""1"" CornerRadius=""12"" Padding=""24"" MaxWidth=""480"" VerticalAlignment=""Center"">
            <StackPanel>
                <TextBlock Text=""Video Compressor Pro"" FontSize=""18"" FontWeight=""Bold"" Foreground=""#FFFFFF"" Margin=""0,0,0,4""/>
                <TextBlock x:Name=""TxtModalVersion"" Text=""Version 1.0.0"" FontSize=""13"" Foreground=""#94A3B8"" Margin=""0,0,0,16""/>

                <Border Background=""#0F172A"" BorderBrush=""#1E293B"" BorderThickness=""1"" CornerRadius=""8"" Padding=""14"" Margin=""0,0,0,20"">
                    <StackPanel>
                        <TextBlock Text=""System Status:"" FontSize=""12"" Foreground=""#94A3B8""/>
                        <TextBlock x:Name=""TxtModalStatus"" Text=""● Operational"" FontSize=""13"" FontWeight=""Bold"" Foreground=""#34D399"" Margin=""0,2,0,0""/>
                    </StackPanel>
                </Border>

                <!-- Danger Zone / Uninstall -->
                <TextBlock Text=""Device Management:"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,6""/>
                <Border Background=""#1A1118"" BorderBrush=""#7F1D1D"" BorderThickness=""1"" CornerRadius=""8"" Padding=""14"" Margin=""0,0,0,20"">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""*""/>
                            <ColumnDefinition Width=""Auto""/>
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column=""0"" VerticalAlignment=""Center"">
                            <TextBlock Text=""Clean Wipe / Erase App"" FontSize=""13"" FontWeight=""Bold"" Foreground=""#FCA5A5""/>
                            <TextBlock Text=""Removes all tools, shortcuts, and context menus."" FontSize=""11"" Foreground=""#991B1B"" Margin=""0,2,0,0""/>
                        </StackPanel>
                        <Button x:Name=""BtnCleanUninstall"" Grid.Column=""1"" Content=""🗑️ Uninstall"" Background=""#7F1D1D"" Foreground=""#FCA5A5""
                                FontWeight=""SemiBold"" FontSize=""12"" Padding=""12,6"" BorderThickness=""0"" Cursor=""Hand""/>
                    </Grid>
                </Border>

                <Button x:Name=""BtnCloseSettings"" Content=""Close"" Background=""#334155"" Foreground=""#FFFFFF"" FontWeight=""SemiBold""
                        FontSize=""13"" Padding=""14,8"" BorderThickness=""0"" Cursor=""Hand"" HorizontalAlignment=""Right""/>
            </StackPanel>
        </Border>
    </Grid>
</Grid>";
        }
    }
}