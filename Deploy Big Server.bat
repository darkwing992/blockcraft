@echo off
title Deploy BlockCraft Big Server
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0server\deploy.ps1"
pause
