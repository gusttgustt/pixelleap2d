using System.Collections.Generic;
using UnityEngine;

/// Caves. Some adventure islands (from island 2, decided by the island's seed so everyone online
/// sees the same) have a cave mouth. Inside: a winding tunnel full of the island's creatures,
/// side rooms with ore and crystals, and a big boss in the hall at the far end. Beat it for
/// treasure (and a skill point the first time on each island); a way out opens beside it.
/// Too much? Walk back to the light at the mouth (or ☰ > leave the cave when nothing is close).
///
/// The cave works like the house (IslandGame.House.cs): StartGame builds it instead of the
/// island, the island outside waits in caveOutsideNodes, and saving while inside saves the island
/// outside with you at the cave mouth (so quitting inside puts you back outside, loot kept).
/// While inside, `biome` is Tropical (no island weather, wind, air or cold in the tunnels) and
/// caveBiome is the island's own.
///
/// Online the cave is a room of its own (CaveKey). One group at a time: you can only go in when
/// nobody is inside, or everyone inside is in your crew. The who's-online list says who's in
/// which room, so that's checked before going in; if two get in at the same moment, the later
/// one is sent back out (the "room" reply lists who was there first). Crew members tell each
/// other (crew relay, ev "cave"), and the captain sends the crew list to the crew ("roster") so
/// every member knows who counts as crew.
public partial class IslandGame
{
    public bool inCave;
    bool caveEntering;                    // StartGame: this load is the cave (any other load is outside)
    Biome caveBiome;                      // the island's own biome while inside
    Vector2 caveReturn;                   // in front of the cave mouth outside
    readonly List<NodeSave> caveOutsideNodes = new List<NodeSave>();
    int caveOutStick, caveOutRock, caveOutObsidian;
    Vector2 caveStart, caveBossSpot;
    readonly List<Vector2> caveFloor = new List<Vector2>();
    Vector2? caveLanding;                 // StartGame: stand here (just came out of the cave)
    bool caveBossDown, caveBossSeen, caveBounce;
    readonly HashSet<int> crewRoster = new HashSet<int>(); // as a crew member: the captain's whole crew
    float caveRosterT, caveAskAt = -1f;

    public const int CaveBossId = 990001;  // the boss's creature id (puppets know it's the boss by it)
    const int CaveRoomBase = 1000000000;   // cave rooms: CaveRoomBase + channel offset + WorldKey (islands stay below, the sea above)

    int CaveKey => InPrison ? PrisonKey : CaveRoomBase + ChannelOffset + WorldKey(island, continent); // a prison floor has its own (IslandGame.Prison.cs)
    static bool CaveRoom(int isl) => isl >= CaveRoomBase && isl < PrisonRoomBase;
    int CaveLevel => IslandLevel + 3; // the level the cave mouth recommends

    /// This island has a cave (its seed decides: the same for everyone online).
    bool HasCave => !atHome && island >= 2 && new System.Random(seed * 13 + 5).Next(100) < 60;

    // ------------------------------------------------------------------ the mouth, on the island
    /// Island generation (fresh only, last so every other id stays as it was).
    void PlaceCave()
    {
        if (!HasCave) return;
        var r = new System.Random(seed * 17 + 3);
        PlaceOnTile(NodeKind.CaveEntrance, 2, 7f, 17f, r, 1.6f);
    }

    /// Everyone counted as my crew: my crew if I'm the captain, my captain and their crew if I'm in one.
    HashSet<int> MyCrewIds()
    {
        var s = new HashSet<int>();
        foreach (var m in crewMates) s.Add(m.id);
        if (InCrew) { s.Add(crewCaptain); foreach (var id in crewRoster) if (id != myId) s.Add(id); }
        return s;
    }

    /// Someone not in my crew is in this island's cave (from the last who's-online list): their name.
    string CaveBlockedBy()
    {
        if (!Online || whoList == null) return null;
        var crew = MyCrewIds();
        foreach (var p in whoList)
            if (p != null && p.c != 1 && p.id != myId && p.isl == CaveKey && !crew.Contains(p.id)) return p.name;
        return null;
    }

