using UnityEngine;

/// The continent bosses. On the last island of every continent (island IslandsPerContinent) lives
/// a boss with a special fruit power no one else has, fully awakened, strong enough that a crew
/// of five is what it's made for:
///   continent 1  เจ้าสมุทรคลื่นคลั่ง  - ผลวังวนมหาสมุทร: tidal walls, a whirlpool that drags you in, a rain of water spears
///   continent 2  ราชินีกระจกเหมันต์  - ผลกระจกเหมันต์: an ice prison, mirror bats, a blizzard
///   continent 3  ไฮดราเพลิงกัลป์    - ผลไฮดราสามเศียร: a meteor shower, three-headed fire breath, rings of erupting lava
///   continent 4  ไททันหลุมดำ        - ผลหลุมดำกลืนดาว: a black hole that pulls you in, stepping through space, falling stars
///
/// Fighting it with fewer people makes it stronger: alone it gets +100% to everything (health,
/// damage), and every crew member with you takes 12.5% of that away, down to +50% with a full
/// crew of five. Beat it: the materials for the continent's gear (a weapon, armour and an
/// accessory, much stronger than anything before), XP and gold, and the first time also the next
/// continent opens, three skill points and a big bounty. It's back whenever you land again, so
/// you can come for more materials.
///
/// Its special moves are worked out on every player's own game, aimed at that player (red
/// marks: get out of them), so online everyone in the fight gets them without anything extra
/// sent; the host runs the boss itself as any other creature.
public partial class IslandGame
{
    public const int ContinentBossId = 990002;
    float bossAtkT = 4f, bossPullT;
    Vector2 bossPullTo;
    int bossMove;
    bool bossSeen, bossDownDone, bossIntro;

    static EnemyKind BossKindFor(int c) => c switch { 1 => EnemyKind.FrostQueen, 2 => EnemyKind.MagmaHydra, 3 => EnemyKind.VoidTitan, _ => EnemyKind.TideLord };
    static string BossPowerName(EnemyKind k) => k switch
    {
        EnemyKind.FrostQueen => "ผลกระจกเหมันต์", EnemyKind.MagmaHydra => "ผลไฮดราสามเศียร", EnemyKind.VoidTitan => "ผลหลุมดำกลืนดาว", _ => "ผลวังวนมหาสมุทร",
    };
    static Color BossColor(EnemyKind k) => k switch
    {
        EnemyKind.FrostQueen => new Color(0.6f, 0.85f, 1f), EnemyKind.MagmaHydra => new Color(1f, 0.5f, 0.2f), EnemyKind.VoidTitan => new Color(0.7f, 0.45f, 1f), _ => new Color(0.3f, 0.75f, 1f),
    };
    static (Item common, Item rare) BossMaterials(int c) => c switch
    {
        1 => (Item.MirrorIce, Item.QueenTear), 2 => (Item.LavaCore, Item.HydraFang), 3 => (Item.VoidShard, Item.StarEye), _ => (Item.TideScale, Item.StormPearl),
    };

    Enemy ContinentBossHere => enemies.Find(e => e != null && e.netId == ContinentBossId);

    /// How many of my crew are here with me (counting me): what weakens the boss.
    int PartyHere()
    {
        int n = 1;
        var crew = MyCrewIds();
        foreach (var r in remotes.Values) if (r != null && crew.Contains(r.netId)) n++;
        return n;
    }

    /// Everything times this: 2.0 alone, 0.125 less per crew member, never under 1.5.
    float BossBuff => 1f + Mathf.Max(0.5f, 1f - 0.125f * (PartyHere() - 1));

    /// StartGame on the boss island (the host's game; the others see the host's).
    void SpawnContinentBoss()
    {
        bossSeen = false; bossDownDone = false; bossIntro = false; bossAtkT = 5f; bossMove = 0;
        if (!OnBossIsland || !IsHost) return;
        Vector2 at = FreeStand(FarFrom(ShipwrightSpot(), Center));
        var kind = BossKindFor(Cont);
        var e = new GameObject("ContinentBoss").AddComponent<Enemy>();
        e.transform.SetParent(world);
        e.Init(kind, at, tier, ContinentBossId);
        e.stayByDay = true;
        enemies.Add(e);
    }

