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
//                        {t:"save", text, room, ver}  the logged-in player's save (and kept home island). ver = the
//                                                     version it builds on: a save from a device that missed newer ones
//                                                     (another device played meanwhile) is refused -> {t:"saveold", ver}
//                                                     otherwise -> {t:"saved", ver} (the new version)
//                        {t:"logout", ev: token}
//   Bounties (wanted posters, kept in DATA_DIR/bounty.json):
//                        {t:"bounty", hits, look}     my bounty and how I look (kept per account, or per name for guests)
//                        {t:"wanted"}                 -> {t:"wanted", players: [{name, b, lk, c}]}  highest first (c:1 = you)
//                        {t:"who"}                    -> {t:"who", hits: online, players: [{id, name, isl, c}]}  everyone in the game and their room (c:1 = you; not those still on the title screen, nor gone quiet)
//   server -> client:     {t:"kicked", why}            this connection is being closed (the same account logged in elsewhere)
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
const DATA_DIR = path.resolve(process.env.DATA_DIR || path.join(__dirname, "data"));

// Every island layout online comes from this. It's kept in DATA_DIR/world.json, so a restart keeps
// the same islands (before, a new one each start reshaped every island and games that reconnected
// after a restart no longer matched). Delete world.json to get a new world.
const WORLD_SEED = (() => {
  const file = path.join(DATA_DIR, "world.json");
  try { const s = JSON.parse(fs.readFileSync(file, "utf8")).seed | 0; if (s > 0) return s; } catch (e) {}
  const seed = crypto.randomInt(1, 2 ** 31 - 1);
  try { fs.mkdirSync(DATA_DIR, { recursive: true }); fs.writeFileSync(file, JSON.stringify({ seed, made: new Date().toISOString() })); }
  catch (e) { console.log("! world.json not written: " + e.message + " (the islands will change at the next start)"); }
  return seed;
})();

// ---------- limits (against people trying to break the server) ----------
const MAX_CLIENTS = 300;              // connections in all
const MAX_PER_IP = 12;                // connections from one address (friends at one house share one)
const MSG_RATE = 80, MSG_BURST = 300;              // messages per second (a player sends ~20)
const BYTE_RATE = 256 * 1024, BYTE_BURST = 12 * 1024 * 1024; // bytes per second (a save or a home island is big, but rare)
const MAX_STRIKES = 400;              // messages dropped for going too fast, then cut off
const HELLO_WITHIN = 10000;           // ms to say hello after connecting
const CONNECTS_PER_MIN = 30;          // new connections from one address per minute (reconnects are a few)
const FRAME_BEFORE_HELLO = 16 * 1024; // the biggest message before hello (hello, a login: tiny)
const FRAME_GUEST = 2 * 1024 * 1024;  // ... before logging in (a guest's home island is the big one)
const SAVE_EVERY = 5000;              // ms between save writes per account (the game sends every 8 s)
const MAX_ACCOUNTS = 5000;
const REGISTER_PER_IP = 3, REGISTER_ALL = 30, REGISTER_WINDOW = 60 * 60 * 1000; // new accounts per hour
const FAILS_PER_IP = 10, FAILS_PER_ACCOUNT = 5, FAIL_LOCK = 15 * 60 * 1000;      // wrong passwords, then a 15 min wait

const clients = new Map();            // id -> socket
const rooms = new Map();              // island -> { order: [ids], events: Map(nodeId -> event) }
let nextId = 1;

/// Players who have started the game (joined a room at least once). Someone still on the title
/// screen (choosing a look, logging in) is connected but not counted as online.
function playingCount() { let n = 0; for (const s of clients.values()) if (active(s)) n++; return n; }

/// A game in play sends something every few seconds (it asks who's online every 8 s at most).
/// One that has gone quiet - a phone locked, a tab in the background, a connection that died
/// without closing - is left out of the online lists after QUIET_HIDE, and cut off after QUIET_DROP.
const QUIET_HIDE = 45 * 1000, QUIET_DROP = 5 * 60 * 1000;
function active(s) { return s.playing && Date.now() - (s.lastHeard || 0) < QUIET_HIDE; }
setInterval(() => {
  const now = Date.now();
  for (const s of [...clients.values()])
    if (now - (s.lastHeard || 0) > QUIET_DROP) { log("! quiet for " + Math.round((now - s.lastHeard) / 1000) + " s, closing " + s.name + " #" + s.pid); s.destroy(); drop(s); }
}, 30 * 1000).unref();

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
    // the whole picture only on this PC; through the tunnel (Cloudflare adds cf-connecting-ip) just the count
    const here = !req.headers["cf-connecting-ip"] && /^(::1|127\.0\.0\.1|::ffff:127\.0\.0\.1)$/.test(req.socket.remoteAddress || "");
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify(here ? { online: playingCount(), connected: clients.size, islands: [...rooms].map(([i, r]) => ({ island: i, players: r.order.length })) }
                                : { online: playingCount() }));
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

