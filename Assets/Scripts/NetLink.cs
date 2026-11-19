using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

/// The game's line to the relay server (Server/server.js) over a WebSocket:
/// desktop/editor use .NET's ClientWebSocket, the web build the browser's WebSocket
/// (Plugins/WebGL/IslandNet.jslib). Text messages only, polled from the main thread.
/// (Adapted from PixelLeap2D.)
public abstract class NetLink
{
    public abstract bool Connected { get; }
    /// True once it gave up (closed or failed): make a new one to retry.
    public abstract bool Dead { get; }
    public abstract void Send(string text);
    public abstract bool TryReceive(out string text);
    public abstract void Close();

    /// The game server runs on the PC that started Server/start-online.bat. Its address changes every
    /// start (a free Cloudflare tunnel), so that script publishes it in this file on GitHub Pages.
    public const string LinkFileUrl = "https://gusttgustt.github.io/pixelleap2d/server.txt";
    /// Last resort: a server on this same computer.
    public const string LocalUrl = "ws://127.0.0.1:8787/ws";

    public static NetLink Open(string url)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return new BrowserLink(url);
#else
        return new DotNetLink(url);
#endif
    }

    /// Where the server is: "-server <url>" on the command line; on the web "?server=<url>"
    /// in the page address (index.html adds it from server.txt), else the page's own host when
    /// that's our server (a tunnel or a local server.js). Desktop otherwise reads server.txt;
    /// last resort a server on this computer.
    public static string ServerUrl()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string page = Application.absoluteURL ?? "";
        try
        {
            var u = new Uri(page);
            // only the real "server" setting in the query (not "myserver=" or one after #), and only
            // a server of ours: anything else could send the player's password to someone else's machine
            foreach (string part in u.Query.TrimStart('?').Split('&'))
                if (part.StartsWith("server=", StringComparison.Ordinal))
                {
                    string v = Uri.UnescapeDataString(part.Substring(7));
                    if (AllowedServer(v, u.Host)) return v;
                    break;
                }
            bool ours = u.Host.EndsWith("trycloudflare.com") || u.Host == "localhost" || u.Host == "127.0.0.1";
            if (ours) return (u.Scheme == "https" ? "wss://" : "ws://") + u.Authority + "/ws";
        }
        catch { }
        return LocalUrl; // the page (index.html) normally adds ?server= from server.txt
#else
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "-server") return args[i + 1];
        return PublishedUrl() ?? LocalUrl;
#endif
    }

    /// A server the game may talk to: a Cloudflare tunnel (wss://<name>.trycloudflare.com/ws) or a
    /// server.js on the same computer / network as the page (pageHost).
    public static bool AllowedServer(string url, string pageHost)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var s) || s.AbsolutePath != "/ws" || !string.IsNullOrEmpty(s.UserInfo)) return false;
        if (s.Scheme == "wss" && s.Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase)) return true;
        return (s.Scheme == "ws" || s.Scheme == "wss") && string.Equals(s.Host, pageHost, StringComparison.OrdinalIgnoreCase);
    }

#if !(UNITY_WEBGL && !UNITY_EDITOR)
    static string published;
    static float publishedAt = -999f;

    /// The address in server.txt (asked again at most every 30 seconds), or null.
    static string PublishedUrl()
    {
        if (Time.realtimeSinceStartup - publishedAt < 30f) return published;
        publishedAt = Time.realtimeSinceStartup;
        try
        {
            using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) })
            {
                string t = http.GetStringAsync(LinkFileUrl + "?t=" + DateTime.UtcNow.Ticks).Result.Trim();
                published = AllowedServer(t, "127.0.0.1") ? t : null;
            }
        }
        catch { published = null; }
        return published;
    }
#endif

