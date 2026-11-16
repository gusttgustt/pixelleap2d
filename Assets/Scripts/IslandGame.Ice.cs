using UnityEngine;

/// The Ice island: a frozen island in the middle of a warm sea. The cold eats your warmth; stay
/// near a campfire or soak in a steaming hot spring. The beaches are sheets of ice you slide on.
/// Melting snowmen burst into little snowlings when beaten, frost bats chill you to the bone.
/// Help the snow hunter and the village gets warm; he teaches you to make spiked boots (no more
/// sliding on ice, and quicker in the snow).
public partial class IslandGame
{
    public float warmth = 100f;
    bool coldWarned;

    bool OnIceIsland => biome == Biome.Ice && voyage == null;
    /// Standing on a sheet of ice (the island's "beach") without spiked boots: you slide.
    public bool Slippery => OnIceIsland && player != null && Count(Item.SpikedBoots) == 0 && TileAt(player.grid) == 1;
    int TileAt(Vector2 g)
    {
        int x = Mathf.RoundToInt(g.x), y = Mathf.RoundToInt(g.y);
        return x >= 0 && y >= 0 && x < Size && y < Size ? tiles[x, y] : 0;
    }
    /// Seconds for full warmth to run out in the open (night is colder).
    float ColdSeconds => (Flag("warm" + island) ? 300f : 150f) * (IsNight ? 0.6f : 1f);
    bool NearWarmth => player != null && (DistanceToFire(player.grid) < 3f
        || nodes.Exists(n => n != null && n.kind == NodeKind.HotSpring && (n.grid - player.grid).magnitude < 1.8f));

    public void Chill(float amount)
    {
        if (!OnIceIsland) return;
        warmth = Mathf.Max(0f, warmth - amount);
        Popup("หนาวเหน็บ!", PlayerPos + Vector3.up * 1f, new Color(0.7f, 0.9f, 1f));
    }

    void PlaceHotSprings()
    {
        if (biome != Biome.Ice) return;
        var r = new System.Random(seed * 19 + 4);
        PlaceRing(NodeKind.HotSpring, 3, 6f, 12f, r);
    }

    void UpdateIce(float dt)
    {
        if (!OnIceIsland || player == null) { warmth = 100f; coldWarned = false; return; }
        if (Random.value < dt * 12f) // snowfall drifting across the screen
            Fx.Burst(cam.transform.position + new Vector3(Random.Range(-6f, 6f), 3.5f, 0f), Color.white, 1, 0.1f, 0.3f, 4f, 0.4f);
        if (dead || Busy) return;
        if (NearWarmth)
        {
            warmth = Mathf.Min(100f, warmth + dt * 25f);
            coldWarned = false;
        }
        else warmth = Mathf.Max(0f, warmth - dt * 100f / ColdSeconds);
        if (warmth < 25f && !coldWarned) { coldWarned = true; Hint("หนาวจนตัวสั่น! รีบไปผิงกองไฟหรือแช่บ่อน้ำร้อน", 5f); }
        if (warmth <= 0f) { health -= dt * 2.5f; CheckDeath(); }
    }

    float DrawIceHud(float y)
    {
        if (!OnIceIsland) return y;
        Color col = warmth < 25f ? new Color(0.6f, 0.75f, 1f) : new Color(1f, 0.6f, 0.3f);
        Bar(new Rect(14, y + 2, 180, 12), warmth / 100f, col, NearWarmth ? "ความอบอุ่น (กำลังอุ่นขึ้น)" : $"ความอบอุ่น {Mathf.CeilToInt(warmth)}");
        return y + 18f;
    }

    /// A beaten melting snowman bursts into two little snowlings.
    void SplitSnowman(Vector2 at)
    {
        if (!IsHost) return;
        for (int i = 0; i < 2; i++)
        {
            Vector2 g = at + new Vector2(i == 0 ? 0.5f : -0.5f, i == 0 ? -0.3f : 0.3f);
            if (!CanWalk(g, 0.2f)) g = at;
            var e = new GameObject("Snowling").AddComponent<Enemy>();
            e.transform.SetParent(world);
            e.Init(EnemyKind.Snowling, g, tier, ++enemyCounter);
            enemies.Add(e);
        }
        Fx.Burst(GridToWorld(at) + Vector3.up * 0.4f, Color.white, 16, 1.2f, 1.4f);
    }

    // ------------------------------------------------------------------ the snow hunter
    Quest HunterQuest() => new Quest
    {
        id = $"warm{island}", title = "ไฟอุ่นให้หมู่บ้าน",
        bring = new[] { (Item.Wood, 10), (Item.Fur, 3) },
        kill = new[] { (EnemyKind.Snowman, 4 + tier) },
        rewardText = "หมู่บ้านอบอุ่นขึ้น (หนาวช้าลง) + เรียนทำรองเท้าหนาม",
        reward = () =>
        {
            flags.Add("warm" + island);
            learned.Add(Item.SpikedBoots);
            AddXp(80, PlayerPos);
            TechPopup(Item.SpikedBoots);
        },
    };

    const string HunterIntro = "ปีนี้หนาวกว่าทุกปี มนุษย์หิมะเดินเพ่นพ่านมาดับกองไฟของเราทุกคืน ฟันมันแล้วระวังด้วย มันแตกเป็นตัวเล็กๆ ได้! เอาฟืนกับขนค้างคาวน้ำแข็งมาให้ข้าทำผ้าห่มกับกองไฟใหญ่ ข้าจะสอนทำรองเท้าหนาม เดินบนน้ำแข็งไม่ลื่นอีกเลย";
}