    void CaveDialog(WorldNode n)
    {
        AskWho(); // fresher list for the next look
        var d = new Dialog { speaker = "ปากถ้ำ" };
        d.text = "ถ้ำมืดลึกลงไปใต้เกาะ ทางเดินเต็มไปด้วยสัตว์ร้าย และมี<color=#ff8a7a><b>เจ้าแห่งถ้ำ</b></color>ตัวใหญ่รออยู่ลึกสุด\n"
               + $"แนะนำเลเวล {CaveLevel} ขึ้นไป (ตอนนี้เลเวล {level}) · สู้ไม่ไหว เดินกลับมาที่แสงตรงปากถ้ำเพื่อออกได้";
        if (Online)
        {
            d.text += MyCrewIds().Count > 0
                ? "\n<color=#9dff9d>ลูกเรือของคุณเข้าตามไปด้วยกันได้</color>"
                : "\nเข้าคนเดียว ถ้ำจะปิดจนกว่าคุณจะออกมา <color=#cccccc>(อยากไปหลายคน ให้เข้าลูกเรือเดียวกันก่อน)</color>";
            string who = CaveBlockedBy();
            if (who != null) d.text += $"\n<color=#ff8a7a><b>ถ้ำปิดอยู่: {who} อยู่ข้างใน</b> รอให้ออกมาก่อน</color>";
        }
        d.options.Add(("เข้าถ้ำ", TryEnterCave, true));
        d.options.Add(("ปิด", CloseDialog, true));
        dialog = d;
        craftOpen = false; skillsOpen = false; bagOpen = false;
    }

    void TryEnterCave()
    {
        CloseDialog();
        if (!Online) { StartCaveWarp(); return; }
        caveAskAt = Time.unscaledTime; // a fresh who's-online list decides (CaveWhoHeard)
        whoAskAt = 0f;
        Send(new NetMsg { t = "who" });
        Hint("กำลังดูว่ามีใครอยู่ในถ้ำ...", 1.5f);
    }

    /// The who's-online list came back (IslandGame.Who.cs), or it took too long (TickCave).
    void CaveWhoHeard()
    {
        if (caveAskAt < 0f) return;
        caveAskAt = -1f;
        string who = CaveBlockedBy();
        if (who != null) { Hint($"ถ้ำปิดอยู่: {who} อยู่ข้างใน รอให้ออกมาก่อน (หรือขอเข้าลูกเรือเดียวกัน)", 4f); return; }
        StartCaveWarp();
    }

    void StartCaveWarp()
    {
        if (dead || Busy || homeT > 0f || player == null || voyage != null) return;
        if (aboard && InCrew) { aboard = false; SendCrew(crewCaptain, "unboard"); } // the captain's boat stays outside
        BeginWarp(Warp.EnterCave);
    }

    /// The fade is dark: build the cave.
    void EnterCave()
    {
        caveOutsideNodes.Clear();
        foreach (var n in nodes) if (n != null) caveOutsideNodes.Add(ToSave(n));
        caveOutStick = stickCap; caveOutRock = rockCap; caveOutObsidian = obsidianCap;
        var mouth = nodes.Find(n => n != null && n.kind == NodeKind.CaveEntrance);
        caveReturn = mouth != null ? mouth.grid + new Vector2(0.9f, 0.9f) : player.grid;
        var d = Snapshot();      // still outside: everything you carry
        d.fresh = true;          // the cave is built from the seed
        d.nodes.Clear();
        caveEntering = true;
        caveBossDown = false; caveBossSeen = false; caveBounce = false;
        StartGame(d);
        foreach (var id in MyCrewIds()) SendCrew(id, "cave", CaveKey); // "X went into the cave"
    }

    /// Back out in front of the cave mouth, the island as you left it.
    void ExitCave()
    {
        if (InPrison) { SurfaceFromPrison(); return; } // up from the sunken prison (IslandGame.Prison.cs)
        inCave = false;          // an ordinary snapshot from here on
        var d = Snapshot();
        d.nodes.Clear();
        d.nodes.AddRange(caveOutsideNodes);
        d.fresh = false;
        d.biomeId = (int)caveBiome + 1;
        d.stickCap = caveOutStick; d.rockCap = caveOutRock; d.obsidianCap = caveOutObsidian;
        d.player = caveReturn;
        caveLanding = caveReturn;
        StartGame(d);
    }

    /// Saving inside the cave saves the island outside, with you at the cave mouth (Snapshot).
    void CaveSnapshot(SaveData d)
    {
        if (InPrison) return; // nothing is saved down there: the snapshot only carries what you have
        d.nodes.Clear();
        d.nodes.AddRange(caveOutsideNodes);
        d.biomeId = (int)caveBiome + 1;
        d.stickCap = caveOutStick; d.rockCap = caveOutRock; d.obsidianCap = caveOutObsidian;
        d.player = caveReturn;
    }

