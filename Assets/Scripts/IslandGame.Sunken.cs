using System.Collections.Generic;
using UnityEngine;

/// The Sunken City: an ancient city on the sea floor. No dome here: you walk the seabed in a
/// diving suit, so your air runs out fast (giant clams breathe out bubbles of air; a diving bell
/// means you never run short). Schools of fish swim overhead, slow currents sweep across the
/// ruins. Electric eels shock you stiff, deep-sea crabs shrug off blows, and the Kraken guards
/// the temple. Help the archaeologist read the temple's relics and learn to forge the trident.
public partial class IslandGame
{
    const float CurrentPeriod = 22f, CurrentLength = 4f;
    readonly List<(SpriteRenderer sr, float speed)> fishSchools = new List<(SpriteRenderer, float)>();
    Vector2 currentDir = Vector2.right;
    int currentIndex = -1;

    bool Sunken => biome == Biome.Sunken && voyage == null;
    bool CurrentFlowing => Sunken && Mathf.Repeat(Now, CurrentPeriod) >= CurrentPeriod - CurrentLength;

    void BuildSunkenCity()
    {
        if (biome != Biome.Sunken) return;
        Vector2 c = Center;
        foreach (var n in nodes.FindAll(n => (n.grid - c).magnitude < 3.2f)) RemoveNode(n);
        Spawn(NodeKind.SunkenTemple, c + new Vector2(-1.5f, -1.5f), false);
        var r = new System.Random(seed * 29 + 1);
        PlaceRing(NodeKind.Pillar, 10, 3.5f, 11f, r);   // the old city's columns
        PlaceRing(NodeKind.Relic, 4, 5f, 13f, r);       // carved tablets for the archaeologist
        PlaceRing(NodeKind.GiantClam, 5, 3f, 12f, r);   // air
    }

    void UpdateSunken(float dt)
    {
        if (!Sunken || player == null || cam == null)
        {
            foreach (var f in fishSchools) if (f.sr != null) Destroy(f.sr.gameObject);
            fishSchools.Clear();
            return;
        }
        fishSchools.RemoveAll(f => f.sr == null); // a new island destroyed the old ones
        // schools of fish gliding by overhead
        if (fishSchools.Count == 0)
            for (int i = 0; i < 4; i++)
            {
                var sr = new GameObject("FishSchool").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(world);
                sr.sprite = PixelArt.FishSchool(i % 2);
                sr.sortingOrder = 32000;
                sr.color = new Color(1f, 1f, 1f, 0.45f);
                sr.transform.position = cam.transform.position + new Vector3(Random.Range(-9f, 9f), Random.Range(-4f, 4f), 0f);
                float sp = Random.Range(0.5f, 1.2f) * (i % 2 == 0 ? 1f : -1f);
                sr.flipX = sp < 0f;
                fishSchools.Add((sr, sp));
            }
        Vector3 c = cam.transform.position;
        foreach (var (sr, speed) in fishSchools)
        {
            if (sr == null) continue;
            var p = sr.transform.position + Vector3.right * speed * dt;
            if (p.x > c.x + 11f) p.x = c.x - 11f; else if (p.x < c.x - 11f) p.x = c.x + 11f;
            if (p.y > c.y + 5f) p.y = c.y - 5f; else if (p.y < c.y - 5f) p.y = c.y + 5f;
            sr.transform.position = p;
        }

        // slow currents sweeping across the ruins
        int idx = Mathf.FloorToInt(Now / CurrentPeriod);
        if (idx != currentIndex)
        {
            currentIndex = idx;
            var rr = new System.Random(seed + idx * 53);
            float a = (float)rr.NextDouble() * Mathf.PI * 2f;
            currentDir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        if (CurrentFlowing && !dead && !Busy)
        {
            if (Random.value < dt * 20f)
            {
                Vector3 w = GridToWorld(currentDir) - GridToWorld(Vector2.zero);
                Fx.Burst(c + new Vector3(Random.Range(-6f, 6f), Random.Range(-3f, 3f), 0f), new Color(0.7f, 0.9f, 1f, 0.6f), 1, 0.1f, 0.2f, 1f, 0f);
                Fx.Burst(c + w * 0.5f + new Vector3(Random.Range(-6f, 6f), Random.Range(-3f, 3f), 0f), new Color(0.7f, 0.9f, 1f, 0.4f), 1, 0.1f, 0.2f, 1f, 0f);
            }
            Vector2 next = player.grid + currentDir * 1.1f * dt;
            if (CanWalk(next, 0.18f)) player.grid = next;
        }
    }

    float DrawSunkenHud(float y)
    {
        if (!Sunken) return y;
        if (CurrentFlowing) { ShadowLabel(new Rect(14, y, 360, 18), "กระแสน้ำกำลังพัด!", small, new Color(0.7f, 0.9f, 1f)); y += 16f; }
        return y;
    }

    void ReadRelic(WorldNode n)
    {
        Give(Item.AncientRelic, 1, n);
        Fx.Burst(n.transform.position + Vector3.up * 0.5f, new Color(0.5f, 1f, 0.95f), 14, 1f, 1.4f);
        Hint("แผ่นจารึกโบราณ! ดร.สมุทรคงอยากเห็นมัน", 3f);
        n.Deplete(1e9f);
    }

    /// The Kraken waits in its temple until someone beats it.
    void SpawnKraken()
    {
        if (biome != Biome.Sunken || Flag("kraken" + island) || !IsHost) return;
        if (enemies.Exists(e => e != null && e.kind == EnemyKind.Kraken)) return;
        Vector2 at = WalkableNear(Center + new Vector2(1.6f, 1.6f));
        var e = new GameObject("Kraken").AddComponent<Enemy>();
        e.transform.SetParent(world);
        e.Init(EnemyKind.Kraken, at, tier, ++enemyCounter);
        enemies.Add(e);
    }

    // ------------------------------------------------------------------ the archaeologist
    Quest ArchaeologistQuest() => new Quest
    {
        id = $"temple{island}", title = "ไขปริศนาวิหารบาดาล",
        bring = new[] { (Item.AncientRelic, 4) },
        help = new[] { ($"kraken{island}", "ปราบอสูรหมึกยักษ์ที่เฝ้าวิหาร") },
        rewardText = "เรียนตีตรีศูลบาดาล (อาวุธ ดาเมจ 9)",
        reward = () =>
        {
            learned.Add(Item.Trident);
            AddXp(120, PlayerPos);
            TechPopup(Item.Trident);
        },
    };

    const string ArchaeologistIntro = "ยินดีต้อนรับสู่นครบาดาล! ข้าคือ ดร.สมุทร นักโบราณคดี เมืองนี้จมทะเลมาพันปี แผ่นจารึกที่กระจายอยู่ตามซากเมืองบอกวิธีตีอาวุธของกษัตริย์บาดาล ช่วยเก็บมาให้ข้า 4 แผ่น แต่ระวัง อสูรหมึกยักษ์เฝ้าวิหารอยู่ ถ้าเจ้าปราบมันได้ ข้าจะสอนวิธีตีตรีศูลให้";
}
