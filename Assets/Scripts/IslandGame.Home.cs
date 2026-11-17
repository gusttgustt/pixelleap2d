using System.Collections.Generic;
using UnityEngine;

/// The private island: your own safe island to farm, keep animals, fish, trade at the town
/// market and build on. Press H (or the button top-right) to go there from any adventure island,
/// and H again to go back to exactly where you were.
///
/// It is built like any other island (same terrain, nodes, regrowing resources and save system)
/// with atHome set: no creatures, no bosses or quests, a fixed seed, and its own nodes. While you
/// are at home, the adventure island you came from waits in `visited`; while you are away, the
/// home island waits in `homeNodes`. Both go into the one save file.
///   IslandGame.Home.cs  - going there and back, generating it, save data, HUD
///   IslandGame.Build.cs - build mode: placing things on the tile grid, the hammer
///   IslandGame.Farm.cs  - crops, animals, fishing
///   IslandGame.Shop.cs  - the town's shops, prices, coins (Item.GoldCoin)
public partial class IslandGame
{
    const int HomeSeed = 0x5EED1E; // everyone's private island has the same shape; what you build is yours
    public bool atHome;
    bool homeMade;                 // been there before (otherwise it gets generated on arrival)
    Vector2 advPlayer;             // where you stood on the adventure island
    readonly List<NodeSave> homeNodes = new List<NodeSave>();
    int homeStickCap, homeRockCap;
    int homeLand;                 // island expansions bought (0 = the island you start with)
    const int MaxHomeLand = 3;
    /// How much wider the private island's land reaches with each expansion.
    float HomeGrow => atHome ? 1f + 0.075f * homeLand : 1f;

    float homeT;                   // fade while travelling (home <-> adventure, in and out of the house)
    bool homeApplied;
    enum Warp { GoHome, LeaveHome, EnterHouse, ExitHouse, Visit, EndVisit, Expand }
    Warp warp;

    public Vector2 TownCenter => Center + new Vector2(5f, -5f); // the market, to the east of your house
    public static bool IsHomeKind(NodeKind k) => k >= NodeKind.Plot;

    // ------------------------------------------------------------------ save data
    void LoadHome(SaveData d)
    {
        atHome = d.atHome;
        homeMade = d.homeMade;
        advPlayer = d.advPlayer;
        homeNodes.Clear();
        homeNodes.AddRange(d.homeNodes);
        homeStickCap = d.homeStickCap; homeRockCap = d.homeRockCap;
        homeLand = Mathf.Clamp(d.homeLand, 0, MaxHomeLand);
        LoadHouse(d); // IslandGame.House.cs
        LoadPets(d);  // IslandGame.Pet.cs
    }

    void SaveHome(SaveData d)
    {
        d.atHome = atHome;
        d.homeMade = homeMade;
        d.advIsland = island;
        d.advPlayer = advPlayer;
        d.homeStickCap = homeStickCap; d.homeRockCap = homeRockCap;
        d.homeLand = homeLand;
        d.homeNodes.Clear();
        if (!atHome || inHouse) d.homeNodes.AddRange(homeNodes); // outdoors at home they're the live nodes (d.nodes)
        SaveHouse(d);
        SavePets(d);
    }

    void HomeTraits()
    {
        T = new Traits { name = "เกาะส่วนตัว", shape = Shape.Round, weather = Weather.Clear, tint = Color.white };
    }

    // ------------------------------------------------------------------ going there and back
    bool CanTravelHome(out string why)
    {
        why = null;
        if (title || dead || player == null) return false;
        if (voyage != null) { why = "กลางทะเลกลับเกาะส่วนตัวไม่ได้ ขึ้นฝั่งก่อน"; return false; }
        if (Busy || homeT > 0f) return false;
        if (atHome && !visiting && (health < 25f || hunger <= 5f || thirst <= 5f))
        { why = "ยังอ่อนแรงเกินไปที่จะออกผจญภัย กินอาหาร ดื่มน้ำ หรือนอนพักก่อน"; return false; }
        if (!atHome)
            foreach (var e in enemies)
                if (e != null && (e.grid - player.grid).magnitude < 5f) { why = "มีศัตรูอยู่ใกล้ๆ! จัดการให้เรียบร้อยก่อนกลับบ้าน"; return false; }
        return true;
    }

