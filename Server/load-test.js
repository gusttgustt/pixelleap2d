// load-test.js — stress test for our own server.js (raw WebSocket client, no packages).
//
//   node Server/load-test.js                 starts its OWN copy of server.js on port 9877 with a
//                                            throwaway data folder, tests it, then stops it
//   node Server/load-test.js ws://host:port/ws   tests a server that is already running (go gently)
//
// Never point it at the live server while people play. Through the tunnel Cloudflare may throttle
// first, so the numbers there say little about server.js itself.
"use strict";
const net = require("net"), crypto = require("crypto"), path = require("path"), os = require("os"), fs = require("fs");
const { spawn, execSync } = require("child_process");

const sleep = ms => new Promise(r => setTimeout(r, ms));
let target = process.argv[2], child = null;
const own = !target;
if (own) target = "ws://127.0.0.1:9877/ws";
const u = new URL(target), HOST = u.hostname, PORT = +u.port || 80;
let fakeIp = 1; // through 127.0.0.1 the server trusts cf-connecting-ip, so each "player" can look like its own address
const nextIp = () => "10.99." + ((fakeIp >> 8) & 255) + "." + (fakeIp++ & 255);

/// A raw connection. ip = the address to pretend to be (null = this PC's own).
function connect(ip, onStatus) {
  return new Promise(res => {
    const s = net.connect(PORT, HOST), key = crypto.randomBytes(16).toString("base64");
    let head = "", up = false, msgs = [], waiters = [];
    s.closedAt = 0;
    s.on("connect", () => s.write("GET /ws HTTP/1.1\r\nHost: " + HOST + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
      "Sec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n" + (ip ? "cf-connecting-ip: " + ip + "\r\n" : "") + "\r\n"));
    let buf = Buffer.alloc(0);
    s.on("data", d => {
      if (!up) {
        head += d.toString("latin1");
        const i = head.indexOf("\r\n\r\n");
        if (i < 0) return;
        const code = +head.split(" ")[1];
        up = code === 101;
        res({ s, code });
        if (!up) return;
        d = Buffer.from(head.slice(i + 4), "latin1");
      }
      buf = Buffer.concat([buf, d]);
      while (buf.length >= 2) {
        let len = buf[1] & 0x7f, at = 2;
        if (len === 126) { if (buf.length < 4) break; len = buf.readUInt16BE(2); at = 4; }
        else if (len === 127) { if (buf.length < 10) break; len = Number(buf.readBigUInt64BE(2)); at = 10; }
        if (buf.length < at + len) break;
        const text = buf.subarray(at, at + len).toString("utf8");
        buf = buf.subarray(at + len);
        msgs.push(text); waiters.splice(0).forEach(w => w());
      }
    });
    s.on("error", () => { if (!up) res({ s, code: "ERR" }); });
    s.on("close", () => { s.closedAt = Date.now(); if (!up) res({ s, code: "CLOSED" }); waiters.splice(0).forEach(w => w()); });
    s.sendText = (text, op = 1) => {
      if (s.destroyed) return;
      const p = Buffer.from(text, "utf8"), mask = crypto.randomBytes(4), n = p.length;
      const h = n < 126 ? Buffer.from([0x80 | op, 0x80 | n]) : n < 65536 ? Buffer.from([0x80 | op, 0xfe, n >> 8, n & 255]) : (() => { const b = Buffer.alloc(10); b[0] = 0x80 | op; b[1] = 0xff; b.writeBigUInt64BE(BigInt(n), 2); return b; })();
      for (let i = 0; i < n; i++) p[i] ^= mask[i & 3];
      s.write(Buffer.concat([h, mask, p]));
    };
    s.next = ms => new Promise(r => {
      if (msgs.length) return r(msgs.shift());
      const t = setTimeout(() => r(null), ms);
      waiters.push(() => { clearTimeout(t); r(msgs.length ? msgs.shift() : null); });
    });
  });
}

/// One ordinary player: connect, hello, time until welcome.
async function canary() {
  const t = Date.now(), { s, code } = await Promise.race([connect(nextIp()), sleep(3000).then(() => ({ code: "TIMEOUT" }))]);
  if (code !== 101) { s && s.destroy(); return "refused(" + code + ")"; }
  s.sendText(JSON.stringify({ t: "hello", name: "CANARY" }));
  const m = await s.next(3000);
  s.destroy();
  return m && m.includes("welcome") ? (Date.now() - t) + "ms" : "TIMEOUT(>3s)";
}

function serverMem() {
  if (!child) return "?";
  try {
    const out = execSync('powershell -NoProfile -Command "(Get-Process -Id ' + child.pid + ').WorkingSet64"').toString().trim();
    return ((+out) / 1e6 | 0) + "MB";
  } catch (e) { return "?"; }
}
const alive = () => !child || child.exitCode === null;
const results = [];
const verdict = (name, ok, detail) => { results.push({ name, ok, detail }); console.log((ok ? "  PASS " : "  FAIL ") + name + " — " + detail); };

