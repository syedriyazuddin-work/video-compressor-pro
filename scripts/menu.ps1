# Interactive Video Converter & Compressor Hub
$Host.UI.RawUI.WindowTitle = "Local Video Converter & Compressor"

function Show-Header {
    Clear-Host
    Write-Host "======================================================================" -ForegroundColor Cyan
    Write-Host "                LOCAL VIDEO CONVERTER & COMPRESSOR                    " -ForegroundColor Yellow
    Write-Host "           Powered by FFmpeg • Fast • Private • 100% Offline          " -ForegroundColor Gray
    Write-Host "======================================================================" -ForegroundColor Cyan
    Write-Host ""
}

. "$PSScriptRoot\get_ffmpeg.ps1"
$tools = Ensure-FFmpegInstalled
if (-not $tools) {
    Read-Host "Press Enter to exit..."
    exit 1
}
$ffmpeg = $tools.FFmpeg

$scriptsDir = $PSScriptRoot

while ($true) {
    Show-Header
    Write-Host "Select an action:" -ForegroundColor White
    Write-Host "  [G] Launch Video Compressor Pro (Desktop GUI App)" -ForegroundColor Green -BackgroundColor DarkBlue
    Write-Host "  [1] Compress Video to Target Size (e.g. 8MB, 10MB, 25MB for Discord/Email)" -ForegroundColor Green
    Write-Host "  [2] Convert Video to Universal MP4 (MKV, MOV, WebM, AVI -> MP4)" -ForegroundColor Cyan
    Write-Host "  [3] High-Efficiency HEVC (H.265) Compression (50-70% size cut, archival)" -ForegroundColor Magenta
    Write-Host "  [4] Next-Gen AV1 Compression (Maximum compression density)" -ForegroundColor DarkCyan
    Write-Host "  [5] Extract Audio to MP3 / AAC" -ForegroundColor Yellow
    Write-Host "  [6] Downscale Resolution (4K/1080p -> 720p/480p)" -ForegroundColor Blue
    Write-Host "  [7] Batch Compress All Videos in a Folder" -ForegroundColor DarkYellow
    Write-Host "  [8] Install Right-Click 'Send To' Shortcuts in Windows Explorer" -ForegroundColor White
    Write-Host "  [0] Exit" -ForegroundColor Gray
    Write-Host ""
    $action = Read-Host "Enter option [0-8]"

    if ($action -eq "0") {
        Write-Host "Goodbye!" -ForegroundColor Green
        break
    }

    switch ($action) {
        "g" {
            $guiPath = Join-Path $scriptsDir "..\VideoCompressorUI.exe"
            if (Test-Path $guiPath) {
                Start-Process $guiPath
                Write-Host "Launched Video Compressor Pro GUI!" -ForegroundColor Green
            } else {
                Write-Host "GUI Executable not found at $guiPath" -ForegroundColor Red
            }
            Start-Sleep -Seconds 1
        }
        "1" {
            & "$scriptsDir\compress_target_size.ps1"
        }
        "2" {
            & "$scriptsDir\convert_format.ps1"
        }
        "3" {
            Write-Host "`nSelect Quality Preset:" -ForegroundColor Cyan
            Write-Host "  [1] CRF 24 (Higher quality, ~40% size reduction)"
            Write-Host "  [2] CRF 26 (Balanced sweet spot, ~55% size reduction) [Default]"
            Write-Host "  [3] CRF 28 (Aggressive compression, ~70% size reduction)"
            $q = Read-Host "Choice [1-3, default 2]"
            $crf = 26
            if ($q -eq "1") { $crf = 24 }
            if ($q -eq "3") { $crf = 28 }
            & "$scriptsDir\compress_hevc_av1.ps1" -Codec "hevc" -CRF $crf
        }
        "4" {
            Write-Host "`nSelect AV1 Quality Preset:" -ForegroundColor Cyan
            Write-Host "  [1] CRF 28 (High quality)"
            Write-Host "  [2] CRF 32 (Balanced) [Default]"
            Write-Host "  [3] CRF 36 (Maximum space saving)"
            $q = Read-Host "Choice [1-3, default 2]"
            $crf = 32
            if ($q -eq "1") { $crf = 28 }
            if ($q -eq "3") { $crf = 36 }
            & "$scriptsDir\compress_hevc_av1.ps1" -Codec "av1" -CRF $crf
        }
        "5" {
            Write-Host "`nAudio Format:" -ForegroundColor Cyan
            Write-Host "  [1] MP3 (320 kbps - Studio Quality)"
            Write-Host "  [2] MP3 (192 kbps - Standard)"
            Write-Host "  [3] AAC (256 kbps - Apple / High Quality)"
            $af = Read-Host "Choice [1-3, default 1]"
            $fmt = "mp3"
            $bitrate = "320k"
            if ($af -eq "2") { $bitrate = "192k" }
            if ($af -eq "3") { $fmt = "aac"; $bitrate = "256k" }
            & "$scriptsDir\extract_audio.ps1" -Format $fmt -Bitrate $bitrate
        }
        "6" {
            Write-Host "`nTarget Resolution:" -ForegroundColor Cyan
            Write-Host "  [1] 1080p (Full HD)"
            Write-Host "  [2] 720p  (HD - great for sharing/screen captures)"
            Write-Host "  [3] 480p  (SD - ultra small)"
            $res = Read-Host "Choice [1-3, default 2]"
            $height = 720
            if ($res -eq "1") { $height = 1080 }
            if ($res -eq "3") { $height = 480 }

            $inputPath = Read-Host "Enter video file path (or drag and drop it here)"
            $inputPath = $inputPath.Trim('"').Trim("'")
            if (Test-Path $inputPath) {
                $item = Get-Item $inputPath
                $out = Join-Path $item.DirectoryName "$([System.IO.Path]::GetFileNameWithoutExtension($item.Name))_${height}p.mp4"
                Write-Host "Downscaling to ${height}p (speed-optimized)..." -ForegroundColor Cyan
                & $ffmpeg -y -hide_banner -loglevel warning -stats -threads 0 -i "$inputPath" -vf "scale=-2:$height" -c:v libx264 -crf 22 -preset faster -c:a aac -b:a 128k -movflags +faststart "$out"
                if (Test-Path $out) {
                    Write-Host "[SUCCESS] Saved: $out" -ForegroundColor Green
                }
                Read-Host "Press Enter to continue..."
            }
        }
        "7" {
            $folder = Read-Host "Enter folder path containing videos"
            $folder = $folder.Trim('"').Trim("'")
            if (Test-Path $folder) {
                $exts = @("*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.wmv")
                $vids = Get-ChildItem -Path $folder -Include $exts -Recurse | Where-Object { $_.Name -notmatch "_compressed" }
                Write-Host "Found $($vids.Count) videos." -ForegroundColor Green
                $tSize = Read-Host "Target size in MB per video (e.g. 25, 50, or enter 0 for HEVC 50% archival)"
                if ($tSize -gt 0) {
                    & "$scriptsDir\compress_target_size.ps1" -Files ($vids | ForEach-Object { $_.FullName }) -TargetSizeMB [double]$tSize
                } else {
                    & "$scriptsDir\compress_hevc_av1.ps1" -Files ($vids | ForEach-Object { $_.FullName }) -Codec "hevc" -CRF 26
                }
            } else {
                Write-Host "Folder does not exist!" -ForegroundColor Red
                Start-Sleep -Seconds 2
            }
        }
        "8" {
            & "$scriptsDir\install_sendto_shortcuts.ps1"
        }
        default {
            Write-Host "Invalid option!" -ForegroundColor Red
            Start-Sleep -Seconds 1
        }
    }
}
