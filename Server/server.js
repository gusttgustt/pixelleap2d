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
//   Characters (3 per account; kind:1 on register/login/token = a game that knows them):
//                                                     -> {t:"auth", hits:1, name, ev: token, text: {list:[{used, name, look, lv, at}]}}
//                        {t:"char", hits: slot, name?, look?}  play this one (name/look: only for an older one with no name yet)
//                                                     -> {t:"char", hits: slot, name, text: save, room: keep, ver} or {t:"char", hits:-1, why}
//                        {t:"charnew", hits: slot, name, look}  -> {t:"chars", text: list} or {t:"chars", why}
//                        {t:"chardel", hits: slot}    -> {t:"chars", text: list}  (its save file is kept aside, not erased)
//                        A game that logs in without kind:1 gets slot 0 picked for it and its save in "auth", as before.
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
const MAX_CLIENTS = 300;              // players (said hello) in all
const MAX_GUESTS = 240;               // ... of them not logged in: the rest is kept for accounts, so a crowd of
                                      // made-up guests can't lock real players out (new accounts are slow to make)
const LOGIN_WITHIN = 15000;           // ms a guest let in over MAX_GUESTS has to log in (a saved token takes a moment)
const OVER_PER_IP = 2;                // such guests waiting to log in, per address
const MAX_PENDING = 60, PENDING_PER_IP = 4; // connections that haven't said hello yet: a pool of their own,
                                      // so a flood of silent connections doesn't take players' places
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
const FAILS_ACCOUNT_ALL = 30;         // ... on one account from all addresses together (its saved logins on devices still work)

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
    // Play on GitHub Pages: the page finds this server through server.txt (a ?server= in the address
    // is ignored there, so a link can't point players at someone else's server)
    res.writeHead(302, { Location: GAME_URL });
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
  // (no *.trycloudflare.com: anyone can make one of those, and this server never serves the game
  // page through the tunnel - it sends people to GAME_URL instead)
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
  let pendingHere = 0, oldest = null, waiting = 0;
  for (const s of sockets) if (!s.pid) { waiting++; if (s.ip === ip) pendingHere++; if (!oldest || s.connectedAt < oldest.connectedAt) oldest = s; }
  if (pendingHere >= PENDING_PER_IP || (perIp.get(ip) || 0) >= MAX_PER_IP) {
    socket.end("HTTP/1.1 503 Service Unavailable\r\nConnection: close\r\n\r\n");
    return;
  }
  // the silent ones' pool is full: the one silent longest goes (a real game says hello at once)
  if (waiting >= MAX_PENDING && oldest) oldest.destroy();
  sockets.add(socket);
  perIp.set(ip, (perIp.get(ip) || 0) + 1);
  socket.ip = ip;
  socket.connectedAt = now;
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

  // Pieces that arrive are kept in a list and joined once there's enough for the next frame:
  // joining on every piece copied a big message over and over (a 4 MB save in 64 KB pieces).
  let waitList = [], waitLen = 0, need = 2;
  socket.on("data", chunk => {
    waitList.push(chunk); waitLen += chunk.length;
    if (waitLen < need) return;
    buf = waitList.length === 1 ? waitList[0] : Buffer.concat(waitList, waitLen);
    waitList = []; waitLen = 0;
    const keep = n => { need = n; if (buf.length) { waitList.push(buf); waitLen = buf.length; } buf = Buffer.alloc(0); };
    for (;;) {
      if (buf.length < 2) return keep(2);
      const fin = (buf[0] & 0x80) !== 0, op = buf[0] & 0x0f, masked = (buf[1] & 0x80) !== 0;
      let len = buf[1] & 0x7f, at = 2;
      if (len === 126) { if (buf.length < 4) return keep(4); len = buf.readUInt16BE(2); at = 4; }
      else if (len === 127) { if (buf.length < 10) return keep(10); len = Number(buf.readBigUInt64BE(2)); at = 10; }
      if (len > frameLimit(socket)) { socket.destroy(); return; }
      const maskAt = at;
      if (masked) at += 4;
      if (buf.length < at + len) return keep(at + len);
      need = 2;
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
  // A connection that doesn't read what it is sent (asking for big answers and never taking them)
  // would keep it all waiting in memory here until the server runs out: past SEND_BACKLOG, closed.
  // (The biggest real answer, a login with a whole save, is ~7 MB.)
  if (socket.writableLength + len > SEND_BACKLOG) {
    if (!socket.backlogLogged) { socket.backlogLogged = true; log("! " + (socket.name || "?") + " #" + (socket.pid || 0) + " (" + socket.ip + ") isn't reading what it is sent: closed"); }
    socket.destroy(); drop(socket); return;
  }
  socket.write(Buffer.concat([head, payload]));
}
const SEND_BACKLOG = 16 * 1024 * 1024;

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
    const banned = banOfSocket(socket); // a banned address (a banned account is turned away at login)
    if (banned) { log("! banned address " + socket.ip + " turned away"); kickOut(socket, banText(banned)); return; }
    if (!roomForPlayer(socket)) { socket.destroy(); return; } // the game goes offline and tries again later
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
  const named = m.t !== "register" && m.t !== "login" && m.t !== "token" && m.t !== "char" && m.t !== "charnew";
  if (named && m.name !== undefined) m.name = socket.name; // nor go by another name than the one checked at hello

  if (m.t === "chat") {
    m.text = cleanText(m.text, 80); if (!m.text) return; // nothing oversized, no rich-text tags
    if (mutedSocket(socket)) return; // muted (the "mute" command): it reaches no one
    if (!bucket(socket, "chatBucket", CHAT_RATE, CHAT_BURST)) return; // nor a flood over the whole room
  }
  if (m.t === "hit") {
    // a blow on the host's creature (passed on below): the host's game caps it too (AllowHit)
    m.dmg = num(m.dmg, 0, 100000);
    m.id = m.id | 0;
    if (typeof m.ev !== "string" || m.ev.length > 16) delete m.ev;
  }

  // asks with big answers (someone's whole island, the lists, a room's world changes): a few at a time
  const ASK = ASK_LIMITS[m.t];
  if (ASK && !bucket(socket, "ask_" + m.t, ASK[0], ASK[1])) return;
  // and per address too: one address may hold MAX_PER_IP connections, each with its own buckets
  const ASK_IP = ASK_IP_LIMITS[m.t];
  if (ASK_IP && !bucket(ipState(socket.ip), "ask_" + m.t, ASK_IP[0], ASK_IP[1])) return;

  switch (m.t) {
    case "join": {
      if (!socket.acct && mod.guestsOff) { kickOut(socket, GUESTS_OFF); return; } // "guests off": accounts only
      const island = (m.island | 0) || 1; // below 0: someone's private island (not remembered)
      if (island < 0 && !mayEnterHome(socket, Math.floor(-island / 2))) {
        // someone else's private island needs its owner online and sharing it (as "visit" does), and a
        // "visit" asked for this connection (or the one before a reconnect from the same address):
        // no walking onto an island whose owner is away, hosting it and changing its farm
        log("! " + socket.name + " #" + socket.pid + " (" + socket.ip + ") tried to join private island " + island + " without visiting");
        leaveRoom(socket);
        return;
      }
      leaveRoom(socket);
      socket.playing = true; // in the game now: counts as online
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
    case "char": onChar(socket, m); return;
    case "charnew": onCharNew(socket, m); return;
    case "chardel": onCharDel(socket, m); return;
    case "save": {
      if (!socket.acct || !accounts[socket.acct] || !socket.ckey) return;
      const text = String(m.text || ""), keep = String(m.room || "");
      if (text.length > MAX_SAVE || keep.length > MAX_SAVE) return;
      const cur = saveVersion(socket.ckey);
      // a game from before save versions sends none: taken as before. Otherwise the save must build
      // on the newest one, or it would put back progress another device has already moved past.
      if (m.ver !== undefined && (m.ver | 0) !== cur) { send(socket, { t: "saveold", ver: cur }); return; }
      // a save the game can't have made: quietly not kept (the account keeps its last good one).
      // The game is told it was saved, so a changed game gets no hint of what gave it away.
      const bad = checkSave(socket.ckey, text);
      if (bad) { noteCheat(socket, bad); send(socket, { t: "saved", ver: cur }); return; }
      const ver = cur + 1;
      saveVersions.set(socket.ckey, ver);
      const lv = trustLevel(socket, saveLevel(text));
      saveSoon(socket.ckey, JSON.stringify({ save: text, keep, at: Date.now(), ver, lv: lv.lv, lvCredit: Math.floor(lv.credit * 100) / 100 }));
      socket.acctLv = lv.lv || socket.acctLv;
      const ch = charsOf(socket.acct)[socket.slot];
      if (ch && ch.used) { // what the character screen shows: written with the accounts when the level moves
        ch.at = Date.now();
        if (lv.lv && ch.lv !== lv.lv) { ch.lv = lv.lv; saveAccountsSoon(); }
      }
      send(socket, { t: "saved", ver });
      return;
    }
    case "logout": {
      const acc = socket.acct && accounts[socket.acct];
      if (acc && m.ev) { acc.tokens = (acc.tokens || []).filter(t => t !== hashToken(m.ev)); saveAccountsSoon(); }
      socket.acct = null; socket.ckey = null; socket.slot = -1;
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
      if (s === socket) { send(socket, { t: "visit", id: s.pid, text: "" }); return; }
      allowVisit(socket.ip, s.pid);
      send(socket, { t: "visit", id: s.pid, name: s.name, text: s.home.text, room: s.home.room, lv: s.home.lv, pets: s.home.pets });
      log("  " + socket.name + " visits " + s.name + "'s island");
      return;
    }
    case "crew": {
      // crews (IslandGame.Crew.cs): reaches the player wherever they are, at sea too
      const s = clients.get(m.to | 0);
      if (!s || s === socket) return;
      if (m.ev === "xp" || m.ev === "xpr") {
        // a share of a kill's XP: only to someone in the same place, and never more than a big kill gives
        if (!socket.island || s.island !== socket.island) return;
        m.d = Math.max(0, Math.min(m.d | 0, 3000));
        if (m.d <= 0) return;
        m.text = typeof m.text === "string" ? m.text.slice(0, 24) : "";
      }
      // rebuilt from the fields the game reads (IslandGame.Crew.cs), nothing big passed on
      send(s, { t: "crew", from: socket.pid, to: s.pid, ev: cleanText(m.ev, 16), name: socket.name, island: m.island | 0,
                d: m.d | 0, x: num(m.x, -1e6, 1e6), y: num(m.y, -1e6, 1e6), look: int(m.look, 0, 2000000), lv: m.lv | 0,
                text: cleanText(m.text, 2000) }); // (crew lists, names: no rich-text tags either)
      return;
    }
    case "knock": {
      const s = clients.get(m.to | 0);
      if (s) send(s, { t: "knock", from: socket.pid, name: socket.name });
      return;
    }
    case "hnode": {
      // A private island changed (m.id = its owner): everyone on it sees it, and the owner gets it.
      // Only the owner, or someone standing on that private island while the owner is there too
      // (outside or in the house): nobody changes a farm behind its owner's back.
      const owner = m.id | 0;
      if (owner <= 0) return;
      if (owner !== socket.pid) {
        const o = clients.get(owner);
        if (!onHomeOf(socket, owner) || !o || !onHomeOf(o, owner)) return;
      }
      // the whole island (ev "full": big, as big as a shared home) or only what just changed (small)
      const one = m.to > 0, full = m.ev === "full" && owner === socket.pid;
      const h = { t: "hnode", from: socket.pid, id: owner, text: String(m.text || "").slice(0, full ? 400000 : 100000),
                  room: String(m.room || "").slice(0, 100000), fl: int(m.fl, 0, 1), lv: int(m.lv, 0, 1000) };
      if (full) h.ev = "full";
      if (one) { const s = clients.get(m.to | 0); if (s && s.island === socket.island) send(s, h); return; }
      if (socket.island) toRoom(socket.island, h, socket.pid);
      const o = clients.get(owner);
      if (o && o !== socket && o.island !== socket.island) send(o, h);
      return;
    }
    case "hgift": {
      // a friend harvested on your farm: you get the same (only from someone on your farm, a handful at a time)
      const s = clients.get(m.to | 0);
      if (!s || s === socket || !onHomeOf(socket, s.pid)) return;
      if (!bucket(socket, "giftBucket", GIFT_RATE, GIFT_BURST)) return; // no faster than hands can harvest
      m.hits = Math.max(0, Math.min(m.hits | 0, 8)); // one harvest: at most 5 crops + 3 from skills
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
      // an account's level is the one in its save here, not what the game says; a guest's rises no faster than play
      p.lv = socket.acctLv || guestLevel(socket, p.lv);
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
    case "spawn": {
      if (!socket.island) return;
      // a player harvests or builds a couple of times a second at most: a flood of made-up world
      // changes (filling the island's MAX_EVENTS for hours) is dropped, and someone who keeps at it
      // stops being remembered at all for this island
      if (!bucket(socket, "eventBucket", EVENT_RATE, EVENT_BURST)) return;
      const ev = m.t === "spawn"
        ? { t: "spawn", from: socket.pid, id: m.id | 0, kind: int(m.kind, 0, 10000), x: num(m.x, MAP_MIN, MAP_MAX), y: num(m.y, MAP_MIN, MAP_MAX) }
        : { t: "node", from: socket.pid, id: m.id | 0, ev: NODE_EVENTS.has(m.ev) ? m.ev : null, dur: num(m.dur, 0, 7 * 24 * 3600), hits: int(m.hits, 0, 100000) };
      if (ev.t === "node" && !ev.ev) return;
      if (socket.island > 0) {
        const r = room(socket.island);
        let keep = true;
        if (ev.t === "spawn") {
          // things built here, per player (by address: a new connection doesn't start afresh)
          const built = r.built || (r.built = new Map()), n = (built.get(socket.ip) || 0) + 1;
          built.set(socket.ip, n);
          keep = n <= SPAWNS_PER_PLAYER;
        }
        if (keep) remember(r, ev);
      }
      toRoom(socket.island, ev, socket.pid);
      return;
    }
    default: {
      if (!socket.island) return;
      // Only the kinds the game sends, each rebuilt from the fields the game reads: nothing else is
      // passed on (before, anything unknown went to the whole room as it came, megabytes included).
      // What only the room's host may say: the creatures (en), their bites (dmg), who killed one
      // (kill), the clock. Anyone else sending them is cheating (or out of date): dropped.
      const build = RELAY[m.t];
      if (!build) return;
      if (HOST_ONLY.has(m.t) && !isHostOf(socket)) return;
      const out = build(m, socket);
      if (!out) return;
      out.t = m.t; out.from = socket.pid;
      if (out.to > 0) { const s = clients.get(out.to); if (s && s.island === socket.island) send(s, out); }
      else { delete out.to; toRoom(socket.island, out, socket.pid); }
    }
  }
}

const ASK_LIMITS = {                  // [per second, burst]
  visit: [0.5, 3], homes: [1, 3], who: [1, 3], wanted: [1, 3], join: [2, 6],
};
// The same asks from all connections of one address together (a family on one home line shares it)
const ASK_IP_LIMITS = {
  visit: [1, 5], homes: [2, 6], who: [2, 6], wanted: [2, 6], join: [4, 12],
};

/// Rate state kept per address (not per connection), forgotten a while after it was last used.
const ipStates = new Map();           // address -> { at, <bucket key>: { n, at } }
function ipState(ip) {
  let o = ipStates.get(ip);
  if (!o) ipStates.set(ip, o = {});
  o.at = Date.now();
  return o;
}
setInterval(() => {
  const now = Date.now();
  for (const [ip, o] of ipStates) if (now - o.at > 10 * 60 * 1000) ipStates.delete(ip);
  for (const [k, t] of authNext) if (t < now) authNext.delete(k);
}, 60 * 1000).unref();
const HOST_ONLY = new Set(["en", "dmg", "kill", "clock"]);
const GIFT_RATE = 1, GIFT_BURST = 8;   // farm gifts ("hgift") one visitor may send per second
const NODE_EVENTS = new Set(["deplete", "remove", "stump", "hits", "set"]); // what ApplyEvent knows
const EVENT_RATE = 6, EVENT_BURST = 40; // world changes one player may send per second
const SPAWNS_PER_PLAYER = 300;        // things one address may build on an island that are remembered

const MAX_EN = 200;                   // creatures in one "en" update (an island has a few dozen)
const BITE_RATE = 3, BITE_BURST = 6;  // bites one player may take per second (each creature bites every 1.3 s)
const BITE_REACH = 4;                 // tiles: a bite needs one of the host's creatures this close to the player
const MAX_BITE = 2500;                // the game takes no more than this from one bite either (MaxNetBite)
// All bites on one player together: the strongest boss bites ~250 every 1.3 s, a swarm a few times
// that; even the toughest player (~1500 health) takes some seconds to go down from a changed host.
const BITE_DMG_RATE = 500, BITE_DMG_BURST = 1000;
// Where the host's creatures are, as the server lets them be: the fastest walks ~2.3 tiles/s (a dash,
// a knock back a little more), so one may move no faster than this between "en" updates. A creature
// the host makes up next to someone has to be new: new ones are few a second, and only bite once
// they have been around a little.
const EN_RATE = 6, EN_BURST = 4;      // tiles/s, tiles
const NEW_EN_RATE = 2, NEW_EN_BURST = 120; // new creatures a room may get per second
const EN_SETTLE = 1500;               // ms before a new creature can bite
const CHAT_RATE = 2, CHAT_BURST = 5;  // chat lines one player may send per second
const KILL_RATE = 6, KILL_BURST = 20; // kills a host may announce per second

/// The host's creatures as the server keeps them (room.enemies: id -> { x, y, since, seen }): each
/// moves toward where the host says, but no faster than a creature can; new ones come a few at a time.
function trackEnemies(r, en) {
  const now = Date.now();
  const map = r.enemies || (r.enemies = new Map());
  for (const e of en) {
    let t = map.get(e.id);
    if (!t) {
      if (!bucket(r, "newEnBucket", NEW_EN_RATE, NEW_EN_BURST)) continue; // a flood of made-up ones: not tracked, can't bite
      t = { x: e.x, y: e.y, since: now, budget: EN_BURST };
      map.set(e.id, t);
    } else {
      // as limitMove: a budget of tiles that refills at EN_RATE, up to EN_BURST
      t.budget = Math.min(EN_BURST, t.budget + EN_RATE * (now - t.seen) / 1000);
      const dx = e.x - t.x, dy = e.y - t.y, d = Math.hypot(dx, dy), k = d > t.budget ? t.budget / d : 1;
      t.x += dx * k; t.y += dy * k; t.budget -= d * k;
    }
    t.seen = now;
  }
  for (const [id, t] of map) if (now - t.seen > 60000) map.delete(id); // gone a while: forgotten
  if (map.size > MAX_EN * 4) for (const [id, t] of map) if (t.seen !== now) map.delete(id);
  r.enemiesAt = now;
}

/// A token bucket kept on any object: true while there's room for one more.
function bucket(o, key, rate, burst) {
  const now = Date.now(), b = o[key] || (o[key] = { n: burst, at: now });
  b.n = Math.min(burst, b.n + (now - b.at) / 1000 * rate); b.at = now;
  if (b.n < 1) return false;
  b.n -= 1; return true;
}

const RELAY = {
  chat: m => ({ text: m.text }),      // cleaned above
  hit: m => ({ to: m.to | 0, id: m.id, dmg: m.dmg, ev: m.ev }),
  clock: m => ({ clock: num(m.clock, 0, 1e9) }),
  en: (m, socket) => {
    if (!Array.isArray(m.en)) return null;
    const en = m.en.slice(0, MAX_EN).filter(e => e && typeof e === "object").map(e => ({
      id: e.id | 0, k: int(e.k, 0, 10000), hp: int(e.hp, 0, 1e9), mhp: int(e.mhp, 0, 1e9), fl: int(e.fl, 0, 1),
      x: num(e.x, -1000, 1000), y: num(e.y, -1000, 1000), // (not only island maps: creatures at sea too)
    }));
    const r = rooms.get(socket.island);
    if (r) trackEnemies(r, en); // where the host's creatures are: bites are checked against it
    return { en };
  },
  dmg: (m, socket) => {
    // A bite from the host's creature. The host is just another player's game, so: only someone in
    // this room, only with one of the host's creatures next to them, and no more often than
    // creatures bite. (A changed host can still bite within that; only the server running the
    // creatures itself would stop that fully.)
    const s = clients.get(m.to | 0);
    if (!s || s === socket || s.island !== socket.island) return null;
    // on a private island only its owner's game runs the creatures (a visitor first in the room doesn't)
    if (socket.island < 0 && !onHomeOf(socket, socket.pid)) return null;
    // creatures chase only players whose place they know ("p"), and leave a knocked-out one alone
    if (!s.pos || s.downed) return null;
    const r = rooms.get(socket.island), now = Date.now();
    if (!r || !r.enemies || now - r.enemiesAt > 3000) return null; // creatures never reported: nothing can be biting
    let near = false;
    for (const e of r.enemies.values())
      if (e.seen === r.enemiesAt && now - e.since >= EN_SETTLE && Math.hypot(e.x - s.pos.x, e.y - s.pos.y) <= BITE_REACH) { near = true; break; }
    if (!near) return null;
    if (!bucket(s, "biteBucket", BITE_RATE, BITE_BURST)) return null;
    // all bites together no more than BITE_DMG_RATE a second (what is left of it, if a bite is bigger)
    const b = s.biteDmg || (s.biteDmg = { n: BITE_DMG_BURST, at: now });
    b.n = Math.min(BITE_DMG_BURST, b.n + (now - b.at) / 1000 * BITE_DMG_RATE); b.at = now;
    const dmg = Math.min(num(m.dmg, 0, MAX_BITE), b.n);
    if (!(dmg > 0)) return null;
    b.n -= dmg;
    return { to: s.pid, dmg, why: cleanText(m.why, 40) };
  },
  kill: (m, socket) => {
    const r = rooms.get(socket.island);
    if (!r || !bucket(r, "killBucket", KILL_RATE, KILL_BURST)) return null;
    return { id: m.id | 0, by: m.by | 0, kind: int(m.kind, 0, 10000), hits: int(m.hits, 0, 1000000), x: num(m.x, -1000, 1000), y: num(m.y, -1000, 1000) };
  },
};

/// A number within lo..hi (anything else - text, NaN, Infinity, missing - becomes lo, or 0 if inside).
function num(v, lo, hi) {
  v = +v;
  if (!Number.isFinite(v)) return Math.max(lo, Math.min(hi, 0));
  return Math.max(lo, Math.min(hi, v));
}
const int = (v, lo, hi) => Math.floor(num(v, lo, hi));

const MAX_LEVEL = 999;
// Every map on land (islands, caves, houses, private islands) is IslandGame.Size = 44 tiles a side:
// a position outside it (x: 999) can't be real, wherever the player first appears.
const MAP_MIN = -2, MAP_MAX = 46;

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

/// The save comes whole from the player's game, so the server can't know what really happened in
/// it. What it can do: the level others see (and the one kept with the save as "lv") rises no faster
/// than playing can raise it. A save claiming more is still kept for that player (it is their own
/// game), but everyone else sees the level the server let through.
/// Levels are earned as credit only while the player is online and in the game (LEVELS_PER_MIN, at
/// most LEVEL_BANK kept): a day away gives nothing to jump with. A guest starts at FIRST_LEVEL at most
/// and rises the same way; a new account's first save brings at most FIRST_LEVEL or what that
/// connection already showed as a guest.
const LEVEL_FREE = 5, LEVELS_PER_MIN = 2, LEVEL_BANK = 60, FIRST_LEVEL = 10;
const trustedLv = new Map();          // account -> { lv, credit }

/// Credit for the time this connection has been in the game since it was last counted.
function earnLevels(socket, t) {
  const now = Date.now();
  if (socket.playing && socket.lvClock) t.credit = Math.min(LEVEL_BANK, (t.credit || 0) + (now - socket.lvClock) / 60000 * LEVELS_PER_MIN);
  socket.lvClock = now;
}

/// The level shown for a claimed one: as much of the rise as the credit allows.
function allowLevel(socket, t, claimed) {
  earnLevels(socket, t);
  if (claimed <= t.lv) { t.lv = claimed; return t.lv; } // (a level lost: fine)
  const up = Math.min(claimed - t.lv, Math.floor(t.credit));
  t.lv += up; t.credit -= up;
  if (t.lv < claimed && Date.now() - (socket.lvLogAt || 0) > 60000) { socket.lvLogAt = Date.now(); log("! " + socket.name + " (" + socket.ip + ") says level " + claimed + ": shown as " + t.lv); }
  return t.lv;
}

function trustLevel(socket, claimed) {
  const key = socket.ckey || socket.acct;
  let t = trustedLv.get(key);
  if (!t || !t.lv) {
    if (!claimed) return t || { lv: 0, credit: 0 };
    const guest = socket.guestLv ? socket.guestLv.lv : 0;
    t = { lv: Math.min(claimed, Math.max(FIRST_LEVEL, guest)), credit: 0 };
    trustedLv.set(key, t);
    earnLevels(socket, t);
  }
  if (claimed) allowLevel(socket, t, claimed);
  return t;
}

/// A guest's level as others see it (kept per connection: a new one starts again from FIRST_LEVEL).
function guestLevel(socket, claimed) {
  if (!socket.guestLv) { socket.guestLv = { lv: Math.min(claimed, FIRST_LEVEL), credit: 0 }; earnLevels(socket, socket.guestLv); }
  return allowLevel(socket, socket.guestLv, claimed);
}

// ---------- what a save can hold ----------
// The save is the player's own game written down, so the server can't replay it. What it can check
// is what no real game can reach: more skill points than levels, shrines, caves and bosses give;
// a boat better than the islands reached need; a save played with the web dev keys ("?dev=1").
// A save that breaks one is not kept (checkSave), and the account is marked (noteCheat): it shows
// on the server log, and it leaves the wanted board. To forgive one: remove its "cheats" fields
// from data/accounts.json while the server is stopped.
// Older accounts that were already past a limit (before these checks) aren't caught for that: only
// a save that goes further past it than the account's last kept save is turned away.
const ISLANDS_PER_CONTINENT = 20, CONTINENTS = 4; // IslandGame.Continent.cs
const SKILL_SLACK = 2;                // a point or two of room (a level and a reward landing in one save)
const saveFacts = new Map();          // account -> what its last kept save held (saveFactsOf)

/// What a save holds that can be checked, or null if it isn't a save at all.
function saveFactsOf(text) {
  let d;
  try { d = JSON.parse(text); } catch (e) { return null; }
  if (!d || typeof d !== "object") return null;
  const n = v => Number.isFinite(+v) ? +v : 0;
  const flags = new Set(Array.isArray(d.flags) ? d.flags.map(String) : []);
  let shrines = 0, caves = 0, alphas = 0, cbosses = 0;
  for (const f of flags) {
    if (/^shrine\d+$/.test(f)) shrines++;
    else if (/^caveboss/.test(f)) caves++;
    else if (/^alpha\d+$/.test(f)) alphas++;
    else if (/^cboss\d+$/.test(f)) cbosses++;
  }
  // skill points: one a level, one a shrine / cave lord / pack leader, three a continent boss, two the prison's prisoner zero
  const earned = Math.max(0, n(d.level) - 1) + shrines + caves + alphas + 3 * cbosses + (flags.has("prison0") ? 2 : 0);
  const skills = Array.isArray(d.skills) ? d.skills.reduce((a, v) => a + Math.max(0, n(v)), 0) : 0;
  const spent = skills + Math.max(0, n(d.mastery)) + Math.max(0, n(d.skillPoints));
  // the boat: Lv N sails one island past N; the levels go on from one continent to the next (19 each)
  const reach = Array.isArray(d.reach) && d.reach.length ? d.reach.map(n) : [Math.max(n(d.farthest), n(d.island))];
  let boatMax = 1;
  for (let c = 0; c < Math.min(reach.length, CONTINENTS); c++)
    if (reach[c] > 0) boatMax = Math.max(boatMax, c * (ISLANDS_PER_CONTINENT - 1) + Math.min(reach[c], ISLANDS_PER_CONTINENT));
  return { dev: flags.has("dev"), skillOver: spent - earned - SKILL_SLACK, boatOver: n(d.raftLevel) - boatMax };
}

/// Why this save can't be from a real game, or null if it can.
function checkSave(key, text) {
  if (!text) return null; // no save (the character died): fine
  const now = saveFactsOf(text);
  if (!now) return "not a save";
  let was = saveFacts.get(key);
  if (was === undefined) {
    const kept = readSave(key);
    was = kept.save ? saveFactsOf(kept.save) : null;
  }
  const over = (k) => now[k] > Math.max(0, was ? was[k] : 0);
  const why = now.dev ? "played with the dev keys" : over("skillOver") ? "skill points +" + now.skillOver + " past what it earned"
            : over("boatOver") ? "boat " + now.boatOver + " levels past its islands" : null;
  if (!why) saveFacts.set(key, now);
  return why;
}

/// A save was turned away: the log says so, the account remembers it, and it leaves the wanted board.
function noteCheat(socket, why) {
  const acc = accounts[socket.acct];
  if (Date.now() - (socket.cheatLogAt || 0) > 60000) { socket.cheatLogAt = Date.now(); log("! " + socket.name + " (" + socket.ip + "): save not kept - " + why); }
  if (!acc) return;
  if (acc.cheatWhy !== why) { acc.cheats = (acc.cheats | 0) + 1; acc.cheatWhy = why; acc.cheatAt = Date.now(); saveAccountsSoon(); }
  if (socket.ckey && board["a:" + socket.ckey]) { delete board["a:" + socket.ckey]; saveBoardSoon(); }
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
    x: sea ? num(x, -1, 2) : num(x, MAP_MIN, MAP_MAX), y: sea ? num(y, -10, 10) : num(y, MAP_MIN, MAP_MAX), // at sea the game keeps a boat within 6.5 of the route
    d: int(m.d, 0, 7), f: int(m.f, 0, 63), fl: int(m.fl, 0, 1),
    hat: int(m.hat, 0, 4096), outfit: int(m.outfit, 0, 4096),
    lv: int(m.lv, 1, MAX_LEVEL), look: int(m.look, 0, 2000000), aw: int(m.aw, 0, 255),
  };
  if (sea) { p.kind = int(m.kind, 0, 255); p.hits = int(m.hits, 0, 1); }
  else { p.pet = int(m.pet, 0, 100000); p.dn = int(m.dn, 0, 1); p.wp = int(m.wp, 0, 4096); }
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

/// Visits handed out: "address>owner" -> when. Kept by address, not connection, so a visitor whose
/// line drops and comes back (a new connection, a new id) can go back onto the island they were on.
const visits = new Map();
const VISIT_KEEP = 3 * 60 * 60 * 1000;
function allowVisit(ip, owner) { visits.set(ip + ">" + owner, Date.now()); }
setInterval(() => { const now = Date.now(); for (const [k, t] of visits) if (now - t > VISIT_KEEP) visits.delete(k); }, 10 * 60 * 1000).unref();

/// May this player go onto owner's private island? Their own always; someone else's only while the
/// owner is online and sharing it, and after asking to visit it.
function mayEnterHome(socket, owner) {
  if (owner === socket.pid) return true;
  const o = clients.get(owner);
  if (!o || !o.home || !o.home.text) return false;
  const t = visits.get(socket.ip + ">" + owner);
  return !!t && Date.now() - t < VISIT_KEEP;
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
const boardKey = socket => socket.ckey ? "a:" + socket.ckey : "g:" + keyOf(socket.name);

const BOUNTY_STEP = 15000000;          // the most a bounty may grow at once (a continent boss's is 3-12 million)
const BOUNTY_EVERY = 5000;             // ms between rises
const BOUNTY_HOUR = 120000000;         // the most a bounty may grow in an hour
const bountyGrowth = new Map();        // board key -> { left, at }

function setBounty(socket, m) {
  // the board everyone sees: logged-in players only (a guest's name isn't theirs to keep), and a
  // bounty rises no faster than the game can raise it, so a changed game can't top the board at once
  if (!socket.acct || !socket.ckey) return;
  if ((accounts[socket.acct] || {}).cheats) return; // a save of theirs was turned away (noteCheat)
  const key = boardKey(socket), old = board[key];
  let b = Math.max(0, Math.min(2000000000, Math.floor(+m.hits || 0)));
  const was = old ? old.b : 0, now = Date.now();
  if (b > was) {
    if (now - (socket.bountyAt || 0) < BOUNTY_EVERY) return;
    // and no more than BOUNTY_HOUR in an hour (ten continent bosses), per account across connections
    const g = bountyGrowth.get(key) || { left: BOUNTY_HOUR, at: now };
    g.left = Math.min(BOUNTY_HOUR, g.left + (now - g.at) / 3600000 * BOUNTY_HOUR); g.at = now;
    b = Math.min(b, was + BOUNTY_STEP, was + Math.floor(g.left));
    g.left -= b - was;
    bountyGrowth.set(key, g);
    socket.bountyAt = now;
    if (b <= was) return;
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

function authOk(socket, acc, kind) {
  const ban = activeEntry(mod.bans, "a:" + keyOf(acc.name)) || activeEntry(mod.bans, "ip:" + socket.ip);
  if (ban) { send(socket, { t: "auth", hits: 0, why: banText(ban) }); log("! banned " + acc.name + " (" + socket.ip + ") tried to log in"); return; }
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
      s.acct = null; s.ckey = null; // its saves stop reaching the account right away
      s.acctLv = 0;
      const old = s;
      setTimeout(() => { old.destroy(); drop(old); }, 300); // let the message go out first
    }
  socket.ckey = null; socket.slot = -1;
  if (kind === 1) { // a game that knows characters: it picks one next ("char")
    socket.acctLv = 1;
    send(socket, { t: "auth", hits: 1, name: acc.name, ev: token, kind: 1, text: JSON.stringify({ list: charList(socket.acct) }) });
  } else { // an older game: the first character, as before
    const data = pickChar(socket, 0);
    send(socket, { t: "auth", hits: 1, name: acc.name, ev: token, text: data.save || "", room: data.keep || "", ver: saveVersion(socket.ckey) });
  }
  log("  " + socket.name + " logged in as " + acc.name);
}

// ---------- moderation (type commands in the "Island server" window; "help" lists them) ----------
// Bans and mutes are by account (a logged-in player, whichever character) or by address (someone
// playing without an account). They're kept in DATA_DIR/moderation.json (backed up with the rest),
// with two switches: no new accounts, and accounts only (no playing without one).
let mod = { bans: {}, mutes: {}, registerOff: false, guestsOff: false };
{
  const file = path.join(DATA_DIR, "moderation.json");
  try { const m = JSON.parse(fs.readFileSync(file, "utf8")) || {}; mod = { bans: m.bans || {}, mutes: m.mutes || {}, registerOff: !!m.registerOff, guestsOff: !!m.guestsOff }; }
  catch (e) { if (fs.existsSync(file)) { try { fs.renameSync(file, file + ".broken"); } catch (e2) {} console.log("! moderation.json was broken: starting with no bans (old one kept as moderation.json.broken)"); } }
}
function saveMod() { try { writeFileSafe(path.join(DATA_DIR, "moderation.json"), JSON.stringify(mod, null, 1)); } catch (e) { log("! moderation not saved: " + e.message); } }

/// The ban or mute under that key ("a:<account>" / "ip:<address>") if it's still on.
function activeEntry(list, key) {
  const e = list[key];
  if (!e) return null;
  if (e.until && Date.now() > e.until) { delete list[key]; saveMod(); return null; }
  return e;
}
const banOfSocket = s => activeEntry(mod.bans, "ip:" + s.ip) || (s.acct ? activeEntry(mod.bans, "a:" + s.acct) : null);
const mutedSocket = s => !!(activeEntry(mod.mutes, "ip:" + s.ip) || (s.acct && activeEntry(mod.mutes, "a:" + s.acct)));
const thTime = t => new Date(t).toLocaleString("th-TH", { timeZone: "Asia/Bangkok", dateStyle: "medium", timeStyle: "short" });
const banText = e => "ถูกระงับการเล่นออนไลน์" + (e.until ? " ถึง " + thTime(e.until) : " ถาวร") + (e.why ? " - เหตุผล: " + e.why : "");
const GUESTS_OFF = "ตอนนี้เซิร์ฟเวอร์เปิดให้เฉพาะผู้เล่นที่มีบัญชี - สมัครหรือเข้าสู่ระบบก่อน";
const REGISTER_OFF = "ปิดรับสมัครบัญชีใหม่ชั่วคราว ลองใหม่ภายหลัง";

/// Tells the game why, then closes the connection (the game shows it and stays offline).
function kickOut(socket, why) {
  send(socket, { t: "kicked", why });
  socket.acct = null; socket.ckey = null; // nothing more reaches an account from it
  setTimeout(() => { socket.destroy(); drop(socket); }, 300);
}

/// Who a command means: "#12" (the id in "who"), an address, an account name, or the name a
/// player shows in the game (a character's name finds its account).
function findTarget(arg) {
  arg = String(arg || "").trim();
  const online = [...clients.values()];
  if (/^#\d+$/.test(arg)) {
    const s = clients.get(+arg.slice(1));
    return s ? { sockets: [s], acct: s.acct || null, ip: s.ip } : null;
  }
  if (/^(\d{1,3}\.){3}\d{1,3}$/.test(arg) || (arg.includes(":") && /^[0-9a-f:]+$/i.test(arg)))
    return { sockets: online.filter(s => s.ip === arg), acct: null, ip: arg };
  const k = keyOf(arg);
  if (accounts[k]) { const ss = online.filter(s => s.acct === k); return { sockets: ss, acct: k, ip: ss[0] ? ss[0].ip : null }; }
  const ss = online.filter(s => keyOf(s.baseName) === k || keyOf(s.name) === k);
  if (!ss.length) return null;
  return { sockets: ss, acct: ss[0].acct || null, ip: ss[0].ip };
}

/// "ban x 24 spamming" -> { target: "x", num: 24, why: "spamming" } (the number is optional).
function parseArgs(rest) {
  const parts = rest.trim().split(/\s+/);
  const target = parts.shift() || "";
  let num = null;
  if (parts.length && /^\d+(\.\d+)?$/.test(parts[0])) num = +parts.shift();
  return { target, num, why: cleanText(parts.join(" "), 100) };
}

const MOD_HELP = [
  "commands (type here, then Enter):",
  "  who                          everyone online: #id, name, account, address",
  "  kick <who> [reason]          disconnect (they can come back)",
  "  ban <who> [hours] [reason]   ban the account (no account: the address); no hours = forever",
  "  banip <who> [hours] [reason] ban the address, whoever plays from it",
  "  unban <account|address>",
  "  mute <who> [minutes]         their chat stops reaching anyone (default 60 min, 0 = forever)",
  "  unmute <who>",
  "  bans                         bans and mutes in force",
  "  register on|off              new accounts allowed / not",
  "  guests on|off                playing without an account allowed / not",
  "  say <text>                   a message to everyone online",
  "  <who> = #id from who, an account name, a player's name, or an address",
].join("\n");

function modCommand(line) {
  const m = /^(\S+)\s*(.*)$/.exec(String(line).trim());
  if (!m) return;
  const cmd = m[1].toLowerCase(), rest = m[2];
  const out = s => console.log(s);
  const describe = t => (t.acct ? "account " + (accounts[t.acct] || {}).name : "address " + t.ip) + (t.sockets.length ? " (" + t.sockets.length + " online)" : "");
  switch (cmd) {
    case "help": case "?": out(MOD_HELP); return;
    case "who": {
      const list = [...clients.values()];
      if (!list.length) { out("nobody online"); return; }
      for (const s of list) out("  #" + s.pid + "  " + s.name + "  " + (s.acct ? "account " + (accounts[s.acct] || {}).name + (s.slot >= 0 ? " slot " + (s.slot + 1) : "") : "(no account)") + "  " + s.ip + (s.playing ? "" : "  (title screen)") + (mutedSocket(s) ? "  MUTED" : ""));
      return;
    }
    case "kick": {
      const a = parseArgs(rest), t = findTarget(a.target);
      if (!t || !t.sockets.length) { out("not online: " + a.target); return; }
      for (const s of t.sockets) kickOut(s, "ถูกเตะออกจากเซิร์ฟเวอร์" + (a.why ? " - เหตุผล: " + a.why : ""));
      log("* kicked " + describe(t)); return;
    }
    case "ban": case "banip": {
      const a = parseArgs(rest), t = findTarget(a.target);
      if (!t) { out("not found: " + a.target + " (an account name, #id from who, a player's name or an address)"); return; }
      const byIp = cmd === "banip" || !t.acct;
      if (byIp && !t.ip) { out("no address known for " + a.target + " (not online): use banip with the address"); return; }
      const key = byIp ? "ip:" + t.ip : "a:" + t.acct;
      const e = { why: a.why, at: Date.now(), until: a.num ? Date.now() + a.num * 3600000 : 0 };
      mod.bans[key] = e; saveMod();
      const hit = [...clients.values()].filter(s => byIp ? s.ip === t.ip : s.acct === t.acct);
      for (const s of hit) kickOut(s, banText(e));
      if (!byIp && board["a:" + t.acct]) { for (const k of Object.keys(board)) if (k === "a:" + t.acct || k.startsWith("a:" + t.acct + "#")) delete board[k]; saveBoardSoon(); }
      log("* banned " + key + (e.until ? " until " + thTime(e.until) : " forever") + (e.why ? " (" + e.why + ")" : "") + ", " + hit.length + " disconnected");
      return;
    }
    case "unban": {
      const arg = rest.trim(), keys = [ "a:" + keyOf(arg), "ip:" + arg ].filter(k => mod.bans[k]);
      if (!keys.length) { out("no ban for " + arg + " (see bans)"); return; }
      for (const k of keys) delete mod.bans[k];
      saveMod(); log("* unbanned " + keys.join(", ")); return;
    }
    case "mute": {
      const a = parseArgs(rest), t = findTarget(a.target);
      if (!t) { out("not found: " + a.target); return; }
      if (!t.acct && !t.ip) { out("no address known for " + a.target); return; }
      const key = t.acct ? "a:" + t.acct : "ip:" + t.ip, mins = a.num === null ? 60 : a.num;
      mod.mutes[key] = { at: Date.now(), until: mins ? Date.now() + mins * 60000 : 0, why: a.why };
      saveMod(); log("* muted " + key + (mins ? " for " + mins + " min" : " forever")); return;
    }
    case "unmute": {
      const arg = rest.trim(), t = findTarget(arg);
      const keys = [ "a:" + keyOf(arg), "ip:" + arg, t && t.acct ? "a:" + t.acct : "", t && t.ip ? "ip:" + t.ip : "" ].filter(k => k && mod.mutes[k]);
      if (!keys.length) { out("not muted: " + arg); return; }
      for (const k of keys) delete mod.mutes[k];
      saveMod(); log("* unmuted " + keys.join(", ")); return;
    }
    case "bans": {
      const show = (title, list) => { const ks = Object.keys(list).filter(k => activeEntry(list, k)); out(title + (ks.length ? "" : " none")); for (const k of ks) { const e = list[k]; out("  " + k + "  " + (e.until ? "until " + thTime(e.until) : "forever") + (e.why ? "  (" + e.why + ")" : "")); } };
      show("bans:", mod.bans); show("mutes:", mod.mutes);
      out("new accounts: " + (mod.registerOff ? "OFF" : "on") + "   playing without an account: " + (mod.guestsOff ? "OFF" : "on"));
      return;
    }
    case "register": case "guests": {
      const v = rest.trim().toLowerCase();
      if (v !== "on" && v !== "off") { out("use: " + cmd + " on|off"); return; }
      if (cmd === "register") mod.registerOff = v === "off"; else mod.guestsOff = v === "off";
      saveMod();
      if (cmd === "guests" && v === "off") { const g = [...clients.values()].filter(s => !s.acct && s.playing); for (const s of g) kickOut(s, GUESTS_OFF); log("* playing without an account: OFF (" + g.length + " disconnected)"); }
      else log("* " + (cmd === "register" ? "new accounts" : "playing without an account") + ": " + v.toUpperCase());
      return;
    }
    case "say": {
      const text = cleanText(rest, 80);
      if (!text) { out("use: say <text>"); return; }
      for (const s of clients.values()) send(s, { t: "chat", from: 0, name: "[ประกาศ]", text });
      log("* said: " + text); return;
    }
    default: out("unknown command: " + cmd + "  (help lists them)");
  }
}
if (process.stdin && process.stdin.readable) {
  try { require("readline").createInterface({ input: process.stdin }).on("line", l => { try { modCommand(l); } catch (e) { console.log("! " + e.message); } }); } catch (e) {}
}

// ---------- characters ----------
// Each account holds CHARS characters, each with its own save file. The first one (slot 0) is the
// account's save from before characters, so nothing had to move: its key is the account's own.
// The others are "<account>#<slot>" ('#' can't be in an account name). accounts[key].chars keeps
// what the character screen shows: [{ used, name, look, lv, at, born }] (born 0 = the one from before characters).
const CHARS = 3;
const charKey = (acct, slot) => slot === 0 ? acct : acct + "#" + slot;

function charsOf(acct) {
  const acc = accounts[acct];
  if (!acc) return [];
  if (!Array.isArray(acc.chars)) {
    // an account from before characters: its save (if it has one) is the first, still without a name
    const data = readSave(acct);
    acc.chars = [];
    for (let i = 0; i < CHARS; i++) acc.chars.push({ used: 0 });
    if (data.save) acc.chars[0] = { used: 1, name: "", look: 0, lv: data.lv | 0 || saveLevel(data.save) || 1, at: data.at || acc.at || 0 };
    saveAccountsSoon();
  }
  while (acc.chars.length < CHARS) acc.chars.push({ used: 0 });
  return acc.chars;
}

const charList = acct => charsOf(acct).slice(0, CHARS).map(c => c && c.used
  ? { used: 1, name: c.name || "", look: c.look | 0, lv: c.lv | 0, at: Math.floor((c.at || 0) / 1000), born: c.born || 0 } : { used: 0 });

/// This connection plays that character from now on: its save, version and level.
function pickChar(socket, slot) {
  socket.slot = slot;
  socket.ckey = charKey(socket.acct, slot);
  const pending = saveTimers.get(socket.ckey); // a save still waiting to be written: on disk first,
  if (pending && pending.timer) { clearTimeout(pending.timer); pending.write(); } // so this one gets the newest
  const data = readSave(socket.ckey);
  if (!trustedLv.has(socket.ckey)) {
    const lv = data.lv | 0 || saveLevel(data.save); // the level the server let through last time (older saves: their own)
    if (lv) trustedLv.set(socket.ckey, { lv, credit: Number.isFinite(+data.lvCredit) ? Math.max(0, Math.min(LEVEL_BANK, +data.lvCredit)) : LEVEL_FREE }); // (time away earns nothing)
  }
  socket.lvClock = Date.now(); // this character's credit counts from now
  socket.acctLv = (trustedLv.get(socket.ckey) || {}).lv || 1; // shown to others from here on (a new one: 1 until its first save)
  return data;
}

// picking (sends the whole save), making and deleting: a few at a time per connection
const CHAR_RATE = 0.5, CHAR_BURST = 6;
const charOpOk = socket => bucket(socket, "charOps", CHAR_RATE, CHAR_BURST);
const slotOf = m => { const n = Number(m.hits); return Number.isInteger(n) && n >= 0 && n < CHARS ? n : -1; };
const acctName = socket => (accounts[socket.acct] || {}).name || "?";
const listMsg = (socket, extra) => Object.assign({ t: "chars", text: JSON.stringify({ list: charList(socket.acct) }) }, extra);

function onChar(socket, m) {
  if (!charOpOk(socket)) { send(socket, { t: "char", hits: -1, why: "ช้าลงหน่อย แล้วลองใหม่" }); return; }
  if (!socket.acct || !accounts[socket.acct]) { send(socket, { t: "char", hits: -1, why: "ต้องเข้าสู่ระบบก่อน" }); return; }
  const slot = slotOf(m), chars = charsOf(socket.acct), c = chars[slot];
  if (slot < 0 || !c || !c.used) { send(socket, { t: "char", hits: -1, why: "ไม่มีตัวละครในช่องนี้" }); return; }
  // one character per connection: another one means going back to the title screen
  if (socket.ckey && socket.slot !== slot) { send(socket, { t: "char", hits: -1, why: "เลือกตัวละครอื่นได้หลังจากกลับหน้าแรก" }); return; }
  if (!c.name) { // a character from before names itself once
    const name = cleanText(m.name, 16);
    if (!name) { send(socket, { t: "char", hits: -1, why: "ตั้งชื่อให้ตัวละครนี้ก่อน" }); return; }
    c.name = name; c.look = int(m.look, 0, 2000000);
    saveAccountsSoon();
  }
  const data = pickChar(socket, slot);
  socket.baseName = c.name;
  nameCheck(socket);
  send(socket, { t: "char", hits: slot, name: c.name, look: c.look | 0, text: data.save || "", room: data.keep || "", ver: saveVersion(socket.ckey) });
  log("  " + acctName(socket) + " plays " + c.name + " (slot " + (slot + 1) + ")");
}

function onCharNew(socket, m) {
  if (!socket.acct || !accounts[socket.acct]) return;
  if (!charOpOk(socket)) { send(socket, listMsg(socket, { why: "ช้าลงหน่อย แล้วลองใหม่" })); return; }
  const slot = slotOf(m), chars = charsOf(socket.acct);
  if (slot < 0 || chars[slot].used) { send(socket, listMsg(socket, { why: "ช่องนี้มีตัวละครอยู่แล้ว" })); return; }
  const name = cleanText(m.name, 16);
  if (!name) { send(socket, listMsg(socket, { why: "ตั้งชื่อตัวละครก่อน" })); return; }
  setAside(charKey(socket.acct, slot)); // an old save left in this slot (there shouldn't be one) isn't the new character's
  chars[slot] = { used: 1, name, look: int(m.look, 0, 2000000), lv: 1, at: Date.now(), born: Date.now() }; // born: a device's files of an older one in this slot aren't this one's
  saveAccountsNow();
  log("  " + acctName(socket) + " made character " + name + " (slot " + (slot + 1) + ")");
  send(socket, listMsg(socket, { hits: slot }));
}

function onCharDel(socket, m) {
  if (!socket.acct || !accounts[socket.acct]) return;
  if (!charOpOk(socket)) { send(socket, listMsg(socket, { why: "ช้าลงหน่อย แล้วลองใหม่" })); return; }
  const slot = slotOf(m), chars = charsOf(socket.acct);
  if (slot < 0 || !chars[slot].used) return;
  const key = charKey(socket.acct, slot);
  for (const s of clients.values()) if (s.ckey === key) { send(socket, listMsg(socket, { why: "ตัวละครนี้กำลังเล่นอยู่ ลบไม่ได้" })); return; }
  log("  " + acctName(socket) + " deleted character " + (chars[slot].name || "(no name)") + " (slot " + (slot + 1) + ")");
  setAside(key);
  if (board["a:" + key]) { delete board["a:" + key]; saveBoardSoon(); }
  chars[slot] = { used: 0 };
  saveAccountsNow();
  send(socket, listMsg(socket, { hits: slot }));
}

/// A character's save goes aside (u_<hex>.json -> .deleted-<time>), never erased: a mistake can be undone by hand.
function setAside(key) {
  const t = saveTimers.get(key);
  if (t && t.timer) { clearTimeout(t.timer); t.timer = null; }
  saveTimers.delete(key); saveVersions.delete(key); saveFacts.delete(key); trustedLv.delete(key);
  const f = saveFile(key);
  try { if (fs.existsSync(f)) fs.renameSync(f, f + ".deleted-" + Date.now()); } catch (e) { log("! couldn't set a save aside: " + e.message); }
  // only the newest KEEP_DELETED copies of a slot stay (make, save, delete over and over can't fill the disk)
  try {
    const base = path.basename(f) + ".deleted-";
    const old = fs.readdirSync(DATA_DIR).filter(n => n.startsWith(base)).sort((a, b) => +b.slice(base.length) - +a.slice(base.length));
    for (const n of old.slice(KEEP_DELETED)) fs.unlinkSync(path.join(DATA_DIR, n));
  } catch (e) { log("! couldn't tidy set-aside saves: " + e.message); }
}
const KEEP_DELETED = 3;

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
  return f.n >= (id.startsWith("ip:") ? FAILS_PER_IP : id.startsWith("all:") ? FAILS_ACCOUNT_ALL : FAILS_PER_ACCOUNT);
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
const WRONG_LOGIN = "ชื่อบัญชีหรือรหัสผ่านไม่ถูกต้อง (ยังไม่มีบัญชี? กดสมัครได้เลย)";
const MIN_PASS = 8;                    // new accounts (older ones keep the password they have)
const DUMMY_SALT = crypto.randomBytes(16).toString("hex");

// Logins from one address (every connection together): each one that gets in is sent the whole
// save, so without this a saved login (no scrypt) repeated fast could use up the home upload.
const AUTH_IP_RATE = 10 / 60, AUTH_IP_BURST = 10;
// And one account gets in at most once per AUTH_GAP: a second login waits its turn (a game
// reconnecting doesn't notice), one that would have to wait longer than AUTH_WAIT is refused.
const AUTH_GAP = 5000, AUTH_WAIT = 10000;
const authNext = new Map();           // account key -> when its next login may get in
const AUTH_SLOW = "เข้าสู่ระบบถี่เกินไป รอสักครู่แล้วลองใหม่";

/// Waits for this account's turn to log in. false: the queue is too long, or the game closed meanwhile.
async function authTurn(socket, key) {
  const now = Date.now(), at = Math.max(now, authNext.get(key) || 0);
  if (at - now > AUTH_WAIT) { send(socket, { t: "auth", hits: 0, why: AUTH_SLOW }); return false; }
  authNext.set(key, at + AUTH_GAP);
  if (at > now) await new Promise(ok => setTimeout(ok, at - now));
  return !socket.destroyed;
}

async function onAccount(socket, m) {
  if (socket.authBusy) return;          // one at a time per connection
  socket.tries = (socket.tries || 0) + 1;
  if (socket.tries > 20) { send(socket, { t: "auth", hits: 0, why: "ลองหลายครั้งเกินไป ปิดแล้วเปิดเกมใหม่" }); return; }
  if (!bucket(ipState(socket.ip), "auth", AUTH_IP_RATE, AUTH_IP_BURST)) {
    log("! too many logins from " + socket.ip);
    send(socket, { t: "auth", hits: 0, why: AUTH_SLOW });
    return;
  }
  const name = String(m.name || "").trim(), key = keyOf(name), pass = String(m.text || "").slice(0, 200);
  const ipId = "ip:" + socket.ip, acctId = "acct:" + key + "@" + socket.ip; // wrong passwords from one address lock that address out of the account, not its owner everywhere
  socket.authBusy = true;
  try {
    if (m.t === "register") {
      if (mod.registerOff) { send(socket, { t: "auth", hits: 0, why: REGISTER_OFF }); return; } // "register off"
      if (!/^[\p{L}\p{M}\p{N}_.\-]{3,20}$/u.test(name)) { send(socket, { t: "auth", hits: 0, why: "ชื่อบัญชีต้องยาว 3-20 ตัว (ตัวอักษร ตัวเลข _ . -) ห้ามเว้นวรรค" }); return; }
      if (pass.length < MIN_PASS) { send(socket, { t: "auth", hits: 0, why: "รหัสผ่านต้องยาวอย่างน้อย " + MIN_PASS + " ตัว" }); return; } // (accounts made before keep theirs)
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
      authOk(socket, accounts[key], m.kind | 0);
      saveAccountsNow(); // a new account is on disk straight away
      return;
    }
    const acc = accounts[key];
    if (m.t === "token") {
      // A device's saved login. Tokens are random (48 hex digits), so they can't be guessed:
      // an old or unknown one (e.g. from before the server moved) is not a wrong password
      // and must not lock anyone out, and a locked address can still use a valid one.
      if (!acc || !(acc.tokens || []).includes(hashToken(pass))) { send(socket, { t: "auth", hits: 0, ev: "token", why: "ต้องเข้าสู่ระบบใหม่อีกครั้ง" }); return; }
      if (!(await authTurn(socket, key))) return;
      if (!(acc.tokens || []).includes(hashToken(pass))) { send(socket, { t: "auth", hits: 0, ev: "token", why: "ต้องเข้าสู่ระบบใหม่อีกครั้ง" }); return; } // used meanwhile
      acc.tokens = acc.tokens.filter(t => t !== hashToken(pass)); // a fresh token each time
      authOk(socket, acc, m.kind | 0);
      return;
    }
    const allId = "all:" + key;       // wrong passwords from every address together (many addresses guessing one account)
    if (locked(ipId) || locked(acctId) || locked(allId)) { send(socket, { t: "auth", hits: 0, why: TOO_MANY }); return; }
    // an unknown name gets the same answer, as slowly, as a wrong password: the login doesn't tell
    // which account names exist
    const h = await hashPassAsync(pass, acc ? acc.salt : DUMMY_SALT);
    if (socket.destroyed) return;
    if (!acc || !crypto.timingSafeEqual(Buffer.from(h, "hex"), Buffer.from(acc.hash, "hex"))) {
      failed(ipId);
      if (acc) { failed(acctId); failed(allId); }
      if (acc && (locked(acctId) || locked(allId))) log("! account " + acc.name + " locked for 15 min" + (locked(allId) ? " (wrong passwords from many addresses)" : " from " + socket.ip + " (wrong passwords)"));
      send(socket, { t: "auth", hits: 0, why: locked(ipId) || locked(acctId) || locked(allId) ? TOO_MANY : WRONG_LOGIN });
      return;
    }
    fails.delete(allId);
    fails.delete(acctId);
    if (!(await authTurn(socket, key))) return;
    authOk(socket, acc, m.kind | 0);
  } catch (e) {
    log("! account error: " + e.message);
  } finally {
    socket.authBusy = false;
  }
}

/// Room for one more player saying hello? A player with an account says hello first and logs in
/// right after, so when the guests' share is full a newcomer still comes in "on trial": it has
/// LOGIN_WITHIN to log in or it's closed. Those on trial have a pool of their own; when it's full
/// the one waiting longest goes (a real player's saved login takes a moment, a bot's wait never ends).
let fullLogAt = 0;
const onTrial = [];                   // sockets let in over MAX_GUESTS, oldest first
function roomForPlayer(socket) {
  for (let i = onTrial.length - 1; i >= 0; i--) if (onTrial[i].destroyed || onTrial[i].acct || !onTrial[i].pid) onTrial.splice(i, 1);
  let guests = 0, settled = 0;
  for (const s of clients.values()) if (!onTrial.includes(s)) { settled++; if (!s.acct) guests++; }
  const fullLog = why => { if (Date.now() - fullLogAt > 60000) { fullLogAt = Date.now(); log("! server " + why + " (" + clients.size + " players, " + guests + " guests): turning newcomers away"); } };
  if (settled >= MAX_CLIENTS) { fullLog("full"); return false; }
  if (guests < MAX_GUESTS) return true;
  if (onTrial.filter(s => s.ip === socket.ip).length >= OVER_PER_IP) { fullLog("full of guests"); return false; }
  if (onTrial.length >= MAX_PENDING) { const old = onTrial.shift(); old.destroy(); }
  onTrial.push(socket);
  fullLog("full of guests");
  setTimeout(() => {
    const i = onTrial.indexOf(socket);
    if (i < 0) return;                // logged in, or gone
    onTrial.splice(i, 1);
    if (socket.destroyed || socket.acct || !socket.pid) return;
    let g = 0; for (const s of clients.values()) if (!s.acct && !onTrial.includes(s) && s !== socket) g++;
    if (g >= MAX_GUESTS) socket.destroy(); // a guest's place came free meanwhile: stays
  }, LOGIN_WITHIN);
  return true;
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

// This PC only: players come in through the Cloudflare tunnel, which connects from here.
// Nothing on the home network (or the internet, if a port is opened) reaches it directly.
server.listen(PORT, "127.0.0.1", () => {
  log("Island server on port " + PORT + " (world seed " + WORLD_SEED + ")");
  log("  game connects to  ws://127.0.0.1:" + PORT + "/ws");
  log("  web build at      http://127.0.0.1:" + PORT + "/  (from " + WEB_ROOT + ")");
  log("  accounts in       " + DATA_DIR + " (" + Object.keys(accounts).length + " players)");
  log("  type help here for kick / ban / mute / register / guests / say");
  backupNow("server start");
});
