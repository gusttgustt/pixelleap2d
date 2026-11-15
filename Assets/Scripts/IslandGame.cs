using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public enum Item
{
    Fiber, Stick, Stone, Wood, Berry, Fruit, CookedFruit, Rope, StoneAxe,
    Spear, StoneKnife, RawFish, CookedFish, CrabMeat, CookedCrab,
    RawMeat, CookedMeat, Bamboo, Obsidian, WaterFlask, ObsidianSpear, ObsidianAxe,
    Hide, Cloth, StrawHat, LeafTunic, HideVest, TribalHeadband,
}

public enum NodeKind { Tree, FruitTree, Bush, Grass, Stick, Rock, Stump, Campfire, Shelter, Raft, Bamboo, Obsidian, Hut, Totem }

public enum Biome { Tropical, Jungle, Volcanic, Tribal }

public enum Skill { Strength, Toughness, Agility, Evasion, Forager, Lumberjack, IronStomach, Fisher }

public class Recipe
{
    public string name, desc;
    public Item? output;        // item added to the inventory, or
    public NodeKind? places;    // structure built next to the player
    public (Item item, int n)[] cost;
    public bool needsFire, atShore;
    public bool raftUpgrade;    // cost depends on the island's biome and raft level (see RefreshRaftUpgrade)
    public bool learn;          // must be taught by the tribe's weaver first
}