    void ToggleHome()
    {
        if (visiting) { if (!Busy && homeT <= 0f) EndVisit(); return; } // back from a friend's island (IslandGame.Visit.cs)
        if (!CanTravelHome(out var why)) { if (why != null) Hint(why, 2.5f); return; }
        BeginWarp(atHome ? Warp.LeaveHome : Warp.GoHome);
    }

    void BeginWarp(Warp w)
    {
        if (homeT > 0f) return;
        warp = w;
        dialog = null;
        homeT = 0.0001f;
        homeApplied = false;
        craftOpen = false; skillsOpen = false; bagOpen = false;
        CloseBuild();
        CancelLine();
        if (cursor != null) cursor.enabled = false;
        Sfx.Play("win", 0.5f);
    }

    void UpdateHomeWarp(float dt)
    {
        homeT += dt;
        if (!homeApplied && homeT >= 1.1f)
        {
            homeApplied = true;
            switch (warp)
            {
                case Warp.GoHome: GoHome(); break;
                case Warp.LeaveHome: LeaveHome(); break;
                case Warp.EnterHouse: EnterHouse(); break;
                case Warp.Visit: visitLoad = true; try { DoVisit(); } finally { visitLoad = false; } break;
                case Warp.EndVisit: DoEndVisit(); break;
                case Warp.Expand: DoExpandIsland(); break;
                default: ExitHouse(); break;
            }
            if (!visiting) { SaveSystem.Write(Snapshot()); savedAt = Time.time; }
        }
        if (homeT >= 2.4f) homeT = 0f;
    }

    void GoHome()
    {
        // keep the adventure island exactly as it is (the raft too: you come back to the same spot)
        var keep = new IslandSave { island = island, continent = continent, seed = seed, biomeId = (int)biome + 1, stickCap = stickCap, rockCap = rockCap, obsidianCap = obsidianCap };
        foreach (var n in nodes) if (n != null) keep.nodes.Add(ToSave(n));
        visited.RemoveAll(v => v.island == island && v.continent == continent);
        visited.Add(keep);
        advPlayer = player.grid;

        bool first = !homeMade;
        var d = Snapshot();
        d.atHome = true;
        d.homeMade = true;
        d.nodes.Clear();
        d.nodes.AddRange(d.homeNodes);
        d.homeNodes.Clear();
        d.fresh = first;
        d.seed = HomeSeed;
        d.biomeId = (int)Biome.Tropical + 1;
        d.stickCap = homeStickCap; d.rockCap = homeRockCap; d.obsidianCap = 0;
        d.player = new Vector2(-99f, -99f); // arrive in front of your house
        StartGame(d);
        if (first) GiveWelcomeKit();
    }

    void LeaveHome()
    {
        if (inHouse) // straight out of the house: the room is what's live, the island is already stored
        {
            StoreRoom();
            inHouse = false;
        }
        else
        {
            homeNodes.Clear();
            foreach (var n in nodes) if (n != null) homeNodes.Add(ToSave(n));
            homeStickCap = stickCap; homeRockCap = rockCap;
        }
        atHome = false; // so the snapshot keeps the home island in homeNodes

        var d = Snapshot();
        d.nodes.Clear();
        var known = Kept(island, continent);
        if (known != null)
        {
            d.fresh = false;
            d.seed = known.seed; d.biomeId = known.biomeId;
            d.stickCap = known.stickCap; d.rockCap = known.rockCap; d.obsidianCap = known.obsidianCap;
            d.nodes.AddRange(known.nodes);
            d.visited.RemoveAll(v => v.island == island && v.continent == continent);
        }
        else // shouldn't happen offline; online the island is rebuilt from the shared seed anyway
        {
            d.fresh = true;
            d.biomeId = (int)BiomeFor(island, continent) + 1;
            d.seed = new System.Random(island * 7919 + 17 + continent * 104729).Next();
        }
        d.player = advPlayer;
        StartGame(d);
    }