// a request that never finishes its headers (slowloris) is cut off instead of held open
server.headersTimeout = 10000;
server.requestTimeout = 20000;

// ---------- WebSocket (RFC 6455, text frames only) ----------

/// Web pages allowed to open the game connection: the game on GitHub Pages, the game served through
/// a Cloudflare tunnel, and this PC / home network. A browser always says which page it is on
/// (Origin), so another website can't use its visitors' browsers to play or read the lists.
/// No Origin at all is the desktop build / editor. (A program of its own can say any Origin it
/// likes: this keeps other web pages out, not a changed game.) More with ALLOW_ORIGINS="a,b".
const GAME_ORIGIN = (() => { try { return new URL(GAME_URL).origin; } catch (e) { return ""; } })();
const EXTRA_ORIGINS = String(process.env.ALLOW_ORIGINS || "").split(",").map(s => s.trim()).filter(Boolean);
function originOk(origin) {
  if (origin === undefined) return true;
  if (origin === GAME_ORIGIN || EXTRA_ORIGINS.includes(origin)) return true;
  let u;
  try { u = new URL(origin); } catch (e) { return false; }
  if (u.protocol === "https:" && /^[a-z0-9-]+\.trycloudflare\.com$/.test(u.hostname)) return true;
  if (u.protocol !== "http:" && u.protocol !== "https:") return false;
  const h = u.hostname;
  return h === "localhost" || h === "127.0.0.1" || h === "[::1]" || /^192\.168\.\d+\.\d+$/.test(h) || /^10\.\d+\.\d+\.\d+$/.test(h);
}

const connects = new Map();           // address -> recent connection times
setInterval(() => {
  const now = Date.now();
  for (const [ip, list] of connects) { const l = list.filter(t => now - t < 60000); if (l.length) connects.set(ip, l); else connects.delete(ip); }
}, 60 * 1000).unref();

server.on("upgrade", (req, socket) => {
  socket.on("error", () => {});
  if ((req.url || "").split("?")[0] !== "/ws" || (req.headers.upgrade || "").toLowerCase() !== "websocket") {
    socket.destroy();
    return;
  }
  const ip = ipOf(req);
  if (!originOk(req.headers.origin)) {
    log("! refused a connection from page " + String(req.headers.origin).slice(0, 80) + " (" + ip + ")");
    socket.end("HTTP/1.1 403 Forbidden\r\nConnection: close\r\n\r\n");
    return;
  }
  const now = Date.now(), recent = (connects.get(ip) || []).filter(t => now - t < 60000);
  recent.push(now);
  connects.set(ip, recent);
  if (recent.length > CONNECTS_PER_MIN) {
    if (recent.length === CONNECTS_PER_MIN + 1) log("! too many connections from " + ip + " (" + CONNECTS_PER_MIN + " a minute)");
    socket.end("HTTP/1.1 429 Too Many Requests\r\nConnection: close\r\n\r\n");
    return;
  }
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
  socket.lastHeard = Date.now();

  socket.on("data", chunk => {
    buf = Buffer.concat([buf, chunk]);
    for (;;) {
      if (buf.length < 2) return;
      const fin = (buf[0] & 0x80) !== 0, op = buf[0] & 0x0f, masked = (buf[1] & 0x80) !== 0;
      let len = buf[1] & 0x7f, at = 2;
      if (len === 126) { if (buf.length < 4) return; len = buf.readUInt16BE(2); at = 4; }
      else if (len === 127) { if (buf.length < 10) return; len = Number(buf.readBigUInt64BE(2)); at = 10; }
      if (len > frameLimit(socket)) { socket.destroy(); return; }
      const maskAt = at;
      if (masked) at += 4;
      if (buf.length < at + len) return;
      const payload = Buffer.from(buf.subarray(at, at + len));
      if (masked) for (let i = 0; i < len; i++) payload[i] ^= buf[maskAt + (i & 3)];
      buf = buf.subarray(at + len);
      socket.lastHeard = Date.now(); // any frame at all: still there

      if (op === 0x8) { sendFrame(socket, 0x8, Buffer.alloc(0)); socket.end(); return; }
      if (op === 0x9) { sendFrame(socket, 0xA, payload); continue; }
      if (op === 0xA) continue;
      if (op === 0x1 || op === 0x2 || op === 0x0) {
        parts.push(payload);
        partsLen += payload.length;
        if (partsLen > frameLimit(socket) || parts.length > 1000) { socket.destroy(); return; } // a message in endless pieces
        if (!fin) continue;
        const text = Buffer.concat(parts).toString("utf8");
        parts = [];
        partsLen = 0;
        if (!allowed(socket, text.length)) { if (socket.destroyed) return; continue; }
        try { onMessage(socket, text); }
        catch (e) { log("! bad message from " + (socket.name || "?") + " (" + socket.ip + "): " + e.message); } // one odd message must not stop the server
      }
    }
  });
  socket.on("end", () => socket.destroy()); // the other side hung up without a close frame: done here too (or it stays half-open, holding a place, until the quiet timeout)
  socket.on("close", () => drop(socket));
  socket.on("error", () => drop(socket));
});

