using System.Collections.Generic;
using UnityEngine;

/// Game feel and readability: ground shadows under everyone, a clear "press E" tag on what you can use,
/// feedback when E finds nothing, the first-steps guide on the first island, a camera that looks
/// where you're going, and a little drifting life in the air.
public partial class IslandGame
{
    // ------------------------------------------------------------------ interaction feedback
    /// The nearest usable thing just out of reach (shown faintly: "come a bit closer").
    WorldNode nearMiss;
    const float NearMissRange = 2.6f;

    void FindNearMiss()
    {
        nearMiss = null;
        if (target != null || enemyTarget != null || villagerTarget != null || pond != null || player == null) return;
        float best = NearMissRange;
        foreach (var n in nodes)
        {
            if (n == null || !n.CanTarget) continue;
            float d = (n.grid - player.grid).magnitude;
            if (d < best) { best = d; nearMiss = n; }
        }
    }

    /// Why using this thing won't do what you'd expect (null = it will).
    string TargetNote(WorldNode n)
    {
        switch (n.kind)
        {
            case NodeKind.Tree: return HasAxe ? null : "ไม่มีขวาน: ได้แค่กิ่งไม้บางครั้ง";
            case NodeKind.Stump: return HasAxe ? null : "ต้องมีขวาน";
            case NodeKind.DigSpot: return Count(Item.Shovel) > 0 ? null : "ต้องมีพลั่ว";
            default: return null;
        }
    }

    /// E pressed with nothing to use: say why instead of doing nothing.
    void MissedInteract()
    {
        if (nearMiss != null)
        {
            string what = Prompt(nearMiss);
            Hint(string.IsNullOrEmpty(what) ? "เดินเข้าไปใกล้อีกนิดแล้วกด E" : $"เดินเข้าไปใกล้อีกนิด ({what})", 2f);
            Popup("ไกลไป", PlayerPos + Vector3.up * 1f, new Color(1f, 0.8f, 0.5f));
        }
        else Hint("ไม่มีอะไรให้ใช้ตรงนี้ - ของที่ใช้ได้จะมีวงกระพริบใต้มันและป้าย [E]", 2.5f);
    }

    /// Turn the player to look at what they're using.
    void FaceTarget()
    {
        if (player == null) return;
        Vector2? g = enemyTarget != null ? enemyTarget.grid : villagerTarget != null ? villagerTarget.grid : target != null ? target.grid : (Vector2?)null;
        if (g != null) player.FaceToward(g.Value);
    }

    /// The tag over the thing you can use: a key badge and what it does, in a dark pill.
    void DrawKeyTag(Vector2 gp, string text, string note, float alpha = 1f, bool showKey = true)
    {
        var st = new GUIStyle(labelCenter) { fontSize = 15 };
        var noteSt = new GUIStyle(smallCenter) { fontSize = 12 };
        float tw = st.CalcSize(Lang.Content(text)).x;
        float nw = note != null ? noteSt.CalcSize(Lang.Content(note)).x : 0f;
        float key = showKey ? 26f : 0f;
        float w = Mathf.Max(tw + key + 18f, nw + 16f), h = note != null ? 42f : 26f;
        var box = new Rect(gp.x - w / 2f, gp.y - h - 10f, w, h);
        Fill(box, new Color(0f, 0f, 0f, 0.7f * alpha));
        Fill(new Rect(box.x, box.yMax - 2f, box.width, 2f), new Color(1f, 0.85f, 0.3f, 0.8f * alpha));
        if (showKey)
        {
            var kr = new Rect(box.x + 5f, box.y + 3f, 20f, 20f);
            Fill(kr, new Color(1f, 0.85f, 0.3f, alpha));
            ShadowLabel(kr, "<color=#1a1208>E</color>", new GUIStyle(labelCenter) { fontSize = 14 });
        }
        ShadowLabel(new Rect(box.x + key + 4f, box.y + 1f, w - key - 8f, 24f), text, st, new Color(1f, 1f, 1f, alpha));
        if (note != null) ShadowLabel(new Rect(box.x, box.y + 22f, w, 18f), note, noteSt, new Color(1f, 0.7f, 0.4f, alpha));
        // a small pointer down at the thing itself
        float bob = Mathf.Sin(Time.time * 5f) * 2f;
        ShadowLabel(new Rect(gp.x - 10f, box.yMax - 4f + bob, 20f, 16f), "▼", new GUIStyle(smallCenter) { fontSize = 12 }, new Color(1f, 0.85f, 0.3f, alpha));
    }