    void TryLeaveCave()
    {
        if (!inCave) return;
        if (player != null)
            foreach (var e in enemies)
                if (e != null && (e.grid - player.grid).magnitude < 4.5f)
                { Hint("สัตว์ร้ายอยู่ใกล้เกินไป! วิ่งหนีให้ห่างก่อน หรือเดินกลับไปที่แสงตรงปากถ้ำ", 3f); return; }
        BeginWarp(Warp.ExitCave);
    }

    // ------------------------------------------------------------------ inside
    /// StartGame: tunnels instead of an island. Everything comes from the seed (the same for everyone).
    void BuildCave()
    {
        tiles = new int[Size, Size];
        heights = new int[Size, Size];
        caveFloor.Clear();
        var r = new System.Random(seed * 29 + 11 + prisonFloor * 7919); // every prison floor its own (IslandGame.Prison.cs)
        var nooks = new List<Vector2>();
        if (prisonFloor == PrisonSecret) // the secret floor: one long vault
        {
            caveStart = new Vector2(8f, Size / 2f);
            caveBossSpot = caveStart + new Vector2(12f, 0f);
            for (float t = 0f; t <= 1f; t += 0.05f) Carve(Vector2.Lerp(caveStart, caveBossSpot, t), 2.6f);
            Carve(caveStart, 2.6f); Carve(caveBossSpot, 3.2f);
        }
        else BuildTunnels(r, nooks);

        // floor and walls (the prison: cold blue stone)
        var root = new GameObject("Cave").transform;
        root.SetParent(world);
        var floorTint = InPrison ? new Color(0.62f, 0.72f, 0.88f) : new Color(0.82f, 0.76f, 0.88f);
        var wallTint = InPrison ? new Color(0.3f, 0.36f, 0.5f) : new Color(0.5f, 0.45f, 0.56f);
        for (int gx = 0; gx < Size; gx++)
            for (int gy = 0; gy < Size; gy++)
            {
                bool floor = tiles[gx, gy] == 2;
                if (floor) caveFloor.Add(new Vector2(gx, gy));
                if (!floor && !NextToFloor(gx, gy)) continue;
                var sr = new GameObject(floor ? "Floor" : "Wall").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(root);
                Vector3 p = GridToWorld(new Vector2(gx, gy));
                if (floor) { sr.sprite = PixelArt.Land(true, r.Next(4), 0, Biome.Volcanic); sr.color = floorTint; }
                else { sr.sprite = PixelArt.Land(true, r.Next(4), 2, Biome.Volcanic); sr.color = wallTint; p.y += PixelArt.HeightUnits(2); }
                sr.sortingOrder = -31000 + 2 * (gx + gy);
                sr.transform.position = p;
            }
        stickCap = 0; rockCap = 0; obsidianCap = 0;
        CaveNodes(r, nooks);
        if (InPrison) DecoratePrisonFloor(r, nooks);
    }

