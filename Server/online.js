// Puts this PC's game server online (run by start-online.bat, after server.js is running).
//
//   1. Starts a free Cloudflare Quick Tunnel to the server (http://localhost:8787).
//   2. The tunnel's address changes every start, so it writes the new one into docs/server.txt
//      and pushes just that file to GitHub. GitHub Pages then serves it next to the game, and the
//      game page (docs/index.html) reads it: the usual GitHub Pages link always reaches this PC.
//
// Needs cloudflared.exe next to this file, and git able to push to the repo (main branch).
// GitHub Pages takes about a minute to show the new address after the push.

const { spawn, execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const PORT = process.env.PORT || 8787;
const REPO = path.resolve(__dirname, "..");
const LINK_FILE = path.join(REPO, "docs", "server.txt");
const GAME_URL = "https://gusttgustt.github.io/pixelleap2d/";

function git(...args) {
  return execFileSync("git", args, { cwd: REPO, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
}

function publish(ws) {
  let old = "";
  try { old = fs.readFileSync(LINK_FILE, "utf8").trim(); } catch (e) {}
  if (old === ws) { console.log("  server.txt already has this address."); return; }
  fs.writeFileSync(LINK_FILE, ws + "\n");
  try {
    const branch = git("rev-parse", "--abbrev-ref", "HEAD");
    if (branch !== "main") throw new Error("the repo is on branch '" + branch + "', not main");
    git("add", "--", "docs/server.txt");
    git("commit", "-m", "Server link", "--", "docs/server.txt"); // only this file, whatever else is changed
    git("push", "origin", "main");
    console.log("  Published. In about a minute, " + GAME_URL + " connects to this PC.");
  } catch (e) {
    const why = String((e.stderr && e.stderr.toString()) || e.message).trim().split("\n").slice(-3).join("\n    ");
    console.log("");
    console.log("  !! Could not publish the address to GitHub:");
    console.log("    " + why);
    console.log("  The tunnel link above still works: send it to your friends instead.");
  }
}

const exe = path.join(__dirname, "cloudflared.exe");
const tunnel = spawn(exe, ["tunnel", "--no-autoupdate", "--url", "http://localhost:" + PORT], { cwd: __dirname });
let found = false;

function watch(chunk) {
  const text = chunk.toString();
  if (found) return;
  const m = text.match(/https:\/\/[a-z0-9-]+\.trycloudflare\.com/);
  if (!m) return;
  found = true;
  const link = m[0];
  console.log("");
  console.log("  ================================================================");
  console.log("   Server is online.");
  console.log("   Friends play at:  " + GAME_URL);
  console.log("   (or right away:   " + link + " )");
  console.log("   Keep this window open while you play. Close it to stop.");
  console.log("  ================================================================");
  console.log("");
  publish("wss://" + link.slice("https://".length) + "/ws");
}

tunnel.stdout.on("data", watch);
tunnel.stderr.on("data", watch);
tunnel.on("error", e => console.log("  !! Could not start cloudflared.exe: " + e.message));
tunnel.on("exit", code => { console.log("  Tunnel stopped (" + code + ")."); process.exit(code || 0); });
console.log("  Starting the Cloudflare tunnel... (takes a few seconds)");