    void DrawNearMiss(float s)
    {
        if (nearMiss == null) return;
        var gp = ToGui(nearMiss.transform.position + Vector3.up * nearMiss.PromptHeight, s);
        string text = Prompt(nearMiss);
        if (string.IsNullOrEmpty(text)) return;
        DrawKeyTag(gp, text, "เดินเข้าไปใกล้อีกนิด", 0.55f, false);
    }

    // ------------------------------------------------------------------ first steps on the first island
    struct TutStep
    {
        public string text, help;
        public NodeKind? find;   // point at the nearest one of these
        public Item? craft;      // highlight this recipe in the crafting panel
        public System.Func<IslandGame, bool> done;
        public System.Func<IslandGame, string> progress;
    }

    static bool HasAnyAxe(IslandGame g) => g.Count(Item.StoneAxe) > 0 || g.Count(Item.ObsidianAxe) > 0;
    static bool HasKnife(IslandGame g) => g.Count(Item.StoneKnife) > 0;

    static readonly TutStep[] TutSteps =
    {
        new TutStep { text = "เก็บกิ่งไม้ 3 อัน", help = "เดินไปที่ลูกศร แล้วกด E", find = NodeKind.Stick,
            done = g => g.Count(Item.Stick) >= 3 || HasKnife(g), progress = g => $"{Mathf.Min(3, g.Count(Item.Stick))}/3" },
        new TutStep { text = "เก็บหิน 3 ก้อน", help = "ก้อนหินเล็กๆ บนพื้น กด E เก็บ", find = NodeKind.Rock,
            done = g => g.Count(Item.Stone) >= 3 || HasKnife(g), progress = g => $"{Mathf.Min(3, g.Count(Item.Stone))}/3" },
        new TutStep { text = "ทำเครื่องมือชิ้นแรก: มีดหิน", help = "กด Tab เปิดหน้าประดิษฐ์ แล้วกด \"สร้าง\" ที่มีดหิน", craft = Item.StoneKnife,
            done = HasKnife },
        new TutStep { text = "เก็บใยพืชจากพุ่มหญ้า 3 เส้น", help = "พุ่มหญ้าสูง กด E (มีดช่วยให้ได้มากขึ้น)", find = NodeKind.Grass,
            done = g => g.Count(Item.Fiber) >= 3 || g.Count(Item.Rope) > 0 || HasAnyAxe(g), progress = g => $"{Mathf.Min(3, g.Count(Item.Fiber))}/3" },
        new TutStep { text = "ทำเชือก", help = "Tab แล้วสร้าง \"เชือก\" (ใช้ใยพืช 3)", craft = Item.Rope,
            done = g => g.Count(Item.Rope) > 0 || HasAnyAxe(g) },
        new TutStep { text = "ทำขวานหิน", help = "Tab แล้วสร้าง \"ขวานหิน\" (กิ่งไม้ 2 หิน 2 เชือก 1)", craft = Item.StoneAxe,
            done = HasAnyAxe },
        new TutStep { text = "ตัดต้นไม้ เอาไม้ 3 ท่อน", help = "ยืนข้างต้นไม้ กด E ค้างไว้", find = NodeKind.Tree,
            done = g => g.Count(Item.Wood) >= 3 || g.nodes.Exists(n => n != null && n.kind == NodeKind.Campfire), progress = g => $"{Mathf.Min(3, g.Count(Item.Wood))}/3" },
        new TutStep { text = "สร้างกองไฟ", help = "Tab แล้วสร้าง \"กองไฟ\" ใช้ปิ้งอาหารและไล่สัตว์ร้ายตอนกลางคืน",
            done = g => g.nodes.Exists(n => n != null && n.kind == NodeKind.Campfire) },
    };

    int tutStep = -1; // -1 = not loaded yet
    const string TutKey = "tut_first_steps";

    bool TutorialActive => tutStep >= 0 && tutStep < TutSteps.Length && !atHome && voyage == null && island == 1 && raftLevel == 0 && !visiting;
    Item? TutorialRecipe => TutorialActive ? TutSteps[tutStep].craft : null;
    NodeKind? TutorialFind => TutorialActive ? TutSteps[tutStep].find : null;