    void KeepHomeOnDeath()
    {
        if (!homeMade && pets.Count == 0) return;
        var d = new SaveData { homeMade = homeMade, homeStickCap = homeStickCap, homeRockCap = homeRockCap, houseLevel = houseLevel, roomMade = roomMade, homeLand = homeLand, homeContinent = homeContinent };
        d.homeNodes.AddRange(homeNodes);
        d.houseNodes.AddRange(houseNodes);
        SavePets(d);
        SaveSystem.WriteHomeKeep(d);
    }

    void DevHomeKeys(UnityEngine.InputSystem.Keyboard kb)
    {
        if (!DevMode || kb == null || player == null || chatOpen) return;
        if (kb.f10Key.wasPressedThisFrame) // what's around you (debugging)
        {
            var near = nodes.FindAll(n => n != null && (n.grid - player.grid).magnitude < 2.5f);
            near.Sort((a, b) => (a.grid - player.grid).magnitude.CompareTo((b.grid - player.grid).magnitude));
            var sb = new System.Text.StringBuilder($"[dev] คุณ {player.grid.x:0.00},{player.grid.y:0.00} เป้า {(target != null ? target.kind.ToString() : "-")} | ");
            foreach (var n in near) sb.Append($"{n.kind} {n.grid.x:0.0},{n.grid.y:0.0} d{(n.grid - player.grid).magnitude:0.00} t{(n.CanTarget ? 1 : 0)} | ");
            Hint(sb.ToString(), 15f);
            Debug.Log(sb.ToString());
        }
        if (kb.f6Key.wasPressedThisFrame) { Now += 60f; Hint("[dev] ข้ามเวลา 1 นาที (ผักและสัตว์)", 2f); }
        if (kb.f7Key.wasPressedThisFrame)
        {
            foreach (var it in new[] { Item.GoldCoin, Item.Wood, Item.Stone, Item.Stick, Item.Plank, Item.Rope, Item.Fiber, Item.Berry, Item.Wheat })
                inv[it] = Count(it) + 50;
            raftLevel = Mathf.Max(raftLevel, 1); // so the sea chart can be tried out
            health = MaxHealth; hunger = 100f; thirst = 100f;
            Hint("[dev] +50 เหรียญและวัตถุดิบ (และแพ)", 2f);
        }
        if (kb.f12Key.wasPressedThisFrame && dialog == null) OpenChart(); // the sea chart without a boat (IslandGame.Chart.cs)
    }

    void GiveWelcomeKit()
    {
        foreach (var (it, n) in new[] { (Item.Hoe, 1), (Item.WateringCan, 1), (Item.Hammer, 1), (Item.CarrotSeed, 6), (Item.WheatSeed, 6), (Item.GoldCoin, 20) })
            inv[it] = Count(it) + n;
        Popup("ได้รับของขวัญต้อนรับ!", PlayerPos + Vector3.up * 1.2f, new Color(1f, 0.85f, 0.3f));
    }

