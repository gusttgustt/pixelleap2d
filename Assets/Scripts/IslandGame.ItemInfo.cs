using System.Collections.Generic;
using UnityEngine;

/// Item grades (ระดับไอเทม) shown on every slot as a coloured frame and pips, the hover tooltip
/// for the bag and the hotbar, and what each island on the sea chart has to farm and fight.
public partial class IslandGame
{
    // ------------------------------------------------------------------ item grades
    /// 1 ธรรมดา, 2 ดี, 3 หายาก, 4 ยอดเยี่ยม, 5 ตำนาน
    public static int Grade(Item it) => it switch
    {
        Item.FruitDash or Item.FruitShadow or Item.FruitVine or Item.FruitMagnet or Item.FruitBubble
            or Item.Trident or Item.GoldenFish => 5,
        Item.CrystalArmor or Item.StormHood or Item.AncientRelic or Item.Balloon or Item.DivingBell
            or Item.ButterflyWings or Item.Sundial or Item.Compass => 4,
        Item.ObsidianSpear or Item.ObsidianAxe or Item.HideVest or Item.TribalHeadband or Item.Antidote
            or Item.MushroomLantern or Item.CanvasSail or Item.Pearl or Item.TimeSand or Item.CrystalShard
            or Item.GoldCoin or Item.Salad or Item.Tuna or Item.Shovel or Item.SpikedBoots or Item.PetNet
            or Item.FurnWorkbench or Item.FurnBed => 3,
        Item.Rope or Item.StoneAxe or Item.Spear or Item.StoneKnife or Item.CookedFish or Item.CookedCrab
            or Item.CookedMeat or Item.WaterFlask or Item.Cloth or Item.Hide or Item.StrawHat or Item.LeafTunic
            or Item.ShellShard or Item.Feather or Item.Coral or Item.Copper or Item.Chitin or Item.Nectar
            or Item.IceShard or Item.Fur or Item.Gear or Item.Milk or Item.CookedEgg or Item.Hoe or Item.WateringCan
            or Item.FishingRod or Item.Hammer or Item.Honey or Item.Obsidian or Item.GlowSpore or Item.CloudCotton
            or Item.FurnTable or Item.FurnChair or Item.FurnRug or Item.FurnPlant or Item.FurnShelf => 2,
        _ => 1,
    };

    public static string GradeName(int g) => g switch { 5 => "ตำนาน", 4 => "ยอดเยี่ยม", 3 => "หายาก", 2 => "ดี", _ => "ธรรมดา" };

    public static Color GradeColor(int g) => g switch
    {
        5 => new Color(1f, 0.72f, 0.18f),   // gold
        4 => new Color(0.78f, 0.42f, 1f),   // purple
        3 => new Color(0.3f, 0.62f, 1f),    // blue
        2 => new Color(0.36f, 0.88f, 0.4f), // green
        _ => new Color(0.72f, 0.72f, 0.72f),
    };

    static string GradeHex(int g) => "#" + ColorUtility.ToHtmlStringRGB(GradeColor(g));

