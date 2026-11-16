using System.Collections.Generic;
using UnityEngine;

/// The Pirate Cove: a sandy island taken over by a skeleton crew. Their warship lies offshore and
/// lobs cannonballs at the island (a red mark shows where one will land - get out of it). Skeleton
/// pirates rattle back to their feet once after you knock them down, monkeys hurl coconuts, and
/// their Captain calls down the guns. Dig up the buried treasure (X marks on the sand, needs a
/// shovel) for gold coins and spend them at the merchant. The old pirate teaches you to make the
/// pirate compass: it points to the nearest treasure, relic, shrine or pack leader on any island.
public partial class IslandGame
{
    class Strike { public Vector2 grid; public float t, radius, damage; public string why; public SpriteRenderer mark; public Color col; }
    readonly List<Strike> strikes = new List<Strike>();
    SpriteRenderer warship;
    Vector3 warshipPos;
    float barrageT = 12f;

    bool PirateIsland => biome == Biome.Pirate && voyage == null;
    bool GunsSilent => Flag("captain" + island);

    /// Something about to land at `g` (cannonball, coconut): a red mark, then a bang.
    public void SpawnStrike(Vector2 g, float delay, float radius, float damage, string why, Color? col = null)
    {
        var s = new Strike { grid = g, t = delay, radius = radius, damage = damage, why = why, col = col ?? new Color(1f, 0.25f, 0.2f) };
        s.mark = new GameObject("StrikeMark").AddComponent<SpriteRenderer>();
        s.mark.transform.SetParent(world);
        s.mark.sprite = PixelArt.StrikeMark();
        Vector3 flat = GridToWorld(g);
        s.mark.transform.position = flat + Vector3.up * HeightAt(g);
        s.mark.transform.localScale = Vector3.one * radius;
        s.mark.sortingOrder = SortOrder(flat.y) - 3;
        strikes.Add(s);
    }

    void UpdateStrikes(float dt)
    {
        for (int i = strikes.Count - 1; i >= 0; i--)
        {
            var s = strikes[i];
            if (s.mark == null) { strikes.RemoveAt(i); continue; } // the island changed
            s.t -= dt;
            s.mark.color = new Color(s.col.r, s.col.g, s.col.b, 0.45f + 0.4f * Mathf.Sin(Time.time * 18f));
            if (s.t > 0f) continue;
            Vector3 at = s.mark.transform.position;
            Fx.Burst(at + Vector3.up * 0.2f, new Color(1f, 0.6f, 0.2f), 18, 1.2f * s.radius, 2f, 0.7f, 2f);
            Fx.Burst(at + Vector3.up * 0.2f, new Color(0.3f, 0.3f, 0.3f), 10, 1f * s.radius, 1f, 1.2f, -1f);
            Sfx.Play("hit", 0.6f);
            if (s.why == "ฟ้าผ่า!")
            {
                LightningFlash();
                for (int k = 0; k < 10; k++) Fx.Burst(at + Vector3.up * k * 0.5f + (Vector3)(Random.insideUnitCircle * 0.2f), new Color(1f, 0.97f, 0.6f), 2, 0.1f, 0.4f); // the bolt
            }
            if (player != null && (player.grid - s.grid).magnitude <= s.radius && !(s.why == "ฟ้าผ่า!" && hat == Item.StormHood)) DamagePlayer(s.damage, s.why);
            Destroy(s.mark.gameObject);
            strikes.RemoveAt(i);
        }
    }

    // ------------------------------------------------------------------ building the cove
    void BuildPirateCove()
    {
        if (biome != Biome.Pirate) return;
        var r = new System.Random(seed * 31 + 13);
        PlaceOnTile(NodeKind.PirateShip, 1, 8f, 20f, r, 2.2f);   // their beached ship
        PlaceRing(NodeKind.Tent, 3, 4f, 8f, r);
        PlaceRing(NodeKind.Cannon, 3, 5f, 10f, r);
        for (int i = 0; i < 3; i++) PlaceOnTile(NodeKind.DigSpot, 1, 6f + i * 3f, 20f, r, 0.8f); // X marks the spot
    }

