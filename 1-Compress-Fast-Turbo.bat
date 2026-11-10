@echo off
title Video Compressor - Turbo Fast
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\compress_target_size.ps1" -SpeedMode fast %*
