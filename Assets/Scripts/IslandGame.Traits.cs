using System.Collections.Generic;
using UnityEngine;

/// What makes every island its own, on top of its type (biome): a name of its own, a shape,
/// a colour cast, weather, one or two landmarks with treasure, a breed of tougher creatures,
/// sometimes a pack leader (alpha) to hunt, and a local with a job for you. All of it comes from
/// the island's seed and number, so the same island always looks the same (and online everyone
/// sees the same island).
public partial class IslandGame
{
    public enum Shape { Round, Long, Crescent, Twin, Archipelago, Peak }
    public enum Weather { Clear, Fog, Rain, Fireflies, Leaves }
    public enum Landmark { Wreck, Ruins, GiantTree, Shrine }
    public enum Variant { None, Giant, Fiery, Armored }

    public class Traits
    {
        public string name;
        public Shape shape;
        public Weather weather;
        public readonly List<Landmark> landmarks = new List<Landmark>();
        public Variant variant;
        public Color tint = Color.white;
        public bool alpha;            // a pack leader lives here
        public EnemyKind alphaKind;
        public float axis;            // direction of long / twin / crescent shapes (radians)
    }

    public Traits T = new Traits();

    static readonly string[] NameA = { "จันทร์", "หมอก", "ฟัน", "ดาว", "ลม", "ทอง", "เงา", "ไฟ", "คลื่น", "มรกต", "ปะการัง", "พราย" };
    static readonly string[] NameB = { "เสี้ยว", "ม่วง", "มังกร", "ตก", "หวน", "คำ", "พราว", "นิทรา", "กระซิบ", "ลับแล" };

    /// Every island number gets a different name (120 combinations, stepped so neighbours differ).
    public static string IslandName(int n)
    {
        int i = ((n - 1) * 37) % (NameA.Length * NameB.Length);
        return "เกาะ" + NameA[i % NameA.Length] + NameB[i / NameA.Length];
    }

    static string WeatherName(Weather w) => w switch
    {
        Weather.Fog => "หมอกลง", Weather.Rain => "ฝนตก", Weather.Fireflies => "หิ่งห้อย", Weather.Leaves => "ใบไม้ปลิว", _ => "ฟ้าใส",
    };

    static string VariantName(Variant v) => v switch { Variant.Giant => "ยักษ์", Variant.Fiery => "เพลิง", Variant.Armored => "เกราะ", _ => "" };

    void ComputeTraits()
    {
        var r = new System.Random(seed ^ (island * 7777));
        T = new Traits { name = IslandName(island), axis = (float)r.NextDouble() * Mathf.PI * 2f };
        if (island <= 1) { T.landmarks.Add(Landmark.Wreck); return; } // keep the first island simple

        T.shape = (Shape)r.Next(System.Enum.GetValues(typeof(Shape)).Length);
        bool dryBiome = biome is Biome.Sky or Biome.Bubble or Biome.Hourglass or Biome.Ice;
        T.weather = biome == Biome.Storm ? Weather.Rain : dryBiome ? (r.Next(3) == 0 ? Weather.Fireflies : Weather.Clear) : (Weather)r.Next(System.Enum.GetValues(typeof(Weather)).Length);
        Color[] tints =
        {
            Color.white, new Color(1f, 0.93f, 0.85f), new Color(0.86f, 0.93f, 1f), new Color(1f, 0.88f, 0.95f),
            new Color(0.9f, 1f, 0.9f), new Color(0.93f, 0.9f, 1f), new Color(1f, 0.97f, 0.82f),
        };
        T.tint = tints[r.Next(tints.Length)];
        var all = new List<Landmark> { Landmark.Wreck, Landmark.Ruins, Landmark.GiantTree, Landmark.Shrine };
        int count = 1 + (r.Next(3) == 0 ? 1 : 0);
        for (int i = 0; i < count; i++) { int k = r.Next(all.Count); T.landmarks.Add(all[k]); all.RemoveAt(k); }
        if (island >= 3) T.variant = (Variant)(1 + r.Next(3));
        var kinds = BiomeEnemies();
        var pick = new List<EnemyKind>();
        foreach (var k in kinds) if (k != EnemyKind.Crab || kinds.Length == 1) pick.Add(k);
        if (island >= 5 && biome != Biome.Mushroom && r.Next(2) == 0 && pick.Count > 0)
        {
            T.alpha = true;
            T.alphaKind = pick[r.Next(pick.Count)];
        }
    }

