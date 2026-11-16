using UnityEngine;

/// Island level and prosperity. An island's level is its number on the chart: the further out,
/// the bigger the island, the tougher its creatures, and the more its people have built:
///   Lv 1-4    ดินแดนรกร้าง  wild: only the shipwright
///   Lv 5-14   หมู่บ้านเล็ก   a couple of huts, a well, a few villagers
///   Lv 15-29  หมู่บ้าน      more huts, farm plots, lamp posts, a trader
///   Lv 30-59  เมืองท่า      stone houses, market stalls, more traders, lamps along the paths
///   Lv 60+    นครรุ่งเรือง   a fountain plaza with a statue, rows of houses, lots of people
public partial class IslandGame
{
    public int IslandLevel => island;
    public int Prosperity => atHome ? 0 : island >= 60 ? 4 : island >= 30 ? 3 : island >= 15 ? 2 : island >= 5 ? 1 : 0;
    public static string ProsperityName(int p) => p switch
    {
        1 => "หมู่บ้านเล็ก", 2 => "หมู่บ้าน", 3 => "เมืองท่า", 4 => "นครรุ่งเรือง", _ => "ดินแดนรกร้าง",
    };

    /// Island radius as a share of the map: small islands early, big ones far out.
    float IslandRadiusFactor => atHome ? 0.36f : 0.30f + 0.12f * Mathf.Clamp01((island - 1) / 40f);

    /// Creature strength from the island's level (replaces the old 4-island laps).
    public float EnemyHpScale => 1f + 0.07f * (island - 1);
    public float EnemyDamageScale => 1f + 0.035f * (island - 1);

    // ------------------------------------------------------------------ building the settlement
    /// On a freshly made island: everything its people have built, on a ring around the middle
    /// (the tribe's village or the camp keeps the very centre).
    void BuildProsperity()
    {
        int p = Prosperity;
        if (p == 0) return;
        var r = new System.Random(seed * 17 + 3);
        int houses = p switch { 1 => 2, 2 => 3, 3 => 5, _ => 7 };
        int lamps = p switch { 1 => 0, 2 => 3, 3 => 6, _ => 10 };
        int farms = p switch { 1 => 0, 2 => 3, 3 => 4, _ => 5 };
        int stalls = p switch { 1 => 0, 2 => 0, 3 => 2, _ => 3 };

        if (p >= 4) PlaceRing(NodeKind.Fountain, 1, 3.8f, 4.2f, r);
        if (p >= 4) PlaceRing(NodeKind.Statue, 1, 4.6f, 5.2f, r);
        PlaceRing(NodeKind.Well, 1, 4.5f, 5.5f, r);
        PlaceRing(p >= 3 ? NodeKind.House : NodeKind.Hut, houses, 5.5f, 8.5f, r);
        PlaceRing(NodeKind.Farm, farms, 6f, 9.5f, r);
        PlaceRing(NodeKind.Stall, stalls, 4.8f, 6.5f, r);
        PlaceRing(NodeKind.Lamp, lamps, 3.5f, 10f, r);
    }

    void PlaceRing(NodeKind k, int count, float rMin, float rMax, System.Random r)
    {
        for (int placed = 0, tries = 0; placed < count && tries < 200; tries++)
        {
            float a = (float)r.NextDouble() * Mathf.PI * 2f, d = rMin + (float)r.NextDouble() * (rMax - rMin);
            var g = Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            int x = Mathf.RoundToInt(g.x), y = Mathf.RoundToInt(g.y);
            if (x < 1 || y < 1 || x >= Size - 1 || y >= Size - 1 || tiles[x, y] != 2) continue;
            float clear = k is NodeKind.House or NodeKind.Hut or NodeKind.Fountain ? 1.6f : 1f;
            foreach (var n in nodes.FindAll(n => n != null && (n.grid - g).magnitude < clear && !n.Blocking)) RemoveNode(n); // clear grass and sticks
            if (!FreeSpot(g, clear)) continue;
            Spawn(k, g, r.Next(2) == 0);
            placed++;
        }
    }

    /// Townsfolk walking around the settlement, and the market's traders.
    void SpawnTownsfolk()
    {
        int p = Prosperity;
        if (p == 0 || biome == Biome.Tribal) return; // the tribe has its own people
        string[] names = { "ดาว", "ต้นกล้า", "ฟ้าใส", "บุญมา", "แก้ว", "ทองดี", "ลำดวน", "สายลม", "มะลิ", "จันทร์" };
        var r = new System.Random(seed * 5 + 1);
        int folk = p switch { 1 => 2, 2 => 3, 3 => 5, _ => 7 };
        var homes = nodes.FindAll(n => n != null && n.kind is NodeKind.House or NodeKind.Hut);
        for (int i = 0; i < folk; i++)
        {
            Vector2 at = homes.Count > 0 ? homes[i % homes.Count].grid + (Center - homes[i % homes.Count].grid).normalized * 1.3f : Center;
            var v = new GameObject("Townsfolk").AddComponent<Villager>();
            v.transform.SetParent(world);
            v.Init(i == 0 ? VillagerRole.Local : VillagerRole.Villager, names[r.Next(names.Length)], 9 + i % 3, WalkableNear(at)); // the first one has a job for you
            villagers.Add(v);
        }
        if (p >= 2)
        {
            var stalls = nodes.FindAll(n => n != null && n.kind == NodeKind.Stall);
            int traders = Mathf.Max(1, stalls.Count);
            for (int i = 0; i < traders; i++)
            {
                Vector2 at = i < stalls.Count ? stalls[i].grid + (Center - stalls[i].grid).normalized * 1.1f : Center + new Vector2(2f, 0f);
                var v = new GameObject("Trader").AddComponent<Villager>();
                v.transform.SetParent(world);
                v.Init(VillagerRole.Trader, i == 0 ? "เถ้าแก่ทอง" : "แม่ค้าบัว", 2, WalkableNear(at));
                villagers.Add(v);
            }
        }
    }

    static readonly string[] TownChatter =
    {
        "เกาะเราเจริญขึ้นทุกปีเลยนะ ตั้งแต่มีเรือแวะมาค้าขาย",
        "ตะเกียงตามทางช่วยไล่สัตว์ร้ายตอนกลางคืนได้ดีทีเดียว",
        "บ่อน้ำนั่นน้ำจืดสะอาด ดื่มได้เลย",
        "ได้ยินว่าเกาะไกลๆ มีเมืองใหญ่กว่านี้อีก",
        "แปลงผักของบ้านข้างๆ ออกผลดกทุกฤดู เก็บไปกินได้นะ",
        "ช่างโป๊ะเป็นช่างต่อเรือที่เก่งที่สุดในย่านนี้",
    };

    void DrinkFromWell(WorldNode n)
    {
        bool refill = Count(Item.WaterFlask) > 0 && flaskWater < FlaskSize;
        if (thirst >= 99f && !refill) { Hint("ยังไม่หิวน้ำ", 1.5f); return; }
        thirst = Mathf.Min(100f, thirst + 30f);
        if (refill) { flaskWater = FlaskSize; Popup("เติมกระบอกน้ำแล้ว", n.transform.position + Vector3.up * 0.8f, new Color(0.55f, 0.85f, 1f)); }
        Popup("+30 น้ำ", n.transform.position + Vector3.up * 0.5f, new Color(0.55f, 0.85f, 1f));
        Sfx.Play("drink", 0.6f);
    }

    public float Daylight => daylight;
}
