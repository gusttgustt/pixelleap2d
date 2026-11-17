@echo off
rem Double-click: runs the game server on this PC and a free Cloudflare Quick Tunnel so
rem friends anywhere can play online with you.
rem   - The server opens in its own window ("Island server").
rem   - This window starts the tunnel and publishes its address (docs/server.txt on GitHub),
rem     so the usual game link https://gusttgustt.github.io/pixelleap2d/ connects to this PC
rem     about a minute later. The tunnel link (https://something.trycloudflare.com) works too.
rem   - Close both windows to stop.
rem Needs Node.js (https://nodejs.org) and git signed in to GitHub. cloudflared.exe must be next to this file.
cd /d "%~dp0"
start "Island server" cmd /k node server.js
timeout /t 2 /nobreak >nul
node online.js
pause
