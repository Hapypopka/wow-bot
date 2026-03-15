@echo off
taskkill /f /im WowBot.Injector.exe >nul 2>&1
timeout /t 1 /nobreak >nul
dotnet publish WowBot.Injector -c Release -r win-x86 --self-contained -o publish
echo.
echo === Ready! Run: publish\WowBot.Injector.exe ===
pause