    /// The winding tunnel with rooms and nooks along it (a cave, or a prison floor).
    void BuildTunnels(System.Random r, List<Vector2> nooks)
    {
        // the main tunnel snakes across the map three times (about 120 tiles), wobbling as it goes
        var way = new[]
        {
            new Vector2(5f, 9f + r.Next(3)), new Vector2(Size - 9f, 8f + r.Next(4)),
            new Vector2(Size - 8f, Size / 2f + r.Next(-2, 3)), new Vector2(9f, Size / 2f + r.Next(-2, 3)),
            new Vector2(8f, Size - 10f - r.Next(3)), new Vector2(Size - 15f, Size - 9f - r.Next(3)),
        };
        var path = new List<Vector2>();
        Vector2 cur = way[0];
        path.Add(cur);
        for (int w = 1; w < way.Length; w++)
        {
            float wob = 0f;
            for (int guard = 0; guard < 200 && (way[w] - cur).magnitude > 1f; guard++)
            {
                Vector2 dir = (way[w] - cur).normalized, side = new Vector2(-dir.y, dir.x);
                wob = Mathf.Clamp(wob + (float)(r.NextDouble() * 2.0 - 1.0) * 0.6f, -1.2f, 1.2f);
                cur += dir + side * wob * 0.5f;
                cur = new Vector2(Mathf.Clamp(cur.x, 4f, Size - 5f), Mathf.Clamp(cur.y, 4f, Size - 5f));
                path.Add(cur);
            }
        }
        caveStart = way[0];
        caveBossSpot = new Vector2(Size - 8.5f, Size - 9f);
        for (int i = 0; i < path.Count; i++) Carve(path[i], 1.15f + (float)r.NextDouble() * 0.6f);
        Carve(caveStart, 2.2f);
        for (float t = 0f; t <= 1f; t += 0.1f) Carve(Vector2.Lerp(path[path.Count - 1], caveBossSpot, t), 1.4f);
        Carve(caveBossSpot, 4.6f); // the boss's hall

        // rooms on the way, and short side tunnels with a little room at the end (ore and crystals)
        for (int i = 8; i < path.Count - 6; i += 7 + r.Next(5))
        {
            Carve(path[i] + new Vector2(0f, r.Next(-2, 3)), 2.2f + (float)r.NextDouble() * 0.8f);
            if (r.Next(2) == 0) continue;
            int dir = r.Next(2) == 0 ? -1 : 1, len = 3 + r.Next(3);
            Vector2 p = path[i];
            for (int k = 0; k < len; k++) { p += new Vector2((float)(r.NextDouble() - 0.5), dir); if (p.y < 4f || p.y > Size - 5f) break; Carve(p, 1.05f); }
            Carve(p, 1.8f);
            nooks.Add(p);
        }
    }

    void CaveNodes(System.Random r, List<Vector2> nooks)
    {
        // the light at the mouth (the way out), rocks and ore along the walls, crystals in the nooks
        Spawn(NodeKind.CaveExit, caveStart + new Vector2(-0.6f, 0f), false);
        foreach (var g in caveFloor)
        {
            if ((g - caveStart).magnitude < 3f || (g - caveBossSpot).magnitude < 4f) continue;
            if (!NextToWall((int)g.x, (int)g.y) || r.NextDouble() > 0.06) continue;
            double roll = r.NextDouble();
            var kind = roll < 0.5 ? NodeKind.Rock : roll < 0.75 ? NodeKind.CopperOre : roll < 0.9 ? NodeKind.GlowShroom : NodeKind.CrystalCluster;
            Spawn(kind, g + new Vector2((float)r.NextDouble() - 0.5f, (float)r.NextDouble() - 0.5f) * 0.3f, r.Next(2) == 0);
        }
        foreach (var p in nooks)
            for (int k = 0; k < 2; k++)
            {
                var g = p + new Vector2((float)r.NextDouble() - 0.5f, (float)r.NextDouble() - 0.5f) * 1.6f;
                if (IsLand(g)) Spawn(k == 0 ? NodeKind.CrystalCluster : NodeKind.CopperOre, g, false);
            }
    }

    /// Floor in a circle (cave and prison layouts).
    void Carve(Vector2 c, float rad)
    {
        int x0 = Mathf.FloorToInt(c.x - rad), x1 = Mathf.CeilToInt(c.x + rad), y0 = Mathf.FloorToInt(c.y - rad), y1 = Mathf.CeilToInt(c.y + rad);
        for (int x = x0; x <= x1; x++)
            for (int yy = y0; yy <= y1; yy++)
                if (x >= 2 && yy >= 2 && x < Size - 2 && yy < Size - 2 && (new Vector2(x, yy) - c).magnitude <= rad) tiles[x, yy] = 2;
    }

    bool NextToFloor(int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (InMap(x + dx, y + dy) && tiles[x + dx, y + dy] == 2) return true;
        return false;
    }

    bool NextToWall(int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (!InMap(x + dx, y + dy) || tiles[x + dx, y + dy] != 2) return true;
        return false;
    }

    /// The island's creatures that make sense in a tunnel (no beach crabs, thieves or peaceful ants).
    EnemyKind[] CaveKinds()
    {
        var list = new List<EnemyKind>();
        foreach (var k in EnemiesOf(caveBiome, tier > 0))
            if (k is not (EnemyKind.Crab or EnemyKind.ChimeBird or EnemyKind.WorkerAnt)) list.Add(k);
        if (list.Count == 0) { list.Add(EnemyKind.Snake); list.Add(EnemyKind.Boar); }
        return list.ToArray();
    }

