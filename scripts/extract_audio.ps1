# Extracts audio track from video files into high quality MP3 or AAC
param(
    [Parameter(Position=0, ValueFromRemainingArguments=$true)]
    [string[]]$Files,
    [Parameter()]
    [string]$Format = "mp3", # "mp3" or "aac"
    [Parameter()]
    [string]$Bitrate = "320k", # "320k", "256k", "192k", or "128k"
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
    Write-Host "             EXTRACT AUDIO FROM VIDEO                    " -ForegroundColor Yellow
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
    $dir = $item.DirectoryName
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($item.Name)
    $outputFile = Join-Path $dir "$($baseName).$Format"

    Write-Host "`n----------------------------------------------------------" -ForegroundColor DarkCyan
    Write-Host "Extracting audio from: $($item.Name)" -ForegroundColor White

    $ffmpegArgs = @(
        "-y", "-hide_banner", "-loglevel", "warning", "-stats",
        "-i", "$filePath",
        "-vn" # No video
    )

    if ($Format -eq "mp3") {
        $ffmpegArgs += @("-c:a", "libmp3lame", "-b:a", "$Bitrate", "$outputFile")
    } elseif ($Format -eq "aac") {
        $ffmpegArgs += @("-c:a", "aac", "-b:a", "$Bitrate", "$outputFile")
    } elseif ($Format -eq "flac") {
        $ffmpegArgs += @("-c:a", "flac", "$outputFile")
    }

    & $ffmpeg $ffmpegArgs

    if (Test-Path $outputFile) {
        $finalItem = Get-Item $outputFile
        $finalSizeMB = [math]::Round($finalItem.Length / 1MB, 2)
        Write-Host "[SUCCESS] Extracted audio successfully!" -ForegroundColor Green
        Write-Host "Saved to: $outputFile ($finalSizeMB MB)" -ForegroundColor Green
    } else {
        Write-Host "[ERROR] Failed to extract audio." -ForegroundColor Red
    }
}

Write-Host "`nAll operations completed." -ForegroundColor Cyan
if (-not $NoPause -and -not [Console]::IsInputRedirected) {
    Read-Host "Press Enter to finish..."
}
