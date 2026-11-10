# Video Compressor & Converter Pro 🎬

> A lightning-fast, zero-dependency, 100% offline desktop video compressor and converter for Windows. Built with native WPF and powered by FFmpeg.

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue.svg)](https://microsoft.com/windows)
[![Framework](https://img.shields.io/badge/.NET-Framework%204.0%2B-512BD4.svg)](https://dotnet.microsoft.com)
[![Privacy](https://img.shields.io/badge/privacy-100%25%20Offline%20%26%20Local-success.svg)]()
[![License](https://img.shields.io/badge/license-MIT-green.svg)]()

---

## ✨ Features

- 🎨 **Modern Desktop GUI**: Sleek dark-mode WPF interface with smooth progress bars, ETA, and speed metrics.
- 🎯 **Target File Size Compression**: Drag a slider or click one of the quick presets to compress videos to exact file size limits:
  - **8 MB / 10 MB** — Discord free tier
  - **16 MB** — WhatsApp
  - **25 MB** — Microsoft Teams / Slack / Gmail attachment limit
  - **50 MB / 100 MB** — Web and portal uploads
- ⚡ **Turbo Fast & Multi-Threaded**: Optimized for multi-core CPUs (1-Pass dynamic bitrate + multi-threading) to compress videos in seconds rather than minutes.
- 🎞️ **Universal Codec Support**:
  - **H.264 (Universal)**: Plays on every phone, browser, TV, and operating system.
  - **HEVC / H.265 (Archival)**: 50–70% smaller file sizes for long-term storage.
  - **AV1 (Next-Gen)**: Maximum storage efficiency.
  - **Audio Extraction**: Rip audio tracks to studio-grade MP3 (320 kbps).
- 🖱️ **Smart Windows Context Menu**:
  - Right-click **any video file** (`.mp4`, `.mkv`, `.mov`, `.avi`, `.webm`, `.flv`, `.ts`, etc.) ➔ click **Video Compressor Pro** to launch instantly.
  - **Zero Clutter**: Does not pollute right-click menus on non-video files (ZIPs, PDFs, Word documents, images remain completely clean).
- 📦 **Zero-Config Portable Deployment**:
  - No Python, Node.js, Electron, or Visual Studio required.
  - Compiles directly using Windows' built-in C# compiler (`csc.exe`).

---

## 🚀 Quick Start

### Option 1: One-Click Installer (Recommended)
1. Download or clone this repository.
2. Double-click **`Install-VideoTools.bat`**.
3. It will:
   - Extract the application to `%LOCALAPPDATA%\VideoTools\`.
   - Automatically configure portable FFmpeg.
   - Place a **Video Compressor Pro** shortcut on your Desktop.
   - Register the clean right-click menu for video files.

### Option 2: Portable / Manual Use
1. Run `build.bat` to compile `VideoCompressorUI.exe` from source.
2. Place `ffmpeg.exe` and `ffprobe.exe` into the `bin\` folder (or run `scripts\get_ffmpeg.ps1`).
3. Double-click `VideoCompressorUI.exe` or drop any video onto it.

---

## 🖥️ Usage

1. **Launch the App**: Double-click `Video Compressor Pro` on your Desktop.
2. **Select Video**:
   - Drag & drop any video file into the window, OR
   - Right-click any video in File Explorer and choose **Video Compressor Pro**.
3. **Choose Compression Mode**:
   - **Target Size**: Pick your desired size (e.g. 25 MB) and encoding mode (Turbo Fast or Precision).
   - **Convert Format**: Choose MP4, MKV, WebM, AVI, or MOV with adjustable quality (CRF).
   - **Extract Audio**: Extract audio tracks to MP3, AAC, or WAV.
4. **Click "Start Compression"**: Watch live progress, encoding speed (fps / multiplier), and final space saved.

---

## 🛠️ Building from Source

This project requires **no IDE or external compiler installed** — it uses the built-in Microsoft .NET Framework C# compiler that comes pre-installed on every modern Windows PC:

```cmd
build.bat
```

Or run via Command Prompt:
```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /platform:anycpu /out:VideoCompressorUI.exe /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /r:System.dll,System.Core.dll,System.Data.dll,System.Xml.dll,System.Xaml.dll,WindowsBase.dll,PresentationCore.dll,PresentationFramework.dll src\VideoCompressorUI.cs
```

---

## 📁 Project Structure

```
├── src/
│   └── VideoCompressorUI.cs     # Full C# WPF Application source code
├── scripts/
│   ├── get_ffmpeg.ps1           # Dynamic portable FFmpeg downloader/locator
│   ├── install_sendto_shortcuts.ps1 # Context menu & extension registrar
│   ├── compress_target_size.ps1 # Target size encoding engine
│   ├── convert_format.ps1       # Universal format conversion engine
│   ├── compress_hevc_av1.ps1    # High-efficiency archival encoder
│   ├── extract_audio.ps1        # Audio extraction engine
│   ├── setup.ps1                # Master installation logic
│   └── menu.ps1                 # Interactive CLI hub fallback
├── build.bat                    # 1-Click native compiler script
├── Install-VideoTools.bat       # Self-contained polyglot installer & updater
├── README.md                    # Project documentation
└── .gitignore                   # Excludes heavy binaries & temp files
```

---

## 🔒 Privacy & Security

- **100% Local**: All video processing and compression happens strictly on your local machine using your CPU/GPU.
- **Zero Telemetry**: No analytics, tracking, or network calls (except the initial one-time download of FFmpeg if not already present).
- **Safe Sandboxing**: Installs into user-space (`%LOCALAPPDATA%`), requiring zero administrative rights.

---

## 📄 License

MIT License. Free for personal and commercial use.
