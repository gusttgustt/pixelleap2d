using UnityEngine;

/// Farming, farm animals and fishing (mostly for the private island; the fishing rod works on
/// every island's shore too).
///
/// A farm plot (NodeKind.Plot) keeps its state in WorldNode fields that the save already has:
///   dropItem  = the seed planted, dropCount = growth stage (0 empty, 1 seeds, 2 sprouts, 3 ripe),
///   hits      = 1 once watered for the current stage, and the regrow timer = time to the next stage.
/// An animal (Chicken / Cow) uses hits = 1 when fed, the timer = until its egg / milk is ready,
/// and available = the product is waiting to be collected.
public partial class IslandGame
{
    public static bool IsSeed(Item it) => it is Item.CarrotSeed or Item.TomatoSeed or Item.WheatSeed;
    static Item CropOf(Item seed) => seed switch { Item.TomatoSeed => Item.Tomato, Item.WheatSeed => Item.Wheat, _ => Item.Carrot };
    /// Seconds for each of the two growth stages, once watered.
    static float StageSeconds(Item seed) => seed switch { Item.TomatoSeed => 45f, Item.WheatSeed => 25f, _ => 35f };
    static (int min, int max) CropYield(Item seed) => seed switch { Item.TomatoSeed => (3, 5), Item.WheatSeed => (2, 4), _ => (2, 3) };

    Item preferredSeed = Item.CarrotSeed;
    bool HasRod => Count(Item.FishingRod) > 0;

    // ------------------------------------------------------------------ crops
    Item? PickSeed()
    {
        if (Count(preferredSeed) > 0) return preferredSeed;
        foreach (var s in new[] { Item.CarrotSeed, Item.TomatoSeed, Item.WheatSeed }) if (Count(s) > 0) return s;
        return null;
    }

    string PlotPrompt(WorldNode n) => n.dropCount switch
    {
        0 => PickSeed() is Item s ? $"ปลูก{Name(CropOf(s))} (เหลือ {Count(s)} เมล็ด)" : "แปลงว่าง (ไม่มีเมล็ดพันธุ์)",
        3 => $"เก็บเกี่ยว{Name(CropOf(n.dropItem))}",
        _ => n.hits == 0 ? $"รดน้ำ{Name(CropOf(n.dropItem))}" + (Count(Item.WateringCan) > 0 ? "" : " (ต้องมีบัวรดน้ำ)")
                         : Count(Item.Fertilizer) > 0 ? $"ใส่ปุ๋ยหมัก ({Count(Item.Fertilizer)}) ให้โตทันที"
                         : $"{Name(CropOf(n.dropItem))}กำลังโต ({Mathf.CeilToInt(n.TimerLeft)} วิ)",
    };

    void UsePlot(WorldNode n)
    {
        Vector3 at = n.transform.position;
        switch (n.dropCount)
        {
            case 0:
                var seed = PickSeed();
                if (seed == null) { Hint("ไม่มีเมล็ดพันธุ์ ซื้อได้ที่ร้านเมล็ดพันธุ์ในตลาด (ทางตะวันออกของเกาะ)", 3f); return; }
                inv[seed.Value]--;
                n.dropItem = seed.Value; n.dropCount = 1; n.hits = 0; n.available = false;
                n.Refresh();
                Popup($"ปลูก{Name(CropOf(seed.Value))}", at + Vector3.up * 0.5f, new Color(0.8f, 1f, 0.7f));
                Fx.Burst(at + Vector3.up * 0.1f, new Color(0.55f, 0.38f, 0.22f), 6, 0.5f, 1f);
                Sfx.Play("pickup", 0.3f);
                if (Count(Item.WateringCan) > 0) Hint("ปลูกแล้ว! กด E อีกครั้งเพื่อรดน้ำ", 2f);
                break;
            case 1:
            case 2:
                if (n.hits > 0 && Count(Item.Fertilizer) > 0) // compost: this stage is done right away
                {
                    inv[Item.Fertilizer]--;
                    n.StartTimer(0.05f);
                    Popup("ใส่ปุ๋ย! โตทันที", at + Vector3.up * 0.5f, new Color(0.8f, 1f, 0.5f));
                    Fx.Burst(at + Vector3.up * 0.3f, new Color(0.55f, 0.4f, 0.2f), 8, 0.6f, 1f);
                    return;
                }
                if (n.hits > 0) { Hint($"{Name(CropOf(n.dropItem))}กำลังโต อีก {Mathf.CeilToInt(n.TimerLeft)} วินาที (ปุ๋ยหมักช่วยให้โตทันที)", 2f); return; }
                if (Count(Item.WateringCan) == 0) { Hint("ต้องมีบัวรดน้ำ (ประดิษฐ์ได้ใน Tab หรือซื้อที่ร้านเมล็ดพันธุ์)", 3f); return; }
                n.hits = 1;
                n.StartTimer(StageSeconds(n.dropItem) * (NearScarecrow(n.grid) ? 0.75f : 1f) / GrowScale);
                n.Refresh();
                Popup("รดน้ำแล้ว", at + Vector3.up * 0.5f, new Color(0.55f, 0.85f, 1f));
                Fx.Burst(at + Vector3.up * 0.3f, new Color(0.6f, 0.85f, 1f), 10, 0.7f, 1.4f, 1f, 1f);
                Sfx.Play("drink", 0.4f);
                break;
            default:
                var (lo, hi) = CropYield(n.dropItem);
                Give(CropOf(n.dropItem), rnd.Next(lo, hi + 1) + Bonus(), n);
                if (rnd.NextDouble() < 0.4) Give(n.dropItem, 1, n); // sometimes a seed to plant again
                Fx.Burst(at + Vector3.up * 0.3f, new Color(0.5f, 0.85f, 0.35f), 10, 0.9f, 1.2f, 1f, 2f);
                n.dropCount = 0; n.hits = 0; n.available = false;
                n.Refresh();
                AddXp(2, at);
                break;
        }
    }

