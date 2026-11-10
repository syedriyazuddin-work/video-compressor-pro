@echo off
title Video Audio Extractor - MP3
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\extract_audio.ps1" -Format mp3 -Bitrate 320k %*
