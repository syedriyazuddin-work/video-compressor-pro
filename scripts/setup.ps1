# Video Tools Master Setup Script
$Host.UI.RawUI.WindowTitle = "Video Tools - Setup"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "               VIDEO CONVERTER & COMPRESSOR SETUP                     " -ForegroundColor Yellow
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

$currentDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$installDir = "$env:LOCALAPPDATA\VideoTools"

# If running on another machine (from Downloads, Temp, USB, etc.), install permanently to LOCALAPPDATA
if ($currentDir -like "*AppData\Local\Temp*" -or ($currentDir -ne $installDir -and $currentDir -ne "d:\Apps\VideoTools")) {
    Write-Host "Installing files to permanent user directory ($installDir)..." -ForegroundColor Cyan
    if (-not (Test-Path $installDir)) { New-Item -ItemType Directory -Path $installDir -Force | Out-Null }
    Copy-Item "$currentDir\*" -Destination $installDir -Recurse -Force
    $toolsDir = $installDir
    $scriptsDir = Join-Path $installDir "scripts"
} else {
    $toolsDir = $currentDir
    $scriptsDir = $PSScriptRoot
}

# Step 1: Ensure FFmpeg is present
Write-Host "[1/3] Checking FFmpeg engine..." -ForegroundColor White
. "$scriptsDir\get_ffmpeg.ps1"
$tools = Ensure-FFmpegInstalled

if (-not $tools) {
    Write-Host "`n[ERROR] FFmpeg setup failed or was cancelled." -ForegroundColor Red
    Read-Host "Press Enter to exit..."
    exit 1
}
Write-Host "      [OK] FFmpeg engine verified: $($tools.FFmpeg)" -ForegroundColor Green

# Step 2: Install Desktop Shortcut
Write-Host "`n[2/3] Creating Desktop shortcut..." -ForegroundColor White
$desktop = [Environment]::GetFolderPath("Desktop")
$WshShell = New-Object -ComObject WScript.Shell

$guiShortcut = $WshShell.CreateShortcut("$desktop\Video Compressor Pro.lnk")
$guiShortcut.TargetPath = "$toolsDir\VideoCompressorUI.exe"
$guiShortcut.WorkingDirectory = $toolsDir
$guiShortcut.Description = "Video Compressor & Converter Pro (Desktop App)"
$guiShortcut.IconLocation = "$toolsDir\VideoCompressorUI.exe,0"
$guiShortcut.Save()
Write-Host "      [+] Desktop Shortcut: 'Video Compressor Pro'" -ForegroundColor Green

# Clean up legacy shortcut if present
$oldDrop = Join-Path $desktop "Compress Video (Drop Here).lnk"
if (Test-Path $oldDrop) { Remove-Item -Force $oldDrop -ErrorAction SilentlyContinue }

# Step 3: Register Right-Click Context Menu for compatible video files only
Write-Host "`n[3/3] Registering Right-Click context menu for compatible video files only..." -ForegroundColor White
& "$scriptsDir\install_sendto_shortcuts.ps1" -NoPause

Write-Host "`n======================================================================" -ForegroundColor Green
Write-Host "                    SETUP COMPLETED SUCCESSFULLY!                     " -ForegroundColor Yellow
Write-Host "======================================================================" -ForegroundColor Green
Write-Host "You are all set! Here is how to use it:" -ForegroundColor Cyan
Write-Host "  1. Double-click 'Video Compressor Pro' on your Desktop (or drop videos onto it)" -ForegroundColor White
Write-Host "  2. Right-click ANY video file in Windows Explorer -> 'Video Compressor Pro'" -ForegroundColor White
Write-Host "     (Non-video files like ZIP, PDF, images will remain clean with zero clutter!)" -ForegroundColor Gray
Write-Host ""
Read-Host "Press Enter to finish setup..."
