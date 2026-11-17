using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// Online play: players on the same island share it. The relay server (Server/server.js)
/// passes messages around; one player per island is the "host" whose game runs the
/// creatures. Everyone gets the same island layout from the server's world seed, and
/// harvesting / building is broadcast so the island stays the same for everybody.
public partial class IslandGame
{
    NetLink link;
    public int myId;          // 0 = offline
    int worldSeed, hostId, enemyCounter, genCounter, localCounter;
    bool inRoom, helloSent, connecting, genPhase;
    float netSendT, enemySendT, clockSendT, connectStart;
    string netStatus = "";
    public readonly Dictionary<int, RemotePlayer> remotes = new Dictionary<int, RemotePlayer>();
    readonly Dictionary<int, WorldNode> nodeById = new Dictionary<int, WorldNode>();

    // title screen (character creator) & chat
    public bool title = true;
    string playerName = "";
    /// The character's appearance (CharacterLook.Code), chosen on the title screen.
    public int lookCode;
    CharacterLook look;
    int previewDir;
    public bool chatOpen;
    string chatText = "";
    float chatOpenedAt, myChatUntil;
    string myChat;
    readonly List<(string who, string text, float t)> chatLog = new List<(string, string, float)>();

    public bool Online => link != null && myId > 0;
    bool Shared => Online && inRoom;                        // other players may be on this island
    public bool IsHost => atHome ? !visiting : !Shared || hostId == myId; // offline you run everything yourself; a private island is its owner's
    public bool InputBlocked => Busy || chatOpen;

    /// Island layouts are the same for everyone online: derived from the server's world seed.
    int SeedFor(int islandNumber)
    {
        unchecked
        {
            uint h = (uint)worldSeed * 2654435761u ^ (uint)islandNumber * 40503u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            return (int)(h & 0x7fffffff) | 1;
        }
    }

    // ------------------------------------------------------------------ title screen
    void InitTitle()
    {
        title = true;
        playerName = PlayerPrefs.GetString("playerName", "");
        if (string.IsNullOrEmpty(playerName)) playerName = "ชาวเกาะ" + Random.Range(100, 1000);
        look = PlayerPrefs.HasKey("look") ? CharacterLook.FromCode(PlayerPrefs.GetInt("look")) : CharacterLook.CanChoose ? CharacterLook.Random() : CharacterLook.Default;
        if (!CharacterLook.CanChoose && look.preset == 0) look = CharacterLook.Default; // ready-made characters only (for now)
        lookCode = look.Code;
    }

    /// Remember name and look for next time.
    void KeepCharacter()
    {
        playerName = CleanName(playerName);
        lookCode = look.Code;
        PlayerPrefs.SetString("playerName", playerName);
        PlayerPrefs.SetInt("look", lookCode);
        PlayerPrefs.Save();
    }

    void PlayOnline()
    {
        KeepCharacter();
        link = NetLink.Open(NetLink.ServerUrl());
        helloSent = false;
        if (pendingAuth == null && SavedToken.Length > 0 && SavedUser.Length > 0) pendingAuth = "token"; // this device is logged in (IslandGame.Account.cs)
        authSent = false;
        connecting = true;
        connectStart = Time.time;
        netStatus = "กำลังเชื่อมต่อ...";
    }

    void PlayOffline()
    {
        KeepCharacter();
        pendingAuth = null;
        if (link != null) { link.Close(); link = null; }
        connecting = false;
        myId = 0;
        title = false;
        StartGame(SaveSystem.Read());
    }

    static string CleanName(string n)
    {
        n = (n ?? "").Trim();
        if (n.Length > 16) n = n.Substring(0, 16);
        return n.Length == 0 ? "ชาวเกาะ" : n;
    }

