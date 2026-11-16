using UnityEngine;

/// The Hourglass island: time runs wild here. Day and night race past four times as fast and
/// plants grow back in moments. Wind-up dolls only move while you're not looking at them; time
/// beetles blink from place to place. Help the clockmaker wind the great clock tower: time
/// slows down to "only" twice as fast, and he teaches you to make a sundial that stops time
/// for every creature around you for a few seconds.
public partial class IslandGame
{
    float sundialCd;

    /// How fast the day goes by on this island.
    public float TimeScale => biome == Biome.Hourglass && voyage == null ? (Flag("clock" + island) ? 2f : 4f) : 1f;
    /// How much faster plants regrow on this island.
    public float GrowScale => biome == Biome.Hourglass && voyage == null ? 3f : 1f;

    /// Sundial (from the hotbar / bag): every creature nearby freezes for 5 seconds.
    void UseSundial()
    {
        if (sundialCd > 0f) { Hint($"นาฬิกาแดดยังชาร์จอยู่ ({sundialCd:0} วิ)", 2f); return; }
        if (player == null || voyage != null) { Hint("ใช้นาฬิกาแดดได้บนเกาะเท่านั้น", 2f); return; }
        int n = 0;
        foreach (var e in enemies)
            if (e != null && (e.grid - player.grid).magnitude < 9f) { e.Stun(5f); n++; }
        sundialCd = 60f;
        Sfx.Play("levelup", 0.5f);
        Fx.Burst(PlayerPos + Vector3.up * 0.6f, new Color(1f, 0.85f, 0.4f), 26, 2.5f, 1f, 1.2f, 0f);
        Popup(n > 0 ? $"หยุดเวลา! ({n} ตัว)" : "หยุดเวลา!", PlayerPos + Vector3.up * 1.2f, new Color(1f, 0.85f, 0.4f));
    }

    void TickHourglass(float dt)
    {
        if (sundialCd > 0f) sundialCd -= dt;
    }

    float DrawHourglassHud(float y)
    {
        if (biome != Biome.Hourglass || voyage != null) return y;
        ShadowLabel(new Rect(14, y, 360, 18), $"เวลาไหลเร็ว x{TimeScale:0}  ต้นไม้โตเร็ว x{GrowScale:0}", small, new Color(1f, 0.85f, 0.45f, 0.85f));
        return y + 16f;
    }

    /// The great clock tower in the middle of a fresh Hourglass island (next to the camp).
    void BuildClockTower()
    {
        if (biome != Biome.Hourglass) return;
        var spot = Center + new Vector2(-2.4f, 1.6f);
        foreach (var n in nodes.FindAll(n => (n.grid - spot).magnitude < 2f)) RemoveNode(n);
        Spawn(NodeKind.ClockTower, spot, false);
        var r = new System.Random(seed * 7 + 5);
        PlaceRing(NodeKind.ClockRuin, 5, 7f, 13f, r); // broken cogs from older clocks, scattered about
    }

    // ------------------------------------------------------------------ the clockmaker
    Quest ClockQuest() => new Quest
    {
        id = $"clock{island}", title = "ไขลานหอนาฬิกา",
        bring = new[] { (Item.Gear, 6), (Item.TimeSand, 4) },
        kill = new[] { (EnemyKind.WindupDoll, 4 + tier) },
        rewardText = "เวลาไหลช้าลง + เรียนทำนาฬิกาแดด",
        reward = () =>
        {
            flags.Add("clock" + island);
            learned.Add(Item.Sundial);
            AddXp(70, PlayerPos);
            TechPopup(Item.Sundial);
        },
    };

    const string ClockIntro = "หอนาฬิกาใหญ่หยุดเดิน เวลาบนเกาะเลยวิ่งเตลิดไปหมด กลางวันกลางคืนสลับกันจนข้าปวดหัว ตุ๊กตาไขลานพวกนั้นขโมยเฟืองไปจากหอ ช่วยกำจัดพวกมันแล้วเอาเฟืองกับทรายเวลามาให้ข้า ข้าจะสอนทำนาฬิกาแดดที่หยุดเวลาได้ชั่วครู่";
}