#if !(UNITY_WEBGL && !UNITY_EDITOR)
    /// Desktop / editor: ClientWebSocket with a receive loop and a send loop on the thread pool.
    sealed class DotNetLink : NetLink
    {
        readonly System.Net.WebSockets.ClientWebSocket _ws = new System.Net.WebSockets.ClientWebSocket();
        readonly ConcurrentQueue<string> _in = new ConcurrentQueue<string>();
        readonly BlockingCollection<string> _out = new BlockingCollection<string>(new ConcurrentQueue<string>(), 512);
        readonly CancellationTokenSource _stop = new CancellationTokenSource();
        volatile bool _open, _dead;

        public DotNetLink(string url) { Run(url); }

        async void Run(string url)
        {
            try
            {
                await _ws.ConnectAsync(new Uri(url), _stop.Token);
                _open = true;
                SendLoop();
                var buf = new byte[64 * 1024];
                var msg = new System.IO.MemoryStream();
                while (_ws.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _stop.Token);
                    if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                    msg.Write(buf, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    _in.Enqueue(Encoding.UTF8.GetString(msg.GetBuffer(), 0, (int)msg.Length));
                    msg.SetLength(0);
                }
            }
            catch { }
            _open = false;
            _dead = true;
            _out.CompleteAdding();
        }

        async void SendLoop()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    string s = await System.Threading.Tasks.Task.Run(() => _out.Take(_stop.Token));
                    var data = Encoding.UTF8.GetBytes(s);
                    await _ws.SendAsync(new ArraySegment<byte>(data), System.Net.WebSockets.WebSocketMessageType.Text, true, _stop.Token);
                }
            }
            catch { }
        }

        public override bool Connected => _open;
        public override bool Dead => _dead;

        public override void Send(string text)
        {
            if (!_open) return;
            try { _out.TryAdd(text); } catch { } // full or closed: drop it (positions are resent constantly)
        }

        public override bool TryReceive(out string text) => _in.TryDequeue(out text);

        public override void Close()
        {
            _open = false;
            _dead = true;
            try { _stop.Cancel(); } catch { }
            try { _ws.Abort(); } catch { }
            try { _ws.Dispose(); } catch { }
        }
    }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
    /// Web build: the browser's WebSocket (IslandNet.jslib).
    sealed class BrowserLink : NetLink
    {
        [DllImport("__Internal")] static extern int IS_WsOpen(string url);
        [DllImport("__Internal")] static extern int IS_WsState(int h); // 0 connecting, 1 open, 2 closed
        [DllImport("__Internal")] static extern void IS_WsSend(int h, string text);
        [DllImport("__Internal")] static extern string IS_WsRecv(int h);
        [DllImport("__Internal")] static extern void IS_WsClose(int h);

        readonly int _h;
        public BrowserLink(string url) { _h = IS_WsOpen(url); }
        public override bool Connected => IS_WsState(_h) == 1;
        public override bool Dead => IS_WsState(_h) == 2;
        public override void Send(string text) { if (Connected) IS_WsSend(_h, text); }
        public override bool TryReceive(out string text)
        {
            text = IS_WsRecv(_h);
            return text != null;
        }
        public override void Close() => IS_WsClose(_h);
    }
#endif
}

/// One message to / from the server. JsonUtility fills only the fields a message uses.
[Serializable]
public class NetMsg
{
    public string t;               // type
    public int from, to;           // sender (set by the server) / single receiver (0 = whole island)
    public int id, host, island, worldSeed;
    public string name, text, ev, why;
    public string room, pets;      // a private island sent for visitors (IslandGame.Visit.cs)
    public float x, y, clock, dur, dmg;
    public int d, f, fl, hat, outfit, lv, hits, kind, by, hp, look, pet;
    public int dn;                 // 1 = this player is knocked out, waiting to be revived (IslandGame.Revive.cs)
    public int ver;                // the account save's version (auth / save / saved / saveold, IslandGame.Account.cs)
    public int aw;                 // awakened fruit power (IslandGame.Awaken.cs): 0 = none, else (int)Power
    public int wp;                 // the weapon in hand + 1 (0 = bare hands), drawn when they attack (WeaponSwing.cs)
    public NetEnemy[] en;
    public NetEvent[] events;
    public NetPeer[] players;
}

[Serializable] public class NetEnemy { public int id, k, hp, mhp, fl; public float x, y; }
[Serializable] public class NetEvent { public int id, kind, hits; public string ev; public float dur, x, y, age; }
[Serializable] public class NetPeer { public int id; public string name; public int c; public int b, lk; public int isl; } // c: the continent of their private island (on the wanted board: 1 = you); b: bounty, lk: look