    bool NearScarecrow(Vector2 g)
    {
        foreach (var n in nodes) if (n != null && n.kind == NodeKind.Scarecrow && (n.grid - g).magnitude < 3.5f) return true;
        return false;
    }

    /// A watered plot's timer ran out: on to the next stage (it needs water again until ripe).
    public void CropGrown(WorldNode n)
    {
        if (n.dropCount is < 1 or > 2) return;
        n.dropCount++;
        n.hits = 0;
        n.available = n.dropCount >= 3;
        n.Refresh();
        if (n.available) Fx.Burst(n.transform.position + Vector3.up * 0.4f, new Color(1f, 0.95f, 0.5f), 8, 0.6f, 0.8f, 0.8f, 1f);
    }

    // ------------------------------------------------------------------ animals
    static (Item item, int n)[] FeedOf(NodeKind k) => k == NodeKind.Cow
        ? new[] { (Item.Wheat, 2), (Item.Fiber, 4), (Item.AnimalFeed, 1) }
        : new[] { (Item.Wheat, 1), (Item.Berry, 2), (Item.AnimalFeed, 1) };
    static float ProduceSeconds(NodeKind k) => k == NodeKind.Cow ? 70f : 40f;
    static string AnimalName(NodeKind k) => k == NodeKind.Cow ? "วัว" : "ไก่";

    string AnimalPrompt(WorldNode n)
    {
        string who = AnimalName(n.kind);
        if (n.available) return n.kind == NodeKind.Cow ? "รีดนมวัว" : "เก็บไข่ไก่";
        if (n.hits == 0)
        {
            var f = FeedOf(n.kind);
            return $"ให้อาหาร{who} ({Name(f[0].item)} {f[0].n} หรือ{Name(f[1].item)} {f[1].n})";
        }
        return $"{who}อิ่มแล้ว ({(n.kind == NodeKind.Cow ? "นม" : "ไข่")}อีก {Mathf.CeilToInt(n.TimerLeft)} วิ)";
    }

