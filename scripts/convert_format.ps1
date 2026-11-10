# Converts video formats (MKV, MOV, WebM, AVI, etc.) to clean, universal MP4 (or user selected format)
param(
    [Parameter(Position=0, ValueFromRemainingArguments=$true)]
    [string[]]$Files,
    [Parameter()]
    [string]$TargetFormat = "mp4",
    [Parameter()]
    [string]$Mode = "smart", # "smart" (remux if compatible, else re-encode), "reencode", "copy"
    [Parameter()]
    [switch]$NoPause
)

# Locate ffmpeg & ffprobe dynamically
. "$PSScriptRoot\get_ffmpeg.ps1"
$tools = Ensure-FFmpegInstalled
if (-not $tools) {
    Read-Host "Press Enter to exit..."
    exit 1
}
$ffmpeg = $tools.FFmpeg
$ffprobe = $tools.FFprobe

if (-not $Files -or $Files.Count -eq 0) {
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "             CONVERT VIDEO FORMAT TOOL                   " -ForegroundColor Yellow
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "Tip: You can also drag and drop video files directly onto this script!" -ForegroundColor Gray
    Write-Host ""
    $inputPath = Read-Host "Enter video file path (or drag and drop it here)"
    $inputPath = $inputPath.Trim('"').Trim("'")
    if (-not $inputPath -or -not (Test-Path $inputPath)) {
        Write-Host "Invalid file path!" -ForegroundColor Red
        Read-Host "Press Enter to exit..."
        exit 1
    }
    $Files = @($inputPath)
}

foreach ($file in $Files) {
    $filePath = $file.Trim('"').Trim("'")
    if (-not (Test-Path $filePath)) {
        Write-Host "[SKIP] File not found: $filePath" -ForegroundColor Yellow
        continue
    }

    $item = Get-Item $filePath
    $initialSizeMB = [math]::Round($item.Length / 1MB, 2)
    $dir = $item.DirectoryName
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($item.Name)
    $ext = $item.Extension.TrimStart('.').ToLower()
    $outputFile = Join-Path $dir "$($baseName)_converted.$TargetFormat"

    # Avoid overwriting same file
    if ($item.FullName -eq $outputFile) {
        $outputFile = Join-Path $dir "$($baseName)_converted_new.$TargetFormat"
    }

    Write-Host "`n----------------------------------------------------------" -ForegroundColor DarkCyan
    Write-Host "Processing: $($item.Name)" -ForegroundColor White
    Write-Host "Original Format: $ext ($initialSizeMB MB)" -ForegroundColor Yellow

    # Probe codec
    $vcodec = & $ffprobe -v error -select_streams v:0 -show_entries stream=codec_name -of default=noprint_wrappers=1:nokey=1 "$filePath"
    $acodec = & $ffprobe -v error -select_streams a:0 -show_entries stream=codec_name -of default=noprint_wrappers=1:nokey=1 "$filePath"
    Write-Host "Detected Codecs: Video=$vcodec | Audio=$acodec" -ForegroundColor Gray

    # Determine optimal parameters
    $ffmpegArgs = @("-y", "-hide_banner", "-loglevel", "warning", "-stats", "-i", "$filePath")

    if ($TargetFormat -eq "mp4") {
        if ($Mode -eq "smart" -and ($vcodec -eq "h264" -or $vcodec -eq "hevc") -and ($acodec -eq "aac" -or $acodec -eq "mp3")) {
            Write-Host "Fast lossless remuxing (zero quality loss, instant transfer)..." -ForegroundColor Green
            $ffmpegArgs += @("-c", "copy", "-movflags", "+faststart", "$outputFile")
        } else {
            Write-Host "Re-encoding to universal H.264 + AAC MP4 (faster preset, multithreaded)..." -ForegroundColor Cyan
            $ffmpegArgs += @(
                "-threads", "0",
                "-c:v", "libx264",
                "-preset", "faster",
                "-crf", "21",
                "-pix_fmt", "yuv420p",
                "-c:a", "aac",
                "-b:a", "192k",
                "-movflags", "+faststart",
                "$outputFile"
            )
        }
    } elseif ($TargetFormat -eq "mkv") {
        $ffmpegArgs += @("-c", "copy", "$outputFile")
    } elseif ($TargetFormat -eq "webm") {
        Write-Host "Encoding to WebM (VP9 + Opus, speed-optimized)..." -ForegroundColor Cyan
        $ffmpegArgs += @(
            "-threads", "0",
            "-c:v", "libvpx-vp9",
            "-crf", "32",
            "-b:v", "0",
            "-cpu-used", "4",
            "-deadline", "good",
            "-c:a", "libopus",
            "-b:a", "128k",
            "$outputFile"
        )
    } else {
        $ffmpegArgs += @("-movflags", "+faststart", "$outputFile")
    }

    & $ffmpeg $ffmpegArgs

    if (Test-Path $outputFile) {
        $finalItem = Get-Item $outputFile
        $finalSizeMB = [math]::Round($finalItem.Length / 1MB, 2)
        Write-Host "`n[SUCCESS] Conversion complete!" -ForegroundColor Green
        Write-Host "Saved to: $outputFile ($finalSizeMB MB)" -ForegroundColor Green
    } else {
        Write-Host "[ERROR] Conversion failed." -ForegroundColor Red
    }
}

Write-Host "`nAll files processed." -ForegroundColor Cyan
if (-not $NoPause -and -not [Console]::IsInputRedirected) {
    Read-Host "Press Enter to finish..."
}
