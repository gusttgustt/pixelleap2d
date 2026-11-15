using System.Collections.Generic;
using UnityEngine;

/// The Giant Turtle island: the whole island is the shell of a turtle swimming in the sea.
/// Every so often the turtle dives a little: the ground shakes, then the low ground floods for
/// a while (it hurts and slows you - get up on the hills). Calm the turtle (the keeper's quest)
/// and it dives less often.
public partial class IslandGame
{
    const float DiveWarn = 15f, DiveLength = 22f;
    readonly List<SpriteRenderer> floodTiles = new List<SpriteRenderer>();
    SpriteRenderer turtleHead;
    Vector3 turtleHeadPos;
    float floodLevel, slowT;
    bool floodWarned;

    float DivePeriod => Flag("turtlecalm" + island) ? 300f : 170f;
    /// Seconds into the current dive cycle (game time, so sleeping skips dives too).
    float DiveClock => Mathf.Repeat(Now, DivePeriod); // first dive about two minutes after landing
    float DiveStart => DivePeriod - DiveLength;
    bool DiveWarning => biome == Biome.Turtle && DiveClock >= DiveStart - DiveWarn && DiveClock < DiveStart;
    bool Diving => biome == Biome.Turtle && DiveClock >= DiveStart;

    /// Movement multiplier from barnacles stuck on you and wading through flood water.
    float SlowFactor => (slowT > 0f ? 0.55f : 1f) * (OnFloodedGround ? 0.6f : 1f);
    public bool OnFloodedGround => floodLevel > 0.5f && player != null && IsLowGround(player.grid);
    bool IsLowGround(Vector2 g)
    {
        int x = Mathf.RoundToInt(g.x), y = Mathf.RoundToInt(g.y);
        return x >= 0 && y >= 0 && x < Size && y < Size && tiles[x, y] is 1 or 2 && heights[x, y] == 0;
    }

    public void Slow(float seconds) { if (slowT <= 0f) Popup("ติดเพรียง! เดินช้า", PlayerPos + Vector3.up * 1f, new Color(0.8f, 0.8f, 0.7f)); slowT = Mathf.Max(slowT, seconds); }

