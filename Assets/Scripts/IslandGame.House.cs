using System.Collections.Generic;
using UnityEngine;

/// Your house on the private island: upgrade it with materials you gather (it gets bigger and
/// looks different at every level), walk inside to a room of its own where you arrange furniture
/// on the tile grid (G), and use the workbench there to make farm supplies and furniture.
///
/// The room works like a tiny island: StartGame builds it (BuildRoom) instead of terrain, with
/// floor tiles where you can walk, back walls, and the furniture as nodes. While you're inside,
/// the island outside waits in homeNodes; while you're outside, the room waits in houseNodes.
public partial class IslandGame
{
    public bool inHouse;
    public int houseLevel;          // 0 = no house yet; 1-3
    bool roomMade;                  // the room has been furnished once
    Vector2 homeReturn;             // where you stood outside before going in
    readonly List<NodeSave> houseNodes = new List<NodeSave>();
    public bool benchOpen;          // the crafting panel shows the workbench's list

    public const int MaxHouseLevel = 3;
    public int RoomSize => 3 + 2 * Mathf.Clamp(houseLevel, 1, MaxHouseLevel); // 5, 7, 9 tiles across

    public static string HouseName(int level) => level switch { 2 => "บ้านไม้หลังใหญ่", >= 3 => "บ้านหินหลังงาม", _ => "กระท่อมไม้" };

    static (Item item, int n)[] UpgradeCost(int toLevel) => toLevel switch
    {
        2 => new[] { (Item.Plank, 20), (Item.Wood, 10), (Item.Stone, 15), (Item.Rope, 6) },
        _ => new[] { (Item.Stone, 40), (Item.Plank, 30), (Item.Wood, 15), (Item.Rope, 10) },
    };

    // ------------------------------------------------------------------ save data
    void LoadHouse(SaveData d)
    {
        inHouse = d.inHouse && d.atHome;
        houseLevel = d.houseLevel;
        roomMade = d.roomMade;
        homeReturn = d.homeReturn;
        houseNodes.Clear();
        houseNodes.AddRange(d.houseNodes);
    }

    void SaveHouse(SaveData d)
    {
        d.inHouse = inHouse;
        d.houseLevel = houseLevel;
        d.roomMade = roomMade;
        d.homeReturn = homeReturn;
        d.houseNodes.Clear();
        if (!inHouse) d.houseNodes.AddRange(houseNodes); // inside, the room is the live nodes
    }

    void StoreRoom()
    {
        houseNodes.Clear();
        foreach (var n in nodes) if (n != null && n.kind != NodeKind.HouseDoor) houseNodes.Add(ToSave(n)); // the door is rebuilt with the room
    }

    // ------------------------------------------------------------------ the house outside
    void CottageDialog(WorldNode n)
    {
        int L = Mathf.Clamp(houseLevel, 1, MaxHouseLevel);
        var d = new Dialog { speaker = $"{HouseName(L)} Lv {L}" };
        d.text = $"บ้านของคุณ ห้องข้างในกว้าง {RoomSize}x{RoomSize} ช่อง เข้าไปจัดเฟอร์นิเจอร์และใช้โต๊ะช่างได้";
        d.options.Add(("เข้าบ้าน", () => BeginWarp(Warp.EnterHouse), true));
        if (visiting) // a friend's house (IslandGame.Visit.cs)
        {
            d.speaker = $"{HouseName(L)} ของ {visitName}";
            d.text = $"บ้านของ {visitName} ห้องข้างในกว้าง {RoomSize}x{RoomSize} ช่อง เข้าไปดูการจัดบ้านได้";
        }
        else if (L < MaxHouseLevel)
        {
            var cost = UpgradeCost(L + 1);
            d.text += $"\nอัปเกรดเป็น{HouseName(L + 1)}: ห้องกว้างขึ้นเป็น {3 + 2 * (L + 1)}x{3 + 2 * (L + 1)} ช่อง";
            d.options.Add(($"อัปเกรดเป็น{HouseName(L + 1)} (ใช้ {CostText(cost)})", () => UpgradeHouse(n, cost), CanPay(cost)));
        }
        else d.text += "\nบ้านของคุณใหญ่ที่สุดแล้ว!";
        d.options.Add(("ปิด", CloseDialog, true));
        dialog = d;
        craftOpen = false; skillsOpen = false;
    }

    string CostText((Item item, int n)[] cost)
    {
        var parts = new List<string>();
        foreach (var c in cost) parts.Add($"{Name(c.item)} {c.n} (มี {Count(c.item)})");
        return string.Join(", ", parts);
    }