    // ------------------------------------------------------------------ island shape
    /// The land value for a tile (higher = land), shaped by the island's shape trait.
    float ShapeValue(int gx, int gy, Vector2 center, float radius, float noise)
    {
        Vector2 p = new Vector2(gx, gy) - center;
        Vector2 u = new Vector2(Mathf.Cos(T.axis), Mathf.Sin(T.axis)), w = new Vector2(-u.y, u.x);
        float d = p.magnitude / radius;
        bool sandbar = false;
        switch (T.shape)
        {
            case Shape.Long:
                d = new Vector2(Vector2.Dot(p, u) / 1.35f, Vector2.Dot(p, w) * 1.3f).magnitude / radius;
                break;
            case Shape.Twin: // two round lobes joined in the middle
                d = Mathf.Min((p - u * radius * 0.45f).magnitude, (p + u * radius * 0.45f).magnitude) / (radius * 0.68f);
                break;
            case Shape.Archipelago: // a smaller main island with islets joined by sandbars
            {
                d = p.magnitude / (radius * 0.72f);
                for (int i = 0; i < 4; i++)
                {
                    float a = T.axis + i * Mathf.PI / 2f;
                    Vector2 c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * 0.92f;
                    d = Mathf.Min(d, (p - c).magnitude / (radius * 0.24f));
                    float t = Mathf.Clamp01(Vector2.Dot(p, c) / c.sqrMagnitude); // sandbar
                    if ((p - c * t).magnitude < 1.1f) sandbar = true; // always walkable to the islets
                }
                break;
            }
        }
        float v = 1f - d + (noise - 0.5f) * 0.7f;
        if (sandbar) v = Mathf.Max(v, 0.2f);
        if (T.shape == Shape.Crescent) // a bay bitten out of one side
        {
            float bite = (p - u * radius * 0.85f).magnitude / (radius * 0.6f);
            if (bite < 1f) v -= (1f - bite) * 1.2f;
        }
        return v;
    }