    void TickTutorial()
    {
        if (tutStep < 0) tutStep = PlayerPrefs.GetInt(TutKey, 0);
        if (!TutorialActive) return;
        bool advanced = false;
        while (tutStep < TutSteps.Length && TutSteps[tutStep].done(this)) { tutStep++; advanced = true; }
        if (!advanced) return;
        PlayerPrefs.SetInt(TutKey, tutStep);
        PlayerPrefs.Save();
        Sfx.Play("levelup", 0.4f);
        if (tutStep >= TutSteps.Length)
        {
            Popup("ทำครบทุกขั้นแล้ว!", PlayerPos + Vector3.up * 1.3f, new Color(1f, 0.85f, 0.3f));
            Hint("เก่งมาก! ต่อไป: หาน้ำ หาอาหาร แล้วสร้างแพ (Tab) ที่ริมทะเลเพื่อไปเกาะถัดไป", 7f);
        }
        else Popup("✓ สำเร็จ!", PlayerPos + Vector3.up * 1.3f, new Color(0.6f, 1f, 0.6f));
    }

    WorldNode TutorialTarget()
    {
        var k = TutSteps[tutStep].find;
        if (k == null || player == null) return null;
        WorldNode best = null;
        float bd = float.MaxValue;
        foreach (var n in nodes)
        {
            if (n == null || n.kind != k.Value || !n.CanTarget) continue;
            float d = (n.grid - player.grid).sqrMagnitude;
            if (d < bd) { bd = d; best = n; }
        }
        return best;
    }

    /// The step card (top left) and an arrow at the thing to go to. Returns the y below the card.
    float DrawTutorial(float W, float H, float y, float s)
    {
        if (!TutorialActive) return y;
        var step = TutSteps[tutStep];
        string prog = step.progress != null ? $"  <color=#ffd84d>{step.progress(this)}</color>" : "";
        var card = new Rect(10, y, 300, 50);
        Fill(card, new Color(0.05f, 0.08f, 0.12f, 0.78f));
        Fill(new Rect(card.x, card.y, 3, card.height), new Color(1f, 0.85f, 0.3f));
        ShadowLabel(new Rect(card.x + 10, card.y + 2, 290, 20), $"<size=12><color=#ffd84d>ขั้นที่ {tutStep + 1}/{TutSteps.Length}</color></size>  {step.text}{prog}", label);
        ShadowLabel(new Rect(card.x + 10, card.y + 26, 290, 20), step.help, new GUIStyle(small) { fontSize = 12 }, new Color(0.85f, 0.9f, 1f));

        var t = TutorialTarget();
        if (t != null && dialog == null && !craftOpen)
        {
            var gp = ToGui(t.transform.position + Vector3.up * (t.PromptHeight + 0.25f), s);
            bool on = gp.x > 20 && gp.x < W - 20 && gp.y > 20 && gp.y < H - 90;
            if (on)
            {
                if (t != target && t != nearMiss) // its own tag already marks it
                {
                    float bob = Mathf.Sin(Time.time * 4f) * 4f;
                    ShadowLabel(new Rect(gp.x - 20, gp.y - 34 + bob, 40, 34), "▼", new GUIStyle(titleCenter) { fontSize = 26 }, new Color(1f, 0.85f, 0.3f));
                }
            }
            else
            {
                // off screen: an arrow at the edge pointing the way
                var c = new Vector2(W / 2f, H / 2f);
                Vector2 d = (gp - c).normalized;
                float k = Mathf.Min((W / 2f - 40f) / Mathf.Max(0.001f, Mathf.Abs(d.x)), (H / 2f - 70f) / Mathf.Max(0.001f, Mathf.Abs(d.y)));
                Vector2 p = c + d * k;
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 90f, p);
                ShadowLabel(new Rect(p.x - 20, p.y - 20, 40, 40), "▲", new GUIStyle(titleCenter) { fontSize = 30 }, new Color(1f, 0.85f, 0.3f));
                GUI.matrix = m;
            }
        }
        return card.yMax + 6f;
    }

    // ------------------------------------------------------------------ camera
    Vector2 camLead; // eases toward where you're walking

    Vector3 CameraLead(float dt)
    {
        Vector2 goal = Vector2.zero;
        if (player != null && voyage == null && !Busy)
        {
            Vector2 input = PlayerController.ReadInput();
            if (input.sqrMagnitude > 0.01f) goal = Vector2.ClampMagnitude(input, 1f) * new Vector2(0.55f, 0.32f);
        }
        camLead = Vector2.Lerp(camLead, goal, 1f - Mathf.Exp(-2.2f * dt));
        return camLead;
    }

    // ------------------------------------------------------------------ a bit of life in the air
    float moteTimer;
    readonly List<AmbientMote> motes = new List<AmbientMote>();

    void TickAmbient(float dt)
    {
        motes.RemoveAll(m => m == null);
        if (cam == null || player == null || inHouse || voyage != null) return;
        moteTimer -= dt;
        if (moteTimer > 0f || motes.Count >= 14) return;
        moteTimer = Random.Range(0.25f, 0.6f);
        float h = cam.orthographicSize, w = h * cam.aspect;
        var at = new Vector3(cam.transform.position.x + Random.Range(-w, w), cam.transform.position.y + Random.Range(-h, h * 0.8f), 0f);
        var m = new GameObject("Mote").AddComponent<AmbientMote>();
        m.transform.SetParent(world);
        m.Init(at, IsNight);
        motes.Add(m);
    }
}

