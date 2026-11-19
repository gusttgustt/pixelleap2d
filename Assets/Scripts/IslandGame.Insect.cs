using UnityEngine;

/// The Giant Insect island: everything is huge and you are tiny. Blades of grass taller than you
/// make a maze (cut through with any blade), giant flowers drip nectar, a beehive drips honey
/// (take it and the bees come after you). Worker ants march about minding their own business -
/// hit one and the whole colony turns on you. The Ant Queen sits in her anthill sending out
/// soldiers. Help the butterfly keeper and she gives you butterfly wings (you move faster).
public partial class IslandGame
{
    void BuildInsectIsland()
    {
        if (biome != Biome.Insect) return;
        var r = new System.Random(seed * 43 + 8);
        PlaceRing(NodeKind.Anthill, 1, 8f, 12f, r);
        PlaceRing(NodeKind.Beehive, 2, 6f, 13f, r);
        PlaceRing(NodeKind.GiantFlower, 6, 4f, 14f, r);
        PlaceRing(NodeKind.GiantGrass, 40, 3.5f, 15f, r); // the grass maze
    }

    /// Hit one ant and every worker ant nearby turns on you.
    public void ProvokeAnts(Vector2 g)
    {
        foreach (var e in enemies)
            if (e != null && e.kind == EnemyKind.WorkerAnt && (e.grid - g).magnitude < 6f) e.Provoke();
    }

    void TakeHoney(WorldNode hive)
    {
        Give(Item.Honey, 2 + rnd.Next(2), hive);
        Fx.Burst(hive.transform.position + Vector3.up * 0.8f, new Color(1f, 0.8f, 0.2f), 10, 0.8f, 1f);
        DepleteShared(hive, 150f);
        if (!IsHost) return;
        for (int i = 0; i < 2; i++) // the bees are NOT happy
        {
            Vector2 g = WalkableNear(hive.grid + Random.insideUnitCircle * 1.5f);
            var e = new GameObject("GiantBee").AddComponent<Enemy>();
            e.transform.SetParent(world);
            e.Init(EnemyKind.GiantBee, g, tier, ++enemyCounter);
            enemies.Add(e);
        }
        Hint("ผึ้งยักษ์โกรธแล้ว! วิ่ง!", 3f);
    }

    void CutGiantGrass(WorldNode n)
    {
        bool blade = Count(Item.StoneKnife) > 0 || HasAxe || weapon != null;
        if (!blade) { Hint("ต้นหญ้ายักษ์เหนียวมาก ต้องมีมีด ขวาน หรืออาวุธถึงจะฟันได้", 3f); return; }
        Give(Item.Fiber, 3 + rnd.Next(3), n);
        Fx.Burst(n.transform.position + Vector3.up * 0.8f, new Color(0.4f, 0.8f, 0.3f), 10, 1f, 1f, 1f, 2f);
        Sfx.Play("chop", 0.5f);
        RemoveShared(n); // a path opens
    }

    void SpawnQueen(bool forOthers = false)
    {
        if (biome != Biome.Insect || !forOthers && Flag("queen" + island) || !IsHost) return;
        if (enemies.Exists(e => e != null && e.kind == EnemyKind.AntQueen)) return;
        var hill = nodes.Find(n => n != null && n.kind == NodeKind.Anthill);
        Vector2 at = WalkableNear(hill != null ? hill.grid + new Vector2(1.6f, 0.6f) : FarFrom(ShipwrightSpot(), Center));
        var e = new GameObject("AntQueen").AddComponent<Enemy>();
        e.transform.SetParent(world);
        e.Init(EnemyKind.AntQueen, at, tier, ++enemyCounter);
        enemies.Add(e);
    }

    /// The queen calls out her soldiers.
    public void QueenSpawnsSoldier(Vector2 at)
    {
        if (!IsHost) return;
        int soldiers = 0;
        foreach (var e in enemies) if (e != null && e.kind == EnemyKind.SoldierAnt) soldiers++;
        if (soldiers >= 4) return;
        var s = new GameObject("SoldierAnt").AddComponent<Enemy>();
        s.transform.SetParent(world);
        s.Init(EnemyKind.SoldierAnt, WalkableNear(at + Random.insideUnitCircle), tier, ++enemyCounter);
        enemies.Add(s);
    }

    // ------------------------------------------------------------------ the butterfly keeper
    Quest ButterflyQuest() => new Quest
    {
        id = $"butterfly{island}", title = "สวนผีเสื้อ",
        bring = new[] { (Item.Nectar, 5), (Item.Honey, 2) },
        help = new[] { ($"queen{island}", "ปราบราชินีมดที่ยึดสวนดอกไม้") },
        rewardText = "เรียนทำปีกผีเสื้อ (เดินเร็วขึ้น 15%)",
        reward = () =>
        {
            learned.Add(Item.ButterflyWings);
            AddXp(160, PlayerPos);
            TechPopup(Item.ButterflyWings);
        },
    };

    const string ButterflyIntro = "เจ้าตัวจิ๋ว! อย่าตกใจ ที่นี่ทุกอย่างตัวโตกว่าเจ้าหมด ข้าเลี้ยงผีเสื้อมาทั้งชีวิต แต่ราชินีมดส่งทหารมายึดสวนดอกไม้จนผีเสื้อไม่มีน้ำหวานกิน ช่วยเก็บน้ำหวานจากดอกไม้ยักษ์กับน้ำผึ้งมาให้ข้า แล้วปราบราชินีมดซะ ข้าจะทอปีกผีเสื้อให้ บินเร็วยิ่งกว่าลม";
}
