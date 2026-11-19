using System.Collections.Generic;
using UnityEngine;

/// The weapon a player is holding, drawn over their attack pose so you can tell what they hit
/// with, plus the swing's effects: spears, the trident and the lance wind back and thrust out
/// toward the target leaving speed lines, blades, axes and the scythe are raised and swept
/// through the target leaving a crescent smear; when the blow lands the target gets an impact
/// star, sparks in the weapon's colour and a small shake. Shown only while the attack plays;
/// empty hands draw no weapon (a smaller impact still lands).
///
/// The weapons are drawn here in code at the heroes' resolution (2 art pixels per world pixel)
/// with their single dark outline, at 32 angles, each angle drawn fresh rather than rotating a
/// sprite, so the lines stay clean pixel art at any angle. Used for me (PlayerController) and
/// for other players (RemotePlayer), who send which weapon they hold.
public class WeaponSwing : MonoBehaviour
{
    SpriteRenderer body, sr;
    bool trailDone;           // this swing's smear / speed lines are out
    Vector3? pendingHit;      // where my blow lands, shown at the strike (IslandGame.Attack)
    bool pendingArmed;
    float lastProgress = -1f;

    public static WeaponSwing Attach(SpriteRenderer body)
    {
        var go = new GameObject("Weapon");
        go.transform.SetParent(body.transform, false);
        var w = go.AddComponent<WeaponSwing>();
        w.body = body;
        w.sr = go.AddComponent<SpriteRenderer>();
        w.sr.enabled = false;
        return w;
    }

    /// Thrust (true) or sweep (false).
    static bool Thrusts(Item it) => it is Item.Spear or Item.ObsidianSpear or Item.Trident or Item.FrostLance;

    /// How far past the usual reach a weapon hits, in grid units (IslandGame.FindTarget): long
    /// weapons reach further than short ones, bare hands not at all.
    public static float ExtraReach(Item? it) => it switch
    {
        Item.Spear or Item.ObsidianSpear or Item.Trident or Item.FrostLance => 0.9f,
        Item.VoidScythe => 0.7f,
        Item.TideBlade or Item.MagmaAxe => 0.45f,
        _ => 0f,
    };

    /// The weapon's own colour, for its sparks and the tint of its smear.
    static Color Accent(Item? it) => it switch
    {
        Item.Spear => Hex("#d8dce0"),
        Item.ObsidianSpear => Hex("#a07ae8"),
        Item.Trident => Hex("#7ff5e6"),
        Item.FrostLance => Hex("#bfe8ff"),
        Item.TideBlade => Hex("#7fd4ff"),
        Item.MagmaAxe => Hex("#ff9a3a"),
        Item.VoidScythe => Hex("#c08aff"),
        _ => Color.white,
    };

    // ------------------------------------------------------------------ the swing

    const float Fine = PixelArt.PPU * 2f;          // the heroes' art pixels per world unit
    const float HandHeight = 14f / PixelArt.PPU;   // above the feet (the body's pivot): where the fist is in the punch
    const float HandOut = 6f / PixelArt.PPU;       // out from the middle toward the target
    const float StrikeAt = 0.36f;                  // share of the attack when the blow lands

    /// Where my blow lands: shown (impact star, sparks, shake) when the weapon gets there.
    public void QueueHit(Vector3 at) { pendingHit = at; pendingArmed = true; }

