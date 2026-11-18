using UnityEngine;

/// Special fruits: a rare glowing tree on some islands bears one kind of fruit per island type.
/// Eating one gives you its power (one power at a time). Using a power trains it: Lv 1 -> 5.
///   Dash   (ผลฟ้าแลบ)   - Tropical, Sky, Storm            : dash forward; from Lv 3 it hurts what you pass through
///   Shadow (ผลเงาจันทร์) - Mushroom, Hourglass, Crystal    : turn invisible, creatures lose track of you
///   Vine   (ผลเถาวัลย์)  - Jungle, Insect                 : pull the nearest creature in (and hit it), or grab the nearest pickup
///   Magnet (ผลแม่เหล็ก)  - Tribe, Ice, Pirate             : gather every pickup around you at once
///   Bubble (ผลฟองสบู่)   - Volcano, Turtle, Bubble, Sunken : a bubble that blocks the next hits and poison
public enum Power { None, Dash, Shadow, Vine, Magnet, Bubble,
    Frost, Mirror, Gale, Flame, Quake, Iron, Gravity, Star, Abyss } // the later continents' fruits (IslandGame.NewPowers.cs)

public partial class IslandGame
{
    public Power power;
    public int powerLv = 1, powerXp;
    float powerCd, invisibleT, bubbleT;
    int bubbleHits;

    public bool Invisible => invisibleT > 0f;
    public bool Bubbled => bubbleT > 0f && bubbleHits > 0;
    static readonly int[] PowerXpNeed = { 0, 5, 12, 22, 35 }; // uses needed to reach Lv 2..5

    /// Which fruit an island type's glowing tree bears - spread so every power turns up
    /// all through the 15-island cycle, each picked to suit its island.
    public static Item FruitFor(Biome b) => b switch
    {
        Biome.Mushroom or Biome.Hourglass or Biome.Crystal => Item.FruitShadow,       // dim caps, frozen time, light & shade
        Biome.Jungle or Biome.Insect => Item.FruitVine,                              // tangled green
        Biome.Tribal or Biome.Ice or Biome.Pirate => Item.FruitMagnet,               // iron, frozen loot, gold
        Biome.Volcanic or Biome.Turtle or Biome.Bubble or Biome.Sunken => Item.FruitBubble, // sea & bubbles
        _ => Item.FruitDash,                                                         // Tropical, Sky, Storm
    };

    /// Any of this continent's fruits (treasure, vaults).
    public static Item RandomPowerFruit(System.Random r) { var set = ContinentFruits[Mathf.Clamp(I != null ? I.Cont : 0, 0, Continents - 1)]; return set[r.Next(set.Length)]; }
    public static bool IsPowerFruit(Item it) => it is Item.FruitDash or Item.FruitShadow or Item.FruitVine or Item.FruitMagnet or Item.FruitBubble || IsNewFruit(it);
    static Power PowerOf(Item it) => IsNewFruit(it) ? NewPowerOf(it) : it switch
    {
        Item.FruitShadow => Power.Shadow, Item.FruitVine => Power.Vine, Item.FruitMagnet => Power.Magnet,
        Item.FruitBubble => Power.Bubble, _ => Power.Dash,
    };

    public static string PowerName(Power p) => p switch
    {
        Power.Dash => "ฟ้าแลบ", Power.Shadow => "เงาจันทร์", Power.Vine => "เถาวัลย์",
        Power.Magnet => "แม่เหล็ก", Power.Bubble => "ฟองสบู่", _ => NewPowerName(p),
    };

    static string PowerDesc(Power p) => p switch
    {
        Power.Dash => "พุ่งตัวไปข้างหน้าอย่างรวดเร็ว (Lv 3 ขึ้นไปพุ่งชนแล้วทำดาเมจ)",
        Power.Shadow => "ล่องหน สัตว์ประหลาดมองไม่เห็นชั่วคราว",
        Power.Vine => "ดึงสัตว์ประหลาดที่อยู่ไกลเข้ามาตี หรือดึงของที่อยู่ไกลมาเก็บ",
        Power.Magnet => "ดูดของรอบตัวเข้ากระเป๋าทั้งหมด",
        Power.Bubble => "สร้างโล่ฟองกันการโจมตีและพิษ",
        _ => NewPowerDesc(p),
    };