    void UseAnimal(WorldNode n)
    {
        Vector3 at = n.transform.position;
        string who = AnimalName(n.kind);
        if (n.available)
        {
            if (n.kind == NodeKind.Cow) Give(Item.Milk, 1 + (rnd.NextDouble() < 0.2 ? 1 : 0), n);
            else Give(Item.Egg, 1 + (rnd.NextDouble() < 0.25 ? 1 : 0), n);
            n.available = false; n.hits = 0;
            n.Refresh();
            n.Shake();
            AddXp(1, at);
            return;
        }
        if (n.hits > 0) { Hint($"{who}อิ่มแล้ว รออีก {Mathf.CeilToInt(n.TimerLeft)} วินาที", 2f); return; }
        foreach (var (food, amount) in FeedOf(n.kind))
        {
            if (Count(food) < amount) continue;
            inv[food] -= amount;
            n.hits = 1;
            n.StartTimer(ProduceSeconds(n.kind) / GrowScale);
            Popup($"{who}ชอบ{Name(food)}!", at + Vector3.up * 0.7f, new Color(1f, 0.6f, 0.7f));
            Fx.Burst(at + Vector3.up * 0.5f, new Color(1f, 0.45f, 0.55f), 6, 0.5f, 0.8f, 0.8f, 1.5f); // hearts
            Sfx.Play("eat", 0.4f);
            return;
        }
        var f = FeedOf(n.kind);
        Hint($"{who}หิว! ต้องใช้{Name(f[0].item)} {f[0].n} หรือ{Name(f[1].item)} {f[1].n}", 3f);
    }

    // ------------------------------------------------------------------ fishing with a rod
    int fishState;          // 0 no line out, 1 waiting for a bite, 2 a fish is biting
    float fishT;
    Vector2 fishSpot;
    SpriteRenderer bobber;

    void CastLine(Vector2 spot)
    {
        CancelLine();
        fishState = 1;
        fishSpot = spot;
        fishT = Random.Range(1.6f, 4.8f) * (1f - 0.1f * SkillLv(Skill.Fisher));
        bobber = new GameObject("Bobber").AddComponent<SpriteRenderer>();
        bobber.transform.SetParent(world);
        bobber.sprite = PixelArt.Bobber();
        bobber.sortingOrder = -29900;
        bobber.transform.position = GridToWorld(spot) + Vector3.down * 0.1f;
        if (player != null) player.PlayAction(PixelArt.AttackAnim, 14f);
        Fx.Burst(bobber.transform.position, new Color(0.8f, 0.92f, 1f), 5, 0.5f, 1f);
        Sfx.Play("splash", 0.3f);
    }

    void CancelLine()
    {
        fishState = 0;
        if (bobber != null) Destroy(bobber.gameObject);
        bobber = null;
    }

    void TickFishing(float dt)
    {
        if (fishState == 0) return;
        if (player == null || bobber == null || (player.grid - fishSpot).magnitude > 2.4f) { if (player != null) Popup("เก็บสายเบ็ด", PlayerPos + Vector3.up * 0.9f, Color.white); CancelLine(); return; }
        fishT -= dt;
        Vector3 basePos = GridToWorld(fishSpot) + Vector3.down * 0.1f;
        float bob = fishState == 2 ? -0.06f + Mathf.Sin(Time.time * 30f) * 0.03f : Mathf.Sin(Time.time * 3f) * 0.015f;
        bobber.transform.position = basePos + Vector3.up * bob;
        if (fishState == 1 && fishT <= 0f)
        {
            fishState = 2;
            fishT = 0.85f + 0.15f * SkillLv(Skill.Fisher);
            Fx.Burst(basePos, new Color(0.8f, 0.92f, 1f), 8, 0.7f, 1.3f);
            Sfx.Play("splash", 0.6f);
        }
        else if (fishState == 2 && fishT <= 0f)
        {
            Popup("ปลาหลุดไปแล้ว...", basePos + Vector3.up * 0.5f, new Color(0.8f, 0.9f, 1f));
            CancelLine();
        }
    }

    void ReelIn()
    {
        if (fishState == 2)
        {
            double r = rnd.NextDouble() - 0.02 * SkillLv(Skill.Fisher);
            Item got = r < 0.05 ? Item.GoldenFish : r < 0.27 ? Item.Tuna : r < 0.35 ? Item.ShellShard : r < 0.37 ? Item.Pearl : Item.RawFish;
            int n = got == Item.RawFish && Count(Item.DivingBell) > 0 ? 2 : 1;
            Give(got, n, null);
            if (got == Item.GoldenFish) Hint("ว้าว! ได้ปลาทองคำหายาก ขายที่ตลาดได้ราคาดีมาก", 4f);
            Fx.Burst(bobber.transform.position, new Color(0.8f, 0.92f, 1f), 10, 0.9f, 1.6f);
            AddXp(3, bobber.transform.position);
        }
        else Popup("ยังไม่มีปลากินเบ็ด", PlayerPos + Vector3.up * 0.9f, new Color(0.8f, 0.9f, 1f));
        CancelLine();
    }
}