    void UpdateTitle()
    {
        NetPoll();
        if (!connecting || link == null) return;
        float waited = Time.time - connectStart;
        if (link.Connected && !helloSent) { Send(new NetMsg { t = "hello", name = playerName }); helloSent = true; }
        if (myId > 0) // welcome arrived
        {
            if (TitleAuthStep()) return; // logging in first (IslandGame.Account.cs)
            connecting = false;
            title = false;
            StartGame(SaveSystem.Read());
            return;
        }
        if (link.Dead && waited < 15f) { link = NetLink.Open(NetLink.ServerUrl()); helloSent = false; } // tunnel may still be starting
        if (waited > 4f) netStatus = $"กำลังเชื่อมต่อเซิร์ฟเวอร์... ({waited:0} วิ)";
        if (waited > 20f)
        {
            link.Close();
            link = null;
            connecting = false;
            netStatus = "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ (เซิร์ฟเวอร์อาจปิดอยู่) ลองใหม่อีกครั้ง หรือเล่นออฟไลน์";
        }
    }

    // ------------------------------------------------------------------ connection
    void Send(NetMsg m)
    {
        if (link != null && link.Connected) link.Send(JsonUtility.ToJson(m));
    }

    void NetPoll()
    {
        if (link == null) return;
        int budget = 200;
        while (budget-- > 0 && link.TryReceive(out string text))
        {
            NetMsg m;
            try { m = JsonUtility.FromJson<NetMsg>(text); } catch { continue; }
            if (m != null) Handle(m);
        }
        if (link.Dead && myId > 0) OnDisconnected();
    }

    void OnDisconnected()
    {
        link = null;
        myId = 0;
        inRoom = false;
        serverHomes = false;
        ClearRemotes();
        VisitDisconnected(); // visiting a friend: back home
        foreach (var e in enemies) if (e != null && e.puppet) Destroy(e.gameObject);
        enemies.RemoveAll(e => e == null || e.puppet);
        if (!title) Hint("หลุดจากเซิร์ฟเวอร์ - ตอนนี้กำลังเล่นออฟไลน์", 5f);
    }

    void JoinRoom()
    {
        ClearRemotes();
        inRoom = false;
        hostId = 0;
        if (atHome && !serverHomes) { if (Online) Send(new NetMsg { t = "leave" }); return; } // an older server has no rooms for private islands: you're alone at home
        if (Online) Send(new NetMsg { t = "join", island = RoomKey }); // a private island has a room of its own (IslandGame.Visit.cs)
    }

    void LeaveRoom()
    {
        if (Online) Send(new NetMsg { t = "leave" });
        inRoom = false;
        ClearRemotes();
    }

    void ClearRemotes()
    {
        foreach (var r in remotes.Values) if (r != null) Destroy(r.gameObject);
        remotes.Clear();
    }

    RemotePlayer AddRemote(int id, string name)
    {
        if (remotes.TryGetValue(id, out var r) && r != null) return r;
        r = new GameObject("Remote " + name).AddComponent<RemotePlayer>();
        r.transform.SetParent(world);
        r.Init(id, name);
        remotes[id] = r;
        return r;
    }

    void SetHost(int h)
    {
        bool was = IsHost;
        hostId = h;
        if (!was && IsHost)
        {
            // take over the creatures we could see
            foreach (var e in enemies) if (e != null && e.puppet) { e.puppet = false; enemyCounter = Mathf.Max(enemyCounter, e.netId); }
            Hint("ตอนนี้คุณเป็นคนดูแลสัตว์บนเกาะนี้", 3f);
        }
    }