    /// Every frame on land.
    void TickContinentBoss(float dt)
    {
        if (!OnBossIsland || player == null) return;
        var boss = ContinentBossHere;
        if (boss != null)
        {
            bossSeen = true;
            if (!boss.puppet) boss.SetBossMul(BossBuff); // the host's game: the boss's real strength
            if (!bossIntro)
            {
                bossIntro = true;
                QuestBanner($"เกาะบอสทวีป: {boss.DisplayName}", $"พลัง{BossPowerName(boss.kind)} ปลุกพลังขั้นสูงสุด · ไปเป็นทีมลูกเรือ 5 คน บอสจะอ่อนลง", BossColor(boss.kind));
            }
        }
        else if (bossSeen && !bossDownDone && homeT <= 0f) { ContinentBossDown(); return; }
        if (boss == null || dead) { bossPullT = 0f; return; }

        // dragged by a whirlpool / black hole
        if (bossPullT > 0f)
        {
            bossPullT -= dt;
            Vector2 to = bossPullTo - player.grid;
            if (to.magnitude > 0.4f)
            {
                var next = player.grid + to.normalized * 2.6f * dt;
                if (CanWalk(next, 0.18f)) player.grid = next;
            }
        }

        float dist = (boss.grid - player.grid).magnitude;
        if (dist > 13f || Busy) return;
        bool enraged = boss.hp < boss.maxHp / 2;
        bossAtkT -= dt;
        if (bossAtkT > 0f) return;
        bossAtkT = enraged ? 3.8f : 5.5f;
        float dmg = (boss.puppet ? boss.Damage * BossBuff : boss.Damage) * 0.8f;
        BossMove(boss, bossMove++ % 3, dmg, enraged);
    }

    /// One of the boss's three moves, aimed at me.
    void BossMove(Enemy boss, int move, float dmg, bool enraged)
    {
        Vector2 me = player.grid, b = boss.grid;
        Vector2 dir = (me - b).sqrMagnitude > 0.01f ? (me - b).normalized : Vector2.right, side = new Vector2(-dir.y, dir.x);
        var col = BossColor(boss.kind);
        int more = enraged ? 1 : 0;
        string name;
        switch (boss.kind)
        {
            case EnemyKind.FrostQueen:
                if (move == 0) { name = "คุกน้ำแข็ง!"; Slow(2f); SpawnStrike(me, 1.4f, 1.3f + 0.3f * more, dmg * 1.3f, name, col); }
                else if (move == 1)
                {
                    name = "เงาสะท้อนในกระจก!";
                    if (IsHost) for (int i = 0; i < 2 + more; i++) BossMinion(EnemyKind.FrostBat, b + Random.insideUnitCircle * 2f);
                    for (int i = 0; i < 4; i++) SpawnStrike(me + Random.insideUnitCircle * 2f, 1.2f + i * 0.2f, 0.8f, dmg * 0.7f, name, col);
                }
                else { name = "พายุหิมะ!"; Chill(25f); Slow(1.5f); for (int i = 0; i < 9 + 3 * more; i++) SpawnStrike(me + Random.insideUnitCircle * 4f, 0.9f + i * 0.15f, 0.8f, dmg * 0.6f, name, col); }
                break;
            case EnemyKind.MagmaHydra:
                if (move == 0) { name = "ฝนอุกกาบาต!"; for (int i = 0; i < 10 + 4 * more; i++) SpawnStrike(me + Random.insideUnitCircle * 4f, 1f + i * 0.13f, 1f, dmg * 0.8f, name, col); }
                else if (move == 1)
                {
                    name = "ลมหายใจสามเศียร!";
                    for (int h = -1; h <= 1; h++)
                    {
                        Vector2 d = Quaternion.Euler(0f, 0f, h * 25f) * dir;
                        for (int i = 1; i <= 5; i++) SpawnStrike(b + d * (1.4f * i), 0.9f + i * 0.12f, 0.8f, dmg, name, col);
                    }
                }
                else
                {
                    name = "ลาวาปะทุ!";
                    for (int ring = 0; ring < 3 + more; ring++)
                    {
                        float r = 2f + ring * 1.5f; int n = 8 + ring * 4;
                        for (int i = 0; i < n; i++) SpawnStrike(b + new Vector2(Mathf.Cos(i * Mathf.PI * 2f / n), Mathf.Sin(i * Mathf.PI * 2f / n)) * r, 1f + ring * 0.5f, 0.8f, dmg * 0.9f, name, col);
                    }
                }
                break;
            case EnemyKind.VoidTitan:
                if (move == 0)
                {
                    name = "หลุมดำ!";
                    Vector2 well = me + Random.insideUnitCircle * 1.5f;
                    bossPullTo = well; bossPullT = 1.8f;
                    SpawnStrike(well, 2f, 1.8f + 0.3f * more, dmg * 1.6f, name, col);
                }
                else if (move == 1)
                {
                    name = "ก้าวทะลุมิติ!";
                    if (!boss.puppet) { var to = FreeStand(me - dir * 1.6f); Fx.Burst(boss.transform.position + Vector3.up, col, 20, 1.5f, 1.5f); boss.grid = to; }
                    SpawnStrike(me, 0.9f, 1.5f, dmg * 1.2f, name, col);
                }
                else
                {
                    name = "ดาวตก!";
                    Ink(2f);
                    for (int i = 0; i < 6 + 2 * more; i++) SpawnStrike(me + dir * (0.4f * i) + Random.insideUnitCircle * 0.8f, 0.8f + i * 0.3f, 0.9f, dmg * 0.8f, name, col);
                }
                break;
            default: // the Tide Lord
                if (move == 0)
                {
                    name = "กำแพงคลื่นยักษ์!";
                    for (int k = -4 - more; k <= 4 + more; k++) SpawnStrike(me + side * (0.9f * k), 1.2f + Mathf.Abs(k) * 0.05f, 0.8f, dmg, name, col);
                }
                else if (move == 1)
                {
                    name = "วังวนดูดกลืน!";
                    bossPullTo = b; bossPullT = 1.6f;
                    for (int i = 0; i < 10; i++) SpawnStrike(b + new Vector2(Mathf.Cos(i * 0.628f), Mathf.Sin(i * 0.628f)) * 1.8f, 1.7f, 0.8f, dmg * 0.9f, name, col);
                }
                else { name = "ฝนหอกน้ำ!"; Slow(1f); for (int i = 0; i < 8 + 3 * more; i++) SpawnStrike(me + Random.insideUnitCircle * 3f, 0.8f + i * 0.17f, 0.7f, dmg * 0.7f, name, col); }
                break;
        }
        Popup(name, boss.transform.position + Vector3.up * 2.6f, col);
        Sfx.Play("hit", 0.5f);
    }