    void UpgradeHouse(WorldNode n, (Item item, int n)[] cost)
    {
        if (!CanPay(cost)) return;
        foreach (var c in cost) inv[c.item] -= c.n;
        houseLevel = Mathf.Clamp(Mathf.Max(1, houseLevel) + 1, 1, MaxHouseLevel);
        n.Refresh();
        n.Shake();
        Fx.Burst(n.transform.position + Vector3.up * 0.8f, new Color(1f, 0.85f, 0.3f), 30, 1.8f, 1.6f, 1f, 3f);
        Sfx.Play("levelup", 0.7f);
        CloseDialog();
        Hint($"อัปเกรดเป็น{HouseName(houseLevel)} Lv {houseLevel} แล้ว! ห้องข้างในกว้าง {RoomSize}x{RoomSize} ช่อง", 5f);
        SaveGame();
    }

    // ------------------------------------------------------------------ in and out
    void EnterHouse()
    {
        homeNodes.Clear();
        foreach (var n in nodes) if (n != null) homeNodes.Add(ToSave(n));
        homeStickCap = stickCap; homeRockCap = rockCap;
        homeReturn = player.grid;
        houseLevel = Mathf.Max(1, houseLevel);
        inHouse = true; // so the snapshot keeps the island outside in homeNodes
        bool first = !roomMade;
        roomMade = true;

        var d = Snapshot();
        d.nodes.Clear();
        d.nodes.AddRange(houseNodes);
        d.fresh = first;
        d.stickCap = 0; d.rockCap = 0; d.obsidianCap = 0;
        d.player = DoorTile + new Vector2(0f, -0.7f);
        StartGame(d);
    }

    void ExitHouse()
    {
        StoreRoom();
        inHouse = false;
        var d = Snapshot();
        d.nodes.Clear();
        d.nodes.AddRange(homeNodes);
        d.homeNodes.Clear();
        d.fresh = false;
        d.seed = HomeSeed;
        d.stickCap = homeStickCap; d.rockCap = homeRockCap; d.obsidianCap = 0;
        d.player = homeReturn;
        StartGame(d);
    }

    // ------------------------------------------------------------------ the room
    Vector2Int RoomMin { get { var c = new Vector2Int(Mathf.RoundToInt(Center.x), Mathf.RoundToInt(Center.y)); return c - new Vector2Int(RoomSize / 2, RoomSize / 2); } }
    /// The doormat: middle of the front-left edge of the room.
    Vector2 DoorTile => new Vector2(RoomMin.x + RoomSize / 2, RoomMin.y + RoomSize - 1);

    /// Floor where you can walk (as "grass" tiles, so walking and placing work as outside),
    /// wallpapered back walls, and on the first visit a workbench, a bed and a rug.
    void BuildRoom(bool fresh)
    {
        tiles = new int[Size, Size];
        heights = new int[Size, Size];
        var min = RoomMin;
        int n = RoomSize;
        var root = new GameObject("Room").transform;
        root.SetParent(world);
        for (int x = min.x; x < min.x + n; x++)
            for (int y = min.y; y < min.y + n; y++)
            {
                tiles[x, y] = 2;
                var sr = new GameObject("Floor").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(root);
                sr.sprite = PixelArt.FloorTile(houseLevel, (x * 3 + y * 5) % 3);
                sr.sortingOrder = -31000 + 2 * (x + y);
                sr.transform.position = GridToWorld(new Vector2(x, y));
            }
        // back walls (the front two are left open so you can see in)
        for (int i = 0; i < n; i++)
        {
            Wall(new Vector2(min.x + i, min.y - 1), false, i);
            Wall(new Vector2(min.x - 1, min.y + i), true, i);
        }
        stickCap = 0; rockCap = 0; obsidianCap = 0;

        var door = Spawn(NodeKind.HouseDoor, DoorTile, false);
        door.built = false;
        if (!fresh) return;
        foreach (var (k, g) in new[] { (NodeKind.Workbench, new Vector2(min.x + 1, min.y)), (NodeKind.Bed, new Vector2(min.x + n - 2, min.y + 1)), (NodeKind.Rug, new Vector2(min.x + n / 2, min.y + n / 2)), (NodeKind.PlantPot, new Vector2(min.x, min.y)) })
        {
            var f = Spawn(k, g, false);
            f.built = true;
        }

        void Wall(Vector2 g, bool side, int i)
        {
            var sr = new GameObject("Wall").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(root);
            sr.sprite = PixelArt.RoomWall(houseLevel, i % 3 == 1);
            sr.flipX = side;
            Vector3 flat = GridToWorld(g);
            sr.transform.position = flat;
            sr.sortingOrder = SortOrder(flat.y);
        }
    }