    // ------------------------------------------------------------------ messages in
    void Handle(NetMsg m)
    {
        switch (m.t)
        {
            case "welcome":
                myId = m.id;
                worldSeed = m.worldSeed;
                serverHomes = false;
                serverWanted = false; bountyDirty = true; // the board of wanted posters gets my bounty (IslandGame.Bounty.cs)
                Send(new NetMsg { t = "homes" }); // does this server know private islands? (an older one stays quiet)
                break;

            case "room":
                if (m.island != RoomKey || voyage != null) break;
                inRoom = true;
                SetHost(m.host);
                if (m.players != null) foreach (var p in m.players) AddRemote(p.id, p.name);
                if (m.events != null) foreach (var e in m.events) ApplyEvent(e);
                int others = m.players?.Length ?? 0;
                if (others > 0) Hint($"มีผู้เล่นอื่นบนเกาะนี้ {others} คน! กด Enter เพื่อแชต", 5f);
                if (others > 0 && atHome && !visiting) SendFullHome(0); // friends already here see my island as it is now
                break;

            case "joined":
                if (!inRoom) break;
                AddRemote(m.from, m.name);
                AddChat("", $"{m.name} มาถึงเกาะแล้ว");
                if (IsHost) { clockSendT = 0f; enemySendT = 0f; }
                if (atHome && !visiting) SendFullHome(m.from); // a visitor: show them my island exactly as it is
                break;

            case "left":
                if (remotes.TryGetValue(m.from, out var gone))
                {
                    AddChat("", $"{gone.playerName} ออกจากเกาะไปแล้ว");
                    if (gone != null) Destroy(gone.gameObject);
                    remotes.Remove(m.from);
                }
                break;

            case "host":
                SetHost(m.host);
                break;

            // visiting private islands (IslandGame.Visit.cs)
            case "homes": OnHomes(m.players); break;
            case "visit": OnVisitData(m); break;
            case "knock": OnKnock(m); break;
            case "crew": OnCrew(m); break;     // crews sailing together (IslandGame.Crew.cs)
            case "auth": OnAuth(m); break;       // logged in (IslandGame.Account.cs)
            case "hnode": OnHomeNodes(m); break;   // private island changes (IslandGame.HomeSync.cs)
            case "hgift": OnHomeGift(m); break;    // a friend harvested on my farm
            case "wanted": OnWanted(m.players); break; // the board of wanted posters (IslandGame.Bounty.cs)

            case "p":
                if (inRoom) AddRemote(m.from, m.name).Apply(m);
                break;

            case "node":
                ApplyEvent(new NetEvent { id = m.id, ev = m.ev, dur = m.dur, hits = m.hits });
                break;

            case "spawn":
                ApplyEvent(new NetEvent { id = m.id, ev = "spawn", kind = m.kind, x = m.x, y = m.y });
                break;

            case "en":
                if (!IsHost) SyncPuppets(m.en);
                break;

            case "hit": // someone hit one of my creatures (I'm the host)
                if (!IsHost) break;
                var target = enemies.Find(e => e != null && !e.puppet && e.netId == m.id);
                if (target == null) break;
                Vector2 from = remotes.TryGetValue(m.from, out var hitter) && hitter != null ? hitter.grid : target.grid;
                Vector3 at = target.transform.position;
                var kind = target.kind;
                int xpGain = target.xp;
                if (target.Hit(Mathf.RoundToInt(m.dmg), from))
                {
                    enemies.Remove(target);
                    Send(new NetMsg { t = "kill", id = m.id, by = m.from, kind = (int)kind, hits = xpGain, x = at.x, y = at.y });
                    BossDown(kind); // a friend finished it, but the island is calm for me too
                }
                break;

            case "kill":
                var dead = enemies.Find(e => e != null && e.netId == m.id);
                Vector3 where = dead != null ? dead.transform.position : new Vector3(m.x, m.y, 0f);
                if (dead != null)
                {
                    Fx.Burst(where + Vector3.up * 0.15f, new Color(1f, 0.5f, 0.3f), 8);
                    enemies.Remove(dead);
                    Destroy(dead.gameObject);
                }
                if (m.by == myId) GrantKill((EnemyKind)m.kind, m.hits, where); // my blow finished it
                else BossDown((EnemyKind)m.kind);                                // someone else's: still counts for the island
                break;

            case "dmg":
                if (m.to == myId) DamagePlayer(m.dmg, m.why);
                break;

            case "rev": // a friend brought me back (IslandGame.Revive.cs)
                if (m.to == myId) Revived(m.from);
                break;

            case "clock":
                if (!IsHost && Mathf.Abs(clock - m.clock) > 1.5f) clock = m.clock;
                break;

            case "chat":
                string who = remotes.TryGetValue(m.from, out var talker) && talker != null ? talker.playerName : "?";
                AddChat(who, m.text);
                if (talker != null) { talker.chat = m.text; talker.chatUntil = Time.time + 6f; }
                Sfx.Play("pickup", 0.2f);
                break;
        }
    }

