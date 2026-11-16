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
    GlowSpore, Antidote, MushroomLantern, // Glowing Mushroom island
    FruitDash, FruitShadow, FruitVine, FruitMagnet, FruitBubble, // special fruits (IslandGame.Power.cs)
    ShellShard, CanvasSail, // Giant Turtle island
    Feather, CloudCotton, Balloon, // Sky island
    Pearl, Coral, DivingBell, // Bubble island
    Gear, TimeSand, Sundial, // Hourglass island
    IceShard, Fur, SpikedBoots, // Ice island
}

public enum NodeKind { Tree, FruitTree, Bush, Grass, Stick, Rock, Stump, Campfire, Shelter, Raft, Bamboo, Obsidian, Hut, Totem, GlowShroom, GlowTree, Shell, CloudPlant,
    Drop, House, Lamp, Farm, Well, Stall, Fountain, Statue, Kelp, CoralNode, Oyster, AirVent, SandCrystal, ClockRuin, ClockTower, IceCrystal, HotSpring }

public enum Biome { Tropical, Jungle, Volcanic, Tribal, Mushroom, Turtle, Sky, Bubble, Hourglass, Ice }

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

    public const string MissingMaterials = "ขาดวัตถุดิบ";

    public static readonly Recipe[] Recipes =
    {
        new Recipe { name = "เชือก", output = Item.Rope, cost = new[] { (Item.Fiber, 3) }, desc = "ฟั่นใยพืชเข้าด้วยกัน" },
        new Recipe { name = "มีดหิน", output = Item.StoneKnife, cost = new[] { (Item.Stone, 1), (Item.Stick, 1) }, desc = "ได้ใยพืชจากหญ้าและเบอร์รี่มากขึ้น" },
        new Recipe { name = "ขวานหิน", output = Item.StoneAxe, cost = new[] { (Item.Stick, 2), (Item.Stone, 2), (Item.Rope, 1) }, desc = "ตัดต้นไม้เอาไม้" },
        new Recipe { name = "หอก", output = Item.Spear, cost = new[] { (Item.Stick, 3), (Item.Stone, 1), (Item.Rope, 1) }, desc = "แทงปลาในทะเล ดาเมจ 3" },
        new Recipe { name = "กองไฟ", places = NodeKind.Campfire, cost = new[] { (Item.Wood, 3), (Item.Stone, 4), (Item.Stick, 2) }, desc = "ให้แสง ใช้ทำอาหาร ไล่ศัตรู" },
        new Recipe { name = "เพิงพัก", places = NodeKind.Shelter, cost = new[] { (Item.Wood, 6), (Item.Rope, 3), (Item.Fiber, 6) }, desc = "นอนหลับข้ามคืน" },
        new Recipe { name = "ผลไม้ปิ้ง", output = Item.CookedFruit, cost = new[] { (Item.Fruit, 1) }, needsFire = true, desc = "อิ่ม +40, พลังชีวิต +10" },
        new Recipe { name = "ปลาย่าง", output = Item.CookedFish, cost = new[] { (Item.RawFish, 1) }, needsFire = true, desc = "อิ่ม +35, พลังชีวิต +10" },
        new Recipe { name = "ปูย่าง", output = Item.CookedCrab, cost = new[] { (Item.CrabMeat, 1) }, needsFire = true, desc = "อิ่ม +45, พลังชีวิต +15" },
        new Recipe { name = "แพ", places = NodeKind.Raft, atShore = true, cost = new[] { (Item.Wood, 12), (Item.Rope, 6), (Item.Stick, 4) }, desc = "เรือคู่ใจตลอดการเดินทาง สร้างที่ริมทะเล" },
        new Recipe { name = "เนื้อย่าง", output = Item.CookedMeat, cost = new[] { (Item.RawMeat, 1) }, needsFire = true, desc = "อิ่ม +50, พลังชีวิต +15" },
        new Recipe { name = "กระบอกน้ำ", output = Item.WaterFlask, cost = new[] { (Item.Bamboo, 2), (Item.Rope, 1) }, desc = "พกน้ำได้ 3 ครั้ง (Q) เติมที่บ่อน้ำ" },
        new Recipe { name = "หอกออบซิเดียน", output = Item.ObsidianSpear, cost = new[] { (Item.Obsidian, 2), (Item.Stick, 2), (Item.Rope, 1) }, desc = "คมกริบ! ดาเมจ 6 แทงปลาได้" },
        new Recipe { name = "ขวานออบซิเดียน", output = Item.ObsidianAxe, cost = new[] { (Item.Obsidian, 2), (Item.Stick, 2), (Item.Rope, 1) }, desc = "ตัดต้นไม้เร็วขึ้น 2 ครั้ง" },
        // Taught by the tribe (Tribe Island):
        new Recipe { name = "ผ้า", output = Item.Cloth, learn = true, cost = new[] { (Item.Fiber, 4) }, desc = "ทอจากใยพืช ใช้ทำเสื้อผ้าและใบเรือ" },
        new Recipe { name = "หมวกฟาง", output = Item.StrawHat, learn = true, cost = new[] { (Item.Fiber, 6), (Item.Rope, 1) }, desc = "หมวก: หิวน้ำช้าลง 15%" },
        new Recipe { name = "ผ้าคาดหัวชนเผ่า", output = Item.TribalHeadband, learn = true, cost = new[] { (Item.Cloth, 2), (Item.Hide, 1) }, desc = "หมวก: ดาเมจโจมตี +1" },
        new Recipe { name = "เสื้อใบไม้", output = Item.LeafTunic, learn = true, cost = new[] { (Item.Fiber, 8), (Item.Rope, 2), (Item.Cloth, 1) }, desc = "เสื้อ: พลังชีวิตสูงสุด +15" },
        // taught by the Mushroom island's doctor
        new Recipe { name = "ยาแก้พิษ", output = Item.Antidote, learn = true, needsFire = true, cost = new[] { (Item.GlowSpore, 2), (Item.Berry, 2) }, desc = "รักษาอาการพิษ (คลิกในกระเป๋า)" },
        new Recipe { name = "โคมเห็ด", output = Item.MushroomLantern, learn = true, cost = new[] { (Item.GlowSpore, 4), (Item.Stick, 2), (Item.Rope, 1) }, desc = "ส่องสว่างรอบตัวตอนมืด" },
        // taught by the Giant Turtle's keeper
        new Recipe { name = "ใบเรือผ้าใบ", output = Item.CanvasSail, learn = true, cost = new[] { (Item.Fiber, 8), (Item.Rope, 2), (Item.ShellShard, 2) }, desc = "เรือแล่นในทะเลเร็วขึ้น 25%" },
        // taught by the Sky island's wind sage
        new Recipe { name = "บอลลูนลมร้อน", output = Item.Balloon, learn = true, cost = new[] { (Item.CloudCotton, 6), (Item.Feather, 2), (Item.Rope, 3), (Item.Fiber, 6) }, desc = "เรือลอยเหนือคลื่น สัตว์ทะเลทำร้ายได้น้อยลงมาก" },
        // taught by the Bubble island's diver
        new Recipe { name = "ระฆังดำน้ำ", output = Item.DivingBell, learn = true, cost = new[] { (Item.Pearl, 3), (Item.Coral, 4), (Item.Stone, 6), (Item.Rope, 3) }, desc = "ไม่ขาดอากาศใต้ทะเล + ตกปลาได้ทีละ 2 ตัว" },
        // taught by the Hourglass island's clockmaker
        new Recipe { name = "นาฬิกาแดด", output = Item.Sundial, learn = true, cost = new[] { (Item.Gear, 4), (Item.TimeSand, 4), (Item.Stone, 4) }, desc = "หยุดเวลาสัตว์รอบตัว 5 วิ (ใช้จากช่องลัด)" },
        // taught by the Ice island's snow hunter
        new Recipe { name = "รองเท้าหนาม", output = Item.SpikedBoots, learn = true, cost = new[] { (Item.Fur, 2), (Item.IceShard, 4), (Item.Hide, 1), (Item.Rope, 2) }, desc = "เดินบนน้ำแข็งไม่ลื่น" },
        new Recipe { name = "เสื้อกั๊กหนัง", output = Item.HideVest, learn = true, cost = new[] { (Item.Hide, 3), (Item.Rope, 2), (Item.Cloth, 1) }, desc = "เสื้อ: รับดาเมจลดลง 20%" },
    };

    public static readonly (string name, string desc, int max, bool combat)[] SkillInfo =
    {
        ("พละกำลัง", "ดาเมจโจมตี +1", 5, true),
        ("ความอึด", "พลังชีวิตสูงสุด +15", 5, true),
        ("ความว่องไว", "เดินเร็วขึ้น 8%", 3, true),
        ("หลบหลีก", "โอกาสหลบการโจมตี +10%", 3, true),
        ("นักเก็บของ", "โอกาสได้ของเพิ่มตอนเก็บ +30%", 3, false),
        ("คนตัดไม้", "ตัดต้นไม้ใช้น้อยลง 1 ครั้ง", 2, false),
        ("ท้องเหล็ก", "หิวข้าวและหิวน้ำช้าลง 12%", 3, false),
        ("ชาวประมง", "แทงปลาติดง่ายขึ้น 15%", 3, false),
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
    /// Highest island reached, and every other island you've visited (kept as you left it).
    public int farthest = 1;
    readonly List<IslandSave> visited = new List<IslandSave>();
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
    public float SpeedMultiplier => (1f + 0.08f * SkillLv(Skill.Agility)) * SlowFactor;
    public static int XpToNext(int lv) => 25 + 15 * lv;

    public string BiomeName => BiomeNameOf(biome);

    /// The island chain: Tropical -> Jungle -> Tribe -> Volcano, then again with tougher creatures.
    public static Biome BiomeFor(int islandNumber) =>
        new[] { Biome.Tropical, Biome.Mushroom, Biome.Turtle, Biome.Sky, Biome.Bubble, Biome.Hourglass, Biome.Ice, Biome.Jungle, Biome.Tribal, Biome.Volcanic }[(islandNumber - 1) % 10];

    /// Colour tint for plants that have no biome-specific sprite.
    public Color Tint(NodeKind k) =>
        biome == Biome.Volcanic && k is NodeKind.Grass or NodeKind.Bush ? new Color(0.85f, 0.78f, 0.6f)
        : biome == Biome.Jungle && k is NodeKind.Grass or NodeKind.Bush ? new Color(0.8f, 0.95f, 0.8f)
        : biome == Biome.Mushroom && k is NodeKind.Grass or NodeKind.Bush ? new Color(0.75f, 0.7f, 1f)
        : biome == Biome.Turtle && k is NodeKind.Grass or NodeKind.Bush ? new Color(0.9f, 1f, 0.75f)
        : biome == Biome.Sky && k is NodeKind.Grass or NodeKind.Bush ? new Color(0.85f, 1f, 0.95f)
        : biome == Biome.Bubble && k is NodeKind.Grass or NodeKind.Bush ? new Color(0.7f, 0.95f, 1f)
        : biome == Biome.Hourglass && k is NodeKind.Grass or NodeKind.Bush ? new Color(1f, 0.92f, 0.7f)
        : biome == Biome.Ice && k is NodeKind.Grass or NodeKind.Bush ? new Color(0.85f, 0.95f, 1f)
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
    public static bool IsTool(Item it) => it is Item.StoneAxe or Item.Spear or Item.StoneKnife or Item.WaterFlask or Item.ObsidianSpear or Item.ObsidianAxe or Item.MushroomLantern or Item.CanvasSail or Item.Balloon or Item.DivingBell or Item.Sundial or Item.SpikedBoots;
    public static bool IsHat(Item it) => it is Item.StrawHat or Item.TribalHeadband;
    public static bool IsOutfit(Item it) => it is Item.LeafTunic or Item.HideVest;
    public static bool IsClothing(Item it) => IsHat(it) || IsOutfit(it);
    public bool IsWorn(Item it) => hat == it || outfit == it;
    public static bool IsCooked(Item it) => it is Item.CookedFruit or Item.CookedFish or Item.CookedCrab or Item.CookedMeat;

    public static string Name(Item it) => it switch
    {
        Item.Fiber => "ใยพืช",
        Item.Stick => "กิ่งไม้",
        Item.Stone => "หิน",
        Item.Wood => "ไม้",
        Item.Berry => "เบอร์รี่",
        Item.Fruit => "ผลไม้",
        Item.Rope => "เชือก",
        Item.Spear => "หอก",
        Item.Bamboo => "ไม้ไผ่",
        Item.Obsidian => "ออบซิเดียน",
        Item.Hide => "หนังสัตว์",
        Item.Cloth => "ผ้า",
        Item.CookedFruit => "ผลไม้ปิ้ง",
        Item.StoneAxe => "ขวานหิน",
        Item.StoneKnife => "มีดหิน",
        Item.RawFish => "ปลาดิบ",
        Item.CookedFish => "ปลาย่าง",
        Item.CrabMeat => "เนื้อปู",
        Item.CookedCrab => "ปูย่าง",
        Item.RawMeat => "เนื้อดิบ",
        Item.CookedMeat => "เนื้อย่าง",
        Item.WaterFlask => "กระบอกน้ำ",
        Item.ObsidianSpear => "หอกออบซิเดียน",
        Item.ObsidianAxe => "ขวานออบซิเดียน",
        Item.StrawHat => "หมวกฟาง",
        Item.LeafTunic => "เสื้อใบไม้",
        Item.HideVest => "เสื้อกั๊กหนัง",
        Item.TribalHeadband => "ผ้าคาดหัวชนเผ่า",
        Item.GlowSpore => "สปอร์เรืองแสง",
        Item.Antidote => "ยาแก้พิษ",
        Item.MushroomLantern => "โคมเห็ด",
        Item.FruitDash => "ผลฟ้าแลบ",
        Item.FruitShadow => "ผลเงาจันทร์",
        Item.FruitVine => "ผลเถาวัลย์",
        Item.FruitMagnet => "ผลแม่เหล็ก",
        Item.FruitBubble => "ผลฟองสบู่",
        Item.ShellShard => "เศษเปลือกหอย",
        Item.CanvasSail => "ใบเรือผ้าใบ",
        Item.Feather => "ขนนกกระดิ่ง",
        Item.CloudCotton => "ปุยเมฆ",
        Item.Balloon => "บอลลูนลมร้อน",
        Item.Pearl => "ไข่มุก",
        Item.Coral => "ปะการัง",
        Item.DivingBell => "ระฆังดำน้ำ",
        Item.Gear => "เฟืองทองเหลือง",
        Item.TimeSand => "ทรายเวลา",
        Item.Sundial => "นาฬิกาแดด",
        Item.IceShard => "เกล็ดน้ำแข็ง",
        Item.Fur => "ขนค้างคาวน้ำแข็ง",
        Item.SpikedBoots => "รองเท้าหนาม",
        _ => it.ToString(),
    };

    // ------------------------------------------------------------------ setup
    void Awake()
    {
        I = this;
        SetupCamera();
        SetupSun();
        Sfx.Init(gameObject);
        InitTitle(); // choose a name and online / offline first (IslandGame.Net.cs)
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
        if (Online)
        {
            // Online everyone shares the island: build it fresh from the shared seed, then
            // catch up on what other players already changed (see "room" in IslandGame.Net.cs).
            data.seed = SeedFor(Mathf.Max(1, data.island));
            data.fresh = true;
        }
        bool fresh = data.fresh;
        voyage = null; // always start on land
        nodeById.Clear();
        genCounter = 0; localCounter = 0;

        if (world != null) Destroy(world.gameObject);
        world = new GameObject("World").transform;
        world.SetParent(transform);
        nodes.Clear(); enemies.Clear(); water.Clear(); inv.Clear(); popups.Clear();
        dead = false; craftOpen = false; skillsOpen = false;
        Now = 0f; autosaveTimer = 0f; respawnTimer = 0f; enemyTimer = 0f; sleepT = 0f; nightWarnedDay = 0;
        stickCap = 0; rockCap = 0; obsidianCap = 0; pond = null; seaSpot = null; target = null; enemyTarget = null;

        island = Mathf.Max(1, data.island);
        farthest = Mathf.Max(island, data.farthest);
        visited.Clear();
        foreach (var v in data.visited) if (v != null && v.island != island) visited.Add(v);
        // Saves before v6 used a 3-island cycle; keep their island the same biome it was.
        biome = data.biomeId > 0 ? (Biome)(data.biomeId - 1) : data.version < 6 ? (Biome)((island - 1) % 3) : BiomeFor(island);
        tier = (island - 1) / 10;
        seed = data.seed;
        raftLevel = data.raftLevel;
        learned.Clear();
        foreach (int l in data.learned) learned.Add((Item)l);
        hat = data.hatId > 0 ? (Item)(data.hatId - 1) : (Item?)null;
        outfit = data.outfitId > 0 ? (Item)(data.outfitId - 1) : (Item?)null;
        elderHealDay = data.elderHealDay;
        LoadQuests(data);
        LoadBag(data);
        air = 100f; inkT = 0f; warmth = 100f;
        power = (Power)data.powerId; powerLv = Mathf.Clamp(data.powerLv, 1, 5); powerXp = data.powerXp;
        powerCd = 0f; invisibleT = 0f; bubbleT = 0f; bubbleHits = 0;
        poisonT = 0f;
        clouds.Clear();
        villagers.Clear();
        dialog = null;
        rnd = new System.Random(seed);
        genPhase = true; // nodes made from the seed get the same ids on every machine
        BuildIsland(Center, spawnResources: fresh);
        if (fresh && biome == Biome.Tribal) BuildVillage();
        if (fresh && biome == Biome.Mushroom) BuildMushroomCamp();
        if (fresh && biome is Biome.Turtle or Biome.Sky or Biome.Bubble or Biome.Hourglass or Biome.Ice) BuildCamp();
        if (fresh) PlaceHotSprings();
        if (fresh) BuildClockTower();
        if (fresh) PlaceAirVents();
        genPhase = false;

        Vector2? landing = null;
        if (!fresh)
        {
            foreach (var ns in data.nodes)
            {
                var node = Spawn(ns.kind, ns.grid, ns.flip);
                if (ns.kind == NodeKind.Drop) { node.dropItem = (Item)ns.item; node.dropCount = ns.count; }
                node.Restore(ns.available, ns.hits, ns.regrow);
            }
            stickCap = data.stickCap; rockCap = data.rockCap; obsidianCap = data.obsidianCap;
            // v4 saves had no raft level: a raft already standing on this island counts as ready to sail.
            if (data.version < 5 && raftLevel == 0 && RaftNode != null) { raftLevel = island; RaftNode.Refresh(); }
        }
        // Arrived by boat (a new island, or one you're coming back to): it's pulled up on the west shore.
        if (raftLevel > 0 && RaftNode == null) landing = BeachRaft();
        foreach (var st in data.items) if (st.count > 0) inv[st.item] = st.count;
        AutoEquipWeapon(); // old saves and empty hands: hold the best weapon you own
        System.Array.Clear(skills, 0, skills.Length);
        for (int i = 0; i < skills.Length && i < data.skills.Count; i++) skills[i] = data.skills[i];
        level = Mathf.Max(1, data.level); xp = data.xp; skillPoints = data.skillPoints; flaskWater = data.flaskWater;
        health = Mathf.Min(data.health, MaxHealth); hunger = data.hunger; thirst = data.thirst; clock = data.clock;
        if (biome == Biome.Tribal) SpawnVillagers();
        SpawnIslandPeople(); // the shipwright (every island), and the mushroom doctor and her child
        BuildTurtle();       // flood water and the turtle's head (Turtle island only)
        SpawnTownsfolk();
        SpawnBoss();

        Vector2 start = landing ?? (!fresh && IsLand(data.player) ? data.player : Center);
        player = new GameObject("Player").AddComponent<PlayerController>();
        player.transform.SetParent(world);
        player.Init(start);

        cursor = new GameObject("Cursor").AddComponent<SpriteRenderer>();
        cursor.transform.SetParent(world);
        cursor.sprite = PixelArt.Cursor();
        cursor.sortingOrder = -30000; // above ground, below objects

        camPos = player.transform.position;
        if (!fresh && landing != null) Hint($"กลับมาที่{BiomeName} (เกาะที่ {island}) แล้ว ทุกอย่างยังอยู่เหมือนตอนที่จากไป", 5f);
        else if (!fresh) Hint($"ยินดีต้อนรับกลับ! {BiomeName} (เกาะที่ {island}) วันที่ {Day}", 4f);
        else if (island == 1) Hint("หาน้ำจืด (บ่อสีฟ้าอ่อน) และอาหาร เก็บวัตถุดิบ แล้วกด Tab เพื่อประดิษฐ์", 8f);
        else Hint($"เห็นฝั่งแล้ว! เกาะที่ {island}: {BiomeName} {BiomeIntro()}", 9f);
        if (fresh || landing != null) SaveGame(); // replaces any old save right away
        JoinRoom();            // online: meet whoever else is on this island
    }

    string BiomeIntro() => biome switch
    {
        Biome.Mushroom => "ป่าเห็ดยักษ์ส่องแสงสลัว ระวังเห็ดที่ขยับได้! ไปหาช่างต่อเรือ แล้วไปหาหมอโมรากลางเกาะ",
        Biome.Turtle => "เกาะนี้คือกระดองเต่ายักษ์ที่กำลังว่ายน้ำ! เมื่อท่านเต่าดำน้ำ ที่ต่ำจะน้ำท่วม ไปหายายชบาที่กลางเกาะ",
        Biome.Sky => "ลมหมุนยกเรือขึ้นมาถึงเกาะลอยฟ้า! ระวังลมกรรโชกพัดตกขอบเกาะ ไปหาปู่ลมพัดที่กลางเกาะ",
        Biome.Bubble => "เรือดำลงสู่เกาะในโดมอากาศใต้ทะเล! อากาศจะค่อยๆ หมด หายใจที่ช่องอากาศ แล้วไปหาแม่มุกที่กลางเกาะ",
        Biome.Hourglass => "เวลาบนเกาะนี้วิ่งเร็วผิดปกติ! ระวังตุ๊กตาไขลาน มันขยับได้เฉพาะตอนที่เราไม่ได้มอง ไปหาลุงฟันเฟืองที่หอนาฬิกา",
        Biome.Ice => "เกาะน้ำแข็งกลางทะเลอุ่น! ความหนาวจะกัดกินความอบอุ่น อยู่ใกล้กองไฟหรือบ่อน้ำร้อน และระวังลื่นบนพื้นน้ำแข็ง ไปหาพรานธาราที่กลางเกาะ",
        Biome.Jungle => "มีงูซ่อนอยู่ในหญ้า ช่างต่อเรือต้องการไม้ไผ่",
        Biome.Volcanic => "หมูป่าเดินเพ่นพ่านบนเถ้าภูเขาไฟ ช่างต่อเรือต้องการออบซิเดียน",
        Biome.Tribal => "มีควันลอยขึ้นจากหมู่บ้านกลางเกาะ ไปทักทายชนเผ่ากันเถอะ!",
        _ => tier > 0 ? "สัตว์ที่นี่ดูแข็งแกร่งกว่าเดิม..." : "ช่างต่อเรือรออยู่ที่ชายหาด",
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

    /// Land on island `dest`, keeping inventory, level and skills. A new island is generated;
    /// one you've been to comes back exactly as you left it.
    void ArriveAt(int dest)
    {
        var d = Snapshot();
        d.island = dest;
        d.nodes.Clear();
        d.player = new Vector2(-99f, -99f); // stand by the boat
        var known = visited.Find(v => v.island == dest);
        if (known != null)
        {
            d.fresh = false;
            d.seed = known.seed; d.biomeId = known.biomeId;
            d.stickCap = known.stickCap; d.rockCap = known.rockCap; d.obsidianCap = known.obsidianCap;
            d.nodes.AddRange(known.nodes);
            d.visited.RemoveAll(v => v.island == dest);
        }
        else
        {
            d.fresh = true;
            d.biomeId = (int)BiomeFor(dest) + 1;
            d.seed = NewIslandSeed(dest);
        }
        d.farthest = Mathf.Max(farthest, dest);
        StartGame(d);
    }

    /// Island N+1's seed comes from island N's (same chain as before islands were remembered).
    int NewIslandSeed(int dest)
    {
        int from = dest - 1;
        int s = from == island ? seed : visited.Find(v => v.island == from)?.seed ?? seed;
        return new System.Random(s + from * 7919).Next();
    }

    /// Store the island you're leaving so you can come back to it later. The boat goes with you.
    void RememberIsland()
    {
        var keep = new IslandSave
        {
            island = island, seed = seed, biomeId = (int)biome + 1,
            stickCap = stickCap, rockCap = rockCap, obsidianCap = obsidianCap,
        };
        foreach (var n in nodes)
            if (n != null && n.kind != NodeKind.Raft)
                keep.nodes.Add(ToSave(n));
        visited.RemoveAll(v => v.island == island);
        visited.Add(keep);
    }

    public Biome BiomeOf(int n) =>
        n == island ? biome : visited.Find(v => v.island == n) is IslandSave kept && kept.biomeId > 0 ? (Biome)(kept.biomeId - 1) : BiomeFor(n);

    public static string BiomeNameOf(Biome b) => b switch { Biome.Jungle => "เกาะป่าดิบ", Biome.Volcanic => "เกาะภูเขาไฟ", Biome.Tribal => "เกาะชนเผ่า", Biome.Mushroom => "เกาะเห็ดเรืองแสง", Biome.Turtle => "เกาะหลังเต่ายักษ์", Biome.Sky => "เกาะลอยฟ้า", Biome.Bubble => "เกาะใต้ฟองอากาศ", Biome.Hourglass => "เกาะนาฬิกาทราย", Biome.Ice => "เกาะน้ำแข็ง", _ => "เกาะเขตร้อน" };

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
            farthest = farthest,
            powerId = (int)power, powerLv = powerLv, powerXp = powerXp,
        };
        d.visited.AddRange(visited);
        SaveQuests(d);
        SaveBag(d);
        d.skills.AddRange(skills);
        foreach (var l in learned) d.learned.Add((int)l);
        foreach (var kv in inv) if (kv.Value > 0) d.items.Add(new ItemStack { item = kv.Key, count = kv.Value });
        foreach (var n in nodes)
            if (n != null)
                d.nodes.Add(ToSave(n));
        return d;
    }

    static NodeSave ToSave(WorldNode n) => new NodeSave
    {
        kind = n.kind, grid = n.grid, available = n.available, flip = n.flip, hits = n.hits, regrow = n.RegrowRemaining,
        item = (int)n.dropItem, count = n.dropCount,
    };

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
        float radius = Size * IslandRadiusFactor; // further islands are bigger (IslandGame.Town.cs)
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

        float hillBias = biome switch { Biome.Volcanic => 0.1f, Biome.Jungle => 0.03f, Biome.Mushroom => 0.02f, Biome.Turtle => 0.16f, Biome.Sky => 0.05f, Biome.Bubble => 0.04f, Biome.Hourglass => 0.06f, Biome.Ice => 0.05f, _ => 0f }; // the shell rises in the middle
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
                if (t == 0 && biome == Biome.Bubble)
                {
                    p.y -= 0.125f;
                    sr.sprite = PixelArt.DeepWater((gx * 5 + gy * 3) % 4);
                    sr.sortingOrder = -32000 + gx + gy;
                }
                else if (t == 0 && biome == Biome.Sky)
                {
                    p.y -= 0.125f;
                    sr.sprite = PixelArt.CloudTile((gx * 7 + gy * 3) % 4);
                    sr.sortingOrder = -32000 + gx + gy;
                }
                else if (t == 0 || t == 3)
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
                    sr.sortingOrder = -31000 + 2 * (gx + gy); // gaps leave room for the turtle island's flood water
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

        PlaceGlowingTree(); // a rare special-fruit tree (IslandGame.Power.cs)
        BuildProsperity();  // houses, lamps, farms... by island level (IslandGame.Town.cs)

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
            (Biome.Mushroom, true) => new[] { (NodeKind.Tree, .12), (NodeKind.GlowShroom, .07), (NodeKind.Bush, .05), (NodeKind.Grass, .10), (NodeKind.Stick, .04), (NodeKind.Rock, .03) },
            (Biome.Mushroom, false) => new[] { (NodeKind.Stick, .04), (NodeKind.Rock, .04), (NodeKind.GlowShroom, .02) },
            (Biome.Turtle, true) => new[] { (NodeKind.Tree, .07), (NodeKind.FruitTree, .04), (NodeKind.Bush, .05), (NodeKind.Grass, .12), (NodeKind.Stick, .04), (NodeKind.Rock, .03) },
            (Biome.Turtle, false) => new[] { (NodeKind.Shell, .07), (NodeKind.Stick, .03), (NodeKind.Rock, .03) },
            (Biome.Sky, true) => new[] { (NodeKind.Tree, .08), (NodeKind.CloudPlant, .07), (NodeKind.FruitTree, .03), (NodeKind.Bush, .04), (NodeKind.Grass, .10), (NodeKind.Stick, .04), (NodeKind.Rock, .04) },
            (Biome.Sky, false) => new[] { (NodeKind.CloudPlant, .04), (NodeKind.Stick, .03), (NodeKind.Rock, .04) },
            (Biome.Bubble, true) => new[] { (NodeKind.Kelp, .10), (NodeKind.CoralNode, .07), (NodeKind.Bush, .03), (NodeKind.Grass, .10), (NodeKind.Stick, .03), (NodeKind.Rock, .05) },
            (Biome.Bubble, false) => new[] { (NodeKind.Oyster, .07), (NodeKind.CoralNode, .03), (NodeKind.Rock, .03) },
            (Biome.Hourglass, true) => new[] { (NodeKind.Tree, .07), (NodeKind.FruitTree, .05), (NodeKind.Bush, .05), (NodeKind.Grass, .10), (NodeKind.Stick, .04), (NodeKind.Rock, .04) },
            (Biome.Hourglass, false) => new[] { (NodeKind.SandCrystal, .06), (NodeKind.Stick, .03), (NodeKind.Rock, .03) },
            (Biome.Ice, true) => new[] { (NodeKind.Tree, .10), (NodeKind.Bush, .03), (NodeKind.Grass, .05), (NodeKind.Stick, .05), (NodeKind.Rock, .05), (NodeKind.IceCrystal, .02) },
            (Biome.Ice, false) => new[] { (NodeKind.IceCrystal, .06), (NodeKind.Stick, .02), (NodeKind.Rock, .03) },
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

    public WorldNode Spawn(NodeKind k, Vector2 g, bool? flip = null, int? id = null)
    {
        var n = new GameObject(k.ToString()).AddComponent<WorldNode>();
        n.transform.SetParent(world);
        n.id = id ?? (genPhase ? ++genCounter : NewLocalId());
        n.Init(k, g, flip ?? rnd.Next(2) == 0);
        nodes.Add(n);
        nodeById[n.id] = n;
        return n;
    }

    void RemoveNode(WorldNode n)
    {
        nodeById.Remove(n.id);
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
