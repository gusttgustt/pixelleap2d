using UnityEngine;

/// How the boat looks. Every upgrade gives it a bigger, finer look (PixelArt.Boat.cs); the
/// shipwright can dress it back up as any earlier look you've had. Only the picture changes:
/// speed, armour and how far it reaches still come from the real raftLevel.
/// Your boat is never shown to other players (it isn't shared online), so the choice is only saved.
public partial class IslandGame
{
    /// The look picked at the shipwright, 1-8 (0 = always the newest look the boat has reached).
    [System.NonSerialized] public int boatLook;

    /// The look the boat shows now: the picked one, as long as the boat has reached it.
    public int BoatLookNow
    {
        get
        {
            int newest = PixelArt.BoatLook(Mathf.Max(1, raftLevel));
            return boatLook > 0 ? Mathf.Min(boatLook, newest) : newest;
        }
    }

    public Sprite BoatSprite => PixelArt.Boat(BoatLookNow);

    /// Bigger ships lie further out in the water: shift the picture seawards (the raft is beached
    /// on the island's west shore, so the sea is to the screen left).
    public Vector3 BoatShoreOffset => new Vector3(-(PixelArt.BoatWidth(BoatLookNow) - 40) * 0.4f / PixelArt.PPU, 0f, 0f);

    /// The same, for a boat beached at g on any shore: pushed out towards the open water there.
    public Vector3 BoatShoreOffsetAt(Vector2 g)
    {
        float len = (PixelArt.BoatWidth(BoatLookNow) - 40) * 0.4f / PixelArt.PPU;
        return len > 0f ? SeaSide(g) * len : Vector3.zero;
    }

    /// Screen direction (unit length) from g towards the nearby sea; screen-left if it can't tell.
    public Vector3 SeaSide(Vector2 g)
    {
        if (tiles == null) return Vector3.left;
        var t = Tile(g);
        var sum = Vector2.zero;
        for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
                if ((dx != 0 || dy != 0) && IsSea(t.x + dx, t.y + dy)) sum += new Vector2(dx, dy);
        if (sum.sqrMagnitude < 0.01f) return Vector3.left;
        Vector3 w = GridToWorld(sum); // GridToWorld is linear: grid direction -> screen direction
        return w.sqrMagnitude > 0.0001f ? w.normalized : Vector3.left;
    }

    public float BoatPromptHeight => Mathf.Max(1.05f, PixelArt.BoatHeight(BoatLookNow) * 0.75f);

    /// The shipwright's list of every look the boat has had, each with its picture.
    void BoatLookDialog(Villager v)
    {
        int newest = PixelArt.BoatLook(Mathf.Max(1, raftLevel)), now = BoatLookNow;
        var d = new Dialog
        {
            speaker = v.Title,
            text = $"อยากให้เรือหน้าตาแบบไหนล่ะ? ข้าแต่งให้เป็นแบบที่เจ้าเคยมีมาได้ทุกแบบ แต่ตัวเรือยังเป็น Lv {raftLevel} เหมือนเดิม: ความเร็ว ความทนทาน และระยะเดินทางไม่เปลี่ยน",
            icons = new Sprite[newest + 1],
        };
        for (int t = 1; t <= newest; t++)
        {
            int look = t;
            string name = BoatName(PixelArt.BoatLookLevel(look));
            string tag = look == now ? "  (ใช้อยู่)" : look == newest ? "  (แบบล่าสุด)" : "";
            d.options.Add(($"{name}{tag}", () => PickBoatLook(v, look == newest ? 0 : look, name), look != now));
            d.icons[t - 1] = PixelArt.Boat(look);
        }
        d.options.Add(("กลับ", () => OpenDialog(v), true));
        villagerTarget = v;
        dialog = d;
    }

    void PickBoatLook(Villager v, int look, string name)
    {
        boatLook = look;
        var raft = RaftNode;
        if (raft != null)
        {
            raft.Refresh();
            raft.Shake();
            Fx.Burst(raft.transform.position + Vector3.up * 0.4f, new Color(1f, 0.85f, 0.3f), 12, 1.2f, 1.4f, 1f, 3f);
        }
        Sfx.Play("craft", 0.6f);
        OpenDialog(v, $"เรียบร้อย! ตอนนี้เรือของเจ้าเป็นโฉม{name}แล้ว" + (look == 0 ? " และจะเปลี่ยนเป็นแบบใหม่ทุกครั้งที่อัปเกรด" : ""));
    }
}
