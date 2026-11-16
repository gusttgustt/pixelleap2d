using UnityEngine;

/// The Bubble island: an island under the sea inside a great dome of air. The dome leaks, so your
/// air runs down; breathe at the bubbling air vents. Lantern fish lure you off your path, stone
/// squids pretend to be rocks and squirt ink in your eyes. Fix the dome with the diver and learn
/// to build a diving bell (never short of air down here, and better catches at sea).
public partial class IslandGame
{
    public float air = 100f;
    float inkT;
    bool airWarned;

    bool UnderSea => biome is Biome.Bubble or Biome.Sunken && voyage == null;
    /// Seconds for a full tank to run out (much slower once the dome is fixed or with a diving bell).
    float AirSeconds => Count(Item.DivingBell) > 0 ? 9999f : biome == Biome.Sunken ? 110f : Flag("dome" + island) ? 600f : 160f; // no dome in the Sunken City

    public void Ink(float seconds)
    {
        if (inkT <= 0f) Popup("โดนหมึก! มองไม่เห็น", PlayerPos + Vector3.up * 1f, new Color(0.5f, 0.5f, 0.7f));
        inkT = Mathf.Max(inkT, seconds);
    }

    /// Air vents placed on a fresh Bubble island, spread around so one is never too far.
    void PlaceAirVents()
    {
        if (biome != Biome.Bubble) return;
        var r = new System.Random(seed * 13 + 9);
        PlaceRing(NodeKind.AirVent, 2, 2.5f, 4.5f, r);
        PlaceRing(NodeKind.AirVent, 4, 6f, 12f, r);
    }

    void UpdateBubble(float dt)
    {
        if (inkT > 0f) inkT -= dt;
        if (!UnderSea || player == null) { air = 100f; airWarned = false; return; }
        if (dead || Busy) return;

        // breathing at a vent fills you right up
        bool atVent = nodes.Exists(n => n != null && n.kind is NodeKind.AirVent or NodeKind.GiantClam && (n.grid - player.grid).magnitude < 1.3f);
        if (atVent)
        {
            if (air < 99f && Random.value < dt * 6f) Fx.Burst(PlayerPos + Vector3.up * 0.6f, new Color(0.8f, 0.95f, 1f), 1, 0.2f, 0.8f, 0.8f, -1.5f);
            air = Mathf.Min(100f, air + dt * 40f);
            airWarned = false;
        }
        else air = Mathf.Max(0f, air - dt * 100f / AirSeconds);

        if (air < 25f && !airWarned) { airWarned = true; Hint(biome == Biome.Sunken ? "อากาศใกล้หมด! รีบไปหาหอยยักษ์ที่พ่นฟองอากาศ" : "อากาศใกล้หมด! รีบไปที่ช่องอากาศ (ฟองผุดจากพื้น)", 5f); }
        if (air <= 0f)
        {
            health -= dt * 4f;
            CheckDeath();
        }

        // lantern fish lure: their light tugs you towards them
        foreach (var e in enemies)
        {
            if (e == null || e.kind != EnemyKind.LanternFish) continue;
            Vector2 to = e.grid - player.grid;
            float d = to.magnitude;
            if (d > 3.5f || d < 0.6f) continue;
            Vector2 next = player.grid + to / d * 0.55f * dt;
            if (CanWalk(next, 0.18f)) player.grid = next;
        }
    }

    float DrawBubbleHud(float y)
    {
        if (!UnderSea) return y;
        Color col = air < 25f ? new Color(1f, 0.5f, 0.4f) : new Color(0.6f, 0.9f, 1f);
        Bar(new Rect(14, y + 2, 180, 12), air / 100f, col, Count(Item.DivingBell) > 0 ? "อากาศ (ระฆังดำน้ำ)" : $"อากาศ {Mathf.CeilToInt(air)}");
        return y + 18f;
    }

    /// Ink in your eyes: the screen goes dark for a moment.
    void DrawInk(float W, float H)
    {
        if (inkT <= 0f) return;
        float a = Mathf.Clamp01(inkT / 0.6f) * 0.85f;
        Fill(new Rect(0, 0, W, H), new Color(0.05f, 0.04f, 0.1f, a));
    }

    // ------------------------------------------------------------------ the diver
    Quest DiverQuest() => new Quest
    {
        id = $"dome{island}", title = "ซ่อมโดมอากาศ",
        bring = new[] { (Item.Pearl, 4), (Item.Coral, 4) },
        kill = new[] { (EnemyKind.StoneSquid, 3 + tier) },
        rewardText = "โดมรั่วน้อยลง + เรียนทำระฆังดำน้ำ",
        reward = () =>
        {
            flags.Add("dome" + island);
            learned.Add(Item.DivingBell);
            AddXp(60, PlayerPos);
            TechPopup(Item.DivingBell);
        },
    };

    const string DiverIntro = "โดมอากาศของเรารั่ว ทุกคนต้องวิ่งไปหายใจที่ช่องอากาศทั้งวัน หมึกหินชอบไปแทะผนังโดมแล้วพ่นหมึกใส่ใครก็ตามที่เข้าใกล้ ช่วยกำจัดพวกมัน แล้วเอามุกกับปะการังมาอุดรอยรั่วให้ที ข้าจะสอนทำระฆังดำน้ำให้ เจ้าจะไม่มีวันขาดอากาศอีก";
}
