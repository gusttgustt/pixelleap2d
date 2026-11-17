@echo off
rem Double-click: puts a backup of the player accounts and saves back (from Server\backups).
rem Stops the game server window first, then starts it again (also when you cancel).
rem The Cloudflare tunnel window stays open, so the online address does not change.
chcp 65001 >nul
cd /d "%~dp0"
taskkill /FI "WINDOWTITLE eq Island server*" /T /F >nul 2>&1
timeout /t 1 /nobreak >nul
node restore.js
start "Island server" cmd /k node server.js
echo.
echo Island server started again.
echo.
pause
