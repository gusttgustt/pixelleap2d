using UnityEngine;

/// The sunken prison (คุกใต้ทะเล), on the volcano continent. Sailing those waters, sometimes an
/// iron grate shows in a whirlpool along the way: sail onto it and press E to dive in. Inside:
///   floors 1-7  tunnels of escaped prisoners (the island's creatures, the continent's own, fruit
///               users), tougher every floor, and each floor's warden at the end; beat the warden
///               and the stairs down open.
///   floor 8     the special floor (ชั้นพิเศษ): prisoner number zero, an awakened fruit user far
///               beyond the wardens. Beat them for a special fruit and the continent's boss
///               materials, then surface.
///   secret      one of floors 3-6 hides a cracked wall (ประตูลับ): through it is the secret floor
///               (ชั้นลับ), a vault of treasure chests and nothing to fight; its stairs go on to the
///               floor after the one you found it on.
/// The light by the entrance of every floor takes you back up to the surface, where you come
/// ashore at the island you were sailing to. Like at sea, the game isn't saved down there: die
/// (or quit) and you wake where you last saved, without what you picked up.
///
/// It's built like a cave (IslandGame.Cave.cs, inCave is true), one floor at a time from the
/// seed, and online each floor is a room of its own (anyone diving in the same channel meets).
public partial class IslandGame
{
    public int prisonFloor;               // 0 = not in the prison; 1-7, 8 special, 9 secret
    int prisonDest, prisonDestC;           // the island the voyage was going to (where you surface)
    int prisonSecretFrom;                  // the floor the secret door was found on
    bool prisonEntering;                   // StartGame: this load is a prison floor

    const int PrisonSpecial = 8, PrisonSecret = 9, PrisonContinent = 2; // ทวีปภูเขาไฟ
    const int PrisonRoomBase = 1200000000;

    public bool InPrison => inCave && prisonFloor > 0;
    int PrisonKey => PrisonRoomBase + ChannelOffset + prisonFloor;
    static bool PrisonRoom(int isl) => isl >= PrisonRoomBase && isl < SeaRoomBase;

    /// The floor with the secret door (from the seed).
    int SecretDoorFloor => 3 + Mathf.Abs(seed * 7 + 1) % 4;

    string PrisonFloorName => prisonFloor == PrisonSpecial ? "ชั้นพิเศษ" : prisonFloor == PrisonSecret ? "ชั้นลับ" : $"ชั้น {prisonFloor}";

    // ------------------------------------------------------------------ the grate at sea
    /// StartVoyage: on the volcano continent's waters, sometimes.
    void PlanPrisonGate(Voyage v)
    {
        if (voyageVisit != null || destContinent != PrisonContinent || Random.value > 0.35f) return;
        v.prisonAt = new Vector2(v.length * Random.Range(0.3f, 0.7f), Random.Range(-4f, 4f));
        v.prisonSr = new GameObject("PrisonGate").AddComponent<SpriteRenderer>();
        v.prisonSr.transform.SetParent(world);
        v.prisonSr.sprite = PixelArt.PrisonGate();
        v.prisonSr.transform.position = v.prisonAt;
        v.prisonSr.sortingOrder = -32400; // on the water, under the boats
    }

    /// UpdateVoyage: close to the grate, E dives in. Returns true when it took the key press.
    bool PrisonGateTick(Voyage v, bool pressed)
    {
        if (v.prisonSr == null) return false;
        v.prisonSr.transform.Rotate(0f, 0f, -40f * Time.deltaTime); // the whirlpool turns
        float d = (v.raft - v.prisonAt).magnitude;
        if (d > 1.6f) return false;
        if (Time.time >= hintUntil - 0.5f) Hint("พบประตูคุกใต้ทะเล! กด E (หรือปุ่ม E) เพื่อดำลงไป · ข้างในไม่มีการเซฟ", 1.5f);
        if (!pressed) return false;
        prisonDest = destIsland; prisonDestC = destContinent;
        prisonSecretFrom = 0;
        StartPrisonWarp(1);
        return true;
    }

    void StartPrisonWarp(int floor)
    {
        nextPrisonFloor = floor;
        BeginWarp(Warp.PrisonFloor);
    }
    int nextPrisonFloor;

    /// The fade is dark: build the floor.
    void EnterPrisonFloor()
    {
        int floor = nextPrisonFloor;
        var d = Snapshot();
        d.island = Mathf.Max(1, prisonDest);
        d.continent = prisonDestC;
        d.biomeId = (int)BiomeFor(d.island, d.continent) + 1;
        d.seed = 90001 + d.island * 131 + d.continent * 7;
        d.fresh = true;
        d.nodes.Clear();
        prisonFloor = floor;
        prisonEntering = true;
        caveEntering = true;
        caveBossDown = false; caveBossSeen = false; caveBounce = false;
        StartGame(d);
        prisonEntering = false;
        if (floor == PrisonSecret) QuestBanner("ชั้นลับ!", "ห้องเก็บสมบัติที่ผู้คุมซ่อนไว้ · เปิดหีบ แล้วลงบันไดไปต่อ", new Color(1f, 0.82f, 0.3f));
        else if (floor == PrisonSpecial) QuestBanner("คุกใต้ทะเล: ชั้นพิเศษ", "นักโทษหมายเลขศูนย์ ผู้ใช้ผลไม้ปลุกพลัง รออยู่สุดทาง", new Color(1f, 0.5f, 0.4f));
        else QuestBanner($"คุกใต้ทะเล ชั้น {floor} / 7", floor == 1 ? "นักโทษแหกคุกเต็มทางเดิน ผู้คุมรออยู่สุดทาง · แสงที่ทางเข้าพากลับขึ้นผิวน้ำ" : "ยิ่งลึกยิ่งอันตราย · ปราบผู้คุมเพื่อเปิดบันไดลง", new Color(0.6f, 0.75f, 1f));
    }