    /// The slot's grade: a tinted background, a coloured frame (thicker and glowing for the top
    /// grades) and pips in the corner, so the grade reads at a glance. Draw before the icon.
    static void GradeFrame(Rect r, Item it)
    {
        int g = Grade(it);
        Color c = GradeColor(g);
        Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), new Color(c.r, c.g, c.b, g == 1 ? 0.06f : 0.10f + 0.04f * g));
        float th = g >= 4 ? 3f : 2f;
        Color edge = c;
        if (g == 5) edge = Color.Lerp(c, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f));
        if (g == 1) edge.a = 0.45f;
        Fill(new Rect(r.x, r.y, r.width, th), edge); Fill(new Rect(r.x, r.yMax - th, r.width, th), edge);
        Fill(new Rect(r.x, r.y, th, r.height), edge); Fill(new Rect(r.xMax - th, r.y, th, r.height), edge);
        for (int i = 0; i < g - 1; i++) // pips top-right: 1 for ดี ... 4 for ตำนาน
        {
            var p = new Rect(r.xMax - 7f - i * 5f, r.y + 4f, 4f, 4f);
            Fill(new Rect(p.x - 1, p.y - 1, p.width + 2, p.height + 2), new Color(0f, 0f, 0f, 0.6f));
            Fill(p, c);
        }
    }

    // ------------------------------------------------------------------ item details
    string ItemInfo(Item sel)
    {
        string info = FoodValue(sel) > 0 ? $"อาหาร: อิ่ม +{FoodValue(sel)}" + (HealValue(sel) > 0 ? $", พลังชีวิต +{HealValue(sel)}" : "") + (ThirstValue(sel) > 0 ? $", แก้กระหาย +{ThirstValue(sel)}" : "")
                    : IsClothing(sel) || IsWeapon(sel) ? GearText(sel)
                    : IsPowerFruit(sel) ? $"กินแล้วได้พลัง{PowerName(PowerOf(sel))}: {PowerDesc(PowerOf(sel))}"
                    : IsSeed(sel) ? "เมล็ดพันธุ์ ปลูกในแปลงผักบนเกาะส่วนตัว"
                    : sel == Item.Antidote ? "ใช้แก้พิษ"
                    : sel == Item.Sundial ? "ใช้หยุดเวลาสัตว์ประหลาดรอบตัว 5 วินาที (ชาร์จ 60 วิ)"
                    : sel == Item.WaterFlask ? $"ดื่มแก้กระหาย (Q) · น้ำในกระบอก {flaskWater}"
                    : IsTool(sel) ? "เครื่องมือ: มีติดตัวไว้ก็ใช้ได้เลย"
                    : TabOf(sel) == BagTab.Special ? "ของหายาก ใช้ทำของพิเศษหรือส่งภารกิจ"
                    : "วัตถุดิบสำหรับประดิษฐ์";
        return info;
    }

    // ------------------------------------------------------------------ hover tooltip
    Item? tipItem;      // the slot under the mouse this frame (bag, hotbar, equipment)

    void DrawItemTip(float W, float H)
    {
        if (tipItem == null || touchUsed) { tipItem = null; return; }
        var it = tipItem.Value;
        tipItem = null;
        int g = Grade(it);
        var lines = new List<string>
        {
            $"<b><color={GradeHex(g)}>{Name(it)}</color></b>",
            $"<color={GradeHex(g)}>ระดับ: {GradeName(g)}</color>  <color=#aaaaaa>({TabName(TabOf(it))})</color>",
            ItemInfo(it),
            $"มีอยู่ {Count(it)} ชิ้น" + (IsWorn(it) || weapon == it ? "  <color=#ffd84d>(สวมใส่อยู่)</color>" : ""),
        };
        if (SellPrice(it) > 0) lines.Add($"<color=#ffe27a>ขายได้ชิ้นละ {SellPrice(it)} เหรียญ</color>");
        string use = IsClothing(it) ? "คลิก: สวม/ถอด" : IsWeapon(it) ? "คลิก: ถือ/เก็บ" : FoodValue(it) > 0 || IsPowerFruit(it) ? "คลิกในช่องลัด: กิน" : null;
        if (use != null) lines.Add($"<color=#aaaaaa>{use}</color>");

        var st = new GUIStyle(small) { wordWrap = true, richText = true, fontSize = 12 };
        float tw = 250f;
        float th = 10f;
        foreach (var l in lines) th += st.CalcHeight(Lang.Content(l), tw - 16f) + 1f;
        Vector2 m = Event.current.mousePosition;
        float x = m.x + 18f, y = m.y + 12f;
        if (x + tw > W - 4f) x = m.x - tw - 10f;
        if (y + th > H - 4f) y = H - th - 4f;
        x = Mathf.Max(4f, x); y = Mathf.Max(4f, y);
        var box = new Rect(x, y, tw, th);
        Fill(box, new Color(0.05f, 0.04f, 0.03f, 0.96f));
        Outline(box, GradeColor(g));
        float ly = y + 5f;
        foreach (var l in lines)
        {
            float lh = st.CalcHeight(Lang.Content(l), tw - 16f);
            ShadowLabel(new Rect(x + 8f, ly, tw - 16f, lh), l, st);
            ly += lh + 1f;
        }
    }

    // ------------------------------------------------------------------ what's on an island (sea chart)
    /// The creatures of an island type (tierUp: island 16 on, where the jungle gets boars).
    static EnemyKind[] EnemiesOf(Biome b, bool tierUp) => (b, tierUp) switch
    {
        (Biome.Jungle, false) => new[] { EnemyKind.Crab, EnemyKind.Snake },
        (Biome.Jungle, true) => new[] { EnemyKind.Crab, EnemyKind.Snake, EnemyKind.Boar },
        (Biome.Volcanic, _) => new[] { EnemyKind.Crab, EnemyKind.Boar },
        (Biome.Mushroom, _) => new[] { EnemyKind.Crab, EnemyKind.Mimic, EnemyKind.SporeSpider },
        (Biome.Turtle, _) => new[] { EnemyKind.Crab, EnemyKind.Barnacle, EnemyKind.HermitCrab },
        (Biome.Sky, _) => new[] { EnemyKind.StormCloud, EnemyKind.ChimeBird },
        (Biome.Bubble, _) => new[] { EnemyKind.LanternFish, EnemyKind.StoneSquid },
        (Biome.Hourglass, _) => new[] { EnemyKind.WindupDoll, EnemyKind.TimeBeetle },
        (Biome.Ice, _) => new[] { EnemyKind.Snowman, EnemyKind.FrostBat },
        (Biome.Sunken, _) => new[] { EnemyKind.Eel, EnemyKind.DeepCrab },
        (Biome.Pirate, _) => new[] { EnemyKind.Crab, EnemyKind.SkeletonPirate, EnemyKind.PirateMonkey },
        (Biome.Crystal, _) => new[] { EnemyKind.CrystalGolem, EnemyKind.MirrorWisp },
        (Biome.Storm, _) => new[] { EnemyKind.ThunderToad, EnemyKind.StormHawk },
        (Biome.Insect, _) => new[] { EnemyKind.WorkerAnt, EnemyKind.SoldierAnt },
        (_, true) => new[] { EnemyKind.Crab, EnemyKind.Snake },
        _ => new[] { EnemyKind.Crab },
    };

    static EnemyKind? BossOf(Biome b) => b switch
    {
        Biome.Mushroom => EnemyKind.ShroomKing, Biome.Sunken => EnemyKind.Kraken, Biome.Pirate => EnemyKind.Captain,
        Biome.Storm => EnemyKind.Thunderbird, Biome.Insect => EnemyKind.AntQueen, _ => (EnemyKind?)null,
    };

    /// What you can gather on an island type.
    static Item[] MaterialsOf(Biome b)
    {
        var l = new List<Item> { Item.Wood, Item.Stick, Item.Stone, Item.Fiber, Item.Berry };
        switch (b)
        {
            case Biome.Jungle: l.Add(Item.Fruit); l.Add(Item.Bamboo); break;
            case Biome.Volcanic: l.Add(Item.Obsidian); break;
            case Biome.Tribal: l.Add(Item.Fruit); l.Add(Item.Bamboo); break;
            case Biome.Mushroom: l.Add(Item.GlowSpore); break;
            case Biome.Turtle: l.Add(Item.Fruit); l.Add(Item.ShellShard); break;
            case Biome.Sky: l.Add(Item.Fruit); l.Add(Item.CloudCotton); l.Add(Item.Feather); break;
            case Biome.Bubble: l.Add(Item.Coral); l.Add(Item.Pearl); l.Add(Item.RawFish); break;
            case Biome.Hourglass: l.Add(Item.Fruit); l.Add(Item.TimeSand); l.Add(Item.Gear); break;
            case Biome.Ice: l.Add(Item.IceShard); l.Add(Item.Fur); break;
            case Biome.Sunken: l.Add(Item.Coral); l.Add(Item.Pearl); l.Add(Item.AncientRelic); break;
            case Biome.Pirate: l.Add(Item.Fruit); l.Add(Item.ShellShard); l.Add(Item.GoldCoin); break;
            case Biome.Crystal: l.Add(Item.CrystalShard); break;
            case Biome.Storm: l.Add(Item.Fruit); l.Add(Item.Copper); break;
            case Biome.Insect: l.Add(Item.Nectar); l.Add(Item.Honey); l.Add(Item.Chitin); break;
            default: l.Add(Item.Fruit); break;
        }
        return l.ToArray();
    }

    /// Two lines for the chart's info box: what to farm and what lives there.
    string IslandContents(int n, int c)
    {
        var b = BiomeOf(n, c);
        var mats = new List<string>();
        foreach (var it in MaterialsOf(b)) mats.Add($"<color={GradeHex(Grade(it))}>{Name(it)}</color>");
        mats.Add($"<color={GradeHex(Grade(FruitFor(b)))}>{Name(FruitFor(b))} (หายากมาก)</color>");
        var mons = new List<string>();
        foreach (var k in EnemiesOf(b, (n - 1) / 15 > 0)) mons.Add(k == EnemyKind.Crab ? "ปู (กลางคืน)" : EnemyName(k));
        var boss = BossOf(b);
        if (boss != null) mons.Add($"<color=#ff8a7a>บอส: {EnemyName(boss.Value)}</color>");
        return $"<color=#9dff9d>วัตถุดิบ:</color> {string.Join(", ", mats)}\n<color=#ff9a8a>มอนสเตอร์:</color> {string.Join(", ", mons)}";
    }
}
