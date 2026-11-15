// Browser WebSocket for NetLink.cs (web build only). Messages are queued here
// and polled by the game each frame. (Adapted from PixelLeap2D.)
mergeInto(LibraryManager.library, {
  IS_WsOpen: function (urlPtr) {
    if (!Module.isWs) Module.isWs = { next: 1, socks: {} };
    var h = Module.isWs.next++;
    var e = { ws: null, q: [], state: 0 };
    Module.isWs.socks[h] = e;
    try {
      var ws = new WebSocket(UTF8ToString(urlPtr));
      e.ws = ws;
      ws.onopen = function () { e.state = 1; };
      ws.onmessage = function (m) { if (typeof m.data === "string") e.q.push(m.data); };
      ws.onclose = function () { e.state = 2; };
      ws.onerror = function () { e.state = 2; };
    } catch (err) { e.state = 2; }
    return h;
  },

  IS_WsState: function (h) {
    var e = Module.isWs && Module.isWs.socks[h];
    return e ? e.state : 2;
  },

  IS_WsSend: function (h, textPtr) {
    var e = Module.isWs && Module.isWs.socks[h];
    if (e && e.state === 1) { try { e.ws.send(UTF8ToString(textPtr)); } catch (err) {} }
  },

  IS_WsRecv: function (h) {
    var e = Module.isWs && Module.isWs.socks[h];
    if (!e || e.q.length === 0) return null;
    var s = e.q.shift();
    var n = lengthBytesUTF8(s) + 1;
    var buf = _malloc(n);
    stringToUTF8(s, buf, n);
    return buf;
  },

  IS_WsClose: function (h) {
    var e = Module.isWs && Module.isWs.socks[h];
    if (!e) return;
    try { if (e.ws) e.ws.close(); } catch (err) {}
    e.state = 2;
    e.q = [];
    delete Module.isWs.socks[h];
  },
});