    /// Back up to the surface: ashore at the island the voyage was going to.
    void SurfaceFromPrison()
    {
        int dest = Mathf.Max(1, prisonDest), destC = prisonDestC;
        prisonFloor = 0;
        inCave = false;
        ArriveAt(dest, destC);
        Hint($"โผล่ขึ้นผิวน้ำ แล้วเรือพามาถึงเกาะที่ {dest}", 4f);
    }

    // ------------------------------------------------------------------ inside
    /// BuildCave's extras for a prison floor: the stairs, the secret door, the vault's chests.
    void DecoratePrisonFloor(System.Random r, System.Collections.Generic.List<Vector2> nooks)
    {
        if (prisonFloor == PrisonSecret)
        {
            for (int i = 0; i < 5; i++) Spawn(NodeKind.Chest, FreeStand(caveStart + new Vector2(3f + i * 1.3f, (i % 2) * 1.4f - 0.7f)), false);
            Spawn(NodeKind.PrisonStairs, FreeStand(caveStart + new Vector2(10f, 0f)), false);
            return;
        }
        if (prisonFloor == SecretDoorFloor && nooks.Count > 0) Spawn(NodeKind.PrisonSecret, nooks[r.Next(nooks.Count)], false);
    }

    /// The vault's chests: real treasure.
    void OpenPrisonChest(WorldNode n)
    {
        var r = new System.Random(seed + n.id * 17);
        Give(Item.GoldCoin, 40 + r.Next(60), n);
        Give(r.Next(2) == 0 ? Item.LavaCore : Item.CrystalShard, 2 + r.Next(3), n);
        if (r.Next(3) == 0) Give(r.Next(2) == 0 ? Item.HydraFang : Item.Pearl, 1, n);
        if (r.Next(4) == 0) Give(FruitHere(caveBiome), 1, n);
        AddXp(150, n.transform.position);
        RemoveNode(n);
        Sfx.Play("levelup", 0.5f);
    }

    /// The warden of this floor (SpawnCaveMonsters): what it's called and how tough.
    void MakeWarden(Enemy boss)
    {
        float extra = 1f + 0.18f * prisonFloor;
        boss.Empower(prisonFloor == PrisonSpecial ? 2.2f * extra : extra, 1f + 0.08f * prisonFloor, 1.5f, prisonFloor == PrisonSpecial ? new Color(1f, 0.7f, 0.7f) : new Color(0.75f, 0.85f, 1f));
        boss.title = prisonFloor == PrisonSpecial ? "นักโทษหมายเลขศูนย์ " : $"ผู้คุมชั้น {prisonFloor} ";
    }

    /// The warden fell (CaveBossDown): the way on, and what it guarded.
    void PrisonWardenDown()
    {
        int f = prisonFloor;
        Vector3 at = player != null ? PlayerPos : GridToWorld(caveBossSpot);
        AddXp(100 + 60 * f + island * 6, at);
        Give(Item.GoldCoin, 10 + 8 * f, null);
        if (f >= 5) Give(Item.LavaCore, f - 3, null);
        if (f == PrisonSpecial)
        {
            Give(Item.HydraFang, 2, null);
            Give(FruitHere(caveBiome), 1, null);
            string flag = "prison0";
            if (!Flag(flag)) { flags.Add(flag); skillPoints += 2; AddBounty(1500000, at + Vector3.up * 1.4f, null); }
            QuestBanner("ปราบนักโทษหมายเลขศูนย์แล้ว!", "ได้ผลไม้พิเศษ เขี้ยวไฮดรา และของรางวัล · เดินไปที่แสงเพื่อขึ้นสู่ผิวน้ำ", new Color(1f, 0.82f, 0.3f));
            Spawn(NodeKind.CaveExit, FreeStand(caveBossSpot + new Vector2(-2f, 0f)), false);
        }
        else
        {
            QuestBanner($"ปราบผู้คุมชั้น {f} แล้ว!", f < 7 ? $"บันไดลงชั้น {f + 1} เปิดแล้ว" : "บันไดลงชั้นพิเศษเปิดแล้ว", new Color(0.6f, 0.85f, 1f));
            Spawn(NodeKind.PrisonStairs, FreeStand(caveBossSpot + new Vector2(-2f, 0f)), false);
        }
        Sfx.Play("win", 0.7f);
    }

    /// The stairs: down a floor (from the secret floor: on to the floor after the one with the door).
    void TakePrisonStairs()
    {
        int next = prisonFloor == PrisonSecret ? Mathf.Min(7, prisonSecretFrom + 1) : prisonFloor >= 7 ? PrisonSpecial : prisonFloor + 1;
        StartPrisonWarp(next);
    }

    void TakeSecretDoor()
    {
        prisonSecretFrom = prisonFloor;
        Hint("ผนังร้าวเลื่อนเปิด... มีทางลับ!", 2.5f);
        StartPrisonWarp(PrisonSecret);
    }
}
