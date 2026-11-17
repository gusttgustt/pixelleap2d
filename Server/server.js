// Island Survival relay server: no packages needed, only Node.js (18+).
//
//   node Server/server.js            (port 8787)
//   node Server/server.js 9000       (another port)
//
// - ws://<host>:<port>/ws   WebSocket for the game. Messages are JSON text.
//   The server does not run the game. Players on the same island share a "room":
//   the server passes their messages to each other, picks one player per room as
//   the host (the host's game runs that island's creatures), and remembers what
//   happened to the island (harvested plants, built campfires...) so players who
//   arrive later see the same world.
//     client -> server:  {t:"hello", name}            -> {t:"welcome", id, worldSeed}
//                        {t:"join", island}           -> {t:"room", host, players, events}
//                        {t:"leave"}                  (paddling out to sea)
//                        {t:"node"|"spawn", ...}      world change: remembered + passed on
//                        {t:"home", text, room, lv, pets, island}  my private island (island = its continent), for visitors (kept, not passed on)
//                        {t:"homes"}                  -> {t:"homes", players}  who has a private island to visit (and on which continent, c)
//                        {t:"visit", id}              -> {t:"visit", id, name, text, room, lv, pets}  (text "" = gone)
//                        {t:"knock", to}              "I'm visiting your island" (reaches them anywhere)
//                        {t:"hnode", id, text, room}  a private island changed (to everyone on it + its owner)
//                        {t:"hgift", to, kind, hits}  "I harvested on your farm": the owner gets the same
//   Accounts (play the same character anywhere):
//                        {t:"register"|"login", name, text: password} or {t:"token", name, text: token}
//                                                     -> {t:"auth", hits:1, name, ev: token, text: save, room: keep}
//                                                        or {t:"auth", hits:0, why}
//                        {t:"save", text, room}       the logged-in player's save (and kept home island)
//                        {t:"logout", ev: token}
//   Accounts live in DATA_DIR (default Server/data): accounts.json + one file per player's save.
//   Rooms: adventure islands are 1, 2, 3...; a private island is -(owner*2), its house -(owner*2+1).
//                        {t:..., to}                  anything else: to one player (to > 0) or the room
// - http://<host>:<port>/  serves the web build from docs/.
"use strict";
const http = require("http");
const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const PORT = parseInt(process.argv[2] || process.env.PORT || "8787", 10);
const WEB_ROOT = path.resolve(__dirname, "..", "docs");
const MAX_FRAME = 4 * 1024 * 1024;   // game messages are tiny; a save (accounts) or a private island are the big ones
const MAX_SAVE = 3500000;             // characters of save text kept per player
const MAX_EVENTS = 4000;              // remembered world changes per island
const WORLD_SEED = crypto.randomInt(1, 2 ** 31 - 1); // every island layout comes from this

const DATA_DIR = path.resolve(process.env.DATA_DIR || path.join(__dirname, "data"));

// ---------- limits (against people trying to break the server) ----------
const MAX_CLIENTS = 300;              // connections in all
const MAX_PER_IP = 12;                // connections from one address (friends at one house share one)
const MSG_RATE = 80, MSG_BURST = 300;              // messages per second (a player sends ~20)
const BYTE_RATE = 256 * 1024, BYTE_BURST = 12 * 1024 * 1024; // bytes per second (a save or a home island is big, but rare)
const MAX_STRIKES = 400;              // messages dropped for going too fast, then cut off
const HELLO_WITHIN = 15000;           // ms to say hello after connecting
const SAVE_EVERY = 5000;              // ms between save writes per account (the game sends every 8 s)
const MAX_ACCOUNTS = 5000;
const REGISTER_PER_IP = 3, REGISTER_ALL = 30, REGISTER_WINDOW = 60 * 60 * 1000; // new accounts per hour
const FAILS_PER_IP = 10, FAILS_PER_ACCOUNT = 5, FAIL_LOCK = 15 * 60 * 1000;      // wrong passwords, then a 15 min wait

const clients = new Map();            // id -> socket
const rooms = new Map();              // island -> { order: [ids], events: Map(nodeId -> event) }
let nextId = 1;