    float PowerCooldown => Awakened ? AwakenSkillCd : BasePowerCooldown;
    float BasePowerCooldown => power switch
    {
        Power.Dash => 4f - 0.4f * powerLv,
        Power.Shadow => 16f - 1.5f * powerLv,
        Power.Vine => 6f - 0.6f * powerLv,
        Power.Magnet => 20f - 2f * powerLv,
        Power.Bubble => 15f - 1.5f * powerLv,
        Power.None => 1f,
        _ => NewPowerCooldown,
    };

    // ------------------------------------------------------------------ the glowing tree
    /// Most islands hide one glowing fruit tree somewhere inland.
    void PlaceGlowingTree()
    {
        var r = new System.Random(seed * 31 + 7);
        if (r.NextDouble() > 0.7) return; // not every island has one
        for (int i = 0; i < 300; i++)
        {
            var g = new Vector2(r.Next(Size), r.Next(Size));
            if (tiles[(int)g.x, (int)g.y] != 2 || (g - Center).magnitude < 6f || !FreeSpot(g, 0.8f)) continue;
            Spawn(NodeKind.GlowTree, g, false);
            return;
        }
    }

    void PickGlowingFruit(WorldNode n)
    {
        var fruit = FruitHere(biome); // the later continents grow their own (IslandGame.NewPowers.cs)
        Give(fruit, 1, n);
        Fx.Burst(n.transform.position + Vector3.up * 1f, new Color(1f, 0.9f, 0.4f), 16, 1.4f, 1.6f, 1f, 2f);
        Hint($"ได้{Name(fruit)}! คลิกในกระเป๋าเพื่อกิน แล้วได้พลัง{PowerName(PowerOf(fruit))}", 6f);
        Sfx.Play("levelup", 0.5f);
        DepleteShared(n, 900f); // fruits again after a few days
    }

    // ------------------------------------------------------------------ eating
    void EatPowerFruit(Item it)
    {
        if (Count(it) <= 0) return;
        var p = PowerOf(it);
        if (power != Power.None && power != p)
        {
            var d = new Dialog { speaker = Name(it) };
            d.text = $"กินแล้วพลัง{PowerName(power)} Lv {powerLv} ที่มีอยู่จะหายไป แล้วได้พลัง{PowerName(p)} Lv 1 แทน\n{PowerName(p)}: {PowerDesc(p)}";
            d.options.Add(($"กินเลย (เปลี่ยนเป็นพลัง{PowerName(p)})", () => { CloseDialog(); GainPower(it, p); }, true));
            d.options.Add(("เก็บไว้ก่อน", CloseDialog, true));
            dialog = d;
            craftOpen = false; skillsOpen = false;
            return;
        }
        GainPower(it, p);
    }

    void GainPower(Item it, Power p)
    {
        inv[it]--;
        if (power == p) { AddPowerXp(5); Popup($"พลัง{PowerName(p)} แข็งแกร่งขึ้น!", PlayerPos + Vector3.up * 1f, new Color(1f, 0.9f, 0.4f)); return; }
        power = p; powerLv = 1; powerXp = 0; powerCd = 0f;
        Sfx.Play("levelup", 0.7f);
        Fx.Burst(PlayerPos + Vector3.up * 0.5f, new Color(1f, 0.9f, 0.4f), 24, 1.6f, 2f, 1f, 3f);
        Popup($"ได้พลัง{PowerName(p)}!", PlayerPos + Vector3.up * 1.1f, new Color(1f, 0.9f, 0.4f));
        Hint($"พลัง{PowerName(p)}: {PowerDesc(p)}  กด R (หรือปุ่มพลัง) เพื่อใช้ ยิ่งใช้ยิ่งเก่งขึ้น", 7f);
        if (level >= AwakenLevel) Hint($"พลัง{PowerName(p)}: {PowerDesc(p)}  เลเวล {AwakenLevel} แล้ว ปลุกพลัง \"{AwakenName(p)}\" ได้ด้วยปุ่ม Y", 7f);
    }

    void AddPowerXp(int n)
    {
        if (powerLv >= 5) return;
        powerXp += n;
        while (powerLv < 5 && powerXp >= PowerXpNeed[powerLv])
        {
            powerLv++;
            Popup($"พลัง{PowerName(power)} Lv {powerLv}!", PlayerPos + Vector3.up * 1.3f, new Color(1f, 0.9f, 0.4f));
            Sfx.Play("levelup", 0.6f);
            if (player != null || voyage != null) // a power level-up is a moment: a ring of its colour and a fountain of sparks
            {
                var c = PowerColor(power);
                Shockwave(MeGrid, 1.5f + 0.4f * powerLv, c, 0.7f);
                Fx.Rise(PlayerPos, Color.Lerp(c, Color.white, 0.3f), 20 + powerLv * 6, 0.5f, 2.2f, 0.9f, 2f, 0.8f);
                if (powerLv >= 5) { Shockwave(MeGrid, 3.5f, Color.white, 0.8f, 0.15f); ScreenFlash(new Color(c.r, c.g, c.b, 0.3f), 0.5f); Shake(0.08f, 0.35f); }
            }
            if (powerLv == 5 && power == Power.Dash) Hint("ฟ้าแลบ Lv 5! สายฟ้าฟาด (T) ผ่าลงรอบตัวคุณด้วยแล้ว", 5f);
        }
    }

