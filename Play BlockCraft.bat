@echo off
title BlockCraft
cd /d "%~dp0"
rem Starts the BlockCraft save server (uses PowerShell, built into Windows) and opens the game.
rem Your worlds are saved as files in the "saves" folder next to this file.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0server.ps1"
if errorlevel 1 (
  echo.
  echo Could not start the save server - opening the game directly instead.
  echo Worlds will be saved in your browser. You can use "Choose Save Folder" in the game.
  start "" "%~dp0index.html"
  pause
)
