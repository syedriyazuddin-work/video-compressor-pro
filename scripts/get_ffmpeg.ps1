# Resolves ffmpeg and ffprobe paths dynamically across all platforms and locations
function Get-FFmpegBinary {
    param([string]$BinaryName = "ffmpeg.exe")

    $candidates = @(
        (Join-Path $PSScriptRoot "..\bin\$BinaryName"),
        (Join-Path $PSScriptRoot "..\..\ffmpeg\bin\$BinaryName"),
        (Join-Path $env:LOCALAPPDATA "VideoTools\bin\$BinaryName"),
        "d:\Apps\ffmpeg\bin\$BinaryName"
    )

    foreach ($path in $candidates) {
        if (Test-Path $path) {
            return (Resolve-Path $path).Path
        }
    }

    $cmdName = [System.IO.Path]::GetFileNameWithoutExtension($BinaryName)
    $cmd = Get-Command $cmdName -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    return $null
}

function Ensure-FFmpegInstalled {
    $ffmpeg = Get-FFmpegBinary "ffmpeg.exe"
    $ffprobe = Get-FFmpegBinary "ffprobe.exe"

    if ($ffmpeg -and $ffprobe) {
        return @{ FFmpeg = $ffmpeg; FFprobe = $ffprobe }
    }

    Write-Host "`n==========================================================" -ForegroundColor Yellow
    Write-Host "                FFMPEG ENGINE NOT FOUND                   " -ForegroundColor Red
    Write-Host "==========================================================" -ForegroundColor Yellow
    Write-Host "FFmpeg is required to convert and compress videos."
    Write-Host ""
    $reply = Read-Host "Would you like to automatically download and set up FFmpeg now? (Y/n)"
    if ($reply -match "^[nN]") {
        Write-Host "Cannot proceed without FFmpeg. Exiting..." -ForegroundColor Red
        return $null
    }

    $binDir = Join-Path $PSScriptRoot "..\bin"
    if (-not (Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir -Force | Out-Null }

    Write-Host "Downloading portable FFmpeg Essentials (~100 MB)..." -ForegroundColor Cyan
    $zipPath = Join-Path $env:TEMP "ffmpeg_download.zip"
    $extractPath = Join-Path $env:TEMP "ffmpeg_extract_$([System.Guid]::NewGuid().ToString('N'))"

    try {
        curl.exe -L -o $zipPath "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
        Write-Host "Extracting binaries..." -ForegroundColor Cyan
        Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

        $innerDir = Get-ChildItem -Path $extractPath -Directory | Select-Object -First 1
        Copy-Item -Path "$($innerDir.FullName)\bin\ffmpeg.exe" -Destination "$binDir\ffmpeg.exe" -Force
        Copy-Item -Path "$($innerDir.FullName)\bin\ffprobe.exe" -Destination "$binDir\ffprobe.exe" -Force

        Remove-Item -Force $zipPath -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force $extractPath -ErrorAction SilentlyContinue

        Write-Host "FFmpeg successfully configured at: $binDir" -ForegroundColor Green
        return @{
            FFmpeg = (Join-Path $binDir "ffmpeg.exe")
            FFprobe = (Join-Path $binDir "ffprobe.exe")
        }
    } catch {
        Write-Host "[ERROR] Failed to download or extract FFmpeg: $_" -ForegroundColor Red
        return $null
    }
}
