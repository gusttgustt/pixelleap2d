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

  IS_ReloadPage: function () {
    // a fresh address, so the browser fetches the new page (and with it the new build files)
    // instead of a cached copy of the old one
    try {
      var u = new URL(window.location.href);
      u.searchParams.set("v", Date.now().toString());
      window.location.replace(u.toString());
    } catch (e) { window.location.reload(); }
  },

  // ---- chat box for phones (IslandGame.ChatMobile.cs): a real HTML input so the phone's own
  // keyboard (Thai included) comes up. The game polls IS_ChatPoll: null = still typing,
  // "" = cancelled, otherwise the text to send.
  IS_ChatOpen: function (placeholderPtr) {
    var ph = UTF8ToString(placeholderPtr);
    if (!Module.isChat) {
      var box = document.createElement("div");
      // full width on a phone, a centred bar (max 720px) on a tablet or wide screen; clear of the notch
      box.style.cssText = "position:fixed;top:0;left:50%;transform:translateX(-50%);width:100%;max-width:720px;z-index:1000;" +
        "display:none;gap:6px;padding:8px;padding-top:calc(8px + env(safe-area-inset-top));" +
        "padding-left:calc(8px + env(safe-area-inset-left));padding-right:calc(8px + env(safe-area-inset-right));" +
        "background:rgba(20,14,8,0.92);border:2px solid #e6c050;border-top:0;border-radius:0 0 12px 12px;" +
        "box-sizing:border-box;font-family:sans-serif;box-shadow:0 4px 16px rgba(0,0,0,0.5);";
      var input = document.createElement("input");
      input.type = "text"; input.maxLength = 80; input.enterKeyHint = "send"; input.autocomplete = "off";
      input.style.cssText = "flex:1;min-width:0;font-size:16px;padding:10px;border-radius:8px;border:1px solid #886;background:#fff8e8;color:#222;";
      var send = document.createElement("button");
      send.textContent = Module.isLang === "en" ? "Send" : "ส่ง";
      send.style.cssText = "font-size:16px;font-weight:bold;padding:0 16px;border-radius:8px;border:0;background:#e6c050;color:#2a1a08;";
      var cancel = document.createElement("button");
      cancel.textContent = "✕";
      cancel.style.cssText = "font-size:16px;padding:0 14px;border-radius:8px;border:0;background:#555;color:#fff;";
      box.appendChild(input); box.appendChild(send); box.appendChild(cancel);
      document.body.appendChild(box);
      var c = { box: box, input: input, result: null };
      var done = function (text) {
        if (box.style.display === "none") return;
        c.result = text;
        box.style.display = "none";
        input.blur();
      };
      send.addEventListener("click", function (e) { e.preventDefault(); done(input.value); });
      cancel.addEventListener("click", function (e) { e.preventDefault(); done(""); });
      // keys typed here belong to the box, not the game
      var stop = function (e) {
        e.stopPropagation();
        if (e.type === "keydown" && e.key === "Enter") { e.preventDefault(); done(input.value); }
        if (e.type === "keydown" && e.key === "Escape") { e.preventDefault(); done(""); }
      };
      input.addEventListener("keydown", stop); input.addEventListener("keyup", stop); input.addEventListener("keypress", stop);
      ["touchstart", "touchend", "mousedown", "mouseup", "pointerdown", "pointerup"].forEach(function (t) {
        box.addEventListener(t, function (e) { e.stopPropagation(); });
      });
      Module.isChat = c;
    }
    var chat = Module.isChat;
    chat.result = null;
    chat.input.value = "";
    chat.input.placeholder = ph;
    chat.box.style.display = "flex";
    setTimeout(function () { try { chat.input.focus(); } catch (err) {} }, 30);
  },

  IS_ChatPoll: function () {
    var c = Module.isChat;
    if (!c || c.result === null) return null;
    var s = c.result;
    c.result = null;
    var n = lengthBytesUTF8(s) + 1;
    var buf = _malloc(n);
    stringToUTF8(s, buf, n);
    return buf;
  },

  IS_ChatClose: function () {
    var c = Module.isChat;
    if (!c) return;
    c.result = null;
    c.box.style.display = "none";
    c.input.blur();
  },

  // Thai / English (Lang.cs): remembered for the page around the game (index.html), which
  // swaps its own words (loading line, full-screen button, what's new) when it hears it.
  IS_SetLang: function (langPtr) {
    var lang = UTF8ToString(langPtr) === "en" ? "en" : "th";
    Module.isLang = lang;
    try { localStorage.setItem("islandLang", lang); } catch (e) {}
    try { if (typeof window.islandSetLang === "function") window.islandSetLang(lang); } catch (e) {}
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
