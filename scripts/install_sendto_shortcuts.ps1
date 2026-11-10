param(
    [switch]$NoPause,
    [switch]$Uninstall
)

# Registers Right-Click Context Menu for COMPATIBLE VIDEO FILES ONLY
# Cleans up any legacy video items from the generic Windows 'Send to' folder.

$toolsDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$appExe = Join-Path $toolsDir "VideoCompressorUI.exe"

if (-not (Test-Path $appExe)) {
    # Check LocalAppData fallback
    $fallback = "$env:LOCALAPPDATA\VideoTools\VideoCompressorUI.exe"
    if (Test-Path $fallback) {
        $appExe = $fallback
    }
}

$videoExtensions = @(
    ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".flv", 
    ".m4v", ".ts", ".mts", ".m2ts", ".3gp", ".3g2", ".mpeg", 
    ".mpg", ".vob", ".ogv", ".divx", ".asf", ".f4v"
)

# 1. Clean up legacy shortcuts from Windows 'Send to' menu so they don't show up on non-video files
$sendToFolder = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\SendTo")
if (Test-Path $sendToFolder) {
    $legacyNames = @(
        "Compress Video (High-Efficiency HEVC H.265).lnk",
        "Compress Video (Target Size - 8MB, 25MB, 50MB).lnk",
        "Convert Video to MP4.lnk",
        "Extract Audio (MP3 320k).lnk",
        "Video Compressor Pro (GUI).lnk",
        "Video Compressor Pro.lnk"
    )
    foreach ($sc in $legacyNames) {
        $p = Join-Path $sendToFolder $sc
        if (Test-Path $p) {
            Remove-Item -Force $p -ErrorAction SilentlyContinue
        }
    }
    Write-Host "  [OK] Cleaned up legacy shortcuts from Windows 'Send to' menu." -ForegroundColor Green
}

if ($Uninstall) {
    # Remove registry associations
    foreach ($ext in $videoExtensions) {
        $key = "HKCU:\Software\Classes\SystemFileAssociations\$ext\shell\VideoCompressorPro"
        if (Test-Path $key) { Remove-Item -Path $key -Recurse -Force -ErrorAction SilentlyContinue }
    }
    $vKey = "HKCU:\Software\Classes\SystemFileAssociations\video\shell\VideoCompressorPro"
    if (Test-Path $vKey) { Remove-Item -Path $vKey -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host "  [OK] Removed Right-Click context menu associations." -ForegroundColor Green
    return
}

# 2. Register native Windows context menu ONLY for compatible video formats
Write-Host "Registering Right-Click menu for compatible video files only..." -ForegroundColor Cyan

$menuLabel = "Video Compressor Pro"
$iconLoc = "shell32.dll,137"
$commandString = "`"$appExe`" `"%1`""

foreach ($ext in $videoExtensions) {
    $baseKey = "HKCU:\Software\Classes\SystemFileAssociations\$ext\shell\VideoCompressorPro"
    try {
        New-Item -Path $baseKey -Force | Out-Null
        Set-ItemProperty -Path $baseKey -Name "(default)" -Value $menuLabel
        Set-ItemProperty -Path $baseKey -Name "Icon" -Value $iconLoc
        
        $cmdKey = "$baseKey\command"
        New-Item -Path $cmdKey -Force | Out-Null
        Set-ItemProperty -Path $cmdKey -Name "(default)" -Value $commandString
    } catch {
        Write-Host "      [WARNING] Could not register for $ext : $_" -ForegroundColor Yellow
    }
}

# Also register for generic perceived type video
$videoTypeKey = "HKCU:\Software\Classes\SystemFileAssociations\video\shell\VideoCompressorPro"
try {
    New-Item -Path $videoTypeKey -Force | Out-Null
    Set-ItemProperty -Path $videoTypeKey -Name "(default)" -Value $menuLabel
    Set-ItemProperty -Path $videoTypeKey -Name "Icon" -Value $iconLoc
    $cmdKey = "$videoTypeKey\command"
    New-Item -Path $cmdKey -Force | Out-Null
    Set-ItemProperty -Path $cmdKey -Name "(default)" -Value $commandString
} catch { }

Write-Host "  [OK] Video Compressor Pro will now appear directly on right-click for video files ONLY!" -ForegroundColor Green
Write-Host "       (Non-video files such as ZIP, PDF, images, etc. will NOT show this option)" -ForegroundColor Gray

if (-not $NoPause -and -not [Console]::IsInputRedirected) {
    Read-Host "`nPress Enter to finish..."
}
