// play-test.js — N bots that play like real games (not an attack test; see load-test.js for that).
//
//   node Server/play-test.js                       10 bots, 60 s, on its OWN copy of server.js (port 9878)
//   node Server/play-test.js 10 60 same|spread     bots, seconds, all on island 1 (same) or spread over 1-3
//   node Server/play-test.js 10 60 same ws://host:port/ws   against a running server (not the live one while people play)
//
// Each bot: hello, join, then like IslandGame.Net.cs NetTick: "p" every 100 ms (walking about),
// "who" every 8 s, a harvest ("node") now and then, a chat line now and then. The first in each
// room is its host and also sends "en" (30 creatures) every 120 ms and "clock" every 3 s.
// Measured: chat delivery time (sender -> others), "who" answer time, updates received per second,
// bytes in/out, and the server's CPU and memory.
"use strict";
const net = require("net"), crypto = require("crypto"), path = require("path"), os = require("os"), fs = require("fs");
const { spawn, execSync } = require("child_process");

const N = +process.argv[2] || 10, SECS = +process.argv[3] || 60, MODE = process.argv[4] || "same";
let target = process.argv[5], child = null;
const own = !target;
if (own) target = "ws://127.0.0.1:9878/ws";
const u = new URL(target), HOST = u.hostname, PORT = +u.port || 80;
const sleep = ms => new Promise(r => setTimeout(r, ms));

function connect(ip) {
  return new Promise(res => {
    const s = net.connect(PORT, HOST), key = crypto.randomBytes(16).toString("base64");
    let head = "", up = false, buf = Buffer.alloc(0);
    s.bytesIn = 0; s.bytesOut = 0; s.onText = () => {};
    s.setNoDelay(true);
    s.on("connect", () => s.write("GET /ws HTTP/1.1\r\nHost: " + HOST + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
      "Sec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\ncf-connecting-ip: " + ip + "\r\n\r\n"));
    s.on("data", d => {
      s.bytesIn += d.length;
      if (!up) {
        head += d.toString("latin1");
        const i = head.indexOf("\r\n\r\n");
        if (i < 0) return;
        up = +head.split(" ")[1] === 101;
        res({ s, ok: up });
        if (!up) return;
        d = Buffer.from(head.slice(i + 4), "latin1");
      }
      buf = Buffer.concat([buf, d]);
      while (buf.length >= 2) {
        let len = buf[1] & 0x7f, at = 2;
        if (len === 126) { if (buf.length < 4) break; len = buf.readUInt16BE(2); at = 4; }
        else if (len === 127) { if (buf.length < 10) break; len = Number(buf.readBigUInt64BE(2)); at = 10; }
        if (buf.length < at + len) break;
        const op = buf[0] & 15, text = buf.subarray(at, at + len).toString("utf8");
        buf = buf.subarray(at + len);
        if (op === 1) s.onText(text);
      }
    });
    s.on("error", () => { if (!up) res({ s, ok: false }); });
    s.on("close", () => { s.closed = true; });
    s.sendJson = obj => {
      if (s.destroyed) return;
      const p = Buffer.from(JSON.stringify(obj), "utf8"), mask = crypto.randomBytes(4), n = p.length;
      const h = n < 126 ? Buffer.from([0x81, 0x80 | n]) : n < 65536 ? Buffer.from([0x81, 0xfe, n >> 8, n & 255]) : (() => { const b = Buffer.alloc(10); b[0] = 0x81; b[1] = 0xff; b.writeBigUInt64BE(BigInt(n), 2); return b; })();
      for (let i = 0; i < n; i++) p[i] ^= mask[i & 3];
      const out = Buffer.concat([h, mask, p]); s.bytesOut += out.length; s.write(out);
    };
  });
}

function proc() {
  if (!child) return null;
  try {
    const out = execSync('powershell -NoProfile -Command "$p=Get-Process -Id ' + child.pid + '; \\"$($p.CPU) $($p.WorkingSet64)\\""').toString().trim().split(" ");
    return { cpu: +out[0], mem: +out[1] };
  } catch (e) { return null; }
}

const pct = (a, q) => { if (!a.length) return 0; const b = [...a].sort((x, y) => x - y); return b[Math.min(b.length - 1, Math.floor(q * b.length))]; };