    // ------------------------------------------------------------------ generating the home island
    /// First visit: the market town to the east, a starter farm, a well and a chicken pen.
    void BuildHome()
    {
        var c = new Vector2Int(Mathf.RoundToInt(Center.x), Mathf.RoundToInt(Center.y));
        var tc = TownCenter;
        ClearArea(Center, 3.2f);
        ClearArea(tc, 5.5f);

        // --- the market town
        PlaceTown(NodeKind.Fountain, tc);
        foreach (var off in new[] { new Vector2(0f, -3f), new Vector2(3f, 0f), new Vector2(-2.3f, -2.3f), new Vector2(2.3f, 2.3f) })
            PlaceTown(NodeKind.Stall, tc + off);
        PlaceTown(NodeKind.House, tc + new Vector2(3.8f, -3.8f));
        PlaceTown(NodeKind.House, tc + new Vector2(5.2f, 1.2f));
        PlaceTown(NodeKind.House, tc + new Vector2(0.5f, -5.4f));
        foreach (var off in new[] { new Vector2(-3.2f, 0.6f), new Vector2(0.6f, 3.4f), new Vector2(-0.8f, -4.4f), new Vector2(4.4f, -1.8f) })
            PlaceTown(NodeKind.Lamp, tc + off);
        PlaceTown(NodeKind.Signpost, tc + new Vector2(-4.2f, -0.6f));

        // --- your place
        Spawn(NodeKind.Signpost, Center + new Vector2(1.3f, -0.6f), false);
        var well = Spawn(NodeKind.Well, Center + new Vector2(-1.6f, -2.4f), false); well.built = true;
        ClearArea(new Vector2(c.x, c.y + 3.5f), 2.6f); // room around the starter farm
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 2; j++)
            {
                var t = new Vector2(c.x - 1 + i, c.y + 3 + j);
                ClearArea(t, 0.8f);
                if (IsLand(t) && tiles[(int)t.x, (int)t.y] == 2) { var p = Spawn(NodeKind.Plot, t, false); p.built = true; }
            }
        // a 4 x 4 pen with a gate facing the house, and a hen inside
        int x0 = c.x - 7, y0 = c.y - 1;
        ClearArea(new Vector2(x0 + 1.5f, y0 + 1.5f), 3.3f);
        for (int x = x0; x <= x0 + 3; x++)
            for (int y = y0; y <= y0 + 3; y++)
            {
                bool edgeX = x == x0 || x == x0 + 3, edgeY = y == y0 || y == y0 + 3;
                if (!edgeX && !edgeY) continue;
                var t = new Vector2(x, y);
                if (!IsLand(t)) continue;
                var f = Spawn(NodeKind.Fence, t, edgeX && !edgeY); // rows run along x, columns along y
                f.built = true;
            }
        Spawn(NodeKind.Chicken, new Vector2(x0 + 1.5f, y0 + 1.5f), false);