    /// StartGame: the creatures along the tunnel and the boss in the hall. The host's game runs
    /// them (online, a later arrival drops these and sees the host's: "room" in IslandGame.Net.cs).
    void SpawnCaveMonsters()
    {
        var r = new System.Random(System.Environment.TickCount);
        if (prisonFloor == PrisonSecret) return; // the secret vault: treasure, nothing to fight (IslandGame.Prison.cs)
        var kinds = CaveKinds();
        if (InPrison) // escaped prisoners: the island's creatures, the volcano continent's own, and fruit users
        {
            var list = new List<EnemyKind>(kinds);
            list.AddRange(ContinentCreatures(PrisonContinent));
            list.Add(EnemyKind.FruitUser); list.Add(EnemyKind.FruitUser);
            kinds = list.ToArray();
        }
        int want = InPrison ? Mathf.Min(45, 18 + prisonFloor * 3) : Mathf.Min(40, 22 + tier * 3 + island / 3);
        var spots = caveFloor.FindAll(g => (g - caveStart).magnitude > 7f && (g - caveBossSpot).magnitude > 5.5f);
        for (int i = 0; i < want && spots.Count > 0; i++)
        {
            int at = r.Next(spots.Count);
            var g = spots[at];
            spots.RemoveAll(s => (s - g).magnitude < 1.8f); // spread out along the way
            var kind = kinds[r.Next(kinds.Length)];
            var e = new GameObject(kind.ToString()).AddComponent<Enemy>();
            e.transform.SetParent(world);
            e.Init(kind, g, tier, ++enemyCounter);
            e.stayByDay = true;
            if (r.NextDouble() < 0.35 && kind != EnemyKind.FruitUser) e.SetVariant((Variant)(1 + r.Next(3))); // giant, fiery or armoured
            if (InPrison) e.Empower(1f + 0.15f * prisonFloor, 1f + 0.06f * prisonFloor, 1f + 0.1f * prisonFloor, Color.white); // deeper, tougher
            enemies.Add(e);
        }
        var bossKinds = System.Array.FindAll(kinds, k => k is not (EnemyKind.Mimic or EnemyKind.StoneSquid)); // a boss you can see
        var bk = prisonFloor == PrisonSpecial ? EnemyKind.FruitUser : bossKinds.Length > 0 ? bossKinds[r.Next(bossKinds.Length)] : EnemyKind.Boar;
        var boss = new GameObject("CaveBoss").AddComponent<Enemy>();
        boss.transform.SetParent(world);
        boss.Init(bk, caveBossSpot, tier, CaveBossId);
        boss.stayByDay = true;
        boss.MakeCaveBoss();
        if (InPrison) MakeWarden(boss); // the floor's warden (IslandGame.Prison.cs)
        enemies.Add(boss);
    }

    /// Joined the cave's room: someone not in my crew got in first, so this isn't my turn.
    void CaveRoomCheck(NetMsg m)
    {
        caveBossSeen = false; // our own boss may just have made way for the host's
        if (m.players == null || InPrison) return; // the sunken prison is open to anyone who finds it
        var crew = MyCrewIds();
        foreach (var p in m.players)
            if (p != null && !crew.Contains(p.id))
            {
                Hint($"{p.name} เข้าถ้ำไปก่อนแล้ว ถ้ำปิดอยู่ (เข้าลูกเรือเดียวกันก่อนถึงจะเข้าไปด้วยกันได้)", 5f);
                caveBounce = true;
                return;
            }
    }

    /// The boss fell (whoever landed the blow): treasure for everyone in the cave, and a way out by the hall.
    void CaveBossDown()
    {
        if (!inCave || caveBossDown) return;
        caveBossDown = true;
        if (InPrison) { PrisonWardenDown(); return; } // IslandGame.Prison.cs
        Vector3 at = player != null ? PlayerPos : GridToWorld(caveBossSpot);
        AddXp(120 + island * 12, at);
        Give(Item.GoldCoin, 15 + island * 2, null);
        Give(Item.CrystalShard, 2 + tier, null);
        if (rnd.NextDouble() < 0.3) Give(FruitHere(caveBiome), 1, null);
        string f = "caveboss" + WorldKey(island, continent);
        bool first = !Flag(f);
        if (first) { flags.Add(f); skillPoints++; }
        QuestBanner("ปราบเจ้าแห่งถ้ำแล้ว!", first ? "ได้สมบัติ และแต้มสกิล 1 แต้ม · มีทางออกเปิดข้างห้องบอส" : "ได้สมบัติจากถ้ำ · มีทางออกเปิดข้างห้องบอส", new Color(1f, 0.82f, 0.3f));
        Sfx.Play("win", 0.7f);
        Spawn(NodeKind.CaveExit, FreeStand(caveBossSpot + new Vector2(-2f, 0f)), false);
        SaveGame();
    }

