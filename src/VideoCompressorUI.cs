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
                mainWindow.Closed += (s, e) => {
                    try {
                        File.AppendAllText(System.IO.Path.Combine(baseDir, "lifecycle.log"), "Window closed at " + DateTime.Now + "\n");
                    } catch { }
                };
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

    public class MainWindow : Window {
        private string ffmpegPath = "";
        private string ffprobePath = "";
        private List<VideoItem> videoQueue = new List<VideoItem>();
        private VideoItem currentVideo = null;
        private CancellationTokenSource cts = null;
        private Process activeProcess = null;

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

            if (initialArgs != null && initialArgs.Length > 0) {
                LoadFile(initialArgs[0]);
            }
        }

        private void InitializeComponent() {
            this.Title = "Video Compressor & Converter Pro";
            this.Width = 920;
            this.Height = 740;
            this.MinWidth = 850;
            this.MinHeight = 650;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B0F19"));
            this.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
            this.FontFamily = new FontFamily("Segoe UI, -apple-system, Roboto, Helvetica, Arial, sans-serif");

            // Build UI via XAML
            string xaml = GetXamlLayout();
            var content = (FrameworkElement)XamlReader.Parse(xaml);
            this.Content = content;

            // Bind Elements from content
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

            // Attach Events
            this.AllowDrop = true;
            this.DragOver += (s, e) => {
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

        private void LoadFile(string path) {
            if (!File.Exists(path)) return;

            var fi = new FileInfo(path);
            currentVideo = new VideoItem {
                FilePath = path,
                SizeMB = Math.Round(fi.Length / (1024.0 * 1024.0), 2)
            };

            txtFileName.Text = currentVideo.FileName;
            badgeSize.Text = string.Format("{0:0.0} MB", currentVideo.SizeMB);

            dropZone.Visibility = Visibility.Collapsed;
            fileInfoCard.Visibility = Visibility.Visible;
            resultCard.Visibility = Visibility.Collapsed;
            btnStart.IsEnabled = true;

            // Probe duration and resolution in background
            Task.Run(() => {
                ProbeVideo(currentVideo);
                Dispatcher.Invoke(() => {
                    badgeDuration.Text = currentVideo.DurationText ?? "--:--";
                    badgeRes.Text = currentVideo.Resolution ?? "Video";
                });
            });
        }

        private void LocateFFmpeg() {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] candidates = new string[] {
                System.IO.Path.Combine(baseDir, "bin", "ffmpeg.exe"),
                System.IO.Path.Combine(baseDir, "..", "bin", "ffmpeg.exe"),
                System.IO.Path.Combine(baseDir, "..", "..", "ffmpeg", "bin", "ffmpeg.exe"),
                System.IO.Path.Combine(localAppData, "VideoTools", "bin", "ffmpeg.exe"),
                @"d:\Apps\ffmpeg\bin\ffmpeg.exe"
            };

            foreach (var c in candidates) {
                if (File.Exists(c)) {
                    ffmpegPath = c;
                    string probeCandidate = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(c), "ffprobe.exe");
                    if (File.Exists(probeCandidate)) ffprobePath = probeCandidate;
                    break;
                }
            }

            if (string.IsNullOrEmpty(ffmpegPath)) {
                // Check PATH
                ffmpegPath = FindInPath("ffmpeg.exe");
                ffprobePath = FindInPath("ffprobe.exe");
            }

            if (!string.IsNullOrEmpty(ffmpegPath)) {
                txtEngineStatus.Text = "● FFmpeg Engine Ready (" + System.IO.Path.GetFileName(ffmpegPath) + ")";
                txtEngineStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            } else {
                txtEngineStatus.Text = "⚠ FFmpeg not found! Please run Setup.bat";
                txtEngineStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            }
        }

        private string FindInPath(string exe) {
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;
            foreach (var dir in pathEnv.Split(';')) {
                try {
                    string full = System.IO.Path.Combine(dir.Trim(), exe);
                    if (File.Exists(full)) return full;
                } catch { }
            }
            return null;
        }

        private void ProbeVideo(VideoItem item) {
            if (string.IsNullOrEmpty(ffprobePath) || !File.Exists(ffprobePath)) return;

            try {
                var psi = new ProcessStartInfo {
                    FileName = ffprobePath,
                    Arguments = string.Format("-v error -show_entries format=duration:stream=width,height -of default=noprint_wrappers=1 \"{0}\"", item.FilePath),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi)) {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();

                    var lines = output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    int w = 0, h = 0;
                    foreach (var l in lines) {
                        if (l.StartsWith("duration=")) {
                            double dur = 0;
                            if (double.TryParse(l.Substring(9), NumberStyles.Any, CultureInfo.InvariantCulture, out dur)) {
                                item.DurationSeconds = dur;
                                int min = (int)(dur / 60);
                                int sec = (int)(dur % 60);
                                item.DurationText = string.Format("{0:00}:{1:00}", min, sec);
                            }
                        } else if (l.StartsWith("width=")) {
                            int.TryParse(l.Substring(6), out w);
                        } else if (l.StartsWith("height=")) {
                            int.TryParse(l.Substring(7), out h);
                        }
                    }
                    if (w > 0 && h > 0) {
                        item.Resolution = string.Format("{0}x{1}", w, h);
                    }
                }
            } catch { }
        }

        private async Task StartProcessing() {
            if (currentVideo == null || !File.Exists(currentVideo.FilePath)) {
                MessageBox.Show("Please select or drop a valid video file first.", "No Video Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath)) {
                MessageBox.Show("FFmpeg engine was not found on this computer. Please run Setup.bat first.", "Engine Missing", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            cts = new CancellationTokenSource();
            btnStart.IsEnabled = false;
            btnCancel.Visibility = Visibility.Visible;
            progressCard.Visibility = Visibility.Visible;
            resultCard.Visibility = Visibility.Collapsed;
            progressBar.Value = 0;
            txtProgressPercent.Text = "0%";
            txtProgressStatus.Text = "Initializing encoder...";
            txtProgressDetails.Text = "Preparing FFmpeg parameters...";

            string inputFile = currentVideo.FilePath;
            string dir = System.IO.Path.GetDirectoryName(inputFile);
            string baseName = System.IO.Path.GetFileNameWithoutExtension(inputFile);

            try {
                if (tabSize.IsChecked == true) {
                    // Compress to target size
                    double targetMB = sliderTargetSize.Value;
                    bool isTurbo = rbTurboFast.IsChecked == true;
                    string resChoice = ((ComboBoxItem)cmbResolution.SelectedItem).Content.ToString();
                    string outputFile = System.IO.Path.Combine(dir, string.Format("{0}_compressed_{1:0}MB.mp4", baseName, targetMB));

                    await ProcessCompressToSize(inputFile, outputFile, targetMB, isTurbo, resChoice, cts.Token);
                } else if (tabFormat.IsChecked == true) {
                    // Format conversion / HEVC / AV1
                    string fmt = ((ComboBoxItem)cmbFormat.SelectedItem).Content.ToString().ToLower();
                    string codec = ((ComboBoxItem)cmbCodec.SelectedItem).Content.ToString();
                    int crf = (int)sliderQuality.Value;
                    string outputFile = System.IO.Path.Combine(dir, string.Format("{0}_converted.{1}", baseName, fmt));

                    await ProcessFormatConvert(inputFile, outputFile, fmt, codec, crf, cts.Token);
                } else {
                    // Extract audio
                    string fmt = ((ComboBoxItem)cmbAudioFmt.SelectedItem).Content.ToString().ToLower();
                    string bitrate = ((ComboBoxItem)cmbAudioBitrate.SelectedItem).Content.ToString();
                    string outputFile = System.IO.Path.Combine(dir, string.Format("{0}.{1}", baseName, fmt));

                    await ProcessExtractAudio(inputFile, outputFile, fmt, bitrate, cts.Token);
                }
            } catch (OperationCanceledException) {
                txtProgressStatus.Text = "Cancelled by user.";
                txtProgressDetails.Text = "Encoding stopped.";
            } catch (Exception ex) {
                MessageBox.Show("Encoding error: " + ex.Message, "Process Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                btnStart.IsEnabled = true;
                btnCancel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task ProcessCompressToSize(string inputFile, string outputFile, double targetMB, bool isTurbo, string resChoice, CancellationToken token) {
            double duration = currentVideo.DurationSeconds;
            if (duration <= 0) duration = 60.0; // Fallback

            // 93% calculation for headers/muxing
            double usableBytes = (targetMB * 1024.0 * 1024.0) * 0.93;
            double totalBits = usableBytes * 8.0;

            int audioKbps = targetMB < 10 ? 64 : (targetMB < 25 ? 96 : 128);
            double audioBits = (audioKbps * 1000.0) * duration;
            double videoBits = totalBits - audioBits;
            int videoKbps = (int)Math.Max(80, Math.Floor((videoBits / duration) / 1000.0));

            // Resolution downscaling
            string scaleFilter = "";
            if (resChoice.Contains("720p")) scaleFilter = "-vf scale=-2:'min(720,ih)'";
            else if (resChoice.Contains("480p")) scaleFilter = "-vf scale=-2:'min(480,ih)'";
            else if (resChoice.Contains("1080p")) scaleFilter = "-vf scale=-2:'min(1080,ih)'";
            else if (videoKbps < 350) scaleFilter = "-vf scale=-2:'min(480,ih)'";
            else if (videoKbps < 750) scaleFilter = "-vf scale=-2:'min(720,ih)'";

            if (isTurbo) {
                // 1-Pass Turbo Mode
                txtProgressStatus.Text = "Running Turbo Fast Encoding (Single Pass)...";
                int maxrate = (int)(videoKbps * 1.25);
                int bufsize = (int)(videoKbps * 2.0);

                string args = string.Format("-y -hide_banner -threads 0 -i \"{0}\" {1} -c:v libx264 -preset veryfast -b:v {2}k -maxrate {3}k -bufsize {4}k -c:a aac -b:a {5}k -movflags +faststart \"{6}\"",
                    inputFile, scaleFilter, videoKbps, maxrate, bufsize, audioKbps, outputFile);

                await RunFFmpegAsync(args, duration, 1, 1, token);
            } else {
                // 2-Pass Mode
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

                    string speedStr = "1.0x";
                    var sm = speedRegex.Match(e.Data);
                    if (sm.Success) speedStr = sm.Groups[1].Value;

                    Dispatcher.Invoke(() => {
                        progressBar.Value = overallPercent;
                        txtProgressPercent.Text = string.Format("{0:0}%", overallPercent);
                        txtProgressDetails.Text = string.Format("Pass {0}/{1} • Speed: {2} • Time: {3:00}:{4:00}",
                            currentPass, totalPasses, speedStr, (int)(curSecs / 60), (int)(curSecs % 60));
                    });
                }
            };

            activeProcess.Exited += (s, e) => {
                tcs.TrySetResult(true);
            };

            activeProcess.Start();
            activeProcess.BeginErrorReadLine();

            using (token.Register(() => {
                try {
                    if (activeProcess != null && !activeProcess.HasExited) {
                        activeProcess.Kill();
                    }
                } catch { }
                tcs.TrySetCanceled();
            })) {
                await tcs.Task;
            }
        }

        private void CancelProcessing() {
            if (cts != null) {
                cts.Cancel();
            }
        }

        private void ShowCompletionResult(string outputFile) {
            lastOutputFile = outputFile;
            progressCard.Visibility = Visibility.Collapsed;

            if (File.Exists(outputFile)) {
                var fi = new FileInfo(outputFile);
                double finalMB = Math.Round(fi.Length / (1024.0 * 1024.0), 2);
                double origMB = currentVideo.SizeMB;
                double savedPercent = origMB > 0 ? Math.Max(0, Math.Round((1.0 - (finalMB / origMB)) * 100.0, 1)) : 0;
                double savedMB = Math.Max(0, Math.Round(origMB - finalMB, 2));

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
        <RowDefinition Height=""Auto""/>
        <RowDefinition Height=""*""/>
        <RowDefinition Height=""Auto""/>
        <RowDefinition Height=""Auto""/>
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

        <Border Grid.Column=""1"" Background=""#1E293B"" BorderBrush=""#334155"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,6"" VerticalAlignment=""Center"">
            <TextBlock x:Name=""TxtEngineStatus"" Text=""● Detecting FFmpeg..."" FontSize=""12"" Foreground=""#94A3B8"" FontWeight=""SemiBold""/>
        </Border>
    </Grid>

    <!-- Drop Zone / Selected File Card -->
    <Grid Grid.Row=""1"" Margin=""0,0,0,16"">
        <!-- Unloaded State: Drop Zone -->
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
                        FontSize=""13"" Padding=""16,8"" BorderThickness=""0"" Cursor=""Hand"" VerticalAlignment=""Center"">
                    <Button.Resources>
                        <Style TargetType=""Border"">
                            <Setter Property=""CornerRadius"" Value=""6""/>
                        </Style>
                    </Button.Resources>
                </Button>
            </Grid>
        </Border>

        <!-- Loaded State: File Card -->
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
                    <Button x:Name=""BtnChangeFile"" Grid.Column=""1"" Content=""Change Video"" Background=""#334155"" Foreground=""#E2E8F0""
                            FontSize=""12"" Padding=""12,6"" BorderThickness=""0"" Cursor=""Hand"" VerticalAlignment=""Center"">
                        <Button.Resources>
                            <Style TargetType=""Border"">
                                <Setter Property=""CornerRadius"" Value=""6""/>
                            </Style>
                        </Button.Resources>
                    </Button>
                </Grid>
            </Border>
        </StackPanel>
    </Grid>

    <!-- Main Settings Panel (Tabs + Options) -->
    <Border Grid.Row=""2"" Background=""#131B2E"" BorderBrush=""#1E293B"" BorderThickness=""1"" CornerRadius=""12"" Padding=""20"" Margin=""0,0,0,16"">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height=""Auto""/>
                <RowDefinition Height=""*""/>
            </Grid.RowDefinitions>

            <!-- Mode Selector Tabs -->
            <StackPanel Grid.Row=""0"" Orientation=""Horizontal"" Margin=""0,0,0,18"">
                <RadioButton x:Name=""TabSize"" Content=""⚡ Compress to Target Size"" IsChecked=""True""
                             GroupName=""ModeTabs"" Cursor=""Hand"" Foreground=""#F8FAFC"" FontSize=""13"" FontWeight=""SemiBold"" Margin=""0,0,24,0""/>
                <RadioButton x:Name=""TabFormat"" Content=""🔄 Format &amp; Archival (HEVC/AV1)""
                             GroupName=""ModeTabs"" Cursor=""Hand"" Foreground=""#94A3B8"" FontSize=""13"" FontWeight=""SemiBold"" Margin=""0,0,24,0""/>
                <RadioButton x:Name=""TabAudio"" Content=""🎵 Extract Audio (MP3)""
                             GroupName=""ModeTabs"" Cursor=""Hand"" Foreground=""#94A3B8"" FontSize=""13"" FontWeight=""SemiBold""/>
            </StackPanel>

            <!-- Mode 1: Target Size -->
            <StackPanel x:Name=""PanelSize"" Grid.Row=""1"" Visibility=""Visible"">
                <TextBlock Text=""Quick Limits &amp; Presets:"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,8""/>
                <WrapPanel Margin=""0,0,0,16"">
                    <Button x:Name=""BtnPreset8"" Content=""8 MB (Discord)"" Margin=""0,0,8,8"" Padding=""12,6"" Background=""#1E293B"" Foreground=""#E2E8F0"" BorderThickness=""0"" Cursor=""Hand""/>
                    <Button x:Name=""BtnPreset10"" Content=""10 MB (Slack)"" Margin=""0,0,8,8"" Padding=""12,6"" Background=""#1E293B"" Foreground=""#E2E8F0"" BorderThickness=""0"" Cursor=""Hand""/>
                    <Button x:Name=""BtnPreset25"" Content=""25 MB (Email / Nitro)"" Margin=""0,0,8,8"" Padding=""12,6"" Background=""#4F46E5"" Foreground=""#FFFFFF"" BorderThickness=""0"" Cursor=""Hand"" FontWeight=""Bold""/>
                    <Button x:Name=""BtnPreset50"" Content=""50 MB (WhatsApp)"" Margin=""0,0,8,8"" Padding=""12,6"" Background=""#1E293B"" Foreground=""#E2E8F0"" BorderThickness=""0"" Cursor=""Hand""/>
                    <Button x:Name=""BtnPreset100"" Content=""100 MB (Web)"" Margin=""0,0,8,8"" Padding=""12,6"" Background=""#1E293B"" Foreground=""#E2E8F0"" BorderThickness=""0"" Cursor=""Hand""/>
                </WrapPanel>

                <Grid Margin=""0,0,0,16"">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""Auto""/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column=""0"" Text=""Target File Size:"" FontSize=""13"" Foreground=""#E2E8F0"" FontWeight=""SemiBold""/>
                    <TextBlock x:Name=""TxtTargetSizeVal"" Grid.Column=""1"" Text=""25.0 MB"" FontSize=""15"" FontWeight=""Bold"" Foreground=""#60A5FA""/>
                </Grid>
                <Slider x:Name=""SliderTargetSize"" Minimum=""2"" Maximum=""250"" Value=""25"" TickFrequency=""5"" IsSnapToTickEnabled=""False"" Margin=""0,0,0,20"" Cursor=""Hand""/>

                <Grid Margin=""0,0,0,12"">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""*""/>
                    </Grid.ColumnDefinitions>

                    <StackPanel Grid.Column=""0"" Margin=""0,0,12,0"">
                        <TextBlock Text=""Speed vs Quality:"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,6""/>
                        <RadioButton x:Name=""RbTurboFast"" Content=""⚡ Turbo Fast (Single-Pass • 2x Faster)"" IsChecked=""True""
                                     GroupName=""SpeedGroup"" Foreground=""#E2E8F0"" FontSize=""12"" Margin=""0,0,0,4"" Cursor=""Hand""/>
                        <RadioButton x:Name=""RbPrecision"" Content=""🎯 High Precision (2-Pass • Exact match)""
                                     GroupName=""SpeedGroup"" Foreground=""#94A3B8"" FontSize=""12"" Cursor=""Hand""/>
                    </StackPanel>

                    <StackPanel Grid.Column=""1"" Margin=""12,0,0,0"">
                        <TextBlock Text=""Downscale Resolution (Optional):"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,6""/>
                        <ComboBox x:Name=""CmbResolution"" SelectedIndex=""0"" Background=""#1E293B"" Foreground=""#000000"" Padding=""8,5"">
                            <ComboBoxItem Content=""Original (Keep Resolution)""/>
                            <ComboBoxItem Content=""720p HD (Faster &amp; Recommended for Mobile)""/>
                            <ComboBoxItem Content=""480p SD (Ultra Compact)""/>
                            <ComboBoxItem Content=""1080p Full HD""/>
                        </ComboBox>
                    </StackPanel>
                </Grid>
            </StackPanel>

            <!-- Mode 2: Format & Archival -->
            <StackPanel x:Name=""PanelFormat"" Grid.Row=""1"" Visibility=""Collapsed"">
                <Grid Margin=""0,0,0,16"">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""*""/>
                    </Grid.ColumnDefinitions>
                    <StackPanel Grid.Column=""0"" Margin=""0,0,12,0"">
                        <TextBlock Text=""Target Container Format:"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,6""/>
                        <ComboBox x:Name=""CmbFormat"" SelectedIndex=""0"" Padding=""8,5"">
                            <ComboBoxItem Content=""MP4 (Universal Compatible)""/>
                            <ComboBoxItem Content=""MKV (High Fidelity Container)""/>
                            <ComboBoxItem Content=""WebM (Modern Web Streaming)""/>
                        </ComboBox>
                    </StackPanel>
                    <StackPanel Grid.Column=""1"" Margin=""12,0,0,0"">
                        <TextBlock Text=""Video Codec:"" FontSize=""12"" FontWeight=""SemiBold"" Foreground=""#94A3B8"" Margin=""0,0,0,6""/>
                        <ComboBox x:Name=""CmbCodec"" SelectedIndex=""1"" Padding=""8,5"">
                            <ComboBoxItem Content=""H.264 / AVC (Most Compatible)""/>
                            <ComboBoxItem Content=""H.265 / HEVC (50% Space Savings)""/>
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
                <TextBlock Text=""Lower CRF = Higher Quality &amp; Bigger Size • Higher CRF = Smaller Size"" FontSize=""11"" Foreground=""#64748B"" Margin=""0,4,0,0""/>
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
                <TextBlock Text=""Extracts the audio track directly from video with zero video overhead."" FontSize=""12"" Foreground=""#64748B""/>
            </StackPanel>
        </Grid>
    </Border>

    <!-- Progress Card -->
    <Border x:Name=""ProgressCard"" Grid.Row=""3"" Background=""#1E293B"" BorderBrush=""#334155"" BorderThickness=""1"" CornerRadius=""8"" Padding=""16"" Margin=""0,0,0,14"" Visibility=""Collapsed"">
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
    <Border x:Name=""ResultCard"" Grid.Row=""3"" Background=""#064E3B"" BorderBrush=""#059669"" BorderThickness=""1"" CornerRadius=""8"" Padding=""16"" Margin=""0,0,0,14"" Visibility=""Collapsed"">
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
    <Grid Grid.Row=""4"">
        <Button x:Name=""BtnStart"" Content=""⚡ Compress Video"" Background=""#4F46E5"" Foreground=""#FFFFFF""
                FontSize=""16"" FontWeight=""Bold"" Height=""46"" BorderThickness=""0"" Cursor=""Hand"" IsEnabled=""False"">
            <Button.Resources>
                <Style TargetType=""Border"">
                    <Setter Property=""CornerRadius"" Value=""8""/>
                </Style>
            </Button.Resources>
        </Button>
    </Grid>
</Grid>";
        }
    }
}
