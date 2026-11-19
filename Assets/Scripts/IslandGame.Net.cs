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
        LoadChannel(); // the channel picked last time (IslandGame.Channel.cs)
        // logged in on this device: straight to its characters (kept here, so they show offline too)
        guestMode = false; charId = null; SaveSystem.Profile = "";
        acctUser = SavedUser;
        oldCharName = PlayerPrefs.GetString("playerName", ""); // what an older character was called on this device
        MoveOldAccountSave(); // IslandGame.Chars.cs
        if (SavedToken.Length > 0 && SavedUser.Length > 0)
        {
            LoadCachedChars(SavedUser);
            OldSaveAsFirstChar(SavedUser);
            titleScreen = TitleScreen.Chars;
            TitleConnect("token");
        }
        else titleScreen = TitleScreen.Login;
    }

    /// Remember name and look for next time.
    void KeepCharacter()
    {
        playerName = CleanName(playerName);
        Lang.Name(playerName); // my own name stays as I typed it in English too
        lookCode = look.Code;
        PlayerPrefs.SetString("playerName", playerName);
        PlayerPrefs.SetInt("look", lookCode);
        PlayerPrefs.Save();
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
            if (guestMode) { title = false; StartGame(SaveSystem.Read()); } // no account: straight in
            return;
        }
        if (link.Dead && waited < 15f) { link = NetLink.Open(NetLink.ServerUrl()); helloSent = false; } // tunnel may still be starting
        if (waited > 4f) netStatus = $"กำลังเชื่อมต่อเซิร์ฟเวอร์... ({waited:0} วิ)";
        if (waited > 20f)
        {
            TitleConnectFailed(titleScreen == TitleScreen.Chars && chars != null
                ? "ต่อเซิร์ฟเวอร์ไม่ได้ - กด \"เล่นออฟไลน์\" เพื่อเล่นตัวนี้ในเครื่องนี้ หรือลองออนไลน์ใหม่อีกครั้ง"
                : "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ (เซิร์ฟเวอร์อาจปิดอยู่) ลองใหม่อีกครั้ง หรือเล่นออฟไลน์");
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
        // (a message may close the connection while it's handled, e.g. a refused login: link goes null)
        while (budget-- > 0 && link != null && link.TryReceive(out string text))
        {
            NetMsg m;
            try { m = JsonUtility.FromJson<NetMsg>(text); } catch { continue; }
            if (m != null) Handle(m);
        }
        if (link != null && link.Dead && myId > 0) OnDisconnected();
    }

    void OnDisconnected()
    {
        if (title) { TitleConnectFailed("หลุดจากเซิร์ฟเวอร์ - ลองใหม่อีกครั้ง หรือเล่นออฟไลน์"); return; }
        link = null;
        myId = 0;
        inRoom = false;
        serverHomes = false;
        ClearRemotes();
        VisitDisconnected(); // visiting a friend: back home
        foreach (var e in enemies) if (e != null && e.puppet) Destroy(e.gameObject);
        enemies.RemoveAll(e => e == null || e.puppet);
        if (StartReconnect()) Hint("หลุดจากเซิร์ฟเวอร์ - กำลังต่อใหม่...", 4f); // IslandGame.Reconnect.cs
        else if (!title) Hint(kickedWhy != null ? $"{kickedWhy} - เครื่องนี้เล่นออฟไลน์ต่อ (เซฟอยู่ในเครื่องนี้)" : "หลุดจากเซิร์ฟเวอร์ - ตอนนี้กำลังเล่นออฟไลน์", 6f);
        kickedWhy = null;
    }

    string kickedWhy; // the server is closing this connection, and why (the same account logged in elsewhere)

    void JoinRoom()
    {
        ClearRemotes();
        inRoom = false;
        hostId = 0;
        bossAskedHere.Clear(); // a new island: everyone may ask its host once (bossAskedAt keeps the minute)
        if (voyage != null || !atHome) ApplyPendingChannel(); // sailing to a friend in another channel (IslandGame.Channel.cs)
        if (atHome && !serverHomes && voyage == null) { if (Online) Send(new NetMsg { t = "leave" }); return; } // an older server has no rooms for private islands: you're alone at home
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

    RemotePlayer AddRemote(int id, string name, bool atSea = false)
    {
        if (remotes.TryGetValue(id, out var r) && r != null) return r;
        r = new GameObject("Remote " + name).AddComponent<RemotePlayer>();
        r.transform.SetParent(world);
        r.Init(id, name, atSea);
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
            SpawnBossesFor(BossesINeed(false)); // the old host may have beaten the boss I still have to fight
        }
    }

    // ---- bosses online: the host's game runs them, and the host only makes the ones it hasn't
    // beaten itself. So whoever lands after the host beat it would find no boss and be stuck on its
    // quest: on joining, each player tells the host which bosses it still needs (sent as a "hit"
    // with ev "need", a kind the server already passes on), and the host brings those back.
    const int NeedKing = 1, NeedQueen = 2, NeedThunderbird = 4, NeedCaptain = 8, NeedKraken = 16, NeedContinent = 64;

    /// The bosses on this island I haven't beaten (and, landing on a continent's last island, its boss: it's always back).
    int BossesINeed(bool landing)
    {
        if (atHome || inCave || voyage != null) return 0;
        int n = 0;
        if (biome == Biome.Mushroom && !Flag("king" + island)) n |= NeedKing;
        if (biome == Biome.Insect && !Flag("queen" + island)) n |= NeedQueen;
        if (biome == Biome.Storm && !Flag("thunderbird" + island)) n |= NeedThunderbird;
        if (biome == Biome.Pirate && !Flag("captain" + island)) n |= NeedCaptain;
        if (biome == Biome.Sunken && !Flag("kraken" + island)) n |= NeedKraken;
        if (landing && OnBossIsland) n |= NeedContinent;
        return n;
    }

    void SendBossNeeds()
    {
        if (IsHost || !Shared) return;
        int n = BossesINeed(true);
        if (n != 0) Send(new NetMsg { t = "hit", to = hostId, id = 0, ev = "need", dmg = n });
    }

    // A changed game could keep asking, to have the continent boss back again and again for its
    // materials without ever sailing away. So the host answers each player once per visit (until
    // they leave the island), and never sooner than a minute after the last time for them.
    const float BossAskEvery = 60f;
    readonly HashSet<int> bossAskedHere = new HashSet<int>();
    readonly Dictionary<int, float> bossAskedAt = new Dictionary<int, float>();

    /// The host: a player on this island asked for the bosses it still needs.
    void BossNeedFrom(int who, int need)
    {
        if (!IsHost || !remotes.ContainsKey(who) || bossAskedHere.Contains(who)) return;
        if (bossAskedAt.TryGetValue(who, out float at) && Time.unscaledTime - at < BossAskEvery) return;
        bossAskedHere.Add(who);
        bossAskedAt[who] = Time.unscaledTime;
        SpawnBossesFor(need);
    }

    /// The host: bring back the bosses someone here still needs (each only if it isn't here already).
    void SpawnBossesFor(int need)
    {
        if (!IsHost || need == 0 || atHome || inCave || voyage != null) return;
        if ((need & NeedKing) != 0) SpawnBoss(true);
        if ((need & NeedQueen) != 0) SpawnQueen(true);
        if ((need & NeedThunderbird) != 0) SpawnThunderbird(true);
        if ((need & NeedCaptain) != 0) SpawnCaptain(true);
        if ((need & NeedKraken) != 0) SpawnKraken(true);
        if ((need & NeedContinent) != 0 && ContinentBossHere == null) SpawnContinentBoss();
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
                if (reconnecting) ReconnectWelcomed(); // back online after a drop (IslandGame.Reconnect.cs)
                break;

            case "room":
                if (m.island != RoomKey) break;
                inRoom = true;
                if (voyage != null) // at sea: the other boats sailing these waters (no creatures or world to share)
                {
                    if (m.players != null) foreach (var p in m.players) AddRemote(p.id, p.name, true);
                    int boats = m.players?.Length ?? 0;
                    if (boats > 0) Hint($"มีเรือลำอื่นแล่นอยู่ในทะเลนี้ {boats} ลำ! {KeyOr("กด Enter", "แตะปุ่ม \"แชต\"")} เพื่อแชต", 4f);
                    break;
                }
                SetHost(m.host);
                if (!IsHost)
                {
                    // Bosses and pack leaders were made while loading, before we knew who runs this
                    // island's creatures. Someone else does: drop our copies and show theirs.
                    foreach (var e in enemies) if (e != null && !e.puppet) Destroy(e.gameObject);
                    enemies.RemoveAll(e => e == null || !e.puppet);
                    bossSeen = false; // our continent boss made way for the host's: it didn't fall (IslandGame.ContinentBoss.cs)
                    SendBossNeeds();  // ... and the host may have beaten it already: ask for the ones I still need
                }
                if (m.players != null) foreach (var p in m.players) AddRemote(p.id, p.name);
                if (inCave) CaveRoomCheck(m); // someone else's group got into this cave first (IslandGame.Cave.cs)
                if (m.events != null) foreach (var e in m.events) ApplyEvent(e);
                int others = m.players?.Length ?? 0;
                if (others > 0) Hint($"มีผู้เล่นอื่นบนเกาะนี้ {others} คน! {KeyOr("กด Enter", "แตะปุ่ม \"แชต\"")} เพื่อแชต", 5f);
                if (others > 0 && atHome && !visiting && voyage == null) SendFullHome(0); // friends already here see my island as it is now
                break;

            case "joined":
                if (!inRoom) break;
                AddRemote(m.from, m.name, voyage != null);
                AddChat("", voyage != null ? $"เรือของ {m.name} แล่นเข้ามาในทะเลนี้" : $"{m.name} มาถึงเกาะแล้ว");
                if (voyage != null) break;
                if (IsHost) { clockSendT = 0f; enemySendT = 0f; }
                if (atHome && !visiting) SendFullHome(m.from); // a visitor: show them my island exactly as it is
                break;

            case "left":
                if (remotes.TryGetValue(m.from, out var gone))
                {
                    AddChat("", voyage != null ? $"เรือของ {gone.playerName} แล่นออกไปแล้ว" : $"{gone.playerName} ออกจากเกาะไปแล้ว");
                    if (gone != null) Destroy(gone.gameObject);
                    remotes.Remove(m.from);
                    bossAskedHere.Remove(m.from); // when they land again, the boss may come back for them
                }
                break;

            case "host":
                SetHost(m.host);
                SendBossNeeds(); // a new host: it may not have the boss I'm here for
                break;

            // visiting private islands (IslandGame.Visit.cs)
            case "homes": OnHomes(m.players); break;
            case "visit": OnVisitData(m); break;
            case "knock": OnKnock(m); break;
            case "crew": OnCrew(m); break;     // crews sailing together (IslandGame.Crew.cs)
            case "auth": OnAuth(m); break;       // logged in (IslandGame.Account.cs)
            case "chars": OnChars(m); break;     // the account's characters changed (IslandGame.Chars.cs)
            case "char": OnCharSave(m); break;   // the character picked, with its save
            case "hnode": OnHomeNodes(m); break;   // private island changes (IslandGame.HomeSync.cs)
            case "hgift": OnHomeGift(m); break;    // a friend harvested on my farm
            case "wanted": OnWanted(m.players); break; // the board of wanted posters (IslandGame.Bounty.cs)
            case "who": OnWho(m); break;           // everyone online and where (IslandGame.Who.cs)
            case "saved": OnSaved(m); break;       // the account save was taken (IslandGame.Account.cs)
            case "saveold": OnSaveOld(m); break;   // ... or refused: another device has newer progress
            case "kicked":
                kickedWhy = string.IsNullOrEmpty(m.why) ? "บัญชีนี้เข้าเล่นจากที่อื่นแล้ว" : m.why; acctName = null; // saves stop going to the account
                if (title) { TitleConnectFailed(kickedWhy); kickedWhy = null; } // on the title screen: say why (a ban, the server's rules)
                break;

            case "p":
                if (inRoom && voyage == null) AddRemote(m.from, m.name).Apply(m);
                break;

            case "sp": // a boat at sea: x = how far along its crossing (0..1), y = across
                if (inRoom && voyage != null)
                {
                    bool hide = m.hits == 1                                  // a passenger: they're on their captain's deck
                             || (crewRide && m.from == crewCaptain)          // my captain's boat is the one I'm on
                             || (IsCaptain && crewAboard.Contains(m.from));  // my crew stand on my deck
                    AddRemote(m.from, m.name, true).ApplySea(m, voyage.length, hide);
                }
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
                if (m.ev == "need") { BossNeedFrom(m.from, (int)m.dmg & 127); break; } // a player here still has to fight these
                var target = enemies.Find(e => e != null && !e.puppet && e.netId == m.id);
                if (target == null) break;
                if (m.ev == "catch") { CatchFromNet(target, m.from); break; } // a pet net (IslandGame.Pet.cs)
                int allowed = AllowHit(m.from, m.dmg);
                if (allowed <= 0) break;
                Vector2 from = remotes.TryGetValue(m.from, out var hitter) && hitter != null ? hitter.grid : target.grid;
                Vector3 at = target.transform.position;
                var kind = target.kind;
                int xpGain = target.xp;
                if (target.Hit(allowed, from))
                {
                    enemies.Remove(target);
                    Send(new NetMsg { t = "kill", id = m.id, by = m.from, kind = (int)kind, hits = xpGain, x = at.x, y = at.y });
                    BossDown(kind); // a friend finished it, but the island is calm for me too
                }
                break;

            case "kill":
                if (m.from != hostId) break; // only the host decides what died (anyone else: a cheat or a stale game)
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
                // a creature's bite comes from the host's game only, and never more than any creature can do
                if (m.to == myId && m.from == hostId && m.from != myId) DamagePlayer(Mathf.Clamp(m.dmg, 0f, MaxNetBite), m.why);
                break;

            case "rev": // a friend brought me back (IslandGame.Revive.cs)
                if (m.to == myId) Revived(m.from);
                break;

            case "clock":
                if (!IsHost && Mathf.Abs(clock - m.clock) > 1.5f) clock = m.clock;
                break;

            case "chat":
                m.text = CleanPlayerText(m.text, 80); // whatever the server let through: short, no rich-text tags
                if (m.text.Length == 0) break;
                // from 0 = the server itself (an announcement typed in the server window: "say")
                string who = m.from == 0 && !string.IsNullOrEmpty(m.name) ? CleanPlayerText(m.name, 20)
                           : remotes.TryGetValue(m.from, out var talker) && talker != null ? talker.playerName : "?";
                AddChat(who, m.text);
                if (m.from != 0 && remotes.TryGetValue(m.from, out var speaker) && speaker != null) { speaker.chat = m.text; speaker.chatUntil = Time.time + 6f; }
                Sfx.Play("pickup", 0.2f);
                break;
        }
    }

    // ------------------------------------------------------------------ messages out (every frame on an island)
    void NetTick(float dt)
    {
        if (!Shared) return;
        if (voyage != null) { SeaTick(dt); return; }
        if (player == null) return;
        netSendT -= dt;
        if (netSendT <= 0f)
        {
            netSendT = 0.1f;
            Send(new NetMsg
            {
                t = "p", x = player.grid.x, y = player.grid.y, d = player.Dir, f = player.Frame, fl = player.Flip ? 1 : 0,
                hat = hat.HasValue ? (int)hat.Value + 1 : 0, outfit = outfit.HasValue ? (int)outfit.Value + 1 : 0,
                lv = level, name = playerName, look = lookCode, pet = ActivePetSave != null ? ActivePetSave.code + 1 : 0,
                dn = downed ? 1 : 0, aw = Awakened ? (int)power : 0,
                wp = weapon != null && Count(weapon.Value) > 0 ? (int)weapon.Value + 1 : 0,
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

    /// At sea: tell the other boats in these waters where mine is (as a share of the crossing,
    /// so routes of different lengths still line up) and what it looks like.
    void SeaTick(float dt)
    {
        var v = voyage;
        netSendT -= dt;
        if (netSendT > 0f || v.raftSr == null) return;
        netSendT = 0.1f;
        Send(new NetMsg
        {
            t = "sp", x = v.length > 0f ? v.raft.x / v.length : 0f, y = v.raft.y, d = v.dir, f = v.vel.magnitude > 0.2f ? 1 + ((int)v.animT & 1) : 0,
            fl = v.playerSr != null && v.playerSr.flipX ? 1 : 0, kind = crewRide ? crewBoatLook : BoatLookNow, hits = crewRide ? 1 : 0,
            hat = hat.HasValue ? (int)hat.Value + 1 : 0, outfit = outfit.HasValue ? (int)outfit.Value + 1 : 0,
            lv = level, name = playerName, look = lookCode, aw = Awakened ? (int)power : 0,
        });
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
            EnemyKind.ShroomKing => "king", // the Mushroom shipwright's quest waits on it (IslandGame.Quest.cs)
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
            EnemyKind.ShroomKing => "ราชาเห็ดล้มแล้ว! ทางน้ำโล่งแล้ว กลับไปบอกช่างต่อเรือ",
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

    // ---- what other players' games may do to mine (the server relays; these checks keep a cheat in check)
    const float MaxNetBite = 2500f;    // more than the strongest boss bite (Void Titan alone, last island, ~250)
    const float MaxNetHit = 4000f;     // more than one awakened finishing move with the best gear (~2900)
    const float NetHitPerSec = 6500f;  // about the best a fully geared, awakened player deals to one creature
    readonly Dictionary<int, (float budget, float at)> hitBudget = new Dictionary<int, (float, float)>();

    /// How much of a hit from another player to apply: never more than one hit can do, nor more per
    /// second than the strongest player can, so a changed game can't one-shot a boss.
    int AllowHit(int from, float dmg)
    {
        if (!(dmg > 0f)) return 0;
        float now = Time.unscaledTime;
        if (!hitBudget.TryGetValue(from, out var b)) b = (NetHitPerSec, now);
        float budget = Mathf.Min(NetHitPerSec, b.budget + (now - b.at) * NetHitPerSec);
        float take = Mathf.Min(Mathf.Min(dmg, MaxNetHit), budget);
        hitBudget[from] = (budget - take, now);
        return Mathf.FloorToInt(take);
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
                    if (s.id == CaveBossId) e.MakeCaveBoss(); // big, like on the host (IslandGame.Cave.cs)
                    enemies.Add(e);
                }
                e.SetNetState(new Vector2(s.x, s.y), s.hp, s.mhp, s.fl == 1);
            }
        foreach (var e in enemies)
            if (e != null && e.puppet && !alive.Contains(e.netId)) Destroy(e.gameObject); // despawned on the host
        enemies.RemoveAll(e => e == null || (e.puppet && !alive.Contains(e.netId)));
    }

    // ------------------------------------------------------------------ chat
    /// Words from another player: at most `max` characters, without < > (rich-text tags would let one
    /// player resize or recolour everyone's screen) or control characters.
    static string CleanPlayerText(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var b = new System.Text.StringBuilder(Mathf.Min(s.Length, max));
        foreach (char c in s)
        {
            if (b.Length >= max) break;
            if (c == '<' || c == '>' || c < ' ' || c == '') continue;
            b.Append(c);
        }
        return b.ToString().Trim();
    }

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