    // ------------------------------------------------------------------ building
    /// Water that rises over every low tile while the turtle dives, and the turtle's head.
    void BuildTurtle()
    {
        floodTiles.Clear();
        turtleHead = null;
        floodLevel = 0f;
        if (biome != Biome.Turtle) return;
        var root = new GameObject("Flood").transform;
        root.SetParent(world);
        for (int gx = 0; gx < Size; gx++)
            for (int gy = 0; gy < Size; gy++)
            {
                if (!(tiles[gx, gy] is 1 or 2) || heights[gx, gy] != 0) continue;
                var sr = new GameObject("FloodTile").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(root);
                sr.sprite = PixelArt.Water((gx + gy) & 1, false);
                sr.transform.position = GridToWorld(new Vector2(gx, gy));
                sr.sortingOrder = -31000 + 2 * (gx + gy) + 1; // just above its own land tile
                sr.color = new Color(1f, 1f, 1f, 0f);
                floodTiles.Add(sr);
            }

        // the head sticks out of the sea on the east side
        Vector2 best = Center;
        float bestScore = float.MinValue;
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
            {
                if (tiles[x, y] != 0) continue;
                var g = new Vector2(x, y);
                bool nearLand = false;
                for (int dx = -2; dx <= 2 && !nearLand; dx++)
                    for (int dy = -2; dy <= 2 && !nearLand; dy++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < Size && ny < Size && tiles[nx, ny] is 1 or 2) nearLand = true;
                    }
                if (!nearLand) continue;
                float score = (x - y) - Mathf.Abs(x + y - (Size - 1)) * 0.6f; // east, middle
                if (score > bestScore) { bestScore = score; best = g; }
            }
        turtleHead = new GameObject("TurtleHead").AddComponent<SpriteRenderer>();
        turtleHead.transform.SetParent(world);
        turtleHeadPos = GridToWorld(best + new Vector2(1.5f, -1.5f));
        turtleHead.transform.position = turtleHeadPos;
        turtleHead.sortingOrder = SortOrder(turtleHeadPos.y);
        turtleHead.sprite = PixelArt.TurtleHead(false);
    }

    /// The keeper's camp in the middle of a fresh Turtle island.
    void BuildCamp()
    {
        Vector2 c = Center;
        foreach (var n in nodes.FindAll(n => (n.grid - c).magnitude < 3.6f)) RemoveNode(n);
        Spawn(NodeKind.Campfire, c, false);
        Spawn(NodeKind.Hut, c + new Vector2(2.2f, -1.2f), false);
    }

    // ------------------------------------------------------------------ every frame
    void UpdateTurtle(float dt)
    {
        slowT -= dt;
        if (biome != Biome.Turtle || turtleHead == null || voyage != null) return;
        float want = Diving ? 1f : 0f;
        floodLevel = Mathf.MoveTowards(floodLevel, want, dt / 3f);
        float wave = Mathf.Sin(Time.time * 1.6f) * 0.03f;
        foreach (var sr in floodTiles)
        {
            if (sr == null) continue;
            sr.color = new Color(1f, 1f, 1f, 0.7f * floodLevel);
            sr.enabled = floodLevel > 0.01f;
        }
        // the head bobs, blinks, and dips under while diving
        turtleHead.transform.position = turtleHeadPos + Vector3.up * (wave * 3f - 0.45f * floodLevel);
        turtleHead.sprite = PixelArt.TurtleHead(Mathf.Repeat(Time.time, 4f) < 0.15f);
        turtleHead.color = new Color(1f, 1f, 1f, 1f - 0.6f * floodLevel);

        if (DiveWarning)
        {
            if (!floodWarned) { floodWarned = true; Hint("พื้นสั่น! ท่านเต่ากำลังจะดำน้ำ ขึ้นไปบนเนินสูงเร็ว!", 6f); Sfx.Play("hurt", 0.3f); }
            cam.transform.position += (Vector3)(Random.insideUnitCircle * 0.03f); // rumbling
        }
        else if (!Diving) floodWarned = false;

        if (OnFloodedGround && !dead && !Busy)
        {
            health -= dt * 1.5f; // mostly a nuisance: it slows you too
            if (Random.value < dt * 4f) Fx.Burst(PlayerPos, PixelArt.WaterColor, 2, 0.5f, 0.8f);
            CheckDeath();
        }
    }

    /// One HUD line about the turtle's mood.
    float DrawTurtleHud(float y)
    {
        if (biome != Biome.Turtle || voyage != null) return y;
        string text; Color col;
        if (Diving) { text = OnFloodedGround ? "น้ำท่วม! ขึ้นที่สูงเร็ว" : "ท่านเต่าดำน้ำอยู่ - ที่ต่ำน้ำท่วม"; col = new Color(0.5f, 0.8f, 1f, 0.6f + 0.4f * Mathf.Sin(Time.time * 6f)); }
        else if (DiveWarning) { text = $"ท่านเต่าจะดำน้ำใน {DiveStart - DiveClock:0} วิ! ขึ้นเนินสูง"; col = new Color(1f, 0.8f, 0.3f); }
        else { text = $"ท่านเต่าว่ายน้ำสบาย (ดำน้ำอีกใน {DiveStart - DiveWarn - DiveClock:0} วิ)"; col = new Color(0.7f, 0.95f, 0.7f, 0.8f); }
        ShadowLabel(new Rect(14, y, 360, 18), text, small, col);
        return y + 16f;
    }

    // ------------------------------------------------------------------ the keeper
    Quest KeeperQuest() => new Quest
    {
        id = $"turtle{island}", title = "ขัดเพรียงให้ท่านเต่า",
        bring = new[] { (Item.Fiber, 6) },
        kill = new[] { (EnemyKind.Barnacle, 6 + tier) },
        rewardText = "ท่านเต่าใจเย็นลง + เรียนทำใบเรือผ้าใบ",
        reward = () =>
        {
            flags.Add("turtlecalm" + island);
            learned.Add(Item.CanvasSail);
            AddXp(40, PlayerPos);
            TechPopup(Item.CanvasSail);
        },
    };

    const string KeeperIntro = "ข้าเฝ้าท่านเต่ามาทั้งชีวิต เพรียงเกาะกระดองจนท่านคันยุบยิบ เลยดำน้ำบ่อยจนหมู่บ้านน้ำท่วม ช่วยกำจัดเพรียง แล้วเอาใยพืชมาทำแปรงขัดกระดองที ข้าจะสอนทำใบเรือผ้าใบให้ เรือจะแล่นเร็วขึ้นมาก";
}
