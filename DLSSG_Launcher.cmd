@echo off
rem Double-click to open the DLSSG SM86 Launcher (finds games in C:\Games, installs the mod, launches the game).
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%~dp0DLSSG_Launcher.ps1"