        // more stone and sticks to collect than on a wild island (they keep coming back)
        for (int i = 0, tries = 0; i < 14 && tries < 400; tries++)
        {
            var g = new Vector2(rnd.Next(Size), rnd.Next(Size));
            if (!IsLand(g) || (g - Center).magnitude < 6f || (g - tc).magnitude < 6.5f || !FreeSpot(g, 0.4f)) continue;
            Spawn(i % 2 == 0 ? NodeKind.Rock : NodeKind.Stick, g, false);
            i++;
        }
        stickCap = 0; rockCap = 0; obsidianCap = 0;
        foreach (var n in nodes)
        {
            if (n.kind == NodeKind.Stick) stickCap++;
            else if (n.kind == NodeKind.Rock) rockCap++;
        }
    }

    /// Removes what grew around g (not things people built, and not animals).
    void ClearArea(Vector2 g, float r)
    {
        foreach (var n in nodes.FindAll(n => n != null && !n.built && !n.IsAnimal && !IsHomeKind(n.kind) && (n.grid - g).magnitude < r))
            RemoveNode(n);
    }

    void PlaceTown(NodeKind k, Vector2 want)
    {
        for (int ring = 0; ring < 4; ring++)
            for (int a = 0; a < 8; a++)
            {
                var g = want + (ring == 0 ? Vector2.zero : new Vector2(Mathf.Cos(a * Mathf.PI / 4f), Mathf.Sin(a * Mathf.PI / 4f)) * ring * 0.7f);
                var t = Tile(g);
                if (!InMap(t.x, t.y) || tiles[t.x, t.y] != 2) continue;
                ClearArea(g, 1.4f);
                if (!FreeSpot(g, k is NodeKind.House or NodeKind.Fountain ? 1.0f : 0.5f)) continue;
                Spawn(k, g, k is NodeKind.House or NodeKind.Stall && a % 2 == 0);
                return;
            }
    }

    void ReadSign(WorldNode n)
    {
        bool town = (n.grid - TownCenter).magnitude < (n.grid - Center).magnitude; // whichever sign it is nearer to
        if (inHouse) town = false;
        var d = new Dialog { speaker = town ? "ป้ายตลาดเมืองท่า" : visiting ? $"ป้ายหน้าบ้านของ {visitName}" : "ป้ายหน้าบ้าน" };
        d.text = town
            ? "ตลาดเมืองท่า: ร้านเมล็ดพันธุ์และสัตว์ (ปลูก/เลี้ยง), ร้านช่างไม้ (วัสดุก่อสร้าง), ร้านอุปกรณ์ประมง, ร้านสัตว์เลี้ยง (หมา แมว กระต่าย นกแก้ว ตาข่ายจับสัตว์) และร้านรับซื้อของ ใช้เหรียญทองซื้อขาย เอาผัก ไข่ นม ปลา มาขายแลกเหรียญได้เลย"
            : visiting ? $"ยินดีต้อนรับสู่เกาะของ {visitName}!\nเดินชมได้ตามสบาย ช่วยปลูกผัก รดน้ำ เก็บเกี่ยว และให้อาหารสัตว์ได้ (ของที่เก็บได้ เจ้าของบ้านได้ด้วยเท่ากัน) ร้านค้าในตลาดใช้ได้ตามปกติ กด Enter เพื่อแชตกับเจ้าของบ้าน กด H เพื่อกลับ"
            : "เกาะส่วนตัวของคุณ ที่นี่ปลอดภัย ไม่มีสัตว์ร้าย\n" +
              "• ฟาร์ม: กด G เลือก \"แปลงผัก\" ขุดด้วยจอบ ยืนที่แปลงกด E เพื่อปลูก รดน้ำ (ทุกช่วงการโต) แล้วเก็บเกี่ยว\n" +
              "• สัตว์: ให้อาหาร (ข้าวสาลี/เบอร์รี่/หญ้า) แล้วรอเก็บไข่หรือรีดนม ซื้อเพิ่มได้ที่ตลาด\n" +
              "• ตกปลา: มีเบ็ดแล้วยืนริมทะเลกด E รอทุ่นจม แล้วกด E อีกครั้ง\n" +
              "• สร้าง: กด G วางรั้ว ทางเดิน ดอกไม้ บ้าน ฯลฯ ตามช่อง (Z X เลือก, R หมุน) ช่องแรกคือค้อนไว้รื้อ\n" +
              "• สัตว์เลี้ยง: ซื้อที่ร้านสัตว์เลี้ยง หรือจับสัตว์ป่าด้วยตาข่าย กด P ดูสัตว์เลี้ยง\n" +
              "• เล่นออนไลน์: กด V ไปเยี่ยมบ้านเพื่อน และเพื่อนก็มาเยี่ยมบ้านคุณได้\n" +
              "• ตลาดอยู่ทางขวา (ตะวันออก) กด H เพื่อกลับไปผจญภัยต่อ";
        if (!town && !visiting && !inHouse)
        {
            if (homeLand < MaxHomeLand)
            {
                var cost = ExpandCost(homeLand + 1);
                d.options.Add(($"ขยายเกาะ ขั้นที่ {homeLand + 1}/{MaxHomeLand} (ใช้ {CostText(cost)})", () => ExpandIsland(cost), CanPay(cost)));
            }
            else d.text += "\n\nเกาะของคุณขยายจนสุดแล้ว";
        }
        d.options.Add(("ปิด", CloseDialog, true));
        dialog = d;
    }

    // ------------------------------------------------------------------ making the island bigger
    static (Item item, int n)[] ExpandCost(int step) => step switch
    {
        1 => new[] { (Item.GoldCoin, 80), (Item.Stone, 20), (Item.Wood, 10) },
        2 => new[] { (Item.GoldCoin, 200), (Item.Stone, 40), (Item.Wood, 20), (Item.Plank, 10) },
        _ => new[] { (Item.GoldCoin, 400), (Item.Stone, 60), (Item.Plank, 25), (Item.Rope, 10) },
    };

    void ExpandIsland((Item item, int n)[] cost)
    {
        if (!CanPay(cost) || homeLand >= MaxHomeLand || visiting || !atHome || inHouse) return;
        foreach (var c in cost) inv[c.item] -= c.n;
        homeLand++;
        CloseDialog();
        BeginWarp(Warp.Expand);
    }

    /// After the fade: the island is rebuilt with its land reaching further out to sea. What you
    /// built stays where it is (only the shoreline grows).
    void DoExpandIsland()
    {
        var d = Snapshot();
        d.fresh = false;
        StartGame(d);
        Sfx.Play("levelup", 0.7f);
        Hint($"เกาะขยายแล้ว (ขั้นที่ {homeLand}/{MaxHomeLand})! ชายหาดใหม่รอบเกาะว่างให้สร้างและทำฟาร์มได้", 5f);
    }

    void ResetHomeTools()
    {
        buildOpen = false; ghost = null;
        fishState = 0; bobber = null;
    }

    // ------------------------------------------------------------------ sprites for home nodes
    public static Sprite HomeSprite(WorldNode n, int frame) => n.kind switch
    {
        NodeKind.Plot => PixelArt.Plot(n.dropCount > 0 ? n.dropItem : (Item?)null, n.dropCount, n.hits > 0 && n.dropCount is 1 or 2),
        NodeKind.Fence => PixelArt.Fence(),
        NodeKind.StonePath => PixelArt.StonePath(),
        NodeKind.Flowers => PixelArt.Flowers(Mathf.Abs(Mathf.RoundToInt(n.grid.x * 3f + n.grid.y * 5f)) % 3),
        NodeKind.Bench => PixelArt.Bench(),
        NodeKind.Scarecrow => PixelArt.Scarecrow(),
        NodeKind.Cottage => PixelArt.Cottage(I.houseLevel),
        NodeKind.Bed => PixelArt.Bed(),
        NodeKind.Table => PixelArt.Table(),
        NodeKind.Chair => PixelArt.Chair(),
        NodeKind.Rug => PixelArt.Rug(),
        NodeKind.PlantPot => PixelArt.PlantPot(),
        NodeKind.Shelf => PixelArt.Shelf(),
        NodeKind.Workbench => PixelArt.Workbench(),
        NodeKind.HouseDoor => PixelArt.Doormat(),
        NodeKind.Signpost => PixelArt.Signpost(),
        NodeKind.Chicken => PixelArt.Chicken(frame),
        NodeKind.Cow => PixelArt.Cow(frame),
        _ => PixelArt.Signpost(),
    };

    // ------------------------------------------------------------------ HUD
    float hudButtonsBottom = 72f; // where the top-right buttons end (the quest list goes below)

    void DrawHomeButton(float W)
    {
        hudButtonsBottom = 72f;
        if (player == null || voyage != null || dead || homeT > 0f) return;
        var st = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold };
        st.normal.textColor = atHome ? new Color(0.6f, 0.85f, 1f) : new Color(1f, 0.85f, 0.45f);
        st.hover.textColor = Color.white;
        if (CrispGUI.Button(new Rect(W - 182, 50, 172, 22), visiting ? "กลับบ้านตัวเอง (H)" : atHome ? "กลับไปผจญภัย (H)" : "ไปเกาะส่วนตัว (H)", st)) ToggleHome();
        if (craftOpen || skillsOpen || bagOpen || dialog != null) return; // a panel covers this corner: its buttons must get the clicks
        float y = 76f;
        if (atHome && !buildOpen && !visiting)
        {
            var bst = new GUIStyle(st); bst.normal.textColor = new Color(0.75f, 1f, 0.7f);
            if (CrispGUI.Button(new Rect(W - 182, y, 172, 22), inHouse ? "จัดบ้าน (G)" : "สร้าง / รื้อ (G)", bst)) ToggleBuild();
            y += 26f;
            hudButtonsBottom = y;
        }
        if (buildOpen) return;
        var pst = new GUIStyle(st); pst.normal.textColor = new Color(1f, 0.72f, 0.82f);
        if (Online && serverHomes && CrispGUI.Button(new Rect(W - 182, y, 172, 22), "เยี่ยมบ้านเพื่อน (V)", pst)) AskHomes();
        if (Online && serverHomes) y += 26f;
        if (CrispGUI.Button(new Rect(W - 182, y, 172, 22), pets.Count > 0 ? $"สัตว์เลี้ยง {pets.Count} ตัว (P)" : "สัตว์เลี้ยง (P)", pst)) OpenPetMenu();
        hudButtonsBottom = y + 22f;
    }

    void DrawHomeWarp(float W, float H)
    {
        if (homeT <= 0f) return;
        float a = homeT < 1.1f ? homeT / 1.1f : Mathf.Clamp01((2.4f - homeT) / 1.3f);
        Fill(new Rect(0, 0, W, H), warp is Warp.EnterHouse or Warp.ExitHouse ? new Color(0.08f, 0.05f, 0.03f, a) : new Color(0.12f, 0.3f, 0.22f, a));
        string text = warp switch
        {
            Warp.GoHome => homeApplied ? "เกาะส่วนตัว" : "กำลังล่องเรือกลับบ้าน...",
            Warp.LeaveHome => homeApplied ? $"เกาะที่ {island}: {T.name}" : "ออกไปผจญภัยต่อ...",
            Warp.EnterHouse => homeApplied ? $"{HouseName(houseLevel)}" : "เข้าบ้าน...",
            Warp.Visit => homeApplied ? $"บ้านของ {visitName}" : "กำลังล่องเรือไปเยี่ยมบ้านเพื่อน...",
            Warp.Expand => homeApplied ? "เกาะกว้างขึ้นแล้ว!" : "กำลังถมทะเลขยายเกาะ...",
            Warp.EndVisit => homeApplied ? (atHome ? "เกาะส่วนตัว" : $"เกาะที่ {island}: {T.name}") : "กลับบ้าน...",
            _ => homeApplied ? "เกาะส่วนตัว" : "ออกจากบ้าน...",
        };
        ShadowLabel(new Rect(0, H / 2 - 20, W, 40), text, titleCenter, new Color(1f, 1f, 1f, a));
    }

    /// Coins, and little icons over animals and plots that need you.
    float DrawHomeHud(float infoY, float s)
    {
        if (atHome)
        {
            ShadowLabel(new Rect(14, infoY, 320, 18), $"เหรียญทอง {Count(Item.GoldCoin)}", small, new Color(1f, 0.85f, 0.35f));
            infoY += 16f;
        }
        if (dialog != null || Busy || voyage != null) return infoY;
        DrawPetTags(s); // pet names up close (IslandGame.Pet.cs)
        float bounce = Mathf.Sin(Time.time * 4f) * 2f;
        foreach (var n in nodes)
        {
            if (n == null || (player != null && (n.grid - player.grid).magnitude > 12f)) continue;
            Sprite icon = null;
            float alpha = 1f;
            if (n.IsAnimal) { icon = n.available ? PixelArt.Icon(n.kind == NodeKind.Cow ? Item.Milk : Item.Egg) : n.hits == 0 ? PixelArt.Icon(Item.Wheat) : null; alpha = n.available ? 1f : 0.6f; }
            else if (n.kind == NodeKind.Plot && n.dropCount is 1 or 2 && n.hits == 0) { icon = PixelArt.Icon(Item.WateringCan); alpha = 0.75f; }
            if (icon == null) continue;
            var gp = ToGui(n.transform.position + Vector3.up * (n.PromptHeight + 0.15f), s);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            Icon(new Rect(gp.x - 8, gp.y - 16 + bounce, 16, 16), icon);
            GUI.color = Color.white;
        }
        if (fishState == 2 && bobber != null)
        {
            var gp = ToGui(bobber.transform.position + Vector3.up * 0.3f, s);
            ShadowLabel(new Rect(gp.x - 10, gp.y - 26 + bounce, 20, 24), "!", titleCenter, new Color(1f, 0.85f, 0.3f));
        }
        return infoY;
    }
}
