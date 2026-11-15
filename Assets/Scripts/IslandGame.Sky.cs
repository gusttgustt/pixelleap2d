using UnityEngine;

/// The Sky island: floats above a sea of clouds. Gusts of wind sweep across it now and then and
/// can blow you right off the edge (you land back by your boat, bruised). Help the wind sage and
/// the gusts calm down; he teaches you the hot-air balloon (your boat flies over sharks at sea).
public partial class IslandGame
{
    const float GustPeriod = 26f, GustWarn = 3f, GustLength = 3.5f;
    Vector2 gustDir = Vector2.right;
    int gustIndex = -1;
    bool gustWarned;

    float GustClock => Mathf.Repeat(Now, GustPeriod);
    bool Sky => biome == Biome.Sky && voyage == null;
    bool GustWarning => Sky && GustClock >= GustPeriod - GustLength - GustWarn && GustClock < GustPeriod - GustLength;
    bool Gusting => Sky && GustClock >= GustPeriod - GustLength;
    float GustStrength => Flag("windcalm" + island) ? 0.9f : 1.9f; // grid units per second

    static string DirName(Vector2 d)
    {
        Vector3 w = GridToWorld(d) - GridToWorld(Vector2.zero);
        if (Mathf.Abs(w.x) > Mathf.Abs(w.y)) return w.x > 0 ? "ตะวันตก" : "ตะวันออก"; // where the wind comes FROM
        return w.y > 0 ? "ใต้" : "เหนือ";
    }

    void UpdateSky(float dt)
    {
        if (!Sky || player == null) return;
        int idx = Mathf.FloorToInt(Now / GustPeriod);
        if (idx != gustIndex) // a new gust direction every cycle
        {
            gustIndex = idx;
            var r = new System.Random(seed + idx * 131);
            float a = (float)r.NextDouble() * Mathf.PI * 2f;
            gustDir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            gustWarned = false;
        }
        if (GustWarning)
        {
            if (!gustWarned) { gustWarned = true; Hint($"ลมกรรโชกกำลังมาจากทิศ{DirName(gustDir)}! อย่ายืนใกล้ขอบเกาะ", 4f); }
            if (Random.value < dt * 20f) WindStreak();
        }
        if (Gusting && !dead && !Busy)
        {
            if (Random.value < dt * 30f) WindStreak();
            Vector2 next = player.grid + gustDir * GustStrength * dt;
            if (!IsLand(next)) { FallOff(); return; }
            if (CanWalk(next, 0.18f)) player.grid = next; // trees and rocks shelter you
        }
    }

    void WindStreak()
    {
        Vector3 off = new Vector3(Random.Range(-4f, 4f), Random.Range(-2.5f, 2.5f), 0f);
        Vector3 w = GridToWorld(gustDir) - GridToWorld(Vector2.zero);
        Fx.Burst(cam.transform.position + off - w * 2f, new Color(1f, 1f, 1f, 0.8f), 1, 0.2f, 0.8f, 0.6f, 0f);
        Fx.Burst(cam.transform.position + off, new Color(1f, 1f, 1f, 0.6f), 1, 0.2f, 0.8f, 0.6f, 0f);
    }

    /// Blown off the island: you drift down onto the cloud bank by your boat.
    void FallOff()
    {
        var raft = RaftNode;
        Vector2 safe = raft != null ? WalkableNear(raft.grid + (Center - raft.grid).normalized * 1.4f) : WalkableNear(Center);
        player.grid = safe;
        Fx.Burst(PlayerPos + Vector3.up * 0.5f, Color.white, 20, 1.6f, 1.4f, 1f, 1f);
        Hint("ปลิวตกเกาะ! ร่อนลงบนก้อนเมฆข้างเรือ", 4f);
        Sfx.Play("splash", 0.6f);
        health -= 15f;
        hurtT = 0.3f;
        CheckDeath();
    }

    float DrawSkyHud(float y)
    {
        if (!Sky) return y;
        string text; Color col;
        if (Gusting) { text = "ลมกรรโชก! หลบหลังต้นไม้หรือก้อนหิน"; col = new Color(1f, 1f, 1f, 0.6f + 0.4f * Mathf.Sin(Time.time * 8f)); }
        else if (GustWarning) { text = $"ลมกำลังมาจากทิศ{DirName(gustDir)} ใน {GustPeriod - GustLength - GustClock:0} วิ"; col = new Color(1f, 0.85f, 0.4f); }
        else { text = $"ลมสงบ (ลมกรรโชกอีกใน {GustPeriod - GustLength - GustWarn - GustClock:0} วิ)"; col = new Color(0.8f, 0.9f, 1f, 0.8f); }
        ShadowLabel(new Rect(14, y, 360, 18), text, small, col);
        return y + 16f;
    }

    // ------------------------------------------------------------------ the chime birds' loot
    /// A chime bird grabs something from your bag and flies off with it.
    public (Item item, int n) StealItem()
    {
        var pool = new System.Collections.Generic.List<Item>();
        foreach (var kv in inv) if (kv.Value > 0 && !IsTool(kv.Key) && !IsClothing(kv.Key)) pool.Add(kv.Key);
        if (pool.Count == 0) return (Item.Fiber, 0);
        var it = pool[Random.Range(0, pool.Count)];
        int n = Mathf.Min(Count(it), 3);
        inv[it] -= n;
        Popup($"นกขโมย{Name(it)} {n}!", PlayerPos + Vector3.up * 1f, new Color(1f, 0.6f, 0.6f));
        Sfx.Play("hurt", 0.4f);
        return (it, n);
    }

    /// Beat a thief and get your things back.
    void ReturnStolen(Enemy e)
    {
        if (e.stolenCount <= 0) return;
        inv[e.stolenItem] = Count(e.stolenItem) + e.stolenCount;
        Popup($"ได้{Name(e.stolenItem)}คืน {e.stolenCount}", e.transform.position + Vector3.up * 0.9f, new Color(0.7f, 1f, 0.7f));
        e.stolenCount = 0;
    }

    // ------------------------------------------------------------------ the wind sage
    Quest SageQuest() => new Quest
    {
        id = $"wind{island}", title = "ตามหากระดิ่งลม",
        bring = new[] { (Item.Feather, 4) },
        kill = new[] { (EnemyKind.ChimeBird, 3 + tier) },
        rewardText = "ลมสงบลง + เรียนทำบอลลูนลมร้อน",
        reward = () =>
        {
            flags.Add("windcalm" + island);
            learned.Add(Item.Balloon);
            AddXp(50, PlayerPos);
            TechPopup(Item.Balloon);
        },
    };

    const string SageIntro = "ฝูงนกกระดิ่งลมขโมยกระดิ่งศักดิ์สิทธิ์ของข้าไป ไม่มีเสียงกระดิ่ง ลมเลยพัดแรงไม่หยุด ช่วยไล่นกพวกนั้น แล้วเอาขนนกมาให้ข้าทำพู่กระดิ่งใหม่ ข้าจะสอนทำบอลลูนลมร้อนให้ เรือของเจ้าจะลอยเหนือคลื่น ฉลามกัดไม่ถึง";
}