(async () => {
  if (own) {
    const data = fs.mkdtempSync(path.join(os.tmpdir(), "island-loadtest-"));
    child = spawn(process.execPath, [path.join(__dirname, "server.js"), String(PORT)], { env: { ...process.env, DATA_DIR: data }, stdio: ["ignore", "pipe", "pipe"] });
    child.stdout.on("data", () => {}); child.stderr.on("data", d => process.stderr.write("[server] " + d));
    await sleep(800);
    console.log("own test server pid " + child.pid + " on port " + PORT + " (data in " + data + ")");
  }
  console.log("baseline: canary " + await canary() + ", server " + serverMem());

  // 1. one address opening many connections
  console.log("\n1. connection flood from ONE address");
  {
    const ip = nextIp(), codes = {}, open = [];
    for (let i = 0; i < 40; i++) { const r = await connect(ip); codes[r.code] = (codes[r.code] || 0) + 1; if (r.code === 101) open.push(r.s); else r.s.destroy(); await sleep(5); }
    verdict("limit per address", (codes[101] || 0) <= 12, JSON.stringify(codes) + " (101 = let in, 503 = full, 429 = too fast)");
    open.forEach(s => s.destroy());
  }

  // 2. many addresses, ramping up, with an ordinary player measured at each step
  console.log("\n2. ramp of connections from many addresses (each says hello)");
  {
    const open = []; let refused = 0, worst = 0;
    for (const step of [10, 50, 100, 200, 300, 350]) {
      while (open.length + refused < step) {
        const r = await connect(nextIp());
        if (r.code === 101) { r.s.sendText(JSON.stringify({ t: "hello", name: "bot" + open.length })); open.push(r.s); } else { refused++; r.s.destroy(); }
        await sleep(3);
      }
      await sleep(500);
      const c = await canary(); const ms = parseInt(c) || 0; if (ms > worst) worst = ms;
      console.log("   conn=" + String(open.length).padStart(4) + " refused=" + refused + " | server " + serverMem() + " | canary " + c);
      if (!alive()) break;
      if (/TIMEOUT|ERR/.test(c)) { console.log("   >> server getting slow — stopping the ramp"); break; }
    }
    await sleep(1000);
    const held = open.filter(s => !s.closedAt).length;
    verdict("guest cap", held <= 300 && held > 0, held + " still connected of " + open.length + " (" + refused + " refused at connect)");
    verdict("ordinary player latency while busy", worst < 300, "worst " + worst + "ms");
    open.forEach(s => s.destroy());
    await sleep(1500);
    console.log("   after closing all: server " + serverMem() + ", canary " + await canary());
  }

  // 2b. lock-out: fill the server with silent connections and fake guests from many addresses,
  //     then a real player (account, logs in after hello like the game) must still get in
  if (own) {
    console.log("\n2b. real player with an account while bots fill every place");
    const acct = { name: "tester" + (Date.now() % 100000), pass: "pass" + crypto.randomBytes(4).toString("hex") };
    {
      const { s } = await connect(nextIp());
      s.sendText(JSON.stringify({ t: "hello", name: "reg" })); await s.next(2000);
      s.sendText(JSON.stringify({ t: "register", name: acct.name, text: acct.pass }));
      const a = await s.next(5000); s.destroy();
      if (!a || !a.includes('"hits":1')) console.log("   (could not make a test account: " + a + ")");
    }
    const bots = [];
    const flood = async n => { for (let i = 0; i < n; i++) { const r = await connect(nextIp()); if (r.code === 101) { r.s.sendText(JSON.stringify({ t: "hello", name: "bot" })); bots.push(r.s); } else r.s.destroy(); await sleep(2); } };
    const silent = [];
    for (let i = 0; i < 120; i++) { const r = await connect(nextIp()); if (r.code === 101) silent.push(r.s); else r.s.destroy(); }
    await flood(400);
    await sleep(500);
    const realPlayer = async () => {
      const t = Date.now(), r = await connect(nextIp());
      if (r.code !== 101) { r.s.destroy(); return "refused(" + r.code + ")"; }
      r.s.sendText(JSON.stringify({ t: "hello", name: "REAL" }));
      const w = await r.s.next(3000);
      if (!w || !w.includes("welcome")) { r.s.destroy(); return "no welcome"; }
      r.s.sendText(JSON.stringify({ t: "login", name: acct.name, text: acct.pass }));
      let a; do a = await r.s.next(5000); while (a && !a.includes('"auth"'));
      await sleep(LOGIN_CHECK);
      const ok = a && a.includes('"hits":1') && !r.s.closedAt;
      r.s.destroy();
      return ok ? "in (" + (Date.now() - t - LOGIN_CHECK) + "ms)" : "turned away (" + (a || "closed") + ")";
    };
    const LOGIN_CHECK = 16000; // still connected after the guests' login window
    const stillOpen = () => bots.filter(s => !s.closedAt).length;
    console.log("   silent held " + silent.filter(s => !s.closedAt).length + "/120, bots (hello) held " + stillOpen() + "/400");
    const r1 = await realPlayer();
    console.log("   bots still in after 16 s: " + stillOpen() + " | server " + serverMem());
    verdict("account player gets in while full", r1.startsWith("in"), r1);
    const g = await canary();
    verdict("guests beyond the guest share are turned away", stillOpen() <= 240 + 2, stillOpen() + " bots held (guest share 240), new guest: " + g);
    bots.concat(silent).forEach(s => s.destroy());
    await sleep(1500);
  }

  // 3. slowloris: connect and never say hello
  console.log("\n3. idle connection that never says hello (10 s allowed)");
  {
    const { s, code } = await connect(nextIp()), t = Date.now();
    if (code === 101) { await new Promise(r => { s.on("close", r); setTimeout(r, 15000); }); }
    const held = (s.closedAt || Date.now()) - t;
    verdict("idle cut off", s.closedAt > 0 && held < 13000, "closed after " + (held / 1000).toFixed(1) + " s");
  }

  // 4. message flood
  console.log("\n4. message flood from one connection");
  {
    const { s } = await connect(nextIp());
    s.sendText(JSON.stringify({ t: "hello", name: "flood" })); await s.next(2000);
    const t = Date.now(); let sent = 0;
    while (!s.closedAt && Date.now() - t < 5000) { for (let i = 0; i < 200; i++) { s.sendText('{"t":"who"}'); sent++; } await sleep(1); }
    verdict("flooder cut off", s.closedAt > 0, (s.closedAt ? "cut off after " + sent + " messages, " + (s.closedAt - t) + "ms" : "still open after " + sent));
    s.destroy();
    console.log("   canary right after: " + await canary());
  }

  // 5. big payloads
  console.log("\n5. oversized messages");
  {
    let { s } = await connect(nextIp());
    s.sendText(JSON.stringify({ t: "hello", name: "x".repeat(20000) }));  // > 16 KB before hello
    await sleep(500);
    verdict("20 KB before hello", s.destroyed || s.closedAt > 0, s.closedAt ? "connection closed" : "still open");
    s.destroy();
    ({ s } = await connect(nextIp()));
    s.sendText(JSON.stringify({ t: "hello", name: "big" })); await s.next(2000);
    s.sendText(JSON.stringify({ t: "p", en: new Array(1500000).fill(1) }));  // ~3 MB as a guest (limit 2 MB)
    await sleep(800);
    verdict("3 MB as a guest", s.closedAt > 0, s.closedAt ? "connection closed" : "still open");
    s.destroy();
    // a header that claims an enormous frame, then nothing: must not make the server allocate it
    ({ s } = await connect(nextIp()));
    const h = Buffer.alloc(14); h[0] = 0x81; h[1] = 0xff; h.writeBigUInt64BE(BigInt(2 ** 40), 2); s.write(h);
    await sleep(500);
    verdict("frame header claiming 1 TB", s.closedAt > 0 && alive(), s.closedAt ? "closed, server alive" : "still open");
    s.destroy();
  }

  // 6. junk
  console.log("\n6. malformed messages");
  {
    const { s } = await connect(nextIp());
    s.sendText(JSON.stringify({ t: "hello", name: "junk" })); await s.next(2000);
    const junk = ["{{{not json", "null", "[]", "42", '{"t":5}', '{"t":"join","island":"x"}', '{"t":"join","island":1e308}',
      '{"t":"visit","id":{"a":1}}', '{"t":"save","text":[1,2]}', '{"t":"chat","text":{"x":1}}', "[".repeat(5000) + "]".repeat(5000),
      '{"t":"__proto__"}', '{"t":"node","__proto__":{"x":1}}', '{"t":"hit","dmg":"NaN","id":"x"}', '{"t":"knock","to":-1}'];
    for (const j of junk) s.sendText(j);
    s.sendText("\xff\xfe binary-ish", 2);
    await sleep(800);
    const c = await canary();
    verdict("server survives junk", alive() && /ms$/.test(c), "server " + (alive() ? "alive" : "DEAD") + ", canary " + c);
    s.destroy();
  }

  // 7. leak check: open and close a lot, memory should come back down
  if (own) {
    console.log("\n7. memory after many connect/close rounds");
    const before = serverMem();
    for (let round = 0; round < 5; round++) {
      const open = [];
      for (let i = 0; i < 200; i++) { const r = await connect(nextIp()); if (r.code === 101) { r.s.sendText(JSON.stringify({ t: "hello", name: "r" })); r.s.sendText(JSON.stringify({ t: "join", island: 1 + (i % 5) })); open.push(r.s); } else r.s.destroy(); }
      await sleep(300); open.forEach(s => s.destroy()); await sleep(300);
    }
    await sleep(2000);
    const after = serverMem();
    verdict("no runaway memory", parseInt(after) - parseInt(before) < 60, before + " -> " + after + " after 1000 connections");
  }

  console.log("\n" + results.filter(r => r.ok).length + "/" + results.length + " passed");
  if (child) child.kill();
  process.exit(0);
})();