/// <summary>
/// Island survival adventure. Starts itself automatically when you press Play in any scene.
/// Split into partial files:
///   IslandGame.cs       - setup, world generation, islands, save/load, world queries
///   IslandGame.Play.cs  - main loop, survival, gathering, crafting, combat, XP/skills, sleep, sailing
///   IslandGame.Hud.cs   - on-screen UI
/// </summary>
public partial class IslandGame : MonoBehaviour
{
    public static IslandGame I { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<IslandGame>() == null) new GameObject("IslandGame").AddComponent<IslandGame>();
    }

    // ------------------------------------------------------------------ isometric math
    // A tile is 1 unit wide and 0.5 units tall (32x16 px at 32 PPU).
    public const float HalfW = 0.5f, HalfH = 0.25f;
    public static Vector3 GridToWorld(Vector2 g) => new Vector3((g.x - g.y) * HalfW, -(g.x + g.y) * HalfH, 0f);
    public static Vector2 WorldDeltaToGrid(Vector2 w)
    {
        float a = w.x / HalfW, b = -w.y / HalfH;
        return new Vector2((a + b) * 0.5f, (b - a) * 0.5f);
    }
    public static int SortOrder(float worldY) => Mathf.RoundToInt(-worldY * 100f);

    // ------------------------------------------------------------------ tuning
    public const int Size = 44;            // map is Size x Size tiles
    const float DayLength = 240f;          // seconds per full day
    const float StarveSeconds = 300f;      // full -> empty hunger
    const float ThirstSeconds = 220f;      // full -> empty thirst
    const float InteractRange = 1.3f;
    const float AutosaveInterval = 30f;
    const float RespawnInterval = 15f;     // how often a new stick / rock may appear
    const int FlaskSize = 3;

    public static readonly Recipe[] Recipes =
    {
        new Recipe { name = "Rope", output = Item.Rope, cost = new[] { (Item.Fiber, 3) }, desc = "Twist plant fibers together" },
        new Recipe { name = "Stone Knife", output = Item.StoneKnife, cost = new[] { (Item.Stone, 1), (Item.Stick, 1) }, desc = "More fiber from grass, more berries" },
        new Recipe { name = "Stone Axe", output = Item.StoneAxe, cost = new[] { (Item.Stick, 2), (Item.Stone, 2), (Item.Rope, 1) }, desc = "Chop trees for wood" },
        new Recipe { name = "Spear", output = Item.Spear, cost = new[] { (Item.Stick, 3), (Item.Stone, 1), (Item.Rope, 1) }, desc = "Fish at the sea, 3 damage" },
        new Recipe { name = "Campfire", places = NodeKind.Campfire, cost = new[] { (Item.Wood, 3), (Item.Stone, 4), (Item.Stick, 2) }, desc = "Light, cooking, scares enemies" },
        new Recipe { name = "Shelter", places = NodeKind.Shelter, cost = new[] { (Item.Wood, 6), (Item.Rope, 3), (Item.Fiber, 6) }, desc = "Sleep through the night" },
        new Recipe { name = "Cooked Fruit", output = Item.CookedFruit, cost = new[] { (Item.Fruit, 1) }, needsFire = true, desc = "+40 hunger, +10 health" },
        new Recipe { name = "Cooked Fish", output = Item.CookedFish, cost = new[] { (Item.RawFish, 1) }, needsFire = true, desc = "+35 hunger, +10 health" },
        new Recipe { name = "Cooked Crab", output = Item.CookedCrab, cost = new[] { (Item.CrabMeat, 1) }, needsFire = true, desc = "+45 hunger, +15 health" },
        new Recipe { name = "Raft", places = NodeKind.Raft, atShore = true, cost = new[] { (Item.Wood, 12), (Item.Rope, 6), (Item.Stick, 4) }, desc = "Your boat for the whole journey. Build at the shore" },
        new Recipe { name = "Upgrade Raft", raftUpgrade = true, cost = new (Item, int)[0], desc = "" },
        new Recipe { name = "Cooked Meat", output = Item.CookedMeat, cost = new[] { (Item.RawMeat, 1) }, needsFire = true, desc = "+50 hunger, +15 health" },
        new Recipe { name = "Water Flask", output = Item.WaterFlask, cost = new[] { (Item.Bamboo, 2), (Item.Rope, 1) }, desc = "Carry 3 drinks (Q). Refills at ponds" },
        new Recipe { name = "Obsidian Spear", output = Item.ObsidianSpear, cost = new[] { (Item.Obsidian, 2), (Item.Stick, 2), (Item.Rope, 1) }, desc = "Sharp! 6 damage, fishing" },
        new Recipe { name = "Obsidian Axe", output = Item.ObsidianAxe, cost = new[] { (Item.Obsidian, 2), (Item.Stick, 2), (Item.Rope, 1) }, desc = "Fells trees in 2 fewer hits" },
        // Taught by the tribe (Tribe Island):
        new Recipe { name = "Cloth", output = Item.Cloth, learn = true, cost = new[] { (Item.Fiber, 4) }, desc = "Woven fiber for clothes and sails" },
        new Recipe { name = "Straw Hat", output = Item.StrawHat, learn = true, cost = new[] { (Item.Fiber, 6), (Item.Rope, 1) }, desc = "Hat: thirst drains 15% slower" },
        new Recipe { name = "Tribal Headband", output = Item.TribalHeadband, learn = true, cost = new[] { (Item.Cloth, 2), (Item.Hide, 1) }, desc = "Hat: +1 attack damage" },
        new Recipe { name = "Leaf Tunic", output = Item.LeafTunic, learn = true, cost = new[] { (Item.Fiber, 8), (Item.Rope, 2), (Item.Cloth, 1) }, desc = "Body: +15 max health" },
        new Recipe { name = "Hide Vest", output = Item.HideVest, learn = true, cost = new[] { (Item.Hide, 3), (Item.Rope, 2), (Item.Cloth, 1) }, desc = "Body: take 20% less damage" },
    };

    public static readonly (string name, string desc, int max, bool combat)[] SkillInfo =
    {
        ("Strength", "+1 attack damage", 5, true),
        ("Toughness", "+15 max health", 5, true),
        ("Agility", "+8% move speed", 3, true),
        ("Evasion", "+10% chance to dodge a hit", 3, true),
        ("Forager", "+30% chance of an extra item when gathering", 3, false),
        ("Lumberjack", "1 fewer chop per tree", 2, false),
        ("Iron Stomach", "Hunger & thirst drain 12% slower", 3, false),
        ("Fisher", "+15% fishing success", 3, false),
    };

    // ------------------------------------------------------------------ state
    [System.NonSerialized] public int[,] tiles;   // 0 sea, 1 sand, 2 grass, 3 fresh water (pond)
    [System.NonSerialized] public int[,] heights; // terrain level 0..2 on grass
    public readonly List<WorldNode> nodes = new List<WorldNode>();
    public readonly List<Enemy> enemies = new List<Enemy>();
    public readonly Dictionary<Item, int> inv = new Dictionary<Item, int>();
    public float health, hunger, thirst;
    public bool dead, craftOpen, skillsOpen;
    public PlayerController player;
    public WorldNode target;
    public int island = 1, tier, level = 1, xp, skillPoints, flaskWater;
    /// 0 = no raft yet. The raft travels with you; Lv N is strong enough to reach island N+1.
    public int raftLevel;
    /// Clothing recipes taught by the tribe, and what is being worn.
    public readonly HashSet<Item> learned = new HashSet<Item>();
    public Item? hat, outfit;
    int elderHealDay;
    public Biome biome;
    readonly int[] skills = new int[System.Enum.GetValues(typeof(Skill)).Length];
    /// Game time in seconds. Unlike Time.time it jumps forward when you sleep.
    public float Now { get; private set; }

    Transform world;
    readonly List<(SpriteRenderer sr, int parity, bool fresh)> water = new List<(SpriteRenderer, int, bool)>();
    float waterTimer, clock, daylight = 1f, autosaveTimer, respawnTimer;
    int waterFrame, zoom, seed, stickCap, rockCap, obsidianCap;
    Camera cam;
    Vector3 camPos;
    Light2D sun;
    SpriteRenderer cursor;
    System.Random rnd;

    public Vector2 Center => new Vector2((Size - 1) / 2f, (Size - 1) / 2f);
    public int Count(Item it) => inv.TryGetValue(it, out var n) ? n : 0;
    public float Phase => Mathf.Repeat(clock / DayLength + 0.3f, 1f); // 0 = midnight, 0.5 = noon
    public int Day => 1 + Mathf.FloorToInt(clock / DayLength + 0.05f);
    public bool IsNight => daylight < 0.35f;
    public int SkillLv(Skill s) => skills[(int)s];
    public float MaxHealth => 100f + 15f * SkillLv(Skill.Toughness) + (outfit == Item.LeafTunic ? 15f : 0f);
    public float SpeedMultiplier => 1f + 0.08f * SkillLv(Skill.Agility);
    public static int XpToNext(int lv) => 25 + 15 * lv;

    public string BiomeName => biome switch { Biome.Jungle => "Jungle Island", Biome.Volcanic => "Volcano Island", Biome.Tribal => "Tribe Island", _ => "Tropical Island" };

    /// The island chain: Tropical -> Jungle -> Tribe -> Volcano, then again with tougher creatures.
    public static Biome BiomeFor(int islandNumber) =>
        new[] { Biome.Tropical, Biome.Jungle, Biome.Tribal, Biome.Volcanic }[(islandNumber - 1) % 4];

    /// Colour tint for plants that have no biome-specific sprite.
    public Color Tint(NodeKind k) =>
        biome == Biome.Volcanic && k is NodeKind.Grass or NodeKind.Bush ? new Color(0.85f, 0.78f, 0.6f)
        : biome == Biome.Jungle && k is NodeKind.Grass or NodeKind.Bush ? new Color(0.8f, 0.95f, 0.8f)
        : Color.white;

    // ------------------------------------------------------------------ items
    public static int FoodValue(Item it) => it switch
    {
        Item.Berry => 10, Item.Fruit => 20, Item.CookedFruit => 40,
        Item.RawFish => 8, Item.CookedFish => 35, Item.CrabMeat => 8, Item.CookedCrab => 45,
        Item.RawMeat => 10, Item.CookedMeat => 50,
        _ => 0,
    };
    public static int HealValue(Item it) => it switch { Item.CookedFruit => 10, Item.CookedFish => 10, Item.CookedCrab => 15, Item.CookedMeat => 15, _ => 0 };
    public static int ThirstValue(Item it) => it switch { Item.Berry => 3, Item.Fruit => 6, _ => 0 }; // juicy food helps a little
    public static bool IsTool(Item it) => it is Item.StoneAxe or Item.Spear or Item.StoneKnife or Item.WaterFlask or Item.ObsidianSpear or Item.ObsidianAxe;
    public static bool IsHat(Item it) => it is Item.StrawHat or Item.TribalHeadband;
    public static bool IsOutfit(Item it) => it is Item.LeafTunic or Item.HideVest;
    public static bool IsClothing(Item it) => IsHat(it) || IsOutfit(it);
    public bool IsWorn(Item it) => hat == it || outfit == it;
    public static bool IsCooked(Item it) => it is Item.CookedFruit or Item.CookedFish or Item.CookedCrab or Item.CookedMeat;

    public static string Name(Item it) => it switch
    {
        Item.Fiber => "Plant Fiber",
        Item.Berry => "Berries",
        Item.CookedFruit => "Cooked Fruit",
        Item.StoneAxe => "Stone Axe",
        Item.StoneKnife => "Stone Knife",
        Item.RawFish => "Raw Fish",
        Item.CookedFish => "Cooked Fish",
        Item.CrabMeat => "Crab Meat",
        Item.CookedCrab => "Cooked Crab",
        Item.RawMeat => "Raw Meat",
        Item.CookedMeat => "Cooked Meat",
        Item.WaterFlask => "Water Flask",
        Item.ObsidianSpear => "Obsidian Spear",
        Item.ObsidianAxe => "Obsidian Axe",
        Item.StrawHat => "Straw Hat",
        Item.LeafTunic => "Leaf Tunic",
        Item.HideVest => "Hide Vest",
        Item.TribalHeadband => "Tribal Headband",
        _ => it.ToString(),
    };

    // ------------------------------------------------------------------ setup
    void Awake()
    {
        I = this;
        SetupCamera();
        SetupSun();
        Sfx.Init(gameObject);
        StartGame(SaveSystem.Read());
    }

    static SaveData NewRun() => new SaveData
    {
        seed = System.Environment.TickCount, island = 1, fresh = true,
        level = 1, health = 100f, hunger = 100f, thirst = 100f,
    };

    /// Loads an island from save data. data == null starts a brand new run on island 1.
    /// data.fresh means the island hasn't been generated yet (new run / just arrived by raft).
    void StartGame(SaveData data)
    {
        if (data == null) data = NewRun();
        bool fresh = data.fresh;
        voyage = null; // always start on land

        if (world != null) Destroy(world.gameObject);
        world = new GameObject("World").transform;
        world.SetParent(transform);
        nodes.Clear(); enemies.Clear(); water.Clear(); inv.Clear(); popups.Clear();
        dead = false; craftOpen = false; skillsOpen = false;
        Now = 0f; autosaveTimer = 0f; respawnTimer = 0f; enemyTimer = 0f; sleepT = 0f; nightWarnedDay = 0;
        stickCap = 0; rockCap = 0; obsidianCap = 0; pond = null; seaSpot = null; target = null; enemyTarget = null;

        island = Mathf.Max(1, data.island);
        // Saves before v6 used a 3-island cycle; keep their island the same biome it was.
        biome = data.biomeId > 0 ? (Biome)(data.biomeId - 1) : data.version < 6 ? (Biome)((island - 1) % 3) : BiomeFor(island);
        tier = (island - 1) / 4;
        seed = data.seed;
        raftLevel = data.raftLevel;
        learned.Clear();
        foreach (int l in data.learned) learned.Add((Item)l);
        hat = data.hatId > 0 ? (Item)(data.hatId - 1) : (Item?)null;
        outfit = data.outfitId > 0 ? (Item)(data.outfitId - 1) : (Item?)null;
        elderHealDay = data.elderHealDay;
        villagers.Clear();
        dialog = null;
        rnd = new System.Random(seed);
        BuildIsland(Center, spawnResources: fresh);
        if (fresh && biome == Biome.Tribal) BuildVillage();

        Vector2? landing = null;
        if (!fresh)
        {
            foreach (var ns in data.nodes) Spawn(ns.kind, ns.grid, ns.flip).Restore(ns.available, ns.hits, ns.regrow);
            stickCap = data.stickCap; rockCap = data.rockCap; obsidianCap = data.obsidianCap;
            // v4 saves had no raft level: a raft already standing on this island counts as ready to sail.
            if (data.version < 5 && raftLevel == 0 && RaftNode != null) { raftLevel = island; RaftNode.Refresh(); }
        }
        else if (raftLevel > 0) landing = BeachRaft(); // arrived by raft: it's pulled up on the west shore
        foreach (var st in data.items) if (st.count > 0) inv[st.item] = st.count;
        System.Array.Clear(skills, 0, skills.Length);
        for (int i = 0; i < skills.Length && i < data.skills.Count; i++) skills[i] = data.skills[i];
        level = Mathf.Max(1, data.level); xp = data.xp; skillPoints = data.skillPoints; flaskWater = data.flaskWater;
        health = Mathf.Min(data.health, MaxHealth); hunger = data.hunger; thirst = data.thirst; clock = data.clock;
        if (biome == Biome.Tribal) SpawnVillagers();

        Vector2 start = !fresh && IsLand(data.player) ? data.player : landing ?? Center;
        player = new GameObject("Player").AddComponent<PlayerController>();
        player.transform.SetParent(world);
        player.Init(start);

        cursor = new GameObject("Cursor").AddComponent<SpriteRenderer>();
        cursor.transform.SetParent(world);
        cursor.sprite = PixelArt.Cursor();
        cursor.sortingOrder = -30000; // above ground, below objects

        camPos = player.transform.position;
        if (!fresh) Hint($"Welcome back! {BiomeName} (island {island}), day {Day}.", 4f);
        else if (island == 1) Hint("Find fresh water (light-blue ponds) and food. Gather materials, Tab to craft.", 8f);
        else Hint($"Land ho! Island {island}: {BiomeName}. {BiomeIntro()}", 9f);
        if (fresh) SaveGame(); // replaces any old save right away
    }

    string BiomeIntro() => biome switch
    {
        Biome.Jungle => "Snakes hide in the grass. Upgrade your raft with Bamboo to go on.",
        Biome.Volcanic => "Wild boars roam the ash. Upgrade your raft with Obsidian to go on.",
        Biome.Tribal => "Smoke rises from a village in the middle of the island. Go and meet the tribe!",
        _ => tier > 0 ? "The creatures here look stronger than before..." : "Upgrade your raft with wood to go on.",
    };

    public WorldNode RaftNode => nodes.Find(n => n != null && n.kind == NodeKind.Raft);

    /// Puts the raft on the shore tile facing the way we came (screen-left / west) and returns
    /// a spot next to it for the player.
    Vector2 BeachRaft()
    {
        Vector2? best = null;
        float bestScore = float.MaxValue;
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
            {
                if (tiles[x, y] != 1 && tiles[x, y] != 2) continue;
                var g = new Vector2(x, y);
                if (!TouchesSea(g)) continue;
                // smallest x-y = furthest west on screen; prefer the middle of the island vertically
                float score = (x - y) + Mathf.Abs(x + y - (Size - 1)) * 0.5f;
                if (score < bestScore) { bestScore = score; best = g; }
            }
        Vector2 raftAt = best ?? Center;
        Vector2 inland = (Center - raftAt).normalized;
        Vector2 stand = raftAt + inland * 1.3f;
        if (!IsLand(stand)) stand = Center;

        // clear whatever grew on the landing spot
        foreach (var n in nodes.FindAll(n => (n.grid - raftAt).magnitude < 1.3f || (n.grid - stand).magnitude < 0.9f))
            RemoveNode(n);
        Spawn(NodeKind.Raft, raftAt, false);
        return stand;
    }

    /// Arrive at the next island, keeping inventory, level and skills.
    void NextIsland()
    {
        var d = Snapshot();
        d.fresh = true;
        d.island = island + 1;
        d.biomeId = (int)BiomeFor(island + 1) + 1;
        d.seed = new System.Random(seed + island * 7919).Next();
        d.nodes.Clear();
        StartGame(d);
    }

    // ------------------------------------------------------------------ save / load
    SaveData Snapshot()
    {
        var d = new SaveData
        {
            seed = seed, island = island, clock = clock, health = health, hunger = hunger, thirst = thirst,
            player = player != null ? player.grid : Center, stickCap = stickCap, rockCap = rockCap, obsidianCap = obsidianCap,
            level = level, xp = xp, skillPoints = skillPoints, flaskWater = flaskWater, raftLevel = raftLevel,
            biomeId = (int)biome + 1, hatId = hat.HasValue ? (int)hat.Value + 1 : 0,
            outfitId = outfit.HasValue ? (int)outfit.Value + 1 : 0, elderHealDay = elderHealDay,
        };
        d.skills.AddRange(skills);
        foreach (var l in learned) d.learned.Add((int)l);
        foreach (var kv in inv) if (kv.Value > 0) d.items.Add(new ItemStack { item = kv.Key, count = kv.Value });
        foreach (var n in nodes)
            if (n != null)
                d.nodes.Add(new NodeSave { kind = n.kind, grid = n.grid, available = n.available, flip = n.flip, hits = n.hits, regrow = n.RegrowRemaining });
        return d;
    }

    public void SaveGame()
    {
        // At sea or mid-fade there's nothing to save: the last save (next to the raft) stands.
        if (dead || player == null || sailT > 0f || voyage != null) return;
        SaveSystem.Write(Snapshot());
        savedAt = Time.time;
        autosaveTimer = 0f;
    }

    void OnApplicationQuit() => SaveGame();          // also fires when you stop Play mode
    void OnApplicationPause(bool paused) { if (paused) SaveGame(); }

    // ------------------------------------------------------------------ world generation
    void BuildIsland(Vector2 center, bool spawnResources)
    {
        float ox = (float)rnd.NextDouble() * 1000f, oy = (float)rnd.NextDouble() * 1000f;
        float hx = (float)rnd.NextDouble() * 1000f, hy = (float)rnd.NextDouble() * 1000f;
        float radius = Size * 0.36f;
        tiles = new int[Size, Size];
        heights = new int[Size, Size];

        for (int gx = 0; gx < Size; gx++)
            for (int gy = 0; gy < Size; gy++)
            {
                float d = Vector2.Distance(new Vector2(gx, gy), center) / radius;
                float n = Mathf.PerlinNoise(ox + gx * 0.13f, oy + gy * 0.13f);
                float v = 1f - d + (n - 0.5f) * 0.7f;
                tiles[gx, gy] = d < 0.25f || v > 0.3f ? 2 : v > 0.13f ? 1 : 0;
            }

        float hillBias = biome switch { Biome.Volcanic => 0.1f, Biome.Jungle => 0.03f, _ => 0f };
        BuildHills(center, radius, hx, hy, hillBias);
        CarvePonds(center);

        // Ground tiles. Water is drawn under all land so cliff edges show over the sea.
        var ground = new GameObject("Ground").transform;
        ground.SetParent(world);
        for (int gx = 0; gx < Size; gx++)
            for (int gy = 0; gy < Size; gy++)
            {
                var sr = new GameObject("Tile").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(ground);
                Vector3 p = GridToWorld(new Vector2(gx, gy));
                int t = tiles[gx, gy];
                if (t == 0 || t == 3)
                {
                    bool fresh = t == 3;
                    p.y -= 0.125f;
                    int parity = (gx + gy) & 1;
                    sr.sprite = PixelArt.Water(parity, fresh);
                    sr.sortingOrder = -32000 + gx + gy;
                    water.Add((sr, parity, fresh));
                }
                else
                {
                    int h = heights[gx, gy];
                    sr.sprite = PixelArt.Land(t == 2, rnd.Next(4), h, biome);
                    sr.sortingOrder = -31000 + gx + gy;
                    p.y += PixelArt.HeightUnits(h);
                }
                sr.transform.position = p;
            }

        if (!spawnResources) return; // loading a save: resources come from the save file

        // Resources (cumulative chance tables per biome)
        for (int gx = 0; gx < Size; gx++)
            for (int gy = 0; gy < Size; gy++)
            {
                int t = tiles[gx, gy];
                var g = new Vector2(gx, gy);
                if (t == 0 || t == 3 || Vector2.Distance(g, center) < 2.5f) continue;
                var k = PickResource(t == 2, rnd.NextDouble());
                if (k != null) Spawn(k.Value, g + new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 0.4f);
            }

        // A starter kit close to the landing point
        Spawn(NodeKind.Stick, center + new Vector2(2.2f, -0.4f));
        Spawn(NodeKind.Stick, center + new Vector2(-0.5f, 2.4f));
        Spawn(NodeKind.Rock, center + new Vector2(2.5f, 1.2f));
        Spawn(NodeKind.Rock, center + new Vector2(-2.3f, -1f));
        Spawn(NodeKind.Grass, center + new Vector2(1f, 2.6f));
        Spawn(NodeKind.Grass, center + new Vector2(-2.6f, 1.2f));
        Spawn(NodeKind.Grass, center + new Vector2(0.3f, -2.7f));
        Spawn(NodeKind.Bush, center + new Vector2(-1.6f, -2.7f));

        // Respawn keeps the island at roughly its starting amount of these materials.
        foreach (var n in nodes)
        {
            if (n.kind == NodeKind.Stick) stickCap++;
            else if (n.kind == NodeKind.Rock) rockCap++;
            else if (n.kind == NodeKind.Obsidian) obsidianCap++;
        }
    }

    NodeKind? PickResource(bool grass, double r)
    {
        (NodeKind kind, double chance)[] table = (biome, grass) switch
        {
            (Biome.Jungle, true) => new[] { (NodeKind.Tree, .16), (NodeKind.FruitTree, .045), (NodeKind.Bush, .05), (NodeKind.Bamboo, .06), (NodeKind.Grass, .14), (NodeKind.Stick, .04), (NodeKind.Rock, .02) },
            (Biome.Volcanic, true) => new[] { (NodeKind.Tree, .05), (NodeKind.FruitTree, .015), (NodeKind.Bush, .02), (NodeKind.Grass, .07), (NodeKind.Stick, .04), (NodeKind.Rock, .06), (NodeKind.Obsidian, .035) },
            (Biome.Volcanic, false) => new[] { (NodeKind.Stick, .03), (NodeKind.Rock, .05), (NodeKind.Obsidian, .03) },
            (Biome.Tribal, true) => new[] { (NodeKind.Tree, .08), (NodeKind.FruitTree, .06), (NodeKind.Bush, .06), (NodeKind.Grass, .18), (NodeKind.Stick, .04), (NodeKind.Rock, .025), (NodeKind.Bamboo, .02) },
            (_, true) => new[] { (NodeKind.Tree, .10), (NodeKind.FruitTree, .035), (NodeKind.Bush, .04), (NodeKind.Grass, .145), (NodeKind.Stick, .04), (NodeKind.Rock, .025) },
            _ => new[] { (NodeKind.Stick, .05), (NodeKind.Rock, .05) },
        };
        double acc = 0;
        foreach (var (kind, chance) in table)
        {
            acc += chance;
            if (r < acc) return kind;
        }
        return null;
    }

    /// Raises hills (levels 1-2). Neighbouring tiles never differ by more than one level,
    /// so every hill can be walked onto.
    void BuildHills(Vector2 center, float radius, float hx, float hy, float bias)
    {
        for (int gx = 0; gx < Size; gx++)
            for (int gy = 0; gy < Size; gy++)
            {
                if (tiles[gx, gy] != 2 || Vector2.Distance(new Vector2(gx, gy), center) < 3f) continue;
                float d = Vector2.Distance(new Vector2(gx, gy), center) / radius;
                float v = Mathf.PerlinNoise(hx + gx * 0.09f, hy + gy * 0.09f) - d * 0.3f + bias;
                heights[gx, gy] = v > 0.55f ? 2 : v > 0.42f ? 1 : 0;
            }
        for (int pass = 0; pass < 4; pass++)
            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                {
                    if (heights[x, y] == 0) continue;
                    int min = int.MaxValue;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            int nh = !InMap(nx, ny) || tiles[nx, ny] != 2 ? 0 : heights[nx, ny];
                            min = Mathf.Min(min, nh);
                        }
                    if (heights[x, y] - min > 1) heights[x, y] = min + 1;
                }
    }

    /// Carves 2-3 small freshwater ponds into flat grass, one of them fairly close to spawn.
    void CarvePonds(Vector2 center)
    {
        var candidates = new List<Vector2Int>();
        for (int x = 2; x < Size - 2; x++)
            for (int y = 2; y < Size - 2; y++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                if (d < 5f) continue;
                bool ok = true; // keep ponds away from the coast and off hills
                for (int dx = -2; dx <= 2 && ok; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                        if (tiles[x + dx, y + dy] != 2 || heights[x + dx, y + dy] != 0) { ok = false; break; }
                if (ok) candidates.Add(new Vector2Int(x, y));
            }

        var chosen = new List<Vector2Int>();
        var near = candidates.FindAll(c => Vector2.Distance(c, center) < 9f);
        if (near.Count > 0) chosen.Add(near[rnd.Next(near.Count)]);
        for (int tries = 0; tries < 40 && chosen.Count < 3 && candidates.Count > 0; tries++)
        {
            var c = candidates[rnd.Next(candidates.Count)];
            if (chosen.TrueForAll(o => Vector2.Distance(o, c) > 7f)) chosen.Add(c);
        }

        foreach (var c in chosen)
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= 1.1f || (d <= 1.6f && rnd.NextDouble() < 0.6)) tiles[c.x + dx, c.y + dy] = 3;
                }
    }

    public WorldNode Spawn(NodeKind k, Vector2 g, bool? flip = null)
    {
        var n = new GameObject(k.ToString()).AddComponent<WorldNode>();
        n.transform.SetParent(world);
        n.Init(k, g, flip ?? rnd.Next(2) == 0);
        nodes.Add(n);
        return n;
    }

    void RemoveNode(WorldNode n)
    {
        nodes.Remove(n);
        if (target == n) target = null;
        Destroy(n.gameObject);
    }

    // ------------------------------------------------------------------ world queries
    static Vector2Int Tile(Vector2 g) => new Vector2Int(Mathf.RoundToInt(g.x), Mathf.RoundToInt(g.y));
    static bool InMap(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size;

    public bool IsLand(Vector2 g)
    {
        var t = Tile(g);
        return InMap(t.x, t.y) && (tiles[t.x, t.y] == 1 || tiles[t.x, t.y] == 2);
    }

    bool IsFresh(int x, int y) => InMap(x, y) && tiles[x, y] == 3;
    bool IsSea(int x, int y) => !InMap(x, y) || tiles[x, y] == 0;

    int LevelAt(Vector2 g)
    {
        var t = Tile(g);
        return InMap(t.x, t.y) && tiles[t.x, t.y] == 2 ? heights[t.x, t.y] : 0;
    }

    /// Terrain height under a grid position, in world units.
    public float HeightAt(Vector2 g) => PixelArt.HeightUnits(LevelAt(g));

    /// Can only step up or down one level at a time.
    public bool CanClimb(Vector2 from, Vector2 to) => Mathf.Abs(LevelAt(from) - LevelAt(to)) <= 1;

    /// Land, and not inside anything solid (pad = walker's own radius).
    public bool CanWalk(Vector2 g, float pad)
    {
        if (!IsLand(g)) return false;
        foreach (var n in nodes)
        {
            if (!n.Blocking) continue;
            float r = n.BlockRadius + pad;
            if ((n.grid - g).sqrMagnitude < r * r) return false;
        }
        return true;
    }

    /// True if nothing stands within reach of g (blocking things get extra padding).
    bool FreeSpot(Vector2 g, float pad)
    {
        foreach (var n in nodes)
        {
            float r = n.Blocking ? n.BlockRadius + pad : 0.4f;
            if ((n.available || n.Blocking) && (n.grid - g).magnitude < r) return false;
        }
        return true;
    }

    public float DistanceToFire(Vector2 g)
    {
        float best = float.MaxValue;
        foreach (var n in nodes)
            if (n.kind == NodeKind.Campfire) best = Mathf.Min(best, (n.grid - g).magnitude);
        return best;
    }

    public Vector2 AwayFromFire(Vector2 g)
    {
        WorldNode best = null;
        float bd = float.MaxValue;
        foreach (var n in nodes)
            if (n.kind == NodeKind.Campfire && (n.grid - g).magnitude < bd) { bd = (n.grid - g).magnitude; best = n; }
        if (best == null || bd < 0.001f) return Vector2.zero;
        return (g - best.grid) / bd;
    }

    bool TouchesSea(Vector2 g)
    {
        var t = Tile(g);
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (IsSea(t.x + dx, t.y + dy)) return true;
        return false;
    }

    /// Nearest sea tile within range of g, if any.
    Vector2? NearestSea(Vector2 g, float range)
    {
        var t = Tile(g);
        Vector2? best = null;
        float bd = range;
        for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                if (!IsSea(t.x + dx, t.y + dy)) continue;
                var s = new Vector2(t.x + dx, t.y + dy);
                float d = (s - g).magnitude;
                if (d < bd) { bd = d; best = s; }
            }
        return best;
    }

    // ------------------------------------------------------------------ camera & lights
    void SetupCamera()
    {
        cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
        }
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = PixelArt.WaterColor;
        zoom = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 300f), 2, 6);
    }

    void SetupSun()
    {
        foreach (var l in FindObjectsByType<Light2D>())
            if (l.lightType == Light2D.LightType.Global) { sun = l; break; }
        if (sun == null) sun = CreateLight("Sun", Light2D.LightType.Global, null);
    }

    /// Creates a 2D light that affects every sorting layer (runtime-added lights default to none).
    public static Light2D CreateLight(string name, Light2D.LightType type, Transform parent)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        if (parent != null) go.transform.SetParent(parent, false);
        var l = go.AddComponent<Light2D>();
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var typeField = typeof(Light2D).GetField("m_LightType", flags);
        if (typeField != null) typeField.SetValue(l, type); else l.lightType = type;
        var layersField = typeof(Light2D).GetField("m_ApplyToSortingLayers", flags);
        if (layersField != null)
        {
            var layers = SortingLayer.layers;
            var ids = new int[layers.Length];
            for (int i = 0; i < layers.Length; i++) ids[i] = layers[i].id;
            layersField.SetValue(l, ids);
        }
        go.SetActive(true);
        return l;
    }
}
