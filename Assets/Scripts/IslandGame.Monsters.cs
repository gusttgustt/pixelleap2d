using UnityEngine;

/// Every continent after the first brings creatures of its own on top of each island's, each
/// stranger than the last:
///   ทวีปน้ำแข็ง       ผีหิมะหน้ากระจก (steps through mirrors to behind you, a freezing touch)
///                     แมมมอธน้ำแข็งสองหัว (winds up, then charges; the hit slows you)
///   ทวีปภูเขาไฟ       ซาลาแมนเดอร์ลาวา (spits fireballs: a red mark where they land)
///                     โกเลมหินหนืด (stamps: the ground cracks in a ring around it; half of every blow bounces off)
///   ทวีปฟ้าและทะเลลึก  แมงกะพรุนเวหา (pulses lightning around itself)
///                     อสูรตาร้อยดวง (stares a beam straight at you)
///
/// And people with fruit powers (นักล่าผลไม้): from about the 8th island of the world on, more of
/// them the further out, and from about the 15th some have awakened their power (an aura of
/// its colour, much stronger, faster abilities). Beating one is news: bounty, and now and then a
/// special fruit. Their power and whether it's awakened come from their id, so every player
/// online sees the same.
public partial class IslandGame
{
    public static string MonsterName(EnemyKind k) => k switch
    {
        EnemyKind.MirrorWraith => "ผีหิมะหน้ากระจก", EnemyKind.FrostMammoth => "แมมมอธน้ำแข็งสองหัว",
        EnemyKind.LavaSalamander => "ซาลาแมนเดอร์ลาวา", EnemyKind.MagmaGolem => "โกเลมหินหนืด",
        EnemyKind.SkyJelly => "แมงกะพรุนเวหา", EnemyKind.StarWatcher => "อสูรตาร้อยดวง",
        _ => k.ToString(),
    };

    static EnemyKind[] ContinentCreatures(int c) => c switch
    {
        1 => new[] { EnemyKind.MirrorWraith, EnemyKind.FrostMammoth },
        2 => new[] { EnemyKind.LavaSalamander, EnemyKind.MagmaGolem },
        3 => new[] { EnemyKind.SkyJelly, EnemyKind.StarWatcher },
        _ => new EnemyKind[0],
    };

    EnemyKind[] WithContinentEnemies(EnemyKind[] native)
    {
        var list = new System.Collections.Generic.List<EnemyKind>(native);
        list.AddRange(ContinentCreatures(Cont));
        if (FruitUserCap > 0) list.Add(EnemyKind.FruitUser);
        return list.ToArray();
    }

    /// How many fruit users can be about at once on this island.
    int FruitUserCap => atHome || WorldStep < 8 ? 0 : Mathf.Min(4, 1 + WorldStep / 20);

    // ------------------------------------------------------------------ fruit users
    /// Their power, from their id (the same on every machine).
    public static Power UserPower(Enemy e) => (Power)(1 + Mathf.Abs(e.netId * 7 + 3) % 5);

    /// Awakened? None before about island 15 of the world, all of them by about island 65.
    public static bool UserAwakened(Enemy e)
    {
        float chance = Mathf.Clamp01((I.WorldStep - 15) / 50f);
        return Mathf.Abs(e.netId * 37 + 11) % 100 < chance * 100f;
    }

    public static string FruitUserName(Enemy e) => $"นักล่าผลไม้{PowerName(UserPower(e))}" + (UserAwakened(e) ? " (ปลุกพลัง)" : "");

    /// Right after Init: an awakened one is far tougher.
    public void SetupFruitUser(Enemy e)
    {
        var col = PowerColor(UserPower(e));
        if (UserAwakened(e)) e.Empower(2.6f, 1.6f, 3f, Color.Lerp(col, Color.white, 0.55f));
        else e.Empower(1f, 1f, 1f, Color.Lerp(col, Color.white, 0.75f));
    }

