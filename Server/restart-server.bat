@echo off
rem Restarts only the game server (the "Island server" window) with the newest server.js.
rem The Cloudflare tunnel window stays open, so the online address does not change.
cd /d "%~dp0"
taskkill /FI "WINDOWTITLE eq Island server*" /T /F >nul 2>&1
timeout /t 1 /nobreak >nul
start "Island server" cmd /k node server.js
