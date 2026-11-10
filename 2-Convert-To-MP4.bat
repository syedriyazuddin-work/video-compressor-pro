@echo off
title Video Converter - Universal MP4
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\convert_format.ps1" -TargetFormat mp4 %*
