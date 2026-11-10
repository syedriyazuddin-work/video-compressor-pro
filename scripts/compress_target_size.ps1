# Compresses video files to match an exact target size (e.g., 8MB, 10MB, 25MB, 50MB, etc.)
param(
    [Parameter(Position=0, ValueFromRemainingArguments=$true)]
    [string[]]$Files,
    [Parameter()]
    [double]$TargetSizeMB = 0,
    [Parameter()]
    [string]$SpeedMode = "", # "fast" or "quality"
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
    Write-Host "         COMPRESS VIDEO TO TARGET FILE SIZE              " -ForegroundColor Yellow
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

if ($TargetSizeMB -le 0) {
    Write-Host ""
    Write-Host "Common Target Size Presets:" -ForegroundColor Cyan
    Write-Host "  [1]  8 MB   (Discord Free / Legacy limit)"
    Write-Host "  [2] 10 MB   (Slack / Web upload limit)"
    Write-Host "  [3] 25 MB   (Discord Nitro Basic / Gmail / Outlook attachment limit)"
    Write-Host "  [4] 50 MB   (WhatsApp Document limit)"
    Write-Host "  [5] 100 MB  (Fast web sharing)"
    Write-Host "  [6] Custom target size (MB)"
    Write-Host ""
    $choice = Read-Host "Select preset [1-6] or enter target size in MB (Default: 25)"
    switch ($choice.Trim()) {
        "1" { $TargetSizeMB = 8 }
        "2" { $TargetSizeMB = 10 }
        "3" { $TargetSizeMB = 25 }
        "4" { $TargetSizeMB = 50 }
        "5" { $TargetSizeMB = 100 }
        "6" {
            $custom = Read-Host "Enter target size in MB (e.g. 15, 30, 45)"
            $TargetSizeMB = [double]$custom
        }
        "" { $TargetSizeMB = 25 }
        default {
            if ($choice -match '^\d+(\.\d+)?$') {
                $TargetSizeMB = [double]$choice
            } else {
                $TargetSizeMB = 25
            }
        }
    }
}

Write-Host "`nTarget size set to: $TargetSizeMB MB" -ForegroundColor Green

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
    $outputFile = Join-Path $dir "$($baseName)_compressed_${TargetSizeMB}MB.mp4"

    Write-Host "`n----------------------------------------------------------" -ForegroundColor DarkCyan
    Write-Host "Processing: $($item.Name)" -ForegroundColor White
    Write-Host "Original Size: $initialSizeMB MB" -ForegroundColor Yellow

    if ($initialSizeMB -le $TargetSizeMB) {
        Write-Host "Note: File is already smaller than or equal to target size ($initialSizeMB MB <= $TargetSizeMB MB)." -ForegroundColor Gray
    }

    # Get duration using ffprobe
    $durationStr = & $ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 "$filePath"
    $duration = 0.0
    [double]::TryParse($durationStr, [ref]$duration) | Out-Null

    if ($duration -le 0) {
        Write-Host "[ERROR] Could not determine video duration for $($item.Name)" -ForegroundColor Red
        continue
    }

    $minutes = [math]::Floor($duration / 60)
    $seconds = [math]::Round($duration % 60)
    Write-Host "Duration: ${minutes}m ${seconds}s ($([math]::Round($duration, 1))s total)" -ForegroundColor Cyan

    # Target calculation
    # Use 93% of target size to account for container overhead, packet headers, metadata
    $usableBytes = ($TargetSizeMB * 1024 * 1024) * 0.93
    $totalBits = $usableBytes * 8

    # Audio bitrate selection
    if ($TargetSizeMB -lt 10) {
        $audioKbps = 64
    } elseif ($TargetSizeMB -lt 25) {
        $audioKbps = 96
    } else {
        $audioKbps = 128
    }

    $audioBits = ($audioKbps * 1000) * $duration
    $videoBits = $totalBits - $audioBits
    $videoBitrateBps = $videoBits / $duration
    $videoKbps = [math]::Floor($videoBitrateBps / 1000)

    # Resolution downscaling policy based on calculated video bitrate
    $scaleFilter = ""
    if ($videoKbps -lt 300) {
        $scaleFilter = "-vf scale=-2:'min(480,ih)'"
        Write-Host "Bitrate is low ($videoKbps kbps) -> Downscaling to max 480p for clear details." -ForegroundColor Magenta
    } elseif ($videoKbps -lt 700) {
        $scaleFilter = "-vf scale=-2:'min(720,ih)'"
        Write-Host "Bitrate is medium ($videoKbps kbps) -> Downscaling to max 720p for smooth playback." -ForegroundColor Magenta
    } elseif ($videoKbps -lt 2500) {
        $scaleFilter = "-vf scale=-2:'min(1080,ih)'"
    }

    if ($videoKbps -lt 50) {
        Write-Host "[WARNING] Video duration is too long for target size $TargetSizeMB MB!" -ForegroundColor Yellow
        Write-Host "Forcing minimum 80 kbps video bitrate." -ForegroundColor Yellow
        $videoKbps = 80
    }

    if (-not $SpeedMode) {
        Write-Host ""
        Write-Host "Encoding Speed Mode:" -ForegroundColor Cyan
        Write-Host "  [1] Turbo Fast (1-Pass, veryfast preset - 2x to 3x faster) [Default]"
        Write-Host "  [2] High Precision (2-Pass, medium preset - maximum precision)"
        $sm = Read-Host "Select speed mode [1 or 2, default 1]"
        if ($sm.Trim() -eq "2") { $SpeedMode = "quality" } else { $SpeedMode = "fast" }
    }

    Write-Host "Encoding Settings: Video ${videoKbps} kbps | Audio ${audioKbps} kbps (AAC) | Mode: $($SpeedMode.ToUpper())" -ForegroundColor Cyan

    if ($SpeedMode -eq "fast") {
        Write-Host "Running Turbo Fast 1-Pass encoding (utilizing all CPU cores)..." -ForegroundColor Green
        $maxrate = [math]::Round($videoKbps * 1.25)
        $bufsize = [math]::Round($videoKbps * 2.0)
        $fastArgs = @(
            "-y", "-hide_banner", "-loglevel", "warning", "-stats",
            "-threads", "0",
            "-i", "$filePath"
        )
        if ($scaleFilter) {
            $fastArgs += $scaleFilter.Split(" ")
        }
        $fastArgs += @(
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-b:v", "${videoKbps}k",
            "-maxrate", "${maxrate}k",
            "-bufsize", "${bufsize}k",
            "-c:a", "aac",
            "-b:a", "${audioKbps}k",
            "-movflags", "+faststart",
            "$outputFile"
        )
        & $ffmpeg $fastArgs
    } else {
        Write-Host "Running 2-pass encoding for optimal quality at exact target size..." -ForegroundColor White

        # Temp pass log prefix
        $passlog = Join-Path $env:TEMP "ffmpeg2pass_$([System.Guid]::NewGuid().ToString('N'))"

        # Pass 1
        Write-Host "Pass 1/2 analyzing video motion..." -ForegroundColor Gray
        $pass1Args = @(
            "-y", "-hide_banner", "-loglevel", "warning",
            "-threads", "0",
            "-i", "$filePath"
        )
        if ($scaleFilter) {
            $pass1Args += $scaleFilter.Split(" ")
        }
        $pass1Args += @(
            "-c:v", "libx264",
            "-preset", "faster",
            "-b:v", "${videoKbps}k",
            "-pass", "1",
            "-passlogfile", "$passlog",
            "-an",
            "-f", "null",
            "NUL"
        )
        & $ffmpeg $pass1Args

        # Pass 2
        Write-Host "Pass 2/2 encoding final output..." -ForegroundColor Gray
        $pass2Args = @(
            "-y", "-hide_banner", "-loglevel", "warning", "-stats",
            "-threads", "0",
            "-i", "$filePath"
        )
        if ($scaleFilter) {
            $pass2Args += $scaleFilter.Split(" ")
        }
        $pass2Args += @(
            "-c:v", "libx264",
            "-preset", "faster",
            "-b:v", "${videoKbps}k",
            "-pass", "2",
            "-passlogfile", "$passlog",
            "-c:a", "aac",
            "-b:a", "${audioKbps}k",
            "-movflags", "+faststart",
            "$outputFile"
        )
        & $ffmpeg $pass2Args

        # Clean up pass logs
        Remove-Item -Force "$($passlog)*" -ErrorAction SilentlyContinue
    }

    if (Test-Path $outputFile) {
        $finalItem = Get-Item $outputFile
        $finalSizeMB = [math]::Round($finalItem.Length / 1MB, 2)
        $savings = [math]::Round((1 - ($finalItem.Length / $item.Length)) * 100, 1)
        Write-Host "`n[SUCCESS] Compression complete!" -ForegroundColor Green
        Write-Host "Output: $outputFile" -ForegroundColor White
        Write-Host "Initial Size: $initialSizeMB MB -> Compressed Size: $finalSizeMB MB ($savings% reduction)" -ForegroundColor Green
    } else {
        Write-Host "[ERROR] Failed to produce output file." -ForegroundColor Red
    }
}

Write-Host "`nAll operations completed." -ForegroundColor Cyan
if (-not $NoPause -and -not [Console]::IsInputRedirected) {
    Read-Host "Press Enter to finish..."
}