    /// The skeleton crew's warship anchored off the east shore (scenery that fires the guns).
    void BuildWarship()
    {
        warship = null;
        if (biome != Biome.Pirate) return;
        Vector2 best = Center;
        float bestScore = float.MinValue;
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
            {
                if (tiles[x, y] != 0) continue;
                bool nearLand = false;
                for (int dx = -3; dx <= 3 && !nearLand; dx++)
                    for (int dy = -3; dy <= 3 && !nearLand; dy++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < Size && ny < Size && tiles[nx, ny] is 1 or 2) nearLand = true;
                    }
                if (!nearLand) continue;
                float score = (x - y) - Mathf.Abs(x + y - (Size - 1)) * 0.6f;
                if (score > bestScore) { bestScore = score; best = new Vector2(x, y); }
            }
        warship = new GameObject("Warship").AddComponent<SpriteRenderer>();
        warship.transform.SetParent(world);
        warshipPos = GridToWorld(best + new Vector2(2f, -2f));
        warship.transform.position = warshipPos;
        warship.sortingOrder = SortOrder(warshipPos.y);
        warship.sprite = PixelArt.Warship();
    }

    void UpdatePirate(float dt)
    {
        UpdateStrikes(dt);
        if (!PirateIsland || player == null) return;
        if (warship != null) warship.transform.position = warshipPos + Vector3.up * Mathf.Sin(Time.time * 1.2f) * 0.05f;
        if (GunsSilent || dead || Busy) return;
        barrageT -= dt;
        if (barrageT > 0f) return;
        barrageT = 14f;
        if (warship != null) Fx.Burst(warshipPos + Vector3.up * 0.6f, new Color(0.4f, 0.4f, 0.4f), 14, 0.8f, 1f, 1.2f, -1f);
        Sfx.Play("hit", 0.4f);
        for (int i = 0; i < 3; i++) // a volley around you
        {
            Vector2 g = player.grid + Random.insideUnitCircle * 3.5f;
            if (IsLand(g)) SpawnStrike(g, 1.4f + i * 0.35f, 1.1f, 14f, "ลูกปืนใหญ่!");
        }
        Hint("ปืนใหญ่จากเรือโจรสลัด! หลบวงสีแดง", 2f);
    }

    float DrawPirateHud(float y)
    {
        if (!PirateIsland) return y;
        string t = GunsSilent ? "เรือรบโจรสลัดเงียบเสียงแล้ว" : $"เรือรบยิงปืนใหญ่อีกใน {Mathf.Max(0f, barrageT):0} วิ";
        ShadowLabel(new Rect(14, y, 360, 18), t, small, GunsSilent ? new Color(0.7f, 1f, 0.7f, 0.8f) : new Color(1f, 0.6f, 0.5f));
        return y + 16f;
    }

    // ------------------------------------------------------------------ treasure, gold, merchant
    void DigTreasure(WorldNode n)
    {
        if (Count(Item.Shovel) <= 0) { Hint("ต้องมีพลั่วถึงจะขุดได้ (ประดิษฐ์ Tab)", 3f); return; }
        var r = new System.Random(seed + n.id * 17);
        Give(Item.GoldCoin, 8 + r.Next(8), n);
        Give(new[] { Item.Rope, Item.CookedMeat, Item.Pearl }[r.Next(3)], 2 + r.Next(3), n);
        if (r.Next(5) == 0) Give(FruitFor(Biome.Tropical + r.Next(4)), 1, n);
        Fx.Burst(n.transform.position, new Color(0.85f, 0.7f, 0.4f), 16, 1f, 1.6f, 0.8f, 3f);
        Sfx.Play("chop", 0.6f);
        n.Deplete(1e9f);
        if (!nodes.Exists(x => x != null && x.kind == NodeKind.DigSpot && x.available))
        {
            flags.Add("treasure" + island);
            Hint("ขุดสมบัติครบทุกจุดแล้ว!", 4f);
        }
    }

    static readonly (Item item, int n, int price)[] MerchantStock =
    {
        (Item.CookedMeat, 3, 5), (Item.Antidote, 1, 8), (Item.Rope, 5, 4), (Item.Shovel, 1, 6),
        (Item.FruitDash, 1, 45), (Item.FruitBubble, 1, 45), (Item.Pearl, 2, 10),
    };

    void MerchantDialog(Villager v, Dialog d, string text)
    {
        d.text = text ?? $"อยากได้อะไรก็ว่ามา ทองคำพูดได้ทุกภาษา! (มีเหรียญทอง {Count(Item.GoldCoin)})";
        foreach (var s in MerchantStock)
        {
            var st = s;
            d.options.Add(($"ซื้อ{Name(st.item)} x{st.n}  ({st.price} เหรียญ)", () =>
            {
                if (Count(Item.GoldCoin) < st.price) return;
                inv[Item.GoldCoin] -= st.price;
                Give(st.item, st.n, null);
                OpenDialog(v, "ขอบใจ! ของดีราคาถูก มีที่นี่ที่เดียว");
            }, Count(Item.GoldCoin) >= st.price));
        }
    }

    /// Captain Blackbones guards his beached ship until someone beats him.
    void SpawnCaptain()
    {
        if (biome != Biome.Pirate || Flag("captain" + island) || !IsHost) return;
        if (enemies.Exists(e => e != null && e.kind == EnemyKind.Captain)) return;
        var ship = nodes.Find(n => n != null && n.kind == NodeKind.PirateShip);
        Vector2 at = WalkableNear(ship != null ? ship.grid + (Center - ship.grid).normalized * 2.2f : FarFrom(ShipwrightSpot(), Center));
        var e = new GameObject("Captain").AddComponent<Enemy>();
        e.transform.SetParent(world);
        e.Init(EnemyKind.Captain, at, tier, ++enemyCounter);
        enemies.Add(e);
    }

    // ------------------------------------------------------------------ the old pirate
    Quest OldPirateQuest() => new Quest
    {
        id = $"pirate{island}", title = "ขุมทรัพย์ของกัปตัน",
        help = new[] { ($"treasure{island}", "ขุดสมบัติตามรอยกากบาท X ให้ครบทุกจุด"), ($"captain{island}", "ปราบกัปตันกระดูกดำ") },
        rewardText = "เรียนทำเข็มทิศโจรสลัด",
        reward = () =>
        {
            learned.Add(Item.Compass);
            AddXp(130, PlayerPos);
            TechPopup(Item.Compass);
        },
    };

    const string OldPirateIntro = "ฮึ่ม เจ้าหน้าใหม่ ข้าเคยเป็นต้นหนเรือลำนั้น ก่อนกัปตันกระดูกดำจะสาปลูกเรือให้เป็นโครงกระดูกหมด! สมบัติของมันฝังอยู่ตามหาดทราย ดูรอยกากบาทไว้ หาพลั่วมาขุดซะ แล้วจัดการกัปตันให้สิ้นซาก ข้าจะสอนทำเข็มทิศที่ชี้หาสมบัติได้ทุกเกาะ";

    // ------------------------------------------------------------------ the compass
    /// Pirate compass: where's the nearest unopened treasure, relic, shrine, lost bundle or pack leader.
    float DrawCompassHud(float y)
    {
        if (Count(Item.Compass) <= 0 || player == null || voyage != null) return y;
        Vector2? best = null; string what = null; float bd = float.MaxValue;
        foreach (var n in nodes)
        {
            if (n == null) continue;
            string w = n.kind switch
            {
                NodeKind.Chest when n.available => "หีบสมบัติ",
                NodeKind.DigSpot when n.available => "สมบัติฝัง",
                NodeKind.Relic when n.available => "แผ่นจารึก",
                NodeKind.Shrine when !Flag("shrine" + island) => "ศาลเจ้า",
                NodeKind.LostBundle => "ห่อของหาย",
                NodeKind.GlowTree when n.available => "ต้นไม้เรืองแสง",
                _ => null,
            };
            if (w == null) continue;
            float d = (n.grid - player.grid).magnitude;
            if (d < bd) { bd = d; best = n.grid; what = w; }
        }
        foreach (var e in enemies)
            if (e != null && e.alpha) { float d = (e.grid - player.grid).magnitude; if (d < bd) { bd = d; best = e.grid; what = "จ่าฝูง"; } }
        string text = best == null ? "เข็มทิศ: ไม่มีสมบัติเหลือบนเกาะนี้" : $"เข็มทิศ: {what} ทิศ{ScreenDir(best.Value - player.grid)} {bd * 10f:0} ม.";
        ShadowLabel(new Rect(14, y, 360, 18), text, small, new Color(1f, 0.85f, 0.4f));
        return y + 16f;
    }

    static string ScreenDir(Vector2 gridDelta)
    {
        Vector3 w = GridToWorld(gridDelta) - GridToWorld(Vector2.zero);
        float a = Mathf.Atan2(w.y, w.x) * Mathf.Rad2Deg;
        string[] names = { "ตะวันออก", "ตะวันออกเฉียงเหนือ", "เหนือ", "ตะวันตกเฉียงเหนือ", "ตะวันตก", "ตะวันตกเฉียงใต้", "ใต้", "ตะวันออกเฉียงใต้" };
        int i = Mathf.RoundToInt(Mathf.Repeat(a, 360f) / 45f) % 8;
        return names[i];
    }
}