/// The biggest message a connection may send: tiny before hello, a guest's home island before
/// logging in, a whole save after. Someone who never logs in can't make the server hold megabytes.
function frameLimit(socket) {
  return !socket.pid ? FRAME_BEFORE_HELLO : socket.acct ? MAX_FRAME : FRAME_GUEST;
}

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
  if (!r) { r = { order: [], events: new Map(), emptySince: 0 }; rooms.set(island, r); }
  return r;
}

function toRoom(island, obj, exceptId) {
  const r = rooms.get(island);
  if (!r) return;
  for (const id of r.order) if (id !== exceptId) send(clients.get(id), obj);
}

function leaveRoom(socket) {
  const island = socket.island;
  socket.pos = null;                    // the next island starts the move check afresh
  socket.downed = false;
  if (!island) return;
  socket.island = 0;
  const r = rooms.get(island);
  if (!r) return;
  const wasHost = r.order[0] === socket.pid;
  r.order = r.order.filter(id => id !== socket.pid);
  toRoom(island, { t: "left", from: socket.pid });
  if (wasHost && r.order.length > 0) toRoom(island, { t: "host", host: r.order[0] });
  if (r.order.length === 0) {
    if (island < 0 || r.events.size === 0) rooms.delete(island); // a private island keeps nothing here; nothing to remember
    else r.emptySince = Date.now();                              // an island's changes stay a while for the next visitor
  }
}

