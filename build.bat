@echo off
setlocal
echo Compiling VideoCompressorUI.exe using Windows built-in .NET C# compiler...
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

"%CSC%" /target:winexe /optimize+ /platform:anycpu /out:"%~dp0VideoCompressorUI.exe" /lib:"%WPFLIB%" /r:System.dll,System.Core.dll,System.Data.dll,System.Xml.dll,System.Xaml.dll,WindowsBase.dll,PresentationCore.dll,PresentationFramework.dll "%~dp0src\VideoCompressorUI.cs"

if %errorlevel% equ 0 (
    echo [OK] Successfully built VideoCompressorUI.exe!
) else (
    echo [ERROR] Compilation failed.
)
pause