    /// weapon: what they hold (null = bare hands). progress: 0..1 through the attack, below 0 =
    /// not attacking. toward: where they strike, on screen (world units, y up). behind: turned
    /// away from the camera, so the weapon goes behind the body.
    public void Show(Item? weapon, float progress, Vector2 toward, bool behind)
    {
        bool swinging = progress >= 0f && progress <= 1f && body.enabled;
        if (swinging && progress < lastProgress) trailDone = false; // a new swing started over the last one
        if (!swinging) trailDone = false;
        lastProgress = swinging ? progress : -1f;
        if (toward.sqrMagnitude < 0.0001f) toward = Vector2.right;
        float aim = Mathf.Atan2(toward.y, toward.x) * Mathf.Rad2Deg;
        bool left = toward.x < -0.01f;
        Vector3 handWorld = body.transform.position + new Vector3(0f, HandHeight, 0f) + (Vector3)(toward.normalized * HandOut);

        // my blow lands at the strike (bare hands: at once, there's nothing to wait for)
        if (pendingArmed && (weapon == null || !swinging || progress >= StrikeAt))
        {
            pendingArmed = false;
            if (pendingHit != null) Impact(pendingHit.Value, weapon);
            pendingHit = null;
        }

        if (weapon == null || !swinging)
        {
            sr.enabled = false;
            return;
        }
        var it = weapon.Value;
        float angle, reach, alpha;
        if (Thrusts(it))
        {
            // wind back, jab out hard, hold, ease back
            const float drawn = 0.3f, out_ = StrikeAt + 0.04f;
            float jab;
            if (progress < drawn) { reach = Mathf.Lerp(0f, -5f, Mathf.SmoothStep(0f, 1f, progress / drawn)); jab = 0f; }
            else if (progress < out_) { jab = (progress - drawn) / (out_ - drawn); reach = Mathf.Lerp(-5f, 9f, jab); }
            else if (progress < 0.65f) { reach = 9f; jab = 1f; }
            else { reach = Mathf.Lerp(9f, 2f, Mathf.SmoothStep(0f, 1f, (progress - 0.65f) / 0.35f)); jab = 1f; }
            angle = aim + (left ? -1f : 1f) * 10f * (1f - jab); // the tip lifts a little while drawn back
            alpha = Mathf.Clamp01(progress / 0.08f) * Mathf.Clamp01((1f - progress) / 0.2f);
            if (!trailDone && progress >= StrikeAt) { trailDone = true; ThrustLines(it, handWorld, aim, body.sortingOrder + 2); }
        }
        else
        {
            // raised behind, a fast sweep down through the target, a short follow-through
            float s = left ? -1f : 1f;
            float up = 115f, down = -75f;
            float a;
            if (progress < 0.28f) a = Mathf.Lerp(40f, up, Mathf.SmoothStep(0f, 1f, progress / 0.28f));
            else if (progress < StrikeAt + 0.06f) a = Mathf.Lerp(up, down, (progress - 0.28f) / (StrikeAt + 0.06f - 0.28f));
            else a = Mathf.Lerp(down, down - 12f, Mathf.SmoothStep(0f, 1f, (progress - StrikeAt - 0.06f) / 0.3f));
            angle = aim + s * a;
            reach = 1f;
            alpha = Mathf.Clamp01(progress / 0.08f) * Mathf.Clamp01((1f - progress) / 0.22f);
            if (!trailDone && progress >= 0.3f) { trailDone = true; Smear(it, handWorld, aim, left, body.sortingOrder + 2); }
        }

        int idx = AngleIndex(angle);
        float snapped = idx * 360f / Steps;
        Vector2 dir = new Vector2(Mathf.Cos(snapped * Mathf.Deg2Rad), Mathf.Sin(snapped * Mathf.Deg2Rad));
        Vector3 at = handWorld + (Vector3)(dir * reach / PixelArt.PPU);
        at = new Vector3(Mathf.Round(at.x * Fine) / Fine, Mathf.Round(at.y * Fine) / Fine, at.z); // whole art pixels: no shimmer
        transform.position = at;
        transform.rotation = Quaternion.identity;
        Vector3 bs = body.transform.lossyScale; // undo the body's squash so the weapon keeps its shape
        transform.localScale = new Vector3(1f / Mathf.Max(0.01f, bs.x), 1f / Mathf.Max(0.01f, bs.y), 1f);
        sr.sprite = Held(it, idx, left);
        sr.flipX = false;
        sr.sortingLayerID = body.sortingLayerID;
        sr.sortingOrder = body.sortingOrder + (behind ? -1 : 1);
        var c = body.color;
        c.a *= alpha;
        sr.color = c;
        sr.enabled = true;
    }