// Islands nobody has been on for ROOM_KEEP are forgotten (their changes too): the server's memory
// no longer grows with every island, channel, cave and prison floor ever visited.
const ROOM_KEEP = 6 * 60 * 60 * 1000;
setInterval(() => {
  const now = Date.now();
  for (const [island, r] of rooms) if (r.order.length === 0 && now - (r.emptySince || now) > ROOM_KEEP) rooms.delete(island);
}, 10 * 60 * 1000).unref();

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
    socket.baseName = cleanText(m.name, 16) || "Islander";
    nameCheck(socket);
    clients.set(socket.pid, socket);
    send(socket, { t: "welcome", id: socket.pid, worldSeed: WORLD_SEED });
    log("+ " + socket.name + " #" + socket.pid + " (" + clients.size + " online)");
    return;
  }
  if (!socket.pid) return;      // must say hello first
  m.from = socket.pid;          // no one can speak for someone else
  const named = m.t !== "register" && m.t !== "login" && m.t !== "token";
  if (named && m.name !== undefined) m.name = socket.name; // nor go by another name than the one checked at hello

  if (m.t === "chat") { m.text = cleanText(m.text, 80); if (!m.text) return; } // nothing oversized, no rich-text tags
  if (m.t === "hit") {
    // a blow on the host's creature (passed on below): the host's game caps it too (AllowHit)
    m.dmg = num(m.dmg, 0, 100000);
    m.id = m.id | 0;
    if (typeof m.ev !== "string" || m.ev.length > 16) delete m.ev;
  }

  switch (m.t) {
    case "join": {
      leaveRoom(socket);
      socket.playing = true; // in the game now: counts as online
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
      const cur = saveVersion(socket.acct);
      // a game from before save versions sends none: taken as before. Otherwise the save must build
      // on the newest one, or it would put back progress another device has already moved past.
      if (m.ver !== undefined && (m.ver | 0) !== cur) { send(socket, { t: "saveold", ver: cur }); return; }
      const ver = cur + 1;
      saveVersions.set(socket.acct, ver);
      saveSoon(socket.acct, JSON.stringify({ save: text, keep, at: Date.now(), ver }));
      socket.acctLv = saveLevel(text) || socket.acctLv;
      send(socket, { t: "saved", ver });
      return;
    }
    case "logout": {
      const acc = socket.acct && accounts[socket.acct];
      if (acc && m.ev) { acc.tokens = (acc.tokens || []).filter(t => t !== hashToken(m.ev)); saveAccountsSoon(); }
      socket.acct = null;
      socket.acctLv = 0;
      nameCheck(socket);
      return;
    }
    case "bounty":
      setBounty(socket, m);
      return;
    case "wanted":
      send(socket, { t: "wanted", players: wantedFor(socket) });
      return;
    case "who": {
      // everyone online and where they are (isl = their room: island key, sea, or private island)
      const players = [];
      for (const [id, s] of clients) if (active(s) || id === socket.pid) players.push({ id, name: s.name, isl: s.island | 0, c: id === socket.pid ? 1 : 0 });
      send(socket, { t: "who", hits: players.length, players: players.slice(0, 100) });
      return;
    }
    case "home":
      socket.home = { text: String(m.text || "").slice(0, 400000), room: String(m.room || "").slice(0, 100000), lv: m.lv | 0, pets: String(m.pets || "").slice(0, 2000), c: Math.max(0, Math.min(3, m.island | 0)) };
      return;
    case "homes": {
      const players = [];
      for (const [id, s] of clients) if (id !== socket.pid && s.home && s.home.text && active(s)) players.push({ id, name: s.name, c: s.home.c | 0 });
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
    case "crew": {
      // crews (IslandGame.Crew.cs): reaches the player wherever they are, at sea too
      const s = clients.get(m.to | 0);
      if (s && s !== socket) { m.name = socket.name; send(s, m); }
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
    case "p":
    case "sp": {
      // how I look and where I am, ~10 times a second: only the fields the game uses, each within
      // what a real game can send, so a changed game can't show level 99999 or garbage to everyone
      if (!socket.island) return;
      const p = playerUpdate(m);
      if (!p) return;
      if (socket.acctLv) p.lv = socket.acctLv; // an account's level is the one in its save here, not what the game says
      if (p.t === "p") { limitMove(socket, p); socket.downed = p.dn === 1; }
      toRoom(socket.island, p, socket.pid);
      return;
    }
    case "rev": {
      // bringing a knocked-out friend back: only someone standing next to them, and only if they are down
      const s = clients.get(m.to | 0);
      if (!s || s === socket || s.island !== socket.island || !s.downed || !socket.pos || !s.pos) return;
      if (Math.hypot(s.pos.x - socket.pos.x, s.pos.y - socket.pos.y) > REVIVE_REACH) return;
      s.downed = false;
      send(s, { t: "rev", from: socket.pid, to: s.pid });
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
      // what only the room's host may say: the creatures (en), their bites (dmg), who killed one
      // (kill), the clock. Anyone else sending them is cheating (or out of date): dropped.
      if (HOST_ONLY.has(m.t) && !isHostOf(socket)) return;
      if (m.to > 0) { const s = clients.get(m.to); if (s && s.island === socket.island) send(s, m); }
      else toRoom(socket.island, m, socket.pid);
  }
}

const HOST_ONLY = new Set(["en", "dmg", "kill", "clock"]);

/// A number within lo..hi (anything else - text, NaN, Infinity, missing - becomes lo, or 0 if inside).
function num(v, lo, hi) {
  v = +v;
  if (!Number.isFinite(v)) return Math.max(lo, Math.min(hi, 0));
  return Math.max(lo, Math.min(hi, v));
}
const int = (v, lo, hi) => Math.floor(num(v, lo, hi));

const MAX_LEVEL = 999, MAX_POS = 5000;

// How far a player may move, in tiles. The fastest real walker (every speed skill, wings, awakened
// dash power, well rested) does ~11.5 tiles/s; a dash power jumps up to ~16. Faster than that is a
// changed game: the others see it move no faster than this, toward where it claims to be (a real
// jump - respawning, being carried by wind - just looks like a quick glide for others).
const MOVE_RATE = 13, MOVE_BURST = 22;
const REVIVE_REACH = 3;               // tiles (the game revives within 1.3; some room for lag)

function limitMove(socket, p) {
  const now = Date.now();
  if (!socket.pos) { socket.pos = { x: p.x, y: p.y }; socket.moveBudget = MOVE_BURST; socket.moveAt = now; return; } // first update on this island
  socket.moveBudget = Math.min(MOVE_BURST, socket.moveBudget + (now - socket.moveAt) / 1000 * MOVE_RATE);
  socket.moveAt = now;
  const dx = p.x - socket.pos.x, dy = p.y - socket.pos.y, d = Math.hypot(dx, dy);
  if (d > socket.moveBudget) {
    const k = socket.moveBudget / d;
    p.x = socket.pos.x + dx * k;
    p.y = socket.pos.y + dy * k;
    socket.moveBudget = 0;
    if (now - (socket.fastLogAt || 0) > 60000) { socket.fastLogAt = now; log("! " + socket.name + " #" + socket.pid + " (" + socket.ip + ") moves faster than the game allows: slowed down"); }
  } else socket.moveBudget -= d;
  socket.pos.x = p.x;
  socket.pos.y = p.y;
}

/// The level in an account's save (the save's own "level", near its start), or 0 if none.
function saveLevel(text) {
  const m = /"level":(\d+)/.exec(String(text || "").slice(0, 4000));
  return m ? Math.max(1, Math.min(MAX_LEVEL, m[1] | 0)) : 0;
}

/// A player's "p" (on an island) or "sp" (a boat at sea) update, rebuilt from only the fields
/// the game sends (IslandGame.Net.cs NetTick / SeaTick), each clamped. null = not worth passing on.
function playerUpdate(m) {
  const sea = m.t === "sp";
  const x = +m.x, y = +m.y;
  if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
  const p = {
    t: m.t, from: m.from, name: m.name, // already swapped for the checked name above
    x: sea ? num(x, -1, 2) : num(x, -MAX_POS, MAX_POS), y: num(y, -MAX_POS, MAX_POS),
    d: int(m.d, 0, 7), f: int(m.f, 0, 63), fl: int(m.fl, 0, 1),
    hat: int(m.hat, 0, 4096), outfit: int(m.outfit, 0, 4096),
    lv: int(m.lv, 1, MAX_LEVEL), look: int(m.look, 0, 2000000), aw: int(m.aw, 0, 255),
  };
  if (sea) { p.kind = int(m.kind, 0, 255); p.hits = int(m.hits, 0, 1); }
  else { p.pet = int(m.pet, 0, 100000); p.dn = int(m.dn, 0, 1); }
  return p;
}

/// The room's host: the first in it, or on a private island its owner (the owner's game runs it).
function isHostOf(socket) {
  const r = rooms.get(socket.island);
  if (!r) return false;
  if (r.order[0] === socket.pid) return true;
  return socket.island < 0 && onHomeOf(socket, socket.pid);
}

/// Words players type (names, chat): no rich-text tags (< >) that would resize or recolour the
/// screen for everyone, no control characters, and a sane length.
function cleanText(v, max) {
  return String(v == null ? "" : v).replace(/[<>\u0000-\u001f\u007f]/g, "").trim().slice(0, max);
}

/// The name others see. A player's name is free to choose, but the name of an account
/// belongs to that account: anyone else using it gets a "?" after it (e.g. "Gust?"), so
/// nobody can pass as someone else in chat. Logging in to that account takes the "?" away.
function nameCheck(socket) {
  const base = socket.baseName || "Islander", key = keyOf(base);
  const name = accounts[key] && socket.acct !== key ? base.slice(0, 15) + "?" : base;
  socket.name = name; // the others see the change with this player's next update
}

/// Is this player on that owner's private island (outside or in the house)?
function onHomeOf(socket, owner) {
  return socket.island === -(owner * 2) || socket.island === -(owner * 2 + 1);
}

// ---------- bounties (the board of wanted posters) ----------

const MAX_BOARD = 500;                // posters kept (the lowest bounties go first)
const SHOW_BOARD = 60;                // posters sent to a player
let board = {};                       // "a:<account>" or "g:<guest name>" -> { name, b, lk, at }
{
  const file = path.join(DATA_DIR, "bounty.json");
  try { board = JSON.parse(fs.readFileSync(file, "utf8")) || {}; }
  catch (e) {
    board = {};
    if (fs.existsSync(file)) { try { fs.renameSync(file, file + ".broken"); } catch (e2) {} console.log("! bounty.json was broken: starting a new board (old one kept as bounty.json.broken)"); }
  }
  for (const k of Object.keys(board)) if (k.startsWith("g:")) delete board[k]; // guests' posters (anyone could take their name) are gone
}
let boardTimer = null;
function saveBoardSoon() {
  if (boardTimer) return;
  boardTimer = setTimeout(() => {
    boardTimer = null;
    try { writeFileSafe(path.join(DATA_DIR, "bounty.json"), JSON.stringify(board)); } catch (e) { log("! bounty board not saved: " + e.message); }
  }, 3000);
}
const boardKey = socket => socket.acct ? "a:" + socket.acct : "g:" + keyOf(socket.name);

const BOUNTY_STEP = 15000000;          // the most a bounty may grow at once (a continent boss's is 3-12 million)
const BOUNTY_EVERY = 5000;             // ms between rises

function setBounty(socket, m) {
  // the board everyone sees: logged-in players only (a guest's name isn't theirs to keep), and a
  // bounty rises no faster than the game can raise it, so a changed game can't top the board at once
  if (!socket.acct) return;
  const key = boardKey(socket), old = board[key];
  let b = Math.max(0, Math.min(2000000000, Math.floor(+m.hits || 0)));
  const was = old ? old.b : 0, now = Date.now();
  if (b > was) {
    if (now - (socket.bountyAt || 0) < BOUNTY_EVERY) return;
    b = Math.min(b, was + BOUNTY_STEP);
    socket.bountyAt = now;
  }
  if (socket.acct) delete board["g:" + keyOf(socket.name)]; // played as a guest before making the account
  if (old && old.b === b && old.lk === (m.look | 0) && old.name === socket.name) return;
  board[key] = { name: socket.name, b, lk: m.look | 0, at: Date.now() };
  const keys = Object.keys(board);
  if (keys.length > MAX_BOARD) {
    keys.sort((x, y) => board[y].b - board[x].b || board[y].at - board[x].at);
    for (const k of keys.slice(MAX_BOARD)) if (k !== key) delete board[k];
  }
  saveBoardSoon();
}

function wantedFor(socket) {
  const mine = boardKey(socket);
  return Object.entries(board)
    .sort((x, y) => y[1].b - x[1].b || x[1].at - y[1].at)
    .slice(0, SHOW_BOARD)
    .map(([k, e]) => ({ id: 0, name: e.name, b: e.b, lk: e.lk, c: k === mine ? 1 : 0 }));
}

// ---------- accounts ----------

let accounts = {};                    // key (lower-case name) -> { name, salt, hash, tokens: [sha256], at }
{
  const file = path.join(DATA_DIR, "accounts.json");
  let text = null;
  try { text = fs.readFileSync(file, "utf8"); } catch (e) { accounts = {}; } // no file yet: a new server
  if (text !== null) {
    try { accounts = JSON.parse(text) || {}; }
    catch (e) {
      // A broken accounts file must not be written over with an empty one (that would lose every player).
      console.log("\n!! " + file + " is broken (" + e.message + ").\n!! The server did not start. Run Server/restore-backup.bat to put a backup back.\n");
      process.exit(1);
    }
  }
}
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
  nameCheck(socket);
  // one account, one game: an older connection on this account (another device, a tab left open,
  // one that died without closing) is closed, so it neither shows twice in the lists nor writes
  // an older save over this one
  for (const s of [...clients.values()])
    if (s !== socket && s.acct === socket.acct) {
      log("  " + acc.name + " logged in again: closing the older connection #" + s.pid);
      send(s, { t: "kicked", why: "บัญชีนี้เข้าเล่นจากอีกเครื่อง/อีกแท็บแล้ว" });
      s.acct = null; // its saves stop reaching the account right away
      s.acctLv = 0;
      const old = s;
      setTimeout(() => { old.destroy(); drop(old); }, 300); // let the message go out first
    }
  const pending = saveTimers.get(socket.acct); // a save still waiting to be written: on disk first,
  if (pending && pending.timer) { clearTimeout(pending.timer); pending.write(); } // so this login gets the newest
  const data = readSave(socket.acct);
  socket.acctLv = saveLevel(data.save) || 1; // shown to others from here on (a new account: 1 until its first save)
  send(socket, { t: "auth", hits: 1, name: acc.name, ev: token, text: data.save || "", room: data.keep || "", ver: saveVersion(socket.acct) });
  log("  " + socket.name + " logged in as " + acc.name);
}

// Every accepted save gets the next version number (kept in the save file too).
const saveVersions = new Map();        // account -> newest version
function saveVersion(key) {
  if (!saveVersions.has(key)) saveVersions.set(key, readSave(key).ver | 0);
  return saveVersions.get(key);
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
  if (boardTimer) { clearTimeout(boardTimer); boardTimer = null; try { writeFileSafe(path.join(DATA_DIR, "bounty.json"), JSON.stringify(board)); } catch (e) {} }
  for (const s of saveTimers.values()) if (s.timer) { clearTimeout(s.timer); s.write(); }
}
for (const sig of ["SIGINT", "SIGTERM", "SIGHUP", "SIGBREAK"]) {
  try { process.on(sig, () => { log("Server stopping: saving accounts..."); flushAll(); process.exit(0); }); } catch (e) {}
}

// ---------- backups ----------
// A copy of DATA_DIR goes to Server/backups/<date_time>/ when the server starts and every
// BACKUP_EVERY; the newest KEEP_BACKUPS are kept. To also copy them somewhere safer (a OneDrive or
// Google Drive folder), put that folder's path on the first line of Server/backup-to.txt.
// Server/restore-backup.bat puts a backup back.
const BACKUP_DIR = path.resolve(process.env.BACKUP_DIR || path.join(__dirname, "backups"));
const BACKUP_EVERY = 6 * 60 * 60 * 1000;
const KEEP_BACKUPS = 30;

function backupTargets() {
  const out = [BACKUP_DIR];
  try {
    const extra = fs.readFileSync(path.join(__dirname, "backup-to.txt"), "utf8").split(/\r?\n/)[0].trim().replace(/^"|"$/g, "");
    if (extra) out.push(path.resolve(extra, "IslandServerBackups"));
  } catch (e) {}
  return out;
}

function stamp() {
  const d = new Date(), p = n => String(n).padStart(2, "0");
  return d.getFullYear() + "-" + p(d.getMonth() + 1) + "-" + p(d.getDate()) + "_" + p(d.getHours()) + "-" + p(d.getMinutes()) + "-" + p(d.getSeconds());
}

function backupNow(why) {
  flushAll(); // what is still waiting goes to disk first, so the copy is up to date
  let files;
  try { files = fs.readdirSync(DATA_DIR).filter(f => f.endsWith(".json")); } catch (e) { return; }
  if (!files.length) return;
  const name = stamp();
  for (const root of backupTargets()) {
    try {
      const dir = path.join(root, name);
      fs.mkdirSync(dir, { recursive: true });
      for (const f of files) fs.copyFileSync(path.join(DATA_DIR, f), path.join(dir, f));
      const old = fs.readdirSync(root).filter(d => /^\d{4}-\d\d-\d\d_\d\d-\d\d-\d\d$/.test(d)).sort();
      for (const d of old.slice(0, Math.max(0, old.length - KEEP_BACKUPS))) fs.rmSync(path.join(root, d), { recursive: true, force: true });
      log("  backup (" + why + "): " + files.length + " files -> " + dir);
    } catch (e) { log("! backup to " + root + " failed: " + e.message); }
  }
}
setInterval(() => backupNow("every 6 h"), BACKUP_EVERY).unref();

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
  const ipId = "ip:" + socket.ip, acctId = "acct:" + key + "@" + socket.ip; // wrong passwords from one address lock that address out of the account, not its owner everywhere
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
      if (locked(acctId)) log("! account " + acc.name + " locked for 15 min from " + socket.ip + " (wrong passwords)");
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
  backupNow("server start");
});