// ---------- static files (web build) ----------

const TYPES = {
  ".html": "text/html; charset=utf-8", ".js": "application/javascript", ".wasm": "application/wasm",
  ".data": "application/octet-stream", ".json": "application/json", ".css": "text/css",
  ".png": "image/png", ".ico": "image/x-icon", ".svg": "image/svg+xml", ".unityweb": "application/octet-stream",
};

// Where people play: the game files are big (tens of MB), so they come
// from GitHub Pages (free bandwidth) and this PC only carries the small game messages.
const GAME_URL = process.env.GAME_URL || "https://gusttgustt.github.io/pixelleap2d/";

const server = http.createServer((req, res) => {
  let url;
  try { url = decodeURIComponent((req.url || "/").split("?")[0]); } catch (e) { res.writeHead(400); res.end(); return; }
  if (url === "/status") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ online: clients.size, islands: [...rooms].map(([i, r]) => ({ island: i, players: r.order.length })) }));
    return;
  }
  const host = (req.headers.host || "").split(":")[0];
  const local = host === "localhost" || host === "127.0.0.1" || /^192\.168\./.test(host) || /^10\./.test(host);
  if (!local) {
    // Play on GitHub Pages, connected back to this server through the Cloudflare tunnel to this PC.
    const secure = (req.headers["x-forwarded-proto"] || "").includes("https") || /trycloudflare\.com$/.test(host);
    const ws = (secure ? "wss://" : "ws://") + req.headers.host + "/ws";
    res.writeHead(302, { Location: GAME_URL + "?server=" + encodeURIComponent(ws) });
    res.end();
    return;
  }
  if (url === "/") url = "/index.html";
  const file = path.resolve(WEB_ROOT, "." + url);
  if (!file.startsWith(WEB_ROOT + path.sep)) { res.writeHead(403); res.end(); return; }
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404, { "Content-Type": "text/plain" }); res.end("not found"); return; }
    res.writeHead(200, { "Content-Type": TYPES[path.extname(file).toLowerCase()] || "application/octet-stream", "Cache-Control": "no-cache" });
    res.end(data);
  });
});

// ---------- WebSocket (RFC 6455, text frames only) ----------

