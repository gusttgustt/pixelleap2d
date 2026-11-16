using UnityEngine;

/// The Crystal island: everything here is made of glassy crystal that catches the light. In the
/// middle stands a sealed crystal vault; three mirrors around it must each be turned (E) until
/// their beam of light points straight at the vault - light all three and it opens. Crystal
/// golems shrug off half of every blow, mirror wisps throw splinters back at whoever hits them.
/// The glass artisan teaches you to make crystal armour (the best outfit so far).
public partial class IslandGame
{
    float beamFxT;

    /// Which way each mirror must face (0-3) - fixed for the island.
    int MirrorAnswer(WorldNode m) => new System.Random(seed + Mathf.RoundToInt(m.grid.x * 10f) * 7919 + Mathf.RoundToInt(m.grid.y * 10f)).Next(4); // by position: node ids change on reload
    bool MirrorLit(WorldNode m) => m.hits % 4 == MirrorAnswer(m);

    void BuildCrystalVault()
    {
        if (biome != Biome.Crystal) return;
        Vector2 c = Center + new Vector2(-1.4f, 1.4f);
        foreach (var n in nodes.FindAll(n => (n.grid - c).magnitude < 2.2f)) RemoveNode(n);
        Spawn(NodeKind.CrystalVault, c, false);
        var r = new System.Random(seed * 37 + 2);
        for (int i = 0; i < 3; i++) // three mirrors on a ring around the vault
        {
            float a = i * Mathf.PI * 2f / 3f + (float)r.NextDouble() * 0.6f;
            for (float d = 4.5f; d < 9f; d += 0.7f)
            {
                var g = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                int x = Mathf.RoundToInt(g.x), y = Mathf.RoundToInt(g.y);
                if (x < 1 || y < 1 || x >= Size - 1 || y >= Size - 1 || !(tiles[x, y] is 1 or 2)) continue;
                foreach (var n in nodes.FindAll(n => n != null && (n.grid - g).magnitude < 1f && !n.Blocking)) RemoveNode(n);
                if (!FreeSpot(g, 0.8f)) continue;
                var m = Spawn(NodeKind.Mirror, g, false);
                m.hits = (MirrorAnswer(m) + 1 + r.Next(3)) % 4; // start pointing the wrong way
                m.Refresh();
                break;
            }
        }
    }

    WorldNode Vault => nodes.Find(n => n != null && n.kind == NodeKind.CrystalVault);

    void TurnMirror(WorldNode m)
    {
        m.hits = (m.hits + 1) % 4;
        m.Refresh();
        Sfx.Play("craft", 0.4f);
        if (MirrorLit(m))
        {
            Popup("แสงพุ่งเข้าหาห้องนิรภัย!", m.transform.position + Vector3.up * 1f, new Color(0.8f, 0.95f, 1f));
            int lit = 0, all = 0;
            foreach (var n in nodes) if (n != null && n.kind == NodeKind.Mirror) { all++; if (MirrorLit(n)) lit++; }
            if (lit == all && !Flag("vault" + island))
            {
                flags.Add("vault" + island);
                var v = Vault;
                if (v != null) { v.Refresh(); Fx.Burst(v.transform.position + Vector3.up * 1f, new Color(0.85f, 0.95f, 1f), 30, 2f, 2f, 1.2f, 2f); }
                Sfx.Play("win", 0.7f);
                Hint("ห้องนิรภัยผลึกเปิดแล้ว! เข้าไปเอาสมบัติข้างในได้เลย", 6f);
            }
        }
    }

    void OpenVault(WorldNode v)
    {
        if (!Flag("vault" + island)) { Hint("ห้องนิรภัยปิดสนิท... หมุนกระจกทั้ง 3 บานให้แสงส่องมาที่นี่", 4f); return; }
        if (Flag("vaultopen" + island)) { Hint("ห้องนิรภัยว่างเปล่าแล้ว", 2f); return; }
        flags.Add("vaultopen" + island);
        Give(Item.CrystalShard, 8, v);
        Give(Item.GoldCoin, 25, v);
        Give(Item.Pearl, 3, v);
        Give(FruitFor((Biome)new System.Random(seed).Next(4)), 1, v);
        AddXp(80 + island * 4, v.transform.position);
        Sfx.Play("levelup", 0.7f);
    }

    /// Lit mirrors send a beam of light to the vault.
    void UpdateCrystal(float dt)
    {
        if (biome != Biome.Crystal || voyage != null) return;
        beamFxT -= dt;
        if (beamFxT > 0f) return;
        beamFxT = 0.12f;
        var v = Vault;
        if (v == null) return;
        Vector3 to = v.transform.position + Vector3.up * 1f;
        foreach (var m in nodes)
        {
            if (m == null || m.kind != NodeKind.Mirror || !MirrorLit(m)) continue;
            Vector3 from = m.transform.position + Vector3.up * 0.7f;
            float t = Random.value;
            Fx.Burst(Vector3.Lerp(from, to, t), new Color(1f, 0.97f, 0.8f), 1, 0.05f, 0.1f, 0.4f, 0f);
        }
    }

    float DrawCrystalHud(float y)
    {
        if (biome != Biome.Crystal || voyage != null) return y;
        int lit = 0, all = 0;
        foreach (var n in nodes) if (n != null && n.kind == NodeKind.Mirror) { all++; if (MirrorLit(n)) lit++; }
        string t = Flag("vault" + island) ? "ห้องนิรภัยผลึก: เปิดแล้ว" : $"กระจกส่องแสง {lit}/{all} (กด E ที่กระจกเพื่อหมุน)";
        ShadowLabel(new Rect(14, y, 360, 18), t, small, new Color(0.85f, 0.95f, 1f));
        return y + 16f;
    }

    // ------------------------------------------------------------------ the glass artisan
    Quest ArtisanQuest() => new Quest
    {
        id = $"glass{island}", title = "แสงแห่งห้องนิรภัย",
        bring = new[] { (Item.CrystalShard, 6) },
        kill = new[] { (EnemyKind.MirrorWisp, 3 + tier) },
        help = new[] { ($"vault{island}", "หมุนกระจกทั้ง 3 บานให้แสงเปิดห้องนิรภัยผลึก") },
        rewardText = "เรียนทำชุดเกราะผลึก",
        reward = () =>
        {
            learned.Add(Item.CrystalArmor);
            AddXp(140, PlayerPos);
            TechPopup(Item.CrystalArmor);
        },
    };

    const string ArtisanIntro = "ผลึกบนเกาะนี้รับแสงแล้วแข็งแกร่งยิ่งกว่าเหล็ก แต่ต้องเป็นแสงที่ผ่านห้องนิรภัยกลางเกาะ หมุนกระจกให้แสงส่องเข้าไปให้ครบ แล้วไล่ภูตกระจกที่ชอบมาแย่งเศษผลึกของข้า เอาเศษผลึกมาให้ข้าด้วย ข้าจะสอนทำชุดเกราะผลึกให้";
}
