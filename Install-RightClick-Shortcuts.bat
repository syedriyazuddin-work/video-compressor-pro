@echo off
title Install Right-Click Context Menu
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install_sendto_shortcuts.ps1"