server.on("upgrade", (req, socket) => {
  if ((req.url || "").split("?")[0] !== "/ws" || (req.headers.upgrade || "").toLowerCase() !== "websocket") {
    socket.destroy();
    return;
  }
  const ip = ipOf(req);
  if (sockets.size >= MAX_CLIENTS || (perIp.get(ip) || 0) >= MAX_PER_IP) {
    socket.end("HTTP/1.1 503 Service Unavailable\r\nConnection: close\r\n\r\n");
    return;
  }
  sockets.add(socket);
  perIp.set(ip, (perIp.get(ip) || 0) + 1);
  socket.ip = ip;
  socket.once("close", () => {
    sockets.delete(socket);
    const n = (perIp.get(ip) || 1) - 1;
    if (n > 0) perIp.set(ip, n); else perIp.delete(ip);
  });
  socket.helloTimer = setTimeout(() => { if (!socket.pid) socket.destroy(); }, HELLO_WITHIN);
  const key = req.headers["sec-websocket-key"];
  if (!key) { socket.destroy(); return; }
  const accept = crypto.createHash("sha1").update(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").digest("base64");
  socket.write("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
  socket.setNoDelay(true);

  let buf = Buffer.alloc(0);
  let parts = [], partsLen = 0;
  socket.pid = 0;
  socket.island = 0;
  socket.msgs = MSG_BURST;
  socket.bytes = BYTE_BURST;
  socket.lastRefill = Date.now();
  socket.strikes = 0;

  socket.on("data", chunk => {
    buf = Buffer.concat([buf, chunk]);
    for (;;) {
      if (buf.length < 2) return;
      const fin = (buf[0] & 0x80) !== 0, op = buf[0] & 0x0f, masked = (buf[1] & 0x80) !== 0;
      let len = buf[1] & 0x7f, at = 2;
      if (len === 126) { if (buf.length < 4) return; len = buf.readUInt16BE(2); at = 4; }
      else if (len === 127) { if (buf.length < 10) return; len = Number(buf.readBigUInt64BE(2)); at = 10; }
      if (len > MAX_FRAME) { socket.destroy(); return; }
      const maskAt = at;
      if (masked) at += 4;
      if (buf.length < at + len) return;
      const payload = Buffer.from(buf.subarray(at, at + len));
      if (masked) for (let i = 0; i < len; i++) payload[i] ^= buf[maskAt + (i & 3)];
      buf = buf.subarray(at + len);

      if (op === 0x8) { sendFrame(socket, 0x8, Buffer.alloc(0)); socket.end(); return; }
      if (op === 0x9) { sendFrame(socket, 0xA, payload); continue; }
      if (op === 0xA) continue;
      if (op === 0x1 || op === 0x2 || op === 0x0) {
        parts.push(payload);
        partsLen += payload.length;
        if (partsLen > MAX_FRAME || parts.length > 1000) { socket.destroy(); return; } // a message in endless pieces
        if (!fin) continue;
        const text = Buffer.concat(parts).toString("utf8");
        parts = [];
        partsLen = 0;
        if (!allowed(socket, text.length)) { if (socket.destroyed) return; continue; }
        onMessage(socket, text);
      }
    }
  });
  socket.on("close", () => drop(socket));
  socket.on("error", () => drop(socket));
});

function sendFrame(socket, op, payload) {
  if (socket.destroyed) return;
  const len = payload.length;
  let head;
  if (len < 126) head = Buffer.from([0x80 | op, len]);
  else if (len < 65536) { head = Buffer.alloc(4); head[0] = 0x80 | op; head[1] = 126; head.writeUInt16BE(len, 2); }
  else { head = Buffer.alloc(10); head[0] = 0x80 | op; head[1] = 127; head.writeBigUInt64BE(BigInt(len), 2); }
  socket.write(Buffer.concat([head, payload]));
}

function send(socket, obj) { if (socket) sendFrame(socket, 0x1, Buffer.from(JSON.stringify(obj), "utf8")); }

// ---------- who is connecting, and how fast ----------

const sockets = new Set();            // every open game connection, hello or not
const perIp = new Map();              // address -> open connections

/// The player's address. Through the Cloudflare tunnel every connection comes from this PC
/// (127.0.0.1) and Cloudflare tells the real one in a header; only trust that header then.
function ipOf(req) {
  const remote = (req.socket.remoteAddress || "").replace(/^::ffff:/, "");
  const loop = remote === "127.0.0.1" || remote === "::1";
  const cf = String(req.headers["cf-connecting-ip"] || "").trim();
  return loop && cf ? cf.slice(0, 64) : remote;
}

/// Token buckets per connection: too many messages or bytes are dropped; keep at it and you're cut off.
function allowed(socket, size) {
  const now = Date.now(), dt = (now - socket.lastRefill) / 1000;
  socket.lastRefill = now;
  socket.msgs = Math.min(MSG_BURST, socket.msgs + dt * MSG_RATE);
  socket.bytes = Math.min(BYTE_BURST, socket.bytes + dt * BYTE_RATE);
  if (socket.msgs >= 1 && socket.bytes >= size) { socket.msgs -= 1; socket.bytes -= size; return true; }
  if (++socket.strikes > MAX_STRIKES) {
    log("! cut off " + (socket.name || "?") + " (" + socket.ip + "): too many messages");
    socket.destroy();
  }
  return false;
}

// ---------- rooms (one per island number) ----------

function room(island) {
  let r = rooms.get(island);
  if (!r) { r = { order: [], events: new Map() }; rooms.set(island, r); }
  return r;
}

function toRoom(island, obj, exceptId) {
  const r = rooms.get(island);
  if (!r) return;
  for (const id of r.order) if (id !== exceptId) send(clients.get(id), obj);
}

function leaveRoom(socket) {
  const island = socket.island;
  if (!island) return;
  socket.island = 0;
  const r = rooms.get(island);
  if (!r) return;
  const wasHost = r.order[0] === socket.pid;
  r.order = r.order.filter(id => id !== socket.pid);
  toRoom(island, { t: "left", from: socket.pid });
  if (wasHost && r.order.length > 0) toRoom(island, { t: "host", host: r.order[0] });
}

/// World changes still worth telling a newcomer, with how long ago they happened.
function liveEvents(r) {
  const now = Date.now(), out = [];
  for (const [id, e] of r.events) {
    const age = (now - e.at) / 1000;
    if ((e.ev === "deplete" || e.ev === "stump") && age > e.dur) { r.events.delete(id); continue; } // grown back
    out.push({ id: e.id, ev: e.ev, kind: e.kind, dur: e.dur, hits: e.hits, x: e.x, y: e.y, age });
  }
  return out;
}

function remember(r, m) {
  const e = { id: m.id, ev: m.t === "spawn" ? "spawn" : m.ev, kind: m.kind | 0, dur: +m.dur || 0, hits: m.hits | 0, x: +m.x || 0, y: +m.y || 0, at: Date.now() };
  const old = r.events.get(m.id);
  if (e.ev === "remove" && old && old.ev === "spawn") { r.events.delete(m.id); return; } // built and gone again
  if (e.ev === "hits" && old && old.ev !== "hits") return;                               // stump etc. wins
  if (old && old.ev === "spawn" && e.ev !== "remove") { old.hits = e.hits || old.hits; return; } // keep the spawn
  if (r.events.size >= MAX_EVENTS && !old) return;
  r.events.set(m.id, e);
}

// ---------- messages ----------

function onMessage(socket, text) {
  let m;
  try { m = JSON.parse(text); } catch (e) { return; }
  if (!m || typeof m.t !== "string") return;

  if (m.t === "hello") {
    if (socket.pid) return;
    clearTimeout(socket.helloTimer);
    socket.pid = nextId++;
    socket.name = String(m.name || "Islander").slice(0, 16);
    clients.set(socket.pid, socket);
    send(socket, { t: "welcome", id: socket.pid, worldSeed: WORLD_SEED });
    log("+ " + socket.name + " #" + socket.pid + " (" + clients.size + " online)");
    return;
  }
  if (!socket.pid) return;      // must say hello first
  m.from = socket.pid;          // no one can speak for someone else

  switch (m.t) {
    case "join": {
      leaveRoom(socket);
      const island = (m.island | 0) || 1; // below 0: someone's private island (not remembered)
      const r = room(island);
      socket.island = island;
      r.order.push(socket.pid);
      const players = r.order.filter(id => id !== socket.pid).map(id => ({ id, name: clients.get(id).name }));
      send(socket, { t: "room", island, host: r.order[0], players, events: liveEvents(r) });
      toRoom(island, { t: "joined", from: socket.pid, name: socket.name }, socket.pid);
      return;
    }
    case "leave":
      leaveRoom(socket);
      return;
    case "register":
    case "login":
    case "token":
      onAccount(socket, m);
      return;
    case "save": {
      if (!socket.acct || !accounts[socket.acct]) return;
      const text = String(m.text || ""), keep = String(m.room || "");
      if (text.length > MAX_SAVE || keep.length > MAX_SAVE) return;
      saveSoon(socket.acct, JSON.stringify({ save: text, keep, at: Date.now() }));
      return;
    }
    case "logout": {
      const acc = socket.acct && accounts[socket.acct];
      if (acc && m.ev) { acc.tokens = (acc.tokens || []).filter(t => t !== hashToken(m.ev)); saveAccountsSoon(); }
      socket.acct = null;
      return;
    }
    case "home":
      socket.home = { text: String(m.text || "").slice(0, 400000), room: String(m.room || "").slice(0, 100000), lv: m.lv | 0, pets: String(m.pets || "").slice(0, 2000), c: Math.max(0, Math.min(3, m.island | 0)) };
      return;
    case "homes": {
      const players = [];
      for (const [id, s] of clients) if (id !== socket.pid && s.home && s.home.text) players.push({ id, name: s.name, c: s.home.c | 0 });
      send(socket, { t: "homes", players: players.slice(0, 40) });
      return;
    }
    case "visit": {
      const s = clients.get(m.id | 0);
      if (!s || !s.home || !s.home.text) { send(socket, { t: "visit", id: m.id | 0, text: "" }); return; }
      send(socket, { t: "visit", id: s.pid, name: s.name, text: s.home.text, room: s.home.room, lv: s.home.lv, pets: s.home.pets });
      log("  " + socket.name + " visits " + s.name + "'s island");
      return;
    }
    case "knock": {
      const s = clients.get(m.to | 0);
      if (s) send(s, { t: "knock", from: socket.pid, name: socket.name });
      return;
    }
    case "hnode": {
      // A private island changed (m.id = its owner): everyone on it sees it, and the owner gets it
      // wherever they are, so a friend's help on the farm is kept even while the owner is away.
      // Only the owner, or someone standing on that private island, may change it.
      const owner = m.id | 0;
      if (owner <= 0 || (owner !== socket.pid && !onHomeOf(socket, owner))) return;
      if (m.to > 0) { const s = clients.get(m.to); if (s && s.island === socket.island) send(s, m); return; }
      if (socket.island) toRoom(socket.island, m, socket.pid);
      const o = clients.get(owner);
      if (o && o !== socket && o.island !== socket.island) send(o, m);
      return;
    }
    case "hgift": {
      // a friend harvested on your farm: you get the same (only from someone on your farm, a handful at a time)
      const s = clients.get(m.to | 0);
      if (!s || s === socket || !onHomeOf(socket, s.pid)) return;
      m.hits = Math.max(0, Math.min(m.hits | 0, 20));
      if (m.hits > 0) { m.name = socket.name; send(s, m); }
      return;
    }
    case "node":
    case "spawn":
      if (!socket.island) return;
      if (socket.island > 0) remember(room(socket.island), m);
      toRoom(socket.island, m, socket.pid);
      return;
    default:
      if (!socket.island) return;
      if (m.to > 0) { const s = clients.get(m.to); if (s && s.island === socket.island) send(s, m); }
      else toRoom(socket.island, m, socket.pid);
  }
}

/// Is this player on that owner's private island (outside or in the house)?
function onHomeOf(socket, owner) {
  return socket.island === -(owner * 2) || socket.island === -(owner * 2 + 1);
}

// ---------- accounts ----------

let accounts = {};                    // key (lower-case name) -> { name, salt, hash, tokens: [sha256], at }
try { accounts = JSON.parse(fs.readFileSync(path.join(DATA_DIR, "accounts.json"), "utf8")) || {}; } catch (e) { accounts = {}; }
let accountsTimer = null;

function writeFileSafe(file, text) {
  fs.mkdirSync(DATA_DIR, { recursive: true });
  const tmp = file + ".tmp";
  fs.writeFileSync(tmp, text);
  fs.renameSync(tmp, file);
}

function saveAccountsNow() {
  if (accountsTimer) { clearTimeout(accountsTimer); accountsTimer = null; }
  try { writeFileSafe(path.join(DATA_DIR, "accounts.json"), JSON.stringify(accounts)); } catch (e) { log("! accounts not saved: " + e.message); }
}

function saveAccountsSoon() {
  if (accountsTimer) return;
  accountsTimer = setTimeout(saveAccountsNow, 1000);
}

const keyOf = name => String(name || "").trim().toLowerCase();
const saveFile = key => path.join(DATA_DIR, "u_" + Buffer.from(key, "utf8").toString("hex") + ".json");
const hashToken = t => crypto.createHash("sha256").update(String(t)).digest("hex");

function readSave(key) {
  try { return JSON.parse(fs.readFileSync(saveFile(key), "utf8")); } catch (e) { return { save: "", keep: "" }; }
}

function authOk(socket, acc) {
  const token = crypto.randomBytes(24).toString("hex");
  acc.tokens = (acc.tokens || []).concat(hashToken(token)).slice(-8); // up to 8 devices stay logged in
  acc.at = Date.now();
  saveAccountsSoon();
  socket.acct = keyOf(acc.name);
  const data = readSave(socket.acct);
  send(socket, { t: "auth", hits: 1, name: acc.name, ev: token, text: data.save || "", room: data.keep || "" });
  log("  " + socket.name + " logged in as " + acc.name);
}

// A save is written at most every SAVE_EVERY ms per account (the newest one wins).
const saveTimers = new Map();          // account -> { text, timer, last }
function saveSoon(key, text) {
  let s = saveTimers.get(key);
  if (!s) { s = { text: null, timer: null, last: 0 }; saveTimers.set(key, s); }
  s.text = text;
  if (s.timer) return;
  const write = () => {
    s.timer = null;
    s.last = Date.now();
    try { writeFileSafe(saveFile(key), s.text); } catch (e) { log("! save failed: " + e.message); }
    s.text = null;
  };
  s.write = write;
  const wait = s.last + SAVE_EVERY - Date.now();
  if (wait <= 0) write(); else s.timer = setTimeout(write, wait);
}

/// Closing the server window (or Ctrl+C): write everything still waiting, so nothing is lost.
function flushAll() {
  saveAccountsNow();
  for (const s of saveTimers.values()) if (s.timer) { clearTimeout(s.timer); s.write(); }
}
for (const sig of ["SIGINT", "SIGTERM", "SIGHUP", "SIGBREAK"]) {
  try { process.on(sig, () => { log("Server stopping: saving accounts..."); flushAll(); process.exit(0); }); } catch (e) {}
}

// Wrong passwords and new accounts, counted per address and per account.
const fails = new Map();               // "ip:<addr>" or "acct:<key>" -> { n, until }
const registers = new Map();           // address -> [times]
let registersAll = [];

function locked(id) {
  const f = fails.get(id);
  if (!f) return false;
  if (Date.now() > f.until) { fails.delete(id); return false; }
  return f.n >= (id.startsWith("ip:") ? FAILS_PER_IP : FAILS_PER_ACCOUNT);
}
function failed(id) {
  const f = fails.get(id);
  if (!f || Date.now() > f.until) fails.set(id, { n: 1, until: Date.now() + FAIL_LOCK });
  else { f.n++; f.until = Date.now() + FAIL_LOCK; }
}
setInterval(() => { // forget old entries
  const now = Date.now();
  for (const [id, f] of fails) if (now > f.until) fails.delete(id);
  for (const [ip, list] of registers) { const l = list.filter(t => now - t < REGISTER_WINDOW); if (l.length) registers.set(ip, l); else registers.delete(ip); }
  registersAll = registersAll.filter(t => now - t < REGISTER_WINDOW);
}, 60 * 1000).unref();

function hashPassAsync(pass, salt) {
  return new Promise((ok, no) => crypto.scrypt(String(pass), salt, 32, (e, k) => e ? no(e) : ok(k.toString("hex"))));
}

const TOO_MANY = "ใส่รหัสผ่านผิดหลายครั้งเกินไป รอ 15 นาทีแล้วลองใหม่";

async function onAccount(socket, m) {
  if (socket.authBusy) return;          // one at a time per connection
  socket.tries = (socket.tries || 0) + 1;
  if (socket.tries > 20) { send(socket, { t: "auth", hits: 0, why: "ลองหลายครั้งเกินไป ปิดแล้วเปิดเกมใหม่" }); return; }
  const name = String(m.name || "").trim(), key = keyOf(name), pass = String(m.text || "").slice(0, 200);
  const ipId = "ip:" + socket.ip, acctId = "acct:" + key;
  socket.authBusy = true;
  try {
    if (m.t === "register") {
      if (!/^[\p{L}\p{M}\p{N}_.\-]{3,20}$/u.test(name)) { send(socket, { t: "auth", hits: 0, why: "ชื่อบัญชีต้องยาว 3-20 ตัว (ตัวอักษร ตัวเลข _ . -) ห้ามเว้นวรรค" }); return; }
      if (pass.length < 4) { send(socket, { t: "auth", hits: 0, why: "รหัสผ่านต้องยาวอย่างน้อย 4 ตัว" }); return; }
      if (accounts[key]) { send(socket, { t: "auth", hits: 0, why: "ชื่อบัญชีนี้มีคนใช้แล้ว ลองชื่ออื่น หรือกดเข้าสู่ระบบ" }); return; }
      const now = Date.now(), mine = (registers.get(socket.ip) || []).filter(t => now - t < REGISTER_WINDOW);
      registersAll = registersAll.filter(t => now - t < REGISTER_WINDOW);
      if (mine.length >= REGISTER_PER_IP || registersAll.length >= REGISTER_ALL || Object.keys(accounts).length >= MAX_ACCOUNTS) {
        log("! too many new accounts (" + socket.ip + ")");
        send(socket, { t: "auth", hits: 0, why: "สมัครบัญชีใหม่มากเกินไปแล้ว ลองใหม่ภายหลัง" });
        return;
      }
      const salt = crypto.randomBytes(16).toString("hex");
      const hash = await hashPassAsync(pass, salt);
      if (accounts[key]) { send(socket, { t: "auth", hits: 0, why: "ชื่อบัญชีนี้มีคนใช้แล้ว ลองชื่ออื่น หรือกดเข้าสู่ระบบ" }); return; }
      if (socket.destroyed) return;
      mine.push(now); registers.set(socket.ip, mine); registersAll.push(now);
      accounts[key] = { name, salt, hash, tokens: [], at: Date.now() };
      log("+ account " + name);
      authOk(socket, accounts[key]);
      saveAccountsNow(); // a new account is on disk straight away
      return;
    }
    const acc = accounts[key];
    if (m.t === "token") {
      // A device's saved login. Tokens are random (48 hex digits), so they can't be guessed:
      // an old or unknown one (e.g. from before the server moved) is not a wrong password
      // and must not lock anyone out, and a locked address can still use a valid one.
      if (!acc || !(acc.tokens || []).includes(hashToken(pass))) { send(socket, { t: "auth", hits: 0, ev: "token", why: "ต้องเข้าสู่ระบบใหม่อีกครั้ง" }); return; }
      acc.tokens = acc.tokens.filter(t => t !== hashToken(pass)); // a fresh token each time
      authOk(socket, acc);
      return;
    }
    if (locked(ipId) || locked(acctId)) { send(socket, { t: "auth", hits: 0, why: TOO_MANY }); return; }
    if (!acc) { send(socket, { t: "auth", hits: 0, why: "ไม่พบบัญชีนี้ (สมัครใหม่ได้เลย)" }); return; } // not a wrong password: don't count it
    const h = await hashPassAsync(pass, acc.salt);
    if (socket.destroyed) return;
    if (!crypto.timingSafeEqual(Buffer.from(h, "hex"), Buffer.from(acc.hash, "hex"))) {
      failed(ipId); failed(acctId);
      if (locked(acctId)) log("! account " + acc.name + " locked for 15 min (wrong passwords, last from " + socket.ip + ")");
      send(socket, { t: "auth", hits: 0, why: locked(ipId) || locked(acctId) ? TOO_MANY : "รหัสผ่านไม่ถูกต้อง" });
      return;
    }
    fails.delete(acctId);
    authOk(socket, acc);
  } catch (e) {
    log("! account error: " + e.message);
  } finally {
    socket.authBusy = false;
  }
}

function drop(socket) {
  const id = socket.pid;
  if (!id || clients.get(id) !== socket) return;
  leaveRoom(socket);
  clients.delete(id);
  socket.pid = 0;
  log("- " + socket.name + " #" + id + " (" + clients.size + " online)");
}

function log(s) { console.log(new Date().toLocaleTimeString() + "  " + s); }

server.listen(PORT, "0.0.0.0", () => {
  log("Island server on port " + PORT + " (world seed " + WORLD_SEED + ")");
  log("  game connects to  ws://127.0.0.1:" + PORT + "/ws");
  log("  web build at      http://localhost:" + PORT + "/  (from " + WEB_ROOT + ")");
  log("  accounts in       " + DATA_DIR + " (" + Object.keys(accounts).length + " players)");
});
