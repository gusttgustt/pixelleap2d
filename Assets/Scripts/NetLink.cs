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

    /// The public server (Render). The web build served by the server itself uses its own address.
    public const string PublicUrl = "wss://pixelleap.onrender.com/ws";

    public static NetLink Open(string url)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return new BrowserLink(url);
#else
        return new DotNetLink(url);
#endif
    }

    /// Where the server is: "-server <url>" on the command line; on the web "?server=<url>"
    /// in the page address, else the page's own host when that's our server (Render or a
    /// local server.js), else the public server (e.g. when played from GitHub Pages).
    public static string ServerUrl()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        string page = Application.absoluteURL ?? "";
        int q = page.IndexOf("server=", StringComparison.Ordinal);
        if (q >= 0)
        {
            string v = page.Substring(q + 7);
            int amp = v.IndexOf('&');
            return Uri.UnescapeDataString(amp >= 0 ? v.Substring(0, amp) : v);
        }
        try
        {
            var u = new Uri(page);
            bool ours = u.Host.EndsWith("onrender.com") || u.Host == "localhost" || u.Host == "127.0.0.1";
            if (ours) return (u.Scheme == "https" ? "wss://" : "ws://") + u.Authority + "/ws";
        }
        catch { }
        return PublicUrl;
#else
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "-server") return args[i + 1];
        return PublicUrl;
#endif
    }

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
    public float x, y, clock, dur, dmg;
    public int d, f, fl, hat, outfit, lv, hits, kind, by, hp;
    public NetEnemy[] en;
    public NetEvent[] events;
    public NetPeer[] players;
}

[Serializable] public class NetEnemy { public int id, k, hp, mhp, fl; public float x, y; }
[Serializable] public class NetEvent { public int id, kind, hits; public string ev; public float dur, x, y, age; }
[Serializable] public class NetPeer { public int id; public string name; }
