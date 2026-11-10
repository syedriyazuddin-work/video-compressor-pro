@echo off
title Archival Video Compressor - AV1
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\compress_hevc_av1.ps1" -Codec av1 -CRF 32 %*