    /// Turns every bit of land you can't walk to from the middle back into sea.
    void RemoveUnreachableLand(Vector2 center)
    {
        var seen = new bool[Size, Size];
        var q = new System.Collections.Generic.Queue<Vector2Int>();
        var c = new Vector2Int(Mathf.RoundToInt(center.x), Mathf.RoundToInt(center.y));
        q.Enqueue(c); seen[c.x, c.y] = true;
        var steps = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var s in steps)
            {
                var n = p + s;
                if (n.x < 0 || n.y < 0 || n.x >= Size || n.y >= Size || seen[n.x, n.y]) continue;
                if (!(tiles[n.x, n.y] is 1 or 2)) continue;
                if (Mathf.Abs(heights[n.x, n.y] - heights[p.x, p.y]) > 1) continue;
                seen[n.x, n.y] = true;
                q.Enqueue(n);
            }
        }
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
                if (tiles[x, y] is 1 or 2 && !seen[x, y]) { tiles[x, y] = 0; heights[x, y] = 0; }
    }

    float ShapeHillBias => T.shape == Shape.Peak ? 0.15f : 0f;

    // ------------------------------------------------------------------ landmarks
    void PlaceLandmarks()
    {
        var r = new System.Random(seed * 23 + 11);
        foreach (var lm in T.landmarks)
            switch (lm)
            {
                case Landmark.Wreck: // a broken ship washed up on the beach, its chest beside it
                {
                    var at = PlaceOnTile(NodeKind.Wreck, 1, 7f, 20f, r, 1.6f);
                    if (at != null) TrySpawnNear(NodeKind.Chest, at.Value + new Vector2(1.2f, 0.4f));
                    break;
                }
                case Landmark.Ruins: // a ring of old pillars with a chest in the middle
                {
                    var at = PlaceOnTile(NodeKind.Chest, 2, 6f, 13f, r, 1f);
                    if (at != null)
                        for (int i = 0; i < 4; i++)
                            TrySpawnNear(NodeKind.Pillar, at.Value + new Vector2(Mathf.Cos(i * 1.57f + 0.6f), Mathf.Sin(i * 1.57f + 0.6f)) * 1.7f);
                    break;
                }
                case Landmark.GiantTree:
                {
                    var at = PlaceOnTile(NodeKind.GiantTree, 2, 6f, 12f, r, 2f);
                    if (at != null) TrySpawnNear(NodeKind.Chest, at.Value + new Vector2(1.3f, -0.9f));
                    break;
                }
                case Landmark.Shrine:
                    PlaceOnTile(NodeKind.Shrine, 2, 5f, 12f, r, 1.2f);
                    break;
            }
    }

    /// Put a node on a land tile of the given type (1 sand, 2 grass) somewhere in a ring around the middle.
    Vector2? PlaceOnTile(NodeKind k, int tile, float rMin, float rMax, System.Random r, float clear)
    {
        for (int tries = 0; tries < 300; tries++)
        {
            float a = (float)r.NextDouble() * Mathf.PI * 2f, d = rMin + (float)r.NextDouble() * (rMax - rMin);
            var g = Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            int x = Mathf.RoundToInt(g.x), y = Mathf.RoundToInt(g.y);
            if (x < 1 || y < 1 || x >= Size - 1 || y >= Size - 1 || tiles[x, y] != tile) continue;
            foreach (var n in nodes.FindAll(n => n != null && (n.grid - g).magnitude < clear && !n.Blocking)) RemoveNode(n);
            if (!FreeSpot(g, clear)) continue;
            Spawn(k, g, r.Next(2) == 0);
            return g;
        }
        return null;
    }

    void TrySpawnNear(NodeKind k, Vector2 g)
    {
        int x = Mathf.RoundToInt(g.x), y = Mathf.RoundToInt(g.y);
        if (x < 1 || y < 1 || x >= Size - 1 || y >= Size - 1 || !(tiles[x, y] is 1 or 2)) return;
        foreach (var n in nodes.FindAll(n => n != null && (n.grid - g).magnitude < 0.8f && !n.Blocking)) RemoveNode(n);
        if (FreeSpot(g, 0.6f)) Spawn(k, g, false);
    }

    /// Treasure: a few useful things, sometimes something rare.
    void OpenChest(WorldNode n)
    {
        var r = new System.Random(seed + n.id * 31);
        Item local = biome switch
        {
            Biome.Mushroom => Item.GlowSpore, Biome.Turtle => Item.ShellShard, Biome.Sky => Item.CloudCotton,
            Biome.Bubble => Item.Pearl, Biome.Hourglass => Item.Gear, Biome.Ice => Item.IceShard,
            Biome.Jungle => Item.Bamboo, Biome.Volcanic => Item.Obsidian, Biome.Tribal => Item.Cloth, _ => Item.Wood,
        };
        Vector3 at = n.transform.position;
        Give(local, 3 + r.Next(3), n);
        Give(new[] { Item.CookedFish, Item.CookedMeat, Item.CookedFruit }[r.Next(3)], 2, n);
        Give(new[] { Item.Rope, Item.Wood, Item.Stone }[r.Next(3)], 4 + r.Next(4), n);
        if (r.Next(4) == 0) Give(Item.Antidote, 1, n);
        if (r.Next(8) == 0) Give(FruitFor(biome), 1, n); // a special fruit!
        AddXp(10 + island * 2, at);
        Fx.Burst(at + Vector3.up * 0.4f, new Color(1f, 0.85f, 0.3f), 18, 1.2f, 1.6f, 1f, 2f);
        Sfx.Play("levelup", 0.5f);
        n.Deplete(1e9f); // stays open
    }

    void PrayAtShrine(WorldNode n)
    {
        if (Flag("shrine" + island)) { Hint("ศาลเจ้าเงียบสงบ เจ้าได้รับพรไปแล้ว", 2f); return; }
        flags.Add("shrine" + island);
        skillPoints++;
        health = MaxHealth; hunger = 100f; thirst = 100f;
        Fx.Burst(n.transform.position + Vector3.up * 0.8f, new Color(1f, 0.95f, 0.6f), 26, 1.6f, 1.6f, 1.2f, 2f);
        Popup("ได้รับพร! +1 แต้มสกิล", PlayerPos + Vector3.up * 1.2f, new Color(1f, 0.9f, 0.5f));
        Hint("พลังจากศาลเจ้าโบราณ: ฟื้นฟูเต็ม และได้แต้มสกิล 1 แต้ม (กด K)", 5f);
        Sfx.Play("levelup", 0.7f);
    }

    // ------------------------------------------------------------------ weather
    void UpdateWeather(float dt)
    {
        if (player == null || voyage != null || cam == null) return;
        Vector3 c = cam.transform.position;
        switch (T.weather)
        {
            case Weather.Rain:
                for (int i = 0; i < 2; i++)
                    if (Random.value < dt * 30f) Fx.Burst(c + new Vector3(Random.Range(-7f, 7f), 4f, 0f), new Color(0.6f, 0.75f, 1f, 0.8f), 1, 0.05f, 0.2f, 1.2f, 6f);
                break;
            case Weather.Leaves:
                if (Random.value < dt * 5f) Fx.Burst(c + new Vector3(Random.Range(-7f, 7f), 4f, 0f), Random.value < 0.5f ? new Color(0.9f, 0.6f, 0.2f) : new Color(0.5f, 0.8f, 0.3f), 1, 0.3f, 0.4f, 4f, 0.5f);
                break;
            case Weather.Fireflies:
                if (IsNight && Random.value < dt * 6f) Fx.Burst(PlayerPos + new Vector3(Random.Range(-4f, 4f), Random.Range(-2.5f, 2.5f), 0f), new Color(0.85f, 1f, 0.4f), 1, 0.2f, 0.2f, 2f, -0.2f);
                break;
        }
    }

    void DrawWeatherOverlay(float W, float H)
    {
        if (voyage != null) return;
        if (T.weather == Weather.Fog) Fill(new Rect(0, 0, W, H), new Color(0.85f, 0.88f, 0.92f, 0.22f));
        else if (T.weather == Weather.Rain) Fill(new Rect(0, 0, W, H), new Color(0.2f, 0.25f, 0.35f, 0.15f));
    }

    // ------------------------------------------------------------------ alpha (island boss)
    void SpawnAlpha()
    {
        if (!T.alpha || Flag("alpha" + island) || !IsHost) return;
        if (enemies.Exists(e => e != null && e.alpha)) return;
        Vector2 at = FarFrom(ShipwrightSpot(), Center);
        var e = new GameObject("Alpha").AddComponent<Enemy>();
        e.transform.SetParent(world);
        e.Init(T.alphaKind, at, tier, ++enemyCounter);
        e.MakeAlpha();
        enemies.Add(e);
    }

    /// Things that happen when a creature dies by your hand (alpha loot, stolen goods back).
    void KillExtras(Enemy e)
    {
        ReturnStolen(e);
        if (!e.alpha) return;
        flags.Add("alpha" + island);
        Hint($"ล้มจ่าฝูง{EnemyName(e.kind)}แล้ว! ได้สมบัติจากรังของมัน", 6f);
        var r = new System.Random(seed + 99);
        if (r.Next(3) == 0) Give(FruitFor(biome), 1, null);
        Give(Item.CookedMeat, 3, null);
        AddXp(60 + island * 6, e.transform.position);
        skillPoints++;
        Popup("+1 แต้มสกิล", PlayerPos + Vector3.up * 1.4f, new Color(1f, 0.85f, 0.3f));
    }

    // ------------------------------------------------------------------ the local's job
    /// One job per island from the first townsfolk: hunt, gather, or find something lost.
    Quest LocalQuest()
    {
        var r = new System.Random(seed * 3 + 77);
        int kind = r.Next(3);
        var kinds = BiomeEnemies();
        var q = new Quest { id = $"local{island}", rewardText = "ค่าตอบแทนและประสบการณ์" };
        q.reward = () =>
        {
            AddXp(30 + island * 4, PlayerPos);
            Give(new[] { Item.CookedMeat, Item.CookedFish, Item.CookedFruit }[r.Next(3)], 3, null);
            if (r.Next(3) == 0) Give(Item.Antidote, 1, null);
        };
        if (kind == 0)
        {
            var k = kinds[r.Next(kinds.Length)];
            q.title = $"ปราบ{EnemyName(k)}ที่ก่อกวน";
            q.kill = new[] { (k, 4 + tier) };
        }
        else if (kind == 1)
        {
            Item it = biome switch
            {
                Biome.Mushroom => Item.GlowSpore, Biome.Turtle => Item.ShellShard, Biome.Sky => Item.CloudCotton,
                Biome.Bubble => Item.Coral, Biome.Hourglass => Item.TimeSand, Biome.Ice => Item.IceShard,
                Biome.Jungle => Item.Bamboo, Biome.Volcanic => Item.Obsidian, _ => Item.Wood,
            };
            q.title = $"หา{Name(it)}มาซ่อมบ้าน";
            q.bring = new[] { (it, 6 + tier * 2), (Item.Rope, 3) };
        }
        else
        {
            q.title = "ตามหาห่อของที่ทำหาย";
            q.help = new[] { ($"lost{island}", "หาห่อของสีแดงที่ตกอยู่ในป่า แล้วเก็บกลับมา") };
        }
        return q;
    }

    string LocalIntro(Quest q) =>
        q.kill.Length > 0 ? $"ช่วยด้วย! {q.title.Replace("ปราบ", "").Replace("ที่ก่อกวน", "")}มาบุกไร่ของข้าทุกคืน ช่วยไล่ให้หน่อยได้ไหม"
        : q.bring.Length > 0 ? "พายุพัดหลังคาบ้านข้าพังหมดเลย ช่วยหาของมาซ่อมหน่อยนะ"
        : "ข้าทำห่อของสีแดงตกไว้ตอนเข้าไปหาของป่า ในนั้นมีของสำคัญของครอบครัว ช่วยตามหาให้ทีได้ไหม";

    /// The lost bundle for the "find it" job: placed far out once the job is taken.
    void PlaceLostBundle()
    {
        if (Flag($"lost{island}") || nodes.Exists(n => n != null && n.kind == NodeKind.LostBundle)) return;
        var at = FarFrom(ShipwrightSpot(), Center);
        Spawn(NodeKind.LostBundle, WalkableNear(at), false);
    }

    void PickLostBundle(WorldNode n)
    {
        flags.Add($"lost{island}");
        Popup("เจอห่อของแล้ว!", n.transform.position + Vector3.up * 0.6f, new Color(1f, 0.9f, 0.5f));
        Hint("เจอห่อของที่หายแล้ว! กลับไปส่งให้เจ้าของ", 4f);
        Sfx.Play("pickup", 0.6f);
        RemoveNode(n);
    }
}
