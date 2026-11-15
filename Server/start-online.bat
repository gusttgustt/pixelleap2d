@echo off
rem Double-click: runs the game server on this PC and a free Cloudflare Quick Tunnel so
rem friends anywhere can play online with you.
rem   - The server opens in its own window ("Island server").
rem   - This window shows the link to send: https://something.trycloudflare.com
rem     Friends open that link: it takes them to the game, already connected to this PC.
rem   - The link changes every time you start this. Close both windows to stop.
rem Needs Node.js (https://nodejs.org). cloudflared.exe must be next to this file.
cd /d "%~dp0"
start "Island server" cmd /k node server.js
timeout /t 2 /nobreak >nul
echo.
echo  ================================================================
echo   Waiting for the link... look below for a line like:
echo     https://something-something.trycloudflare.com
echo   Send THAT link to your friends (and open it yourself too).
echo   Keep this window open while you play. Close it to stop.
echo  ================================================================
echo.
cloudflared.exe tunnel --no-autoupdate --url http://localhost:8787
pause
