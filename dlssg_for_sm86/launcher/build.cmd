@echo off
rem Builds the single-file launcher with the mod files embedded. Needs: .NET SDK, Python (+ pillow for the icon).
cd /d "%~dp0"
python make_assets.py || exit /b 1
dotnet publish -c Release -o out || exit /b 1
echo.
echo Done: %~dp0out\DLSSG-Launcher.exe
