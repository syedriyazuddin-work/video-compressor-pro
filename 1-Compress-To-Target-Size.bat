@echo off
title Video Compressor - Target Size
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\compress_target_size.ps1" %*
