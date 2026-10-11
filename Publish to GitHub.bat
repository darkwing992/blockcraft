@echo off
title Publish BlockCraft to GitHub
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1"
pause