    // ------------------------------------------------------------------ messages out (every frame on an island)
    void NetTick(float dt)
    {
        if (!Shared || player == null) return;
        netSendT -= dt;
        if (netSendT <= 0f)
        {
            netSendT = 0.1f;
            Send(new NetMsg
            {
                t = "p", x = player.grid.x, y = player.grid.y, d = player.Dir, f = player.Frame, fl = player.Flip ? 1 : 0,
                hat = hat.HasValue ? (int)hat.Value + 1 : 0, outfit = outfit.HasValue ? (int)outfit.Value + 1 : 0,
                lv = level, name = playerName, look = lookCode, pet = ActivePetSave != null ? ActivePetSave.code + 1 : 0,
                dn = downed ? 1 : 0,
            });
        }
        if (!IsHost || remotes.Count == 0) return;
        enemySendT -= dt;
        if (enemySendT <= 0f)
        {
            enemySendT = 0.12f;
            var list = new List<NetEnemy>();
            foreach (var e in enemies)
                if (e != null && !e.puppet)
                    list.Add(new NetEnemy { id = e.netId, k = (int)e.kind, x = e.grid.x, y = e.grid.y, hp = e.hp, mhp = e.maxHp, fl = e.Flip ? 1 : 0 });
            Send(new NetMsg { t = "en", en = list.ToArray() });
        }
        clockSendT -= dt;
        if (clockSendT <= 0f)
        {
            clockSendT = 3f;
            Send(new NetMsg { t = "clock", clock = clock });
        }
    }

    // ------------------------------------------------------------------ shared world changes
    int NewLocalId() => atHome && !visiting ? ++homeIdNext : myId * 1000000 + 500000 + ++localCounter; // home ids are kept in the save

    void SendNode(WorldNode n, string ev, float dur = 0f)
    {
        if (Shared && !atHome) Send(new NetMsg { t = "node", id = n.id, ev = ev, dur = dur, hits = n.hits }); // private islands aren't shared
    }

    void SendSpawn(WorldNode n)
    {
        if (Shared && !atHome) Send(new NetMsg { t = "spawn", id = n.id, kind = (int)n.kind, x = n.grid.x, y = n.grid.y });
    }

    void DepleteShared(WorldNode n, float dur) { n.Deplete(dur); SendNode(n, "deplete", dur); }
    void RemoveShared(WorldNode n) { SendNode(n, "remove"); RemoveNode(n); }
    void StumpShared(WorldNode n, float dur) { n.BecomeStump(dur); SendNode(n, "stump", dur); }

    void ApplyEvent(NetEvent e)
    {
        if (e.ev == "spawn")
        {
            if (nodeById.ContainsKey(e.id)) return;
            var n = Spawn((NodeKind)e.kind, new Vector2(e.x, e.y), false, e.id);
            if (e.age < 1f) Fx.Burst(n.transform.position, new Color(0.8f, 0.7f, 0.5f), 8, 1f, 0.8f);
            return;
        }
        if (!nodeById.TryGetValue(e.id, out var node) || node == null) return;
        float left = e.dur - e.age;
        switch (e.ev)
        {
            case "deplete": if (left > 0f && node.available) { node.Deplete(left); if (node.kind == NodeKind.DigSpot) CheckTreasure(false); } break;
            case "remove": RemoveNode(node); break;
            case "stump": if (node.kind == NodeKind.Tree && left > 0f) node.BecomeStump(left); break;
            case "hits": node.hits = Mathf.Max(node.hits, e.hits); node.Shake(); break;
            case "set": // a mirror turned / a rod mended: take the exact value
                node.hits = e.hits;
                node.Refresh();
                if (node.kind == NodeKind.Mirror) CheckVault(false);
                if (node.kind == NodeKind.LightningRod) CheckRods(false);
                break;
        }
    }

