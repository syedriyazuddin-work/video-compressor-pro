@echo off
title Archival Video Compressor - H.265 / HEVC
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\compress_hevc_av1.ps1" -Codec hevc -CRF 26 %*