    // ------------------------------------------------------------------ using
    void TickPower(float dt)
    {
        powerCd -= dt;
        powerAtkCd -= dt;
        if (invisibleT > 0f) invisibleT -= dt;
        if (bubbleT > 0f) { bubbleT -= dt; if (bubbleT <= 0f) bubbleHits = 0; }
        TickNewPowers(dt); // iron skin, mirror shield (IslandGame.NewPowers.cs)
        PowerAura(dt);
        TickAwaken(dt); // Lv 60 awakening (IslandGame.Awaken.cs)
    }

    bool UsePower()
    {
        if (power == Power.None) { Hint("ยังไม่มีพลัง หาต้นไม้เรืองแสงบนเกาะ แล้วกินผลไม้พิเศษ", 3f); return false; }
        if (powerCd > 0f || (player == null && voyage == null) || dead || Busy) return false;
        bool used = voyage != null ? UseSeaPower() : power switch
        {
            Power.Dash => DoDash(),
            Power.Shadow => DoShadow(),
            Power.Vine => DoVine(),
            Power.Magnet => DoMagnet(),
            Power.Bubble => DoBubble(),
            _ => DoNewPower(), // IslandGame.NewPowers.cs
        };
        if (!used) return false;
        powerCd = PowerCooldown;
        AddPowerXp(1);
        return true;
    }

    bool DoDash()
    {
        float dist = (2.2f + 0.45f * powerLv) * AwakenReach;
        Vector2 start = player.grid;
        player.Dash(dist);
        Sfx.Play("splash", 0.4f);
        DashFx(start, dist);
        if (powerLv >= 3) // lightning: everything along the path gets zapped
        {
            Vector2 from = player.grid, to = player.grid + player.facing * dist;
            foreach (var e in enemies.ToArray())
            {
                if (e == null || e.Disguised) continue;
                if (DistToSegment(e.grid, from, to) > 0.7f) continue;
                HitWithPower(e, 1 + powerLv);
            }
        }
        return true;
    }

    bool DoShadow()
    {
        invisibleT = 3f + powerLv;
        ShadowFx();
        Popup("ล่องหน!", PlayerPos + Vector3.up * 1f, new Color(0.75f, 0.7f, 1f));
        return true;
    }

    bool DoVine()
    {
        float range = (4f + powerLv) * AwakenReach;
        Enemy best = null;
        float bd = range;
        foreach (var e in enemies)
        {
            if (e == null || e.Disguised) continue;
            float d = (e.grid - player.grid).magnitude;
            if (d < bd) { bd = d; best = e; }
        }
        if (best != null)
        {
            Vector2 near = player.grid + (best.grid - player.grid).normalized * 0.8f;
            if (CanWalk(near, 0.2f)) best.grid = near;
            VineFx(best.transform.position);
            HitWithPower(best, 1 + powerLv);
            return true;
        }
        var node = NearestPickup(range);
        if (node == null) { Hint("ไม่มีอะไรให้ดึงในระยะ", 1.5f); return false; }
        VineFx(node.transform.position);
        Interact(node);
        return true;
    }

    bool DoMagnet()
    {
        float range = (2f + powerLv) * AwakenReach;
        int got = 0;
        foreach (var n in nodes.ToArray())
        {
            if (n == null || !IsPickup(n) || (n.grid - player.grid).magnitude > range) continue;
            MagnetPullFx(n.transform.position);
            Interact(n);
            if (++got >= (4 + powerLv * 2) * (Awakened ? 2 : 1)) break;
        }
        if (got == 0) { Hint("ไม่มีของรอบตัวให้ดูด", 1.5f); return false; }
        MagnetFx(range);
        Popup($"ดูดของ {got} ชิ้น!", PlayerPos + Vector3.up * 1.1f, new Color(1f, 0.6f, 0.6f));
        return true;
    }