    /// A boss fell on this island: everyone here gets the calm island (and the quest step),
    /// not just whoever landed the last blow.
    void BossDown(EnemyKind kind)
    {
        string f = kind switch
        {
            EnemyKind.AntQueen => "queen",
            EnemyKind.Thunderbird => "thunderbird",
            EnemyKind.Captain => "captain",
            EnemyKind.Kraken => "kraken",
            _ => null,
        };
        if (f == null || Flag(f + island)) return;
        flags.Add(f + island);
        Hint(kind switch
        {
            EnemyKind.AntQueen => "ราชินีมดพ่ายแพ้! สวนดอกไม้ปลอดภัยแล้ว",
            EnemyKind.Thunderbird => "นกอัสนีร่วงลงแล้ว! พายุเบาลง",
            EnemyKind.Captain => "กัปตันกระดูกดำแตกเป็นผง! ปืนใหญ่เงียบลงแล้ว",
            _ => "อสูรหมึกยักษ์พ่ายแพ้! วิหารบาดาลเปิดแล้ว ไปบอก ดร.สมุทร",
        }, 6f);
    }

    // ------------------------------------------------------------------ shared creatures
    /// Closest living player to a spot: 0 = this player, otherwise a remote player's id.
    public (Vector2 grid, int who) NearestPlayer(Vector2 from)
    {
        // invisible (Shadow power): creatures can't find you
        Vector2 best = player != null && !Invisible ? player.grid : from + new Vector2(999f, 999f);
        int who = 0;
        float bd = player != null && !dead && !Invisible ? (best - from).sqrMagnitude : float.MaxValue;
        foreach (var r in remotes.Values)
        {
            if (r == null || r.downed) continue; // creatures leave a knocked-out player alone
            float d = (r.grid - from).sqrMagnitude;
            if (d < bd) { bd = d; best = r.grid; who = r.netId; }
        }
        return (best, who);
    }

    public void NetDamage(int who, float amount, string why) => Send(new NetMsg { t = "dmg", to = who, dmg = amount, why = why });

    void SyncPuppets(NetEnemy[] list)
    {
        var alive = new HashSet<int>();
        if (list != null)
            foreach (var s in list)
            {
                alive.Add(s.id);
                var e = enemies.Find(x => x != null && x.netId == s.id);
                if (e == null)
                {
                    e = new GameObject(((EnemyKind)s.k).ToString()).AddComponent<Enemy>();
                    e.transform.SetParent(world);
                    e.Init((EnemyKind)s.k, new Vector2(s.x, s.y), tier, s.id, true);
                    enemies.Add(e);
                }
                e.SetNetState(new Vector2(s.x, s.y), s.hp, s.mhp, s.fl == 1);
            }
        foreach (var e in enemies)
            if (e != null && e.puppet && !alive.Contains(e.netId)) Destroy(e.gameObject); // despawned on the host
        enemies.RemoveAll(e => e == null || (e.puppet && !alive.Contains(e.netId)));
    }

    // ------------------------------------------------------------------ chat
    void AddChat(string who, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        chatLog.Add((who, text, Time.time));
        if (chatLog.Count > 8) chatLog.RemoveAt(0);
    }

    void OpenChat()
    {
        chatOpen = true;
        chatText = "";
        chatOpenedAt = Time.time;
    }

    void SubmitChat()
    {
        string text = (chatText ?? "").Trim();
        if (text.Length > 80) text = text.Substring(0, 80);
        chatOpen = false;
        chatText = "";
        if (text.Length == 0 || !Shared) return;
        Send(new NetMsg { t = "chat", text = text });
        AddChat(playerName, text);
        myChat = text;
        myChatUntil = Time.time + 6f;
    }

    void HandleChatKeys(Keyboard kb)
    {
        if (kb == null || chatOpen || !Shared) return;
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) OpenChat();
    }
}