(async () => {
  if (own) {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), "island-playtest-"));
    child = spawn(process.execPath, [path.join(__dirname, "server.js"), String(PORT)], { env: { ...process.env, DATA_DIR: data }, stdio: ["ignore", "pipe", "pipe"] });
    child.stdout.on("data", d => { const t = String(d); if (/^!|\n!/.test(t)) process.stdout.write("[server] " + t); });
    child.stderr.on("data", d => process.stderr.write("[server] " + d));
    await sleep(800);
  }
  console.log(N + " bots, " + SECS + " s, " + (MODE === "same" ? "all on island 1" : "spread over islands 1-3") + ", " + target);

  const bots = [], chatLat = [], whoLat = [], kinds = {};
  let errors = 0;
  for (let i = 0; i < N; i++) {
    const { s, ok } = await connect("10.77.0." + (i + 1));
    if (!ok) { console.log("bot " + i + " refused"); errors++; continue; }
    const b = { s, i, island: MODE === "same" ? 1 : 1 + (i % 3), x: 40 + i, y: 40, dir: Math.random() * 6.28, host: false, got: 0, whoAt: 0, id: 0 };
    s.onText = text => {
      let m; try { m = JSON.parse(text); } catch (e) { return; }
      if (!b.counting) return onEarly(b, m);
      b.got++; kinds[m.t] = (kinds[m.t] || 0) + 1;
      if (m.t === "chat" && /^T\d+$/.test(m.text)) chatLat.push(Date.now() - +m.text.slice(1));
      if (m.t === "who" && b.whoAt) { whoLat.push(Date.now() - b.whoAt); b.whoAt = 0; }
      if (m.t === "room") b.host = m.host === b.id;
    };
    bots.push(b);
    s.sendJson({ t: "hello", name: "Bot" + (i + 1) });
    await sleep(150); // players don't all arrive in the same millisecond
  }
  function onEarly(b, m) {
    if (m.t === "welcome") { b.id = m.id; b.s.sendJson({ t: "join", island: b.island }); }
    if (m.t === "room") { b.host = m.host === b.id; b.counting = true; }
  }
  await sleep(1000);
  const inRoom = bots.filter(b => b.counting).length;
  console.log("joined: " + inRoom + "/" + N + ", hosts: " + bots.filter(b => b.host).map(b => "Bot" + (b.i + 1) + "@island" + b.island).join(", "));

  const p0 = proc(), t0 = Date.now();
  bots.forEach(b => { b.got = 0; b.s.bytesIn = 0; b.s.bytesOut = 0; });
  const timers = [];
  for (const b of bots) {
    // walk: a tile or so a second, turning now and then, within the island
    timers.push(setInterval(() => {
      if (Math.random() < 0.05) b.dir += (Math.random() - 0.5) * 3;
      b.x += Math.cos(b.dir) * 0.35; b.y += Math.sin(b.dir) * 0.35;
      if (b.x < 10 || b.x > 90) b.dir = Math.PI - b.dir;
      if (b.y < 10 || b.y > 90) b.dir = -b.dir;
      b.s.sendJson({ t: "p", x: b.x, y: b.y, d: 2, f: (Date.now() / 150 | 0) % 4, fl: 0, hat: 3, outfit: 2, lv: 5, name: "Bot" + (b.i + 1), look: 12345, pet: 0, dn: 0, aw: 0 });
    }, 100));
    timers.push(setInterval(() => { b.whoAt = Date.now(); b.s.sendJson({ t: "who" }); }, 8000 + b.i * 37));
    timers.push(setInterval(() => { if (Math.random() < 0.5) b.s.sendJson({ t: "node", id: 1000 + ((Math.random() * 400) | 0), ev: "deplete", dur: 60, hits: 1 }); }, 2000));
    timers.push(setInterval(() => b.s.sendJson({ t: "chat", text: "T" + Date.now() }), 5000 + b.i * 53));
    if (b.host) {
      const en = Array.from({ length: 30 }, (_, k) => ({ id: k + 1, k: k % 6, x: 20 + k * 2, y: 30, hp: 20, mhp: 20, fl: 0 }));
      timers.push(setInterval(() => { en.forEach(e => { e.x += (Math.random() - 0.5) * 0.4; e.y += (Math.random() - 0.5) * 0.4; }); b.s.sendJson({ t: "en", en }); }, 120));
      timers.push(setInterval(() => b.s.sendJson({ t: "clock", clock: (Date.now() / 1000) % 600 }), 3000));
    }
  }

  for (let sec = 10; sec <= SECS; sec += 10) {
    await sleep(10000);
    const dt = (Date.now() - t0) / 1000, p = proc();
    const perBot = bots.reduce((a, b) => a + b.got, 0) / bots.length / dt;
    console.log(String(sec).padStart(4) + " s | each bot gets " + perBot.toFixed(0) + " msg/s" +
      " | chat p50 " + pct(chatLat, 0.5) + "ms p95 " + pct(chatLat, 0.95) + "ms max " + Math.max(0, ...chatLat) + "ms" +
      (p && p0 ? " | server CPU " + ((p.cpu - p0.cpu) / dt * 100).toFixed(1) + "% mem " + (p.mem / 1e6 | 0) + "MB" : "") +
      " | dropped " + bots.filter(b => b.s.closed).length);
  }
  timers.forEach(clearInterval);
  const dt = (Date.now() - t0) / 1000, p1 = proc();
  const inB = bots.reduce((a, b) => a + b.s.bytesIn, 0), outB = bots.reduce((a, b) => a + b.s.bytesOut, 0);
  console.log("\n=== summary (" + dt.toFixed(0) + " s) ===");
  console.log("connections dropped: " + bots.filter(b => b.s.closed).length + "/" + bots.length + (errors ? ", refused " + errors : ""));
  console.log("chat delivery: " + chatLat.length + " samples, p50 " + pct(chatLat, 0.5) + "ms, p95 " + pct(chatLat, 0.95) + "ms, max " + Math.max(0, ...chatLat) + "ms");
  console.log("who answer:    " + whoLat.length + " samples, p50 " + pct(whoLat, 0.5) + "ms, max " + Math.max(0, ...whoLat) + "ms");
  console.log("received by kind (all bots): " + Object.entries(kinds).sort((a, b) => b[1] - a[1]).map(([k, v]) => k + "=" + v).join(" "));
  console.log("server upload (to players): " + (inB / dt / 1024).toFixed(0) + " KB/s total, " + (inB / dt / 1024 / bots.length).toFixed(1) + " KB/s per player");
  console.log("server download (from players): " + (outB / dt / 1024).toFixed(0) + " KB/s total");
  if (p0 && p1) console.log("server CPU avg " + ((p1.cpu - p0.cpu) / dt * 100).toFixed(1) + "% of one core, memory " + (p1.mem / 1e6 | 0) + "MB");
  bots.forEach(b => b.s.destroy());
  if (child) child.kill();
  process.exit(0);
})();