    // ------------------------------------------------------------------ the workbench
    Recipe[] CraftList => benchOpen ? BenchRecipes : Recipes;

    void OpenBench()
    {
        craftOpen = true; benchOpen = true;
        skillsOpen = false; bagOpen = false;
        craftScroll = Vector2.zero;
        Hint("โต๊ะช่าง: ทำของสำหรับฟาร์มและของตกแต่งบ้าน (ทำเสร็จแล้วกด G เพื่อวางเฟอร์นิเจอร์)", 3f);
    }

    static readonly Recipe[] BenchRecipes =
    {
        // farm
        new Recipe { name = "ปุ๋ยหมัก", output = Item.Fertilizer, count = 2, cost = new[] { (Item.Fiber, 3), (Item.Berry, 2) }, desc = "ใส่แปลงผักที่รดน้ำแล้ว ให้โตทันทีหนึ่งช่วง (ได้ 2)" },
        new Recipe { name = "อาหารสัตว์", output = Item.AnimalFeed, count = 3, cost = new[] { (Item.Wheat, 2), (Item.Fiber, 2) }, desc = "ไก่และวัวกินได้ ทีละ 1 (ได้ 3)" },
        new Recipe { name = "เมล็ดแครอท", output = Item.CarrotSeed, count = 3, cost = new[] { (Item.Carrot, 1) }, desc = "เก็บเมล็ดจากแครอท (ได้ 3)" },
        new Recipe { name = "เมล็ดมะเขือเทศ", output = Item.TomatoSeed, count = 3, cost = new[] { (Item.Tomato, 1) }, desc = "เก็บเมล็ดจากมะเขือเทศ (ได้ 3)" },
        new Recipe { name = "เมล็ดข้าวสาลี", output = Item.WheatSeed, count = 4, cost = new[] { (Item.Wheat, 1) }, desc = "เก็บเมล็ดจากรวงข้าว (ได้ 4)" },
        new Recipe { name = "จอบ", output = Item.Hoe, cost = new[] { (Item.Stick, 2), (Item.Stone, 2), (Item.Rope, 1) }, desc = "ขุดแปลงผัก" },
        new Recipe { name = "บัวรดน้ำ", output = Item.WateringCan, cost = new[] { (Item.Wood, 2), (Item.Stone, 1), (Item.Rope, 1) }, desc = "รดน้ำแปลงผัก" },
        new Recipe { name = "ค้อน", output = Item.Hammer, cost = new[] { (Item.Stick, 2), (Item.Stone, 3), (Item.Rope, 1) }, desc = "รื้อสิ่งก่อสร้างนอกบ้าน" },
        new Recipe { name = "แผ่นไม้", output = Item.Plank, count = 2, cost = new[] { (Item.Wood, 2) }, desc = "ใช้ทำเฟอร์นิเจอร์และอัปเกรดบ้าน (ได้ 2)" },
        // furniture
        new Recipe { name = "เตียงนอน", output = Item.FurnBed, cost = new[] { (Item.Plank, 6), (Item.Fiber, 8) }, desc = "เฟอร์นิเจอร์: นอนตอนกลางคืน" },
        new Recipe { name = "โต๊ะไม้", output = Item.FurnTable, cost = new[] { (Item.Plank, 4), (Item.Stick, 2) }, desc = "เฟอร์นิเจอร์" },
        new Recipe { name = "เก้าอี้ไม้", output = Item.FurnChair, cost = new[] { (Item.Plank, 2), (Item.Stick, 2) }, desc = "เฟอร์นิเจอร์" },
        new Recipe { name = "พรมทอมือ", output = Item.FurnRug, cost = new[] { (Item.Fiber, 8), (Item.Berry, 2) }, desc = "ปูพื้น วางเฟอร์นิเจอร์ทับได้" },
        new Recipe { name = "กระถางต้นไม้", output = Item.FurnPlant, cost = new[] { (Item.Stone, 3), (Item.Fiber, 2) }, desc = "ของตกแต่ง" },
        new Recipe { name = "ชั้นวางของ", output = Item.FurnShelf, cost = new[] { (Item.Plank, 5), (Item.Stick, 2) }, desc = "ของตกแต่ง" },
        new Recipe { name = "โต๊ะช่าง", output = Item.FurnWorkbench, cost = new[] { (Item.Plank, 4), (Item.Stone, 4), (Item.Wood, 2) }, desc = "ตั้งโต๊ะช่างเพิ่มอีกตัว" },
    };
}
