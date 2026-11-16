using UnityEngine;

/// The Storm island: a dark island under a storm that never ends. Rain lashes down and lightning
/// strikes near you every few seconds (a yellow mark flashes where a bolt will land). Three old
/// lightning rods stand on the island, broken; mend them with copper and any bolt aimed near a
/// rod hits the rod instead. Thunder toads land with a shockwave, storm hawks dive at you, and the
/// Thunderbird calls lightning down around itself. The inventor teaches you the storm hood.
public partial class IslandGame
{
    float boltT = 8f, flashT;

    bool StormIsland => biome == Biome.Storm && voyage == null;
    bool RodsFixed => Flag("rods" + island);

    void BuildStormIsland()
    {
        if (biome != Biome.Storm) return;
        var r = new System.Random(seed * 41 + 6);
        PlaceRing(NodeKind.LightningRod, 3, 5f, 12f, r);
        PlaceRing(NodeKind.CopperOre, 4, 4f, 14f, r);
    }

    WorldNode RodNear(Vector2 g)
    {
        foreach (var n in nodes)
            if (n != null && n.kind == NodeKind.LightningRod && n.hits > 0 && (n.grid - g).magnitude < 4.5f) return n;
        return null;
    }

    void RepairRod(WorldNode rod)
    {
        if (rod.hits > 0) { Hint("สายล่อฟ้าต้นนี้ใช้งานได้แล้ว อยู่ใกล้ๆ มันฟ้าจะไม่ผ่าใส่เรา", 3f); return; }
        if (Count(Item.Copper) < 2) { Hint("ต้องใช้ทองแดง 2 ก้อนซ่อมสายล่อฟ้า (ขุดจากแร่ทองแดงสีส้ม)", 3f); return; }
        inv[Item.Copper] -= 2;
        rod.hits = 1;
        rod.Refresh();
        Sfx.Play("craft", 0.6f);
        Fx.Burst(rod.transform.position + Vector3.up * 1.5f, new Color(1f, 0.95f, 0.5f), 16, 1f, 1.6f);
        Popup("ซ่อมสายล่อฟ้าแล้ว!", rod.transform.position + Vector3.up * 1.8f, new Color(1f, 0.95f, 0.5f));
        if (!nodes.Exists(n => n != null && n.kind == NodeKind.LightningRod && n.hits == 0))
        {
            flags.Add("rods" + island);
            Hint("สายล่อฟ้าครบทั้งเกาะแล้ว! ฟ้าผ่าน้อยลงมาก", 5f);
        }
    }

    void UpdateStorm(float dt)
    {
        if (flashT > 0f) flashT -= dt;
        if (!StormIsland || player == null) return;
        if (Random.value < dt * 50f) // heavy rain
            Fx.Burst(cam.transform.position + new Vector3(Random.Range(-7f, 7f), 4f, 0f), new Color(0.6f, 0.7f, 0.9f, 0.7f), 1, 0.05f, 0.2f, 1f, 8f);
        if (dead || Busy) return;
        boltT -= dt;
        if (boltT > 0f) return;
        boltT = RodsFixed ? 14f : 7f;
        for (int i = 0; i < 2; i++)
        {
            Vector2 g = player.grid + Random.insideUnitCircle * 3f;
            var rod = RodNear(g);
            if (rod != null) { RodCatches(rod); continue; } // the rod draws the bolt away
            if (IsLand(g)) SpawnStrike(g, 1.1f + i * 0.4f, 0.9f, 18f, "ฟ้าผ่า!", new Color(1f, 0.95f, 0.3f));
        }
    }

    void RodCatches(WorldNode rod)
    {
        flashT = 0.15f;
        Vector3 top = rod.transform.position + Vector3.up * 1.6f;
        for (int i = 0; i < 8; i++) Fx.Burst(top + Vector3.up * i * 0.4f + (Vector3)(Random.insideUnitCircle * 0.15f), new Color(1f, 0.95f, 0.5f), 2, 0.1f, 0.4f);
        Sfx.Play("hit", 0.5f);
    }

    /// Called when any lightning bolt lands: the whole screen flashes.
    public void LightningFlash() => flashT = 0.15f;

    void DrawStormOverlay(float W, float H)
    {
        if (!StormIsland) return;
        Fill(new Rect(0, 0, W, H), new Color(0.05f, 0.06f, 0.12f, 0.25f)); // dark storm light
        if (flashT > 0f) Fill(new Rect(0, 0, W, H), new Color(1f, 1f, 0.95f, flashT * 2.5f));
    }

    float DrawStormHud(float y)
    {
        if (!StormIsland) return y;
        int fixedRods = 0, all = 0;
        foreach (var n in nodes) if (n != null && n.kind == NodeKind.LightningRod) { all++; if (n.hits > 0) fixedRods++; }
        ShadowLabel(new Rect(14, y, 360, 18), $"สายล่อฟ้า {fixedRods}/{all}  ฟ้าผ่าอีก {Mathf.Max(0f, boltT):0} วิ", small, new Color(1f, 0.95f, 0.5f));
        return y + 16f;
    }

    void SpawnThunderbird()
    {
        if (biome != Biome.Storm || Flag("thunderbird" + island) || !IsHost) return;
        if (enemies.Exists(e => e != null && e.kind == EnemyKind.Thunderbird)) return;
        Vector2 at = FarFrom(ShipwrightSpot(), Center);
        var e = new GameObject("Thunderbird").AddComponent<Enemy>();
        e.transform.SetParent(world);
        e.Init(EnemyKind.Thunderbird, at, tier, ++enemyCounter);
        enemies.Add(e);
    }

    // ------------------------------------------------------------------ the inventor
    Quest InventorQuest() => new Quest
    {
        id = $"storm{island}", title = "เชื่องพายุ",
        bring = new[] { (Item.Copper, 4) },
        help = new[] { ($"rods{island}", "ซ่อมสายล่อฟ้าให้ครบทั้งเกาะ"), ($"thunderbird{island}", "ปราบนกอัสนีที่เรียกพายุ") },
        rewardText = "เรียนทำผ้าคลุมพายุ (หมวก: โจมตีเร็ว +20%)",
        reward = () =>
        {
            learned.Add(Item.StormHood);
            AddXp(150, PlayerPos);
            TechPopup(Item.StormHood);
        },
    };

    const string InventorIntro = "ข้าคืออัสนี นักประดิษฐ์ผู้หลงรักพายุ! แต่พายุลูกนี้ไม่ธรรมดา นกอัสนีตัวมหึมาเป็นคนเรียกมันมา ช่วยซ่อมสายล่อฟ้าทั้งสามต้นด้วยทองแดง แล้วปราบนกอัสนีให้ได้ ข้าจะเย็บผ้าคลุมพายุให้ ใส่แล้วตัวเบาราวสายฟ้า";
}