    /// Every frame on land (IslandGame.Play.cs).
    void TickCave(float dt)
    {
        if (!InCrew) crewRoster.Clear();
        if (IsCaptain && Online) // the crew list, so every member knows who's crew (the cave lets crew in)
        {
            caveRosterT -= dt;
            if (caveRosterT <= 0f)
            {
                caveRosterT = 4f;
                var ids = new System.Text.StringBuilder(myId.ToString());
                foreach (var m in crewMates) ids.Append(',').Append(m.id);
                foreach (var m in crewMates) Send(new NetMsg { t = "crew", to = m.id, ev = "roster", text = ids.ToString() });
            }
        }
        if (caveAskAt >= 0f && Time.unscaledTime - caveAskAt > 3f) CaveWhoHeard(); // no answer: the room check still guards
        if (!inCave) return;
        if (caveBounce && homeT <= 0f && !Busy) { caveBounce = false; BeginWarp(Warp.ExitCave); return; }
        bool bossHere = enemies.Exists(e => e != null && e.netId == CaveBossId);
        if (bossHere) caveBossSeen = true;
        else if (caveBossSeen && !caveBossDown && homeT <= 0f) CaveBossDown();
    }

    /// The crew relay (IslandGame.Crew.cs): the captain's crew list, and a crewmate going into a cave.
    void OnCaveCrew(NetMsg m, string who)
    {
        if (m.ev == "roster")
        {
            if (m.from != crewCaptain) return;
            crewRoster.Clear();
            foreach (var s in (m.text ?? "").Split(',')) if (int.TryParse(s, out int id)) crewRoster.Add(id);
            return;
        }
        if (m.ev == "cave" && !inCave && voyage == null && m.island == CaveKey)
            Hint($"{who} ลูกเรือของคุณเข้าถ้ำไปแล้ว ไปที่ปากถ้ำแล้วกด E เพื่อตามเข้าไป", 5f);
    }

    /// Top middle while inside: how many creatures are left, and the boss's health.
    void DrawCaveHud(float W)
    {
        if (!inCave || player == null) return;
        int left = 0;
        Enemy boss = null;
        foreach (var e in enemies) { if (e == null) continue; if (e.netId == CaveBossId) boss = e; else left++; }
        var st = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
        string text = InPrison
            ? (prisonFloor == PrisonSecret ? "คุกใต้ทะเล ชั้นลับ · เปิดหีบสมบัติ แล้วลงบันไดไปต่อ"
               : caveBossDown ? $"คุกใต้ทะเล {PrisonFloorName} · ผ่านแล้ว! " + (prisonFloor == PrisonSpecial ? "เดินไปที่แสงเพื่อขึ้นผิวน้ำ" : "ลงบันไดไปชั้นถัดไป")
               : $"คุกใต้ทะเล {PrisonFloorName} · นักโทษเหลือ {left} · ผู้คุมรออยู่สุดทาง")
            : caveBossDown ? "ถ้ำปลอดภัยแล้ว · เดินไปที่แสงเพื่อออก" : $"สัตว์ร้ายเหลือ {left} ตัว · เจ้าแห่งถ้ำรออยู่สุดทาง";
        float tw = st.CalcSize(new GUIContent(text)).x + 10f;
        const float clearOf = 308f; // right of the status bars and the sky dial
        ShadowLabel(new Rect(Mathf.Max(W / 2f - tw / 2f, clearOf), 40f, tw, 18f), text, st, new Color(0.9f, 0.8f, 1f)); // under the online line
        if (boss == null) return;
        var bar = new Rect(Mathf.Max(W / 2f - 130f, clearOf), 62f, 260f, 10f);
        Fill(new Rect(bar.x - 2, bar.y - 2, bar.width + 4, bar.height + 4), new Color(0f, 0f, 0f, 0.6f));
        Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01((float)boss.hp / Mathf.Max(1, boss.maxHp)), bar.height), new Color(0.75f, 0.3f, 0.9f));
        ShadowLabel(new Rect(bar.x, bar.y + 10f, bar.width, 18f), $"{boss.DisplayName}  {boss.hp}/{boss.maxHp}", st, new Color(1f, 0.85f, 1f));
    }
}