    /// One of their abilities, aimed at me (the host's game runs them; only the player it's after gets hit).
    /// Returns true when it took the creature's whole turn.
    public bool CreatureAbility(Enemy e, float dt, Vector2 target, int who, float dist)
    {
        e.abilityT -= dt;
        bool me = who == 0 && player != null && !dead;
        switch (e.kind)
        {
            case EnemyKind.MirrorWraith:
                if (e.abilityT <= 0f && dist < 7f && dist > 1.5f && me)
                {
                    e.abilityT = 4.5f;
                    Vector2 behind = player.grid - (player.facing.sqrMagnitude > 0.01f ? player.facing.normalized : Vector2.right) * 1.1f;
                    if (CanWalk(behind, 0.2f))
                    {
                        Fx.Burst(e.transform.position + Vector3.up * 0.4f, new Color(0.8f, 0.95f, 1f), 12, 1f, 1.2f);
                        e.grid = behind;
                        Popup("ข้างหลัง!", PlayerPos + Vector3.up * 1.2f, new Color(0.8f, 0.95f, 1f));
                    }
                }
                break;
            case EnemyKind.LavaSalamander:
                if (e.abilityT <= 0f && dist < 6f && dist > 1.5f && me) { e.abilityT = 3f; SpawnStrike(target, 1f, 0.8f, e.Damage, "ลูกไฟลาวา!", new Color(1f, 0.45f, 0.1f)); }
                break;
            case EnemyKind.MagmaGolem:
                if (e.abilityT <= 0f && dist < 3.5f && me)
                {
                    e.abilityT = 5f;
                    for (int i = 0; i < 6; i++) SpawnStrike(e.grid + new Vector2(Mathf.Cos(i * 1.047f), Mathf.Sin(i * 1.047f)) * 1.5f, 1.1f, 0.8f, e.Damage, "แผ่นดินแยก!", new Color(1f, 0.5f, 0.2f));
                }
                break;
            case EnemyKind.SkyJelly:
                if (e.abilityT <= 0f && dist < 4f && me)
                {
                    e.abilityT = 4.5f;
                    for (int i = 0; i < 8; i++) SpawnStrike(e.grid + new Vector2(Mathf.Cos(i * 0.785f), Mathf.Sin(i * 0.785f)) * 1.8f, 1.2f, 0.7f, e.Damage * 0.8f, "ฟ้าผ่า!", new Color(0.8f, 0.9f, 1f));
                }
                break;
            case EnemyKind.StarWatcher:
                if (e.abilityT <= 0f && dist < 8f && me)
                {
                    e.abilityT = 4f;
                    Vector2 d = (target - e.grid).normalized;
                    for (int i = 1; i <= 6; i++) SpawnStrike(e.grid + d * (1.2f * i), 0.8f + i * 0.08f, 0.6f, e.Damage, "ลำแสงดวงตา!", new Color(1f, 0.85f, 0.3f));
                }
                break;
            case EnemyKind.FruitUser:
                return FruitUserAbility(e, target, dist, me);
        }
        return false;
    }

    bool FruitUserAbility(Enemy e, Vector2 target, float dist, bool me)
    {
        if (e.abilityT > 0f || dist > 7f) return false;
        bool awake = UserAwakened(e);
        var p = UserPower(e);
        var col = PowerColor(p);
        e.abilityT = awake ? 3f : 5.5f;
        switch (p)
        {
            case Power.Dash: // a flash across the ground into you
                if (dist > 1.5f)
                {
                    Vector2 to = target - (target - e.grid).normalized * 0.7f;
                    if (CanWalk(to, 0.2f)) { Fx.Burst(e.transform.position + Vector3.up * 0.3f, col, 10, 1f, 1.5f); e.grid = to; }
                    if (me) DamagePlayer(e.Damage * (awake ? 1.3f : 1f), "พุ่งฟ้าแลบ!");
                }
                break;
            case Power.Shadow: // melts into the dark for a while
                e.Fade(awake ? 4f : 2.5f);
                break;
            case Power.Vine: // vines round your legs
                if (me) { Slow(awake ? 2.5f : 1.5f); SpawnStrike(target, 0.9f, 0.7f, e.Damage * 0.8f, "เถาวัลย์รัด!", col); }
                break;
            case Power.Magnet: // pulls you in
                if (me) { bossPullTo = e.grid; bossPullT = awake ? 1.4f : 0.9f; Popup("ถูกดูด!", PlayerPos + Vector3.up * 1.2f, col); }
                break;
            case Power.Bubble: // a bubble that soaks up blows
                e.Shield(awake ? 4f : 2.5f);
                break;
        }
        Popup(PowerName(p) + (awake ? "!!" : "!"), e.transform.position + Vector3.up * 1.2f, col);
        return false;
    }

    /// Every frame on land: awakened fruit users glow (everyone online sees it), and the pull of a
    /// magnet power (shared with the continent boss's).
    void TickMonsters(float dt)
    {
        if (player == null) return;
        if (!OnBossIsland && bossPullT > 0f) // the boss island does this itself (IslandGame.ContinentBoss.cs)
        {
            bossPullT -= dt;
            Vector2 to = bossPullTo - player.grid;
            if (to.magnitude > 0.6f) { var next = player.grid + to.normalized * 2.4f * dt; if (CanWalk(next, 0.18f)) player.grid = next; }
        }
        userAuraT -= dt;
        if (userAuraT > 0f) return;
        userAuraT = 1f / 15f;
        foreach (var e in enemies)
            if (e != null && e.kind == EnemyKind.FruitUser && UserAwakened(e) && (e.grid - player.grid).sqrMagnitude < 300f)
                AwakenAuraParticles(e.transform.position, UserPower(e));
    }
    float userAuraT;

    /// A fruit user beaten: the government hears of it, and they sometimes drop a special fruit.
    void FruitUserDown(Vector3 at)
    {
        AddBounty(8000, at + Vector3.up * 1.2f, null);
        if (rnd.NextDouble() < 0.04) { Give(FruitFor(biome), 1, null); Popup("ได้ผลไม้พิเศษ!", at + Vector3.up * 1.5f, new Color(1f, 0.85f, 0.3f)); }
    }
}