    /// The blow landing: a white impact star, sparks in the weapon's colour, and a small shake
    /// (bigger with a weapon).
    static void Impact(Vector3 at, Item? weapon)
    {
        Vector3 p = at + Vector3.up * (6f / PixelArt.PPU);
        Color acc = Accent(weapon);
        bool armed = weapon != null;
        Fx.Wave(Star(), p, Color.white, armed ? 0.7f : 0.5f, armed ? 1.25f : 0.9f, 0.16f, 30001);
        Fx.Wave(Ring(), p, new Color(acc.r, acc.g, acc.b, 0.85f), 0.4f, armed ? 1.4f : 1f, 0.22f, 30000);
        int n = armed ? 10 : 5;
        for (int i = 0; i < n; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            var v = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.7f + 0.3f, 0f) * Random.Range(1.2f, 2.4f);
            Fx.Spark(p, v, i % 3 == 0 ? Color.white : acc, Random.Range(0.18f, 0.3f), 1f, 4f, 4f);
        }
        if (IslandGame.I != null) IslandGame.I.Shake(armed ? 0.045f : 0.025f, 0.1f);
    }

    // ------------------------------------------------------------------ effects

    /// A crescent smear along the sweep, bright at its leading edge, fading behind.
    static void Smear(Item it, Vector3 hand, float aim, bool left, int order)
    {
        int idx = AngleIndex(aim);
        Fx.Wave(SmearSprite(it, idx, left), hand, Color.white, 0.95f, 1.08f, 0.2f, order);
    }

    /// Speed lines along the thrust and a flash at the tip.
    static void ThrustLines(Item it, Vector3 hand, float aim, int order)
    {
        int idx = AngleIndex(aim);
        Fx.Wave(StreakSprite(it, idx), hand, Color.white, 1f, 1.06f, 0.18f, order);
        float a = idx * 360f / Steps * Mathf.Deg2Rad;
        Vector3 tip = hand + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * ((Length(it) + 9f) / PixelArt.PPU);
        Fx.Wave(Star(), tip, new Color(1f, 1f, 1f, 0.9f), 0.35f, 0.7f, 0.12f, order + 1);
    }

    // ------------------------------------------------------------------ drawing

    const int Steps = 32; // angles drawn per weapon
    static int AngleIndex(float deg) => ((Mathf.RoundToInt(deg / (360f / Steps)) % Steps) + Steps) % Steps;

    static Color Hex(string h) => PixelArt.Hex(h);
    static readonly Color Outline = new Color(0.106f, 0.078f, 0.141f, 1f); // #1b1424, like the heroes' outline

    /// One stroke of a weapon, in world pixels along it (u, from the grip toward the tip) and
    /// across it (v, + = the side that turns counterclockwise): a line from a to b, r0 thick at
    /// a narrowing to r1 at b (half-widths).
    struct Stroke
    {
        public Vector2 a, b; public float r0, r1; public Color mid, light, dark;
        public Stroke(float ua, float va, float ub, float vb, float r0, float r1, string mid, string light, string dark = null)
        {
            a = new Vector2(ua, va); b = new Vector2(ub, vb); this.r0 = r0; this.r1 = r1;
            this.mid = Hex(mid); this.light = Hex(light);
            this.dark = dark != null ? Hex(dark) : Color.Lerp(this.mid, Color.black, 0.35f);
        }
    }

    static float Length(Item it) => it switch
    {
        Item.Spear or Item.ObsidianSpear => 27f,
        Item.Trident => 28f,
        Item.FrostLance => 28f,
        Item.TideBlade => 20f,
        Item.MagmaAxe => 16f,
        Item.VoidScythe => 23f,
        _ => 16f,
    };

    /// The weapon's shape, back to front. The blade side of axes and the scythe is -v: the side
    /// that leads a downward sweep facing right (the sprite is mirrored facing left).
    static Stroke[] Shape(Item it) => it switch
    {
        Item.Spear => new[]
        {
            new Stroke(-4, 0, 20, 0, 0.9f, 0.9f, "#8a5a2e", "#b98049", "#5a3a1c"),
            new Stroke(16.5f, 0, 19.5f, 0, 1.5f, 1.5f, "#c8a46a", "#ecd29a"),
            new Stroke(19, 0, 22, 0, 1.6f, 2.5f, "#8e949c", "#dfe3e8", "#5c6168"),
            new Stroke(22, 0, 27.5f, 0, 2.5f, 0.1f, "#8e949c", "#dfe3e8", "#5c6168"),
        },
        Item.ObsidianSpear => new[]
        {
            new Stroke(-4, 0, 19, 0, 0.9f, 0.9f, "#4a3428", "#6e5040", "#2e2018"),
            new Stroke(16, 0, 19, 0, 1.5f, 1.5f, "#8a6ac8", "#b89af0"),
            new Stroke(18.5f, 0, 27.5f, 0, 2.7f, 0.1f, "#2a1a3a", "#8a6ac8", "#140c1e"),
        },
        Item.Trident => new[]
        {
            new Stroke(-4, 0, 19, 0, 0.9f, 0.9f, "#b88a30", "#f2d070", "#7a5a18"),
            new Stroke(19, -3.6f, 19, 3.6f, 1.1f, 1.1f, "#c8a040", "#f2d070", "#7a5a18"),
            new Stroke(19, 0, 28, 0, 1.0f, 0.1f, "#c8ccd8", "#ffffff", "#7a8090"),
            new Stroke(19, 3.4f, 25.5f, 3.4f, 0.9f, 0.1f, "#c8ccd8", "#ffffff", "#7a8090"),
            new Stroke(19, -3.4f, 25.5f, -3.4f, 0.9f, 0.1f, "#c8ccd8", "#ffffff", "#7a8090"),
            new Stroke(18.5f, 0, 20, 0, 1.2f, 1.2f, "#3ac8b8", "#9ffff2"),
        },
        Item.FrostLance => new[]
        {
            new Stroke(-4, 0, 4, 0, 1.0f, 1.0f, "#3a5a7a", "#5a82a8", "#22364a"),
            new Stroke(4, -3, 4, 3, 1.0f, 1.0f, "#a8d8f8", "#ffffff", "#5a8ab8"),
            new Stroke(5, 0, 28, 0, 2.6f, 0.1f, "#6aa8e0", "#eef8ff", "#3a6a9a"),
        },
        Item.TideBlade => new[]
        {
            new Stroke(-3.5f, 0, -3.5f, 0, 1.5f, 1.5f, "#f2c43a", "#fff0a0", "#a07a18"),
            new Stroke(-3, 0, 3, 0, 1.0f, 1.0f, "#5a3a1a", "#7a5430", "#3a2410"),
            new Stroke(3.5f, -4, 3.5f, 4, 1.0f, 1.0f, "#f2c43a", "#fff0a0", "#a07a18"),
            new Stroke(4, 0, 17, 0, 1.8f, 1.6f, "#3a9ad0", "#c8f0ff", "#1f5f8a"),
            new Stroke(17, 0, 20.5f, 0, 1.6f, 0.1f, "#3a9ad0", "#c8f0ff", "#1f5f8a"),
            new Stroke(5, 0.4f, 17, 0.4f, 0.4f, 0.4f, "#e8fbff", "#ffffff"),
        },
        Item.MagmaAxe => new[]
        {
            new Stroke(-4, 0, 16.5f, 0, 1.0f, 1.0f, "#6a2a1a", "#9a4428", "#401810"),
            new Stroke(13, 0.5f, 13, 2.8f, 1.4f, 0.5f, "#5a2418", "#8a3a24"),
            new Stroke(13, -0.5f, 13, -4.4f, 1.4f, 2.9f, "#c8501a", "#ff8a2a", "#6a2414"),
            new Stroke(10.4f, -5.0f, 13, -6.0f, 0.9f, 0.9f, "#ffb04a", "#fff0a0", "#d86a1e"),
            new Stroke(13, -6.0f, 15.6f, -5.0f, 0.9f, 0.9f, "#ffb04a", "#fff0a0", "#d86a1e"),
            new Stroke(14.2f, -2.4f, 14.2f, -2.4f, 0.7f, 0.7f, "#ffe06a", "#ffffff"),
        },
        Item.VoidScythe => new[]
        {
            new Stroke(-4, 0, 22, 0, 0.9f, 0.9f, "#2e1a5a", "#4e3290", "#180c30"),
            new Stroke(22, 0, 21, -4.5f, 2.0f, 1.6f, "#7a4ad8", "#c8a0ff", "#3e1e80"),
            new Stroke(21, -4.5f, 18.5f, -8.5f, 1.6f, 1.0f, "#7a4ad8", "#c8a0ff", "#3e1e80"),
            new Stroke(18.5f, -8.5f, 14.5f, -11f, 1.0f, 0.1f, "#7a4ad8", "#f0d8ff", "#3e1e80"),
            new Stroke(22.6f, 0, 23.6f, 0, 1.2f, 1.2f, "#a86aff", "#f0d8ff"),
        },
        _ => new Stroke[0],
    };

    // Lit from the top-left of the screen, like the rest of the game.
    static readonly Vector2 LightDir = new Vector2(-0.6f, 0.8f);

    static readonly Dictionary<long, Sprite> cache = new Dictionary<long, Sprite>();

    /// The weapon at angle step idx (pointing that way from its grip, the sprite's pivot);
    /// mirror flips its blade side for facing left.
    static Sprite Held(Item it, int idx, bool mirror)
    {
        long key = ((long)it << 8) | ((long)idx << 1) | (mirror ? 1L : 0L);
        if (cache.TryGetValue(key, out var s) && s != null) return s;
        var shape = Shape(it);
        const int N = 128; // art pixels square (64 world pixels), the grip in the middle
        float ang = idx * 360f / Steps * Mathf.Deg2Rad, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // art pixel centre -> world pixels on screen -> along / across the weapon
                float qx = (x + 0.5f - N / 2f) / 2f, qy = (y + 0.5f - N / 2f) / 2f;
                float u = qx * ca + qy * sa, v = -qx * sa + qy * ca;
                if (mirror) v = -v;
                var p = new Vector2(u, v);
                for (int k = shape.Length - 1; k >= 0; k--) // front strokes first
                {
                    var st = shape[k];
                    Vector2 ab = st.b - st.a;
                    float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - st.a, ab) / ab.sqrMagnitude) : 0f;
                    Vector2 close = st.a + ab * t;
                    float r = Mathf.Lerp(st.r0, st.r1, t);
                    Vector2 off = p - close;
                    if (off.magnitude > r + 0.25f) continue;
                    // which way this pixel faces, back on screen, against the light
                    Vector2 n = new Vector2(off.x, mirror ? -off.y : off.y);
                    Vector2 ns = new Vector2(n.x * ca - n.y * sa, n.x * sa + n.y * ca);
                    float lit = Vector2.Dot(ns, LightDir.normalized) / Mathf.Max(0.5f, r);
                    px[y * N + x] = lit > 0.3f ? st.light : lit < -0.45f ? st.dark : st.mid;
                    break;
                }
            }
        AddOutline(px, N, N);
        s = MakeSprite(px, N, N);
        cache[key] = s;
        return s;
    }

    /// A dark single-pixel outline around everything drawn, like the hand-drawn characters.
    static void AddOutline(Color[] px, int w, int h)
    {
        var src = (Color[])px.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (src[y * w + x].a > 0f) continue;
                bool edge = (x > 0 && src[y * w + x - 1].a > 0.5f) || (x < w - 1 && src[y * w + x + 1].a > 0.5f)
                         || (y > 0 && src[(y - 1) * w + x].a > 0.5f) || (y < h - 1 && src[(y + 1) * w + x].a > 0.5f);
                if (edge) px[y * w + x] = Outline;
            }
    }

    static Sprite MakeSprite(Color[] px, int w, int h, float pivotX = 0.5f, float pivotY = 0.5f)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        t.SetPixels(px);
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(pivotX, pivotY), Fine, 0, SpriteMeshType.FullRect);
    }

    /// The sweep's smear: a crescent band at the weapon's tip, from raised behind (faint) to
    /// past the target (bright, white at the outer edge), centred on the hand.
    static Sprite SmearSprite(Item it, int idx, bool left)
    {
        long key = (1L << 40) | ((long)it << 8) | ((long)idx << 1) | (left ? 1L : 0L);
        if (cache.TryGetValue(key, out var s) && s != null) return s;
        float R = Length(it) + 1.5f, inner = R - Mathf.Max(4f, R * 0.32f);
        int N = Mathf.CeilToInt((R + 3f) * 4f / 2f) * 2;
        float aim = idx * 360f / Steps, sgn = left ? -1f : 1f;
        float from = 110f, to = -80f; // relative to aim, before mirroring for left
        Color acc = Accent(it);
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float qx = (x + 0.5f - N / 2f) / 2f, qy = (y + 0.5f - N / 2f) / 2f;
                float d = Mathf.Sqrt(qx * qx + qy * qy);
                if (d < inner || d > R) continue;
                float rel = Mathf.DeltaAngle(aim, Mathf.Atan2(qy, qx) * Mathf.Rad2Deg) * sgn;
                if (rel > from || rel < to) continue;
                float along = (from - rel) / (from - to);            // 0 at the start of the sweep, 1 at its end
                float band = (d - inner) / (R - inner);              // 0 inside, 1 at the tip's path
                float thick = Mathf.Lerp(0.15f, 1f, along);          // the band thickens toward the end
                if (band < 1f - thick) continue;
                float a = Mathf.SmoothStep(0f, 1f, along) * (along > 0.92f ? (1f - along) / 0.08f : 1f);
                Color c = band > 0.8f ? Color.white : Color.Lerp(acc, Color.white, band * 0.6f);
                c.a = Mathf.Clamp01(a * (0.45f + 0.55f * band));
                if (c.a < 0.12f) continue;
                c.a = c.a > 0.6f ? 1f : 0.55f; // two steps of see-through, like pixel art smears
                px[y * N + x] = c;
            }
        s = MakeSprite(px, N, N);
        cache[key] = s;
        return s;
    }

    /// The thrust's speed lines: three streaks along the weapon, brightest at the tip.
    static Sprite StreakSprite(Item it, int idx)
    {
        long key = (2L << 40) | ((long)it << 8) | ((long)idx << 1);
        if (cache.TryGetValue(key, out var s) && s != null) return s;
        float L = Length(it) + 10f;
        int N = Mathf.CeilToInt((L + 3f) * 4f / 2f) * 2;
        float ang = idx * 360f / Steps * Mathf.Deg2Rad, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
        Color acc = Accent(it);
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float qx = (x + 0.5f - N / 2f) / 2f, qy = (y + 0.5f - N / 2f) / 2f;
                float u = qx * ca + qy * sa, v = -qx * sa + qy * ca;
                float av = Mathf.Abs(v);
                float start, end;
                if (av < 0.5f) { start = 6f; end = L; }                          // the middle line, longest
                else if (Mathf.Abs(av - 3.5f) < 0.45f) { start = 9f; end = L - 4f; } // the outer two
                else continue;
                if (u < start || u > end) continue;
                float k = (u - start) / (end - start);
                Color c = k > 0.7f ? Color.white : acc;
                c.a = k > 0.35f ? 1f : 0.55f;
                px[y * N + x] = c;
            }
        s = MakeSprite(px, N, N);
        cache[key] = s;
        return s;
    }

    static Sprite star, ring;

    /// A four-pointed white impact star with a dark rim.
    static Sprite Star()
    {
        if (star != null) return star;
        const int N = 28;
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - N / 2f), qy = Mathf.Abs(y + 0.5f - N / 2f);
                // a plus sign with tapering arms, and a small diagonal cross
                bool arm = (qx < 2.2f - qy * 0.17f && qy < 13f) || (qy < 2.2f - qx * 0.17f && qx < 13f);
                bool diag = Mathf.Abs(qx - qy) < 1.1f && qx + qy < 11f;
                bool core = qx * qx + qy * qy < 9f;
                if (arm || diag || core) px[y * N + x] = core || (arm && qx + qy < 7f) ? Color.white : new Color(1f, 0.95f, 0.75f, 1f);
            }
        star = MakeSprite(px, N, N);
        return star;
    }

    /// A thin flat ring for the shock of the blow.
    static Sprite Ring()
    {
        if (ring != null) return ring;
        const int N = 40;
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float qx = x + 0.5f - N / 2f, qy = (y + 0.5f - N / 2f) * 1.6f;
                float d = Mathf.Sqrt(qx * qx + qy * qy);
                if (d > 16f && d < 19f) px[y * N + x] = Color.white;
            }
        ring = MakeSprite(px, N, N);
        return ring;
    }
}
