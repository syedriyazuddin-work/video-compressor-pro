@echo off
setlocal
echo Compiling VideoCompressorUI.exe using Windows built-in .NET C# compiler with custom app icon...
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "WPFLIB=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"

if not exist "%CSC%" (
    set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    set "WPFLIB=C:\Windows\Microsoft.NET\Framework\v4.0.30319\WPF"
)

if not exist "%CSC%" (
    echo [ERROR] csc.exe not found!
    pause
    exit /b 1
)

set "ICONARG="
if exist "%~dp0app.ico" set "ICONARG=/win32icon:%~dp0app.ico"

"%CSC%" /target:winexe /optimize+ /platform:anycpu %ICONARG% /out:"%~dp0VideoCompressorUI.exe" /lib:"%WPFLIB%" /r:System.dll,System.Core.dll,System.Data.dll,System.Xml.dll,System.Xaml.dll,WindowsBase.dll,PresentationCore.dll,PresentationFramework.dll,System.Drawing.dll "%~dp0src\VideoCompressorUI.cs"

if %errorlevel% equ 0 (
    echo [OK] Successfully built VideoCompressorUI.exe with embedded custom icon!
) else (
    echo [ERROR] Compilation failed.
)
pause
