# High-Efficiency Archival Video Compression using H.265/HEVC or AV1
param(
    [Parameter(Position=0, ValueFromRemainingArguments=$true)]
    [string[]]$Files,
    [Parameter()]
    [string]$Codec = "hevc", # "hevc" (libx265) or "av1" (libsvtav1)
    [Parameter()]
    [int]$CRF = 26, # 24 = very high quality, 28 = high compression
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
    Write-Host "       HIGH-EFFICIENCY ARCHIVAL VIDEO COMPRESSION        " -ForegroundColor Yellow
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

Write-Host "Compression Codec: $($Codec.ToUpper()) | Quality: CRF $CRF" -ForegroundColor Green

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
    $outputFile = Join-Path $dir "$($baseName)_$($Codec.ToLower())_crf$($CRF).mp4"

    Write-Host "`n----------------------------------------------------------" -ForegroundColor DarkCyan
    Write-Host "Processing: $($item.Name) ($initialSizeMB MB)" -ForegroundColor White

    $ffmpegArgs = @("-y", "-hide_banner", "-loglevel", "warning", "-stats", "-i", "$filePath")

    if ($Codec -eq "av1") {
        Write-Host "Compressing using SVT-AV1 (Speed Preset 8, multithreaded)..." -ForegroundColor Cyan
        $ffmpegArgs += @(
            "-threads", "0",
            "-c:v", "libsvtav1",
            "-crf", "$CRF",
            "-preset", "8",
            "-pix_fmt", "yuv420p10le",
            "-c:a", "libopus",
            "-b:a", "128k",
            "$outputFile"
        )
    } else {
        # HEVC / H.265
        Write-Host "Compressing using H.265 / HEVC (Fast preset, multithreaded)..." -ForegroundColor Cyan
        $ffmpegArgs += @(
            "-threads", "0",
            "-c:v", "libx265",
            "-crf", "$CRF",
            "-preset", "fast",
            "-tag:v", "hvc1", # Ensure Apple / QuickTime / Windows 11 compatibility
            "-c:a", "aac",
            "-b:a", "160k",
            "-movflags", "+faststart",
            "$outputFile"
        )
    }

    & $ffmpeg $ffmpegArgs

    if (Test-Path $outputFile) {
        $finalItem = Get-Item $outputFile
        $finalSizeMB = [math]::Round($finalItem.Length / 1MB, 2)
        $savings = [math]::Round((1 - ($finalItem.Length / $item.Length)) * 100, 1)
        Write-Host "`n[SUCCESS] Archival compression complete!" -ForegroundColor Green
        Write-Host "Saved to: $outputFile" -ForegroundColor White
        Write-Host "Initial: $initialSizeMB MB -> Compressed: $finalSizeMB MB ($savings% reduction)" -ForegroundColor Green
    } else {
        Write-Host "[ERROR] Compression failed." -ForegroundColor Red
    }
}

Write-Host "`nAll operations completed." -ForegroundColor Cyan
if (-not $NoPause -and -not [Console]::IsInputRedirected) {
    Read-Host "Press Enter to finish..."
}