    bool DoBubble()
    {
        bubbleT = 6f + powerLv;
        bubbleHits = 1 + powerLv / 2;
        poisonT = 0f;
        Popup("โล่ฟองสบู่!", PlayerPos + Vector3.up * 1f, new Color(0.6f, 0.9f, 1f));
        BubbleFx();
        return true;
    }

    /// Bubble and dash soak up a hit. Returns true if the hit is blocked.
    bool PowerBlocks()
    {
        if ((player != null && player.Dashing) || (voyage != null && voyage.dashT > 0f)) return true;
        if (!Bubbled) return false;
        bubbleHits--;
        if (bubbleHits <= 0) bubbleT = 0f;
        if (mirrorT > 0f) { Popup("สะท้อนกลับ!", PlayerPos + Vector3.up * 0.9f, PowerColor(Power.Mirror)); MirrorReflect(); return true; }
        Popup("ฟองกันไว้!", PlayerPos + Vector3.up * 0.9f, new Color(0.6f, 0.9f, 1f));
        Fx.Burst(PlayerPos + Vector3.up * 0.4f, new Color(0.7f, 0.95f, 1f), 10, 1f, 1.2f);
        return true;
    }

    void HitWithPower(Enemy e, int dmg)
    {
        dmg = AwakenDmg(dmg);
        Vector3 at = e.transform.position;
        if (e.puppet) { e.Hit(dmg, player.grid); Send(new NetMsg { t = "hit", to = hostId, id = e.netId, dmg = dmg }); return; }
        if (e.Hit(dmg, player.grid))
        {
            enemies.Remove(e);
            if (Shared) Send(new NetMsg { t = "kill", id = e.netId, by = myId, kind = (int)e.kind, x = at.x, y = at.y });
            KillExtras(e);
            GrantKill(e.kind, e.xp, at);
        }
        else Popup($"-{dmg}", at + Vector3.up * 0.5f, new Color(1f, 0.9f, 0.4f));
    }

    static bool IsPickup(WorldNode n) => n.available && n.kind is NodeKind.Stick or NodeKind.Rock or NodeKind.Grass or NodeKind.Bush
        or NodeKind.FruitTree or NodeKind.Bamboo or NodeKind.Obsidian or NodeKind.GlowShroom;

    WorldNode NearestPickup(float range)
    {
        WorldNode best = null;
        float bd = range;
        foreach (var n in nodes)
        {
            if (n == null || !IsPickup(n)) continue;
            float d = (n.grid - player.grid).magnitude;
            if (d < bd) { bd = d; best = n; }
        }
        return best;
    }


    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * t)).magnitude;
    }

    // ------------------------------------------------------------------ HUD
    /// "Power: Dash Lv 2 [R]" with a cooldown bar and level progress.
    float DrawPowerHud(float y)
    {
        if (power == Power.None) return y;
        float ready = PowerCooldown <= 0f ? 1f : 1f - Mathf.Clamp01(powerCd / PowerCooldown);
        string lv = powerLv >= 5 ? "Lv 5 (สูงสุด)" : $"Lv {powerLv}  ({powerXp}/{PowerXpNeed[powerLv]})";
        Bar(new Rect(14, y + 2, 180, 12), ready, ready >= 1f ? new Color(1f, 0.8f, 0.25f) : new Color(0.55f, 0.5f, 0.35f),
            ready >= 1f ? $"พลัง{PowerName(power)} {lv} [R]" : $"พลัง{PowerName(power)} {lv}");
        y += 18f;
        float atk = PowerAttackReady;
        Bar(new Rect(14, y + 2, 180, 10), atk, atk >= 1f ? PowerColor(power) : new Color(0.45f, 0.42f, 0.38f),
            atk >= 1f ? $"ท่าพิเศษ: {PowerAttackName(power)} [T]" : $"ท่าพิเศษ: {PowerAttackName(power)}");
        y += 19f;
        if (Invisible) { ShadowLabel(new Rect(14, y, 300, 18), $"ล่องหนอยู่ {invisibleT:0.0} วิ", small, new Color(0.75f, 0.7f, 1f)); y += 19f; }
        if (Bubbled) { ShadowLabel(new Rect(14, y, 300, 18), $"โล่ฟองสบู่: กันได้อีก {bubbleHits} ครั้ง", small, new Color(0.6f, 0.9f, 1f)); y += 19f; }
        y = DrawAwakenHud(y);
        return y;
    }

    public float PowerReady => power == Power.None ? 0f : PowerCooldown <= 0f ? 1f : 1f - Mathf.Clamp01(powerCd / PowerCooldown);
}