    /// A creature the boss calls up (the host's game: the others see it as usual).
    void BossMinion(EnemyKind kind, Vector2 at)
    {
        if (!CanWalk(at, 0.2f)) at = FreeStand(at);
        var e = new GameObject(kind.ToString()).AddComponent<Enemy>();
        e.transform.SetParent(world);
        e.Init(kind, at, tier, ++enemyCounter);
        e.stayByDay = true;
        enemies.Add(e);
        Fx.Burst(e.transform.position + Vector3.up * 0.3f, new Color(0.8f, 0.95f, 1f), 10, 1f, 1.2f);
    }

    /// It fell: everyone who was in the fight gets the spoils.
    void ContinentBossDown()
    {
        bossDownDone = true;
        int c = Cont;
        var (common, rare) = BossMaterials(c);
        bool first = !ContinentCleared(c);
        Vector3 at = PlayerPos;
        Give(common, Random.Range(4, 8), null);
        if (first || Random.value < 0.6f) Give(rare, first ? 2 : Random.Range(1, 3), null);
        Give(Item.GoldCoin, 150 * (c + 1), null);
        AddXp(800 * (c + 1), at);
        if (first)
        {
            flags.Add("cboss" + c);
            skillPoints += 3;
            AddBounty(3000000 * (c + 1), at + Vector3.up * 1.6f, null);
            QuestBanner("ปราบบอสทวีปแล้ว!", c + 1 < Continents
                ? $"{ContinentNames[c + 1]}เปิดแล้ว! ล่องเรือไปได้ ย้ายบ้านไปได้ · แต้มสกิล +3 · วัตถุดิบทำอาวุธ ชุด และเครื่องประดับ"
                : "ผ่านครบทุกทวีปแล้ว! คุณคือตำนานแห่งท้องทะเล · แต้มสกิล +3", new Color(1f, 0.82f, 0.3f));
        }
        else QuestBanner("ปราบบอสทวีปอีกครั้ง!", $"ได้{Name(common)}และของรางวัล · ใช้ทำอุปกรณ์ทวีปนี้ (Tab)", new Color(1f, 0.82f, 0.3f));
        Sfx.Play("win", 0.8f);
        Fx.Burst(at + Vector3.up * 0.5f, new Color(1f, 0.85f, 0.3f), 40, 2.2f, 2.5f, 1f, 3f);
        SaveGame();
    }

    /// The boss's health along the top, with what it is and how strong the small group made it.
    void DrawContinentBossHud(float W)
    {
        if (!OnBossIsland || player == null) return;
        var boss = ContinentBossHere;
        if (boss == null || (boss.grid - player.grid).magnitude > 18f) return;
        const float clearOf = 308f;
        float bw = 300f, x = Mathf.Max(W / 2f - bw / 2f, clearOf);
        var col = BossColor(boss.kind);
        var st = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 12, richText = true };
        ShadowLabel(new Rect(x, 38f, bw, 18f), $"<b>{boss.DisplayName}</b> · พลัง{BossPowerName(boss.kind)} (ปลุกพลังขั้นสูงสุด)", st, col);
        var bar = new Rect(x, 58f, bw, 11f);
        Fill(new Rect(bar.x - 2, bar.y - 2, bar.width + 4, bar.height + 4), new Color(0f, 0f, 0f, 0.65f));
        Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01((float)boss.hp / Mathf.Max(1, boss.maxHp)), bar.height), col);
        int party = PartyHere();
        float buff = BossBuff - 1f;
        ShadowLabel(new Rect(x, 70f, bw, 18f), $"{boss.hp}/{boss.maxHp} · ทีม {party}/5 คน · บอสแรงขึ้น +{buff * 100f:0}%", st, buff > 0.75f ? new Color(1f, 0.6f, 0.5f) : new Color(1f, 0.9f, 0.6f));
    }
}