/// A soft dark ellipse on the ground under a character, drawn below everything standing up.
public class GroundShadow : MonoBehaviour
{
    static Sprite sprite;
    SpriteRenderer sr, owner;
    float scale;

    public static Sprite ShadowSprite()
    {
        if (sprite != null) return sprite;
        const int w = 22, h = 8; // wider than the feet so it shows on both sides
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f - w / 2f) / (w / 2f), dy = (y + 0.5f - h / 2f) / (h / 2f);
                float d = dx * dx + dy * dy;
                float a = d < 0.4f ? 0.5f : d < 1f ? 0.3f : 0f;
                tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
            }
        tex.Apply();
        sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), PixelArt.PPU);
        return sprite;
    }

    /// Put a shadow under `ownerSr`'s object. scale 1 = a person.
    public static GroundShadow Attach(SpriteRenderer ownerSr, float scale = 1f)
    {
        var go = new GameObject("Shadow");
        go.transform.SetParent(ownerSr.transform, false);
        var s = go.AddComponent<GroundShadow>();
        s.owner = ownerSr;
        s.scale = scale;
        s.sr = go.AddComponent<SpriteRenderer>();
        s.sr.sprite = ShadowSprite();
        s.sr.sortingOrder = -30050; // over the ground tiles and the target ring, under anything standing
        go.transform.localPosition = new Vector3(0f, -0.02f, 0f); // centred just under the feet
        go.transform.localScale = new Vector3(scale, scale, 1f);
        return s;
    }

    void LateUpdate()
    {
        if (owner == null) { Destroy(gameObject); return; }
        bool on = owner.enabled && owner.sprite != null && owner.color.a > 0.2f;
        sr.enabled = on;
        if (on) sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(owner.color.a));
    }
}

/// Pollen and drifting specks by day, blinking fireflies by night: the screen is never quite still.
public class AmbientMote : MonoBehaviour
{
    static Sprite dot;
    SpriteRenderer sr;
    float life, t, phase;
    bool firefly;
    Vector3 vel;

    static Sprite Dot()
    {
        if (dot != null) return dot;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        dot = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), PixelArt.PPU);
        return dot;
    }

    public void Init(Vector3 at, bool night)
    {
        firefly = night;
        transform.position = at;
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = Dot();
        sr.sortingOrder = 31000; // in the air, over everything on the ground
        life = Random.Range(3f, 6f);
        phase = Random.Range(0f, 6.3f);
        vel = night ? new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(-0.08f, 0.12f), 0f)
                    : new Vector3(Random.Range(0.05f, 0.25f), Random.Range(-0.05f, 0.08f), 0f);
        if (!night && Random.value < 0.5f) transform.localScale = new Vector3(0.5f, 0.5f, 1f);
        Apply();
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t >= life) { Destroy(gameObject); return; }
        transform.position += (vel + new Vector3(0f, Mathf.Sin(t * 2f + phase) * 0.06f, 0f)) * Time.deltaTime;
        Apply();
    }

    void Apply()
    {
        float fade = Mathf.Clamp01(t / 0.6f) * Mathf.Clamp01((life - t) / 0.8f);
        if (firefly)
        {
            float blink = 0.35f + 0.65f * Mathf.Max(0f, Mathf.Sin(t * 3f + phase));
            sr.color = new Color(0.85f, 1f, 0.45f, fade * blink);
        }
        else sr.color = new Color(1f, 0.97f, 0.8f, fade * 0.55f);
    }
}
