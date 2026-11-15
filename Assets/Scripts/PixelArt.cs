using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates every pixel-art sprite in code so the game runs with no imported art.
///
/// Replacing art: put a PNG named after a sprite key into Assets/Resources/Sprites/
/// (e.g. Assets/Resources/Sprites/tree.png) and it is used instead of the generated one.
/// Use menu "Island > Export Placeholder Art" to get every placeholder as a PNG template
/// with the right size, then paint over it in Aseprite. See ART_GUIDE.md.
///
/// Tile size: 32x16 px (2:1 isometric), 32 pixels per unit.
/// </summary>
public static class PixelArt
{
    public const int PPU = 32;
    public const int HeightPx = 6; // pixels per terrain height level
    public static float HeightUnits(int h) => h * HeightPx / (float)PPU;

    public static readonly Color WaterColor = new Color(0.20f, 0.52f, 0.78f);
    public static readonly Color FreshWaterColor = new Color(0.31f, 0.70f, 0.78f);
    static readonly Color Shadow = new Color(0f, 0f, 0f, 0.28f);

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    static readonly Dictionary<string, Sprite> generated = new Dictionary<string, Sprite>();

    public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    static Sprite Get(string key, Func<Sprite> make)
    {
        if (cache.TryGetValue(key, out var s) && s != null) return s;
        s = make();
        s.name = key;
        generated[key] = s;
        // Hand-made art override: Assets/Resources/Sprites/<key>.png
        var tex = Resources.Load<Texture2D>("Sprites/" + key);
        if (tex != null)
        {
            tex.filterMode = FilterMode.Point;
            var pivot = new Vector2(s.pivot.x / s.rect.width, s.pivot.y / s.rect.height);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, PPU, 0, SpriteMeshType.FullRect);
            s.name = key;
        }
        cache[key] = s;
        return s;
    }

    /// Every generated placeholder sprite, keyed by name (used by the art exporter).
    public static Dictionary<string, Sprite> AllPlaceholders()
    {
        for (int f = 0; f < 2; f++)
        {
            Water(f, false); Water(f, true); Campfire(f);
            foreach (EnemyKind e in Enum.GetValues(typeof(EnemyKind))) Enemy(e, f);
            foreach (SeaKind s in Enum.GetValues(typeof(SeaKind))) SeaCreature(s, f);
        }
        foreach (Biome b in Enum.GetValues(typeof(Biome)))
        {
            for (int v = 0; v < 4; v++)
            {
                Land(false, v, 0, b);
                for (int h = 0; h <= 2; h++) Land(true, v, h, b);
            }
            Tree(b); FruitTree(true, b); FruitTree(false, b);
        }
        Cursor(); Dot(); Stump(); Bush(true); Bush(false);
        Grass(); Stick(); Rock(); Shelter(); Bamboo(); Obsidian(); Wave();
        for (int lv = 1; lv <= 4; lv++) Raft(lv);
        Item?[] hats = { null, Item.StrawHat, Item.TribalHeadband }, outfits = { null, Item.LeafTunic, Item.HideVest };
        for (int d = 0; d < 2; d++)
            for (int f = 0; f < 3; f++)
                foreach (var h in hats)
                    foreach (var o in outfits) Player(d, f, h, o);
        for (int look = 0; look < 4; look++) for (int f = 0; f < 3; f++) Villager(look, f);
        Hut(); Totem();
        foreach (Item it in Enum.GetValues(typeof(Item))) Icon(it);
        return generated;
    }

    static bool InDiamond(int x, int y)
    {
        float dx = Mathf.Abs(x + 0.5f - 16f) / 16f, dy = Mathf.Abs(y + 0.5f - 8f) / 8f;
        return dx + dy <= 1f;
    }

    // ------------------------------------------------------------------ canvas
    /// Simple drawing surface. Coordinates are top-left origin, y grows downward.
    class Canvas
    {
        public readonly int W, H;
        readonly Color[] px;
        public Canvas(int w, int h) { W = w; H = h; px = new Color[w * h]; }

        public void Set(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H || c.a <= 0f) return;
            int i = (H - 1 - y) * W + x;
            Color d = px[i];
            if (c.a >= 1f || d.a <= 0f) { px[i] = c; return; }
            Color o = Color.Lerp(d, c, c.a);
            o.a = Mathf.Max(d.a, c.a);
            px[i] = o;
        }

        public Color GetPixel(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? Color.clear : px[(H - 1 - y) * W + x];

        public void Rect(int x, int y, int w, int h, Color c)
        {
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Set(x + i, y + j, c);
        }

        public void Ellipse(float cx, float cy, float rx, float ry, Color c)
        {
            for (int y = Mathf.FloorToInt(cy - ry); y <= Mathf.CeilToInt(cy + ry); y++)
                for (int x = Mathf.FloorToInt(cx - rx); x <= Mathf.CeilToInt(cx + rx); x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) Set(x, y, c);
                }
        }

        /// Shaded blob lit from the top-left.
        public void Ball(float cx, float cy, float rx, float ry, Color dark, Color mid, Color light)
        {
            for (int y = Mathf.FloorToInt(cy - ry); y <= Mathf.CeilToInt(cy + ry); y++)
                for (int x = Mathf.FloorToInt(cx - rx); x <= Mathf.CeilToInt(cx + rx); x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry, d2 = dx * dx + dy * dy;
                    if (d2 > 1f) continue;
                    float s = dx + dy;
                    Set(x, y, s > 0.55f ? dark : (s < -0.55f && d2 > 0.15f) ? light : mid);
                }
        }

        public void Ring(float cx, float cy, float r, float thick, Color c)
        {
            for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
                for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    if (d <= r && d >= r - thick) Set(x, y, c);
                }
        }

        public void Line(int x0, int y0, int x1, int y1, Color c)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                Set(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// 32x16 isometric diamond whose top row starts at y = top.
        public void Diamond(int top, Func<int, int, Color> col)
        {
            for (int r = 0; r < 16; r++)
            {
                int hw = r < 8 ? 2 * (r + 1) : 2 * (16 - r);
                for (int x = 16 - hw; x < 16 + hw; x++) Set(x, top + r, col(x, r));
            }
        }

        /// pivot given in pixels from the top-left.
        public Sprite ToSprite(float pivotX, float pivotY)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(pivotX / W, 1f - pivotY / H), PPU, 0, SpriteMeshType.FullRect);
        }
    }

    // Shared palette
    static Color Bark => Hex("#7a4b2a");
    static Color BarkD => Hex("#55331b");
    static Color StoneD => Hex("#5e6670");
    static Color StoneM => Hex("#8a929c");
    static Color StoneL => Hex("#c1c8cf");
    static Color RopeC => Hex("#c8a36a");

    // ------------------------------------------------------------------ ground
    public static Sprite Water(int frame, bool fresh = false) => Get($"water_{(fresh ? "fresh" : "sea")}_{frame}", () =>
    {
        var c = new Canvas(32, 16);
        Color hi = fresh ? Hex("#bdf0f5") : Hex("#7cc4f0");
        Color baseCol = fresh ? FreshWaterColor : WaterColor;
        c.Diamond(0, (x, r) => baseCol);
        var rnd = new System.Random(11 + frame * 7);
        for (int k = 0; k < 3; k++)
        {
            int x = rnd.Next(6, 24), y = rnd.Next(3, 13);
            for (int i = 0; i < 3; i++) if (InDiamond(x + i, y)) c.Set(x + i, y, hi);
        }
        return c.ToSprite(16, 8);
    });

    /// Land tile. height raises the top and makes the cliff side taller.
    static string BiomeKey(Biome b) => b switch { Biome.Jungle => "jungle_", Biome.Volcanic => "volcanic_", Biome.Tribal => "tribal_", _ => "" };

    public static Sprite Land(bool grass, int variant, int height, Biome biome = Biome.Tropical) =>
        Get($"{BiomeKey(biome)}{(grass ? "grass" : "sand")}_tile_{variant}_h{height}", () =>
    {
        int depth = 6 + height * HeightPx;
        var c = new Canvas(32, 16 + depth);
        string[] p = (biome, grass) switch
        {
            //                       top        dark       lite       left       right      lip
            (Biome.Jungle, true) => new[] { "#3f8f3a", "#347a30", "#5aa848", "#6b4a2b", "#523720", "#2c6a2a" },
            (Biome.Jungle, false) => new[] { "#dcc98e", "#c9b47a", "#ead9a8", "#b89a5e", "#9a7e48", "#c9b47a" },
            (Biome.Volcanic, true) => new[] { "#6b6460", "#5a5450", "#857d78", "#4a3f3a", "#3a302c", "#4f4844" },
            (Biome.Volcanic, false) => new[] { "#3d3a3a", "#333030", "#514c4c", "#2e2b2b", "#242121", "#333030" },
            (Biome.Tribal, true) => new[] { "#7fb04a", "#6c9a3c", "#9cc860", "#8a5a35", "#6b4327", "#5a8a34" },
            (Biome.Tribal, false) => new[] { "#f0d9a0", "#dcc286", "#faecc4", "#cfae70", "#ae8e54", "#dcc286" },
            (_, true) => new[] { "#5fae4a", "#4f9a3f", "#7cc95c", "#8a5a35", "#6b4327", "#3f7f33" },
            _ => new[] { "#e8d29a", "#d6bb7e", "#f5e6bb", "#c9a86a", "#a8884f", "#d6bb7e" },
        };
        Color top = Hex(p[0]), dark = Hex(p[1]), lite = Hex(p[2]), left = Hex(p[3]), right = Hex(p[4]), lip = Hex(p[5]);
        grass &= biome != Biome.Volcanic; // ash has no grass blades
        Color strataL = Color.Lerp(left, Color.black, 0.18f), strataR = Color.Lerp(right, Color.black, 0.18f);

        for (int s = depth; s >= 1; s--)
        {
            int ss = s;
            bool strata = ss > 1 && ss % HeightPx == 0;
            c.Diamond(ss, (x, r) => ss == 1 ? lip : x < 16 ? (strata ? strataL : left) : (strata ? strataR : right));
        }
        var rnd = new System.Random(variant * 131 + (grass ? 7 : 3) + height * 17);
        c.Diamond(0, (x, r) => { int v = rnd.Next(100); return v < 9 ? dark : v < 14 ? lite : top; });
        if (grass)
            for (int k = 0; k < 5; k++)
            {
                int x = rnd.Next(6, 26), y = rnd.Next(3, 12);
                if (InDiamond(x, y) && InDiamond(x, y - 1)) { c.Set(x, y, dark); c.Set(x, y - 1, lite); }
            }
        return c.ToSprite(16, 8);
    });

    public static Sprite Cursor() => Get("cursor", () =>
    {
        var c = new Canvas(32, 16);
        var col = new Color(1f, 0.92f, 0.35f, 0.9f);
        for (int r = 0; r < 16; r++)
        {
            int hw = r < 8 ? 2 * (r + 1) : 2 * (16 - r);
            c.Set(16 - hw, r, col); c.Set(17 - hw, r, col);
            c.Set(15 + hw, r, col); c.Set(14 + hw, r, col);
        }
        return c.ToSprite(16, 8);
    });

    /// One game pixel, used by particles.
    public static Sprite Dot() => Get("dot", () =>
    {
        var c = new Canvas(1, 1);
        c.Set(0, 0, Color.white);
        return c.ToSprite(0.5f, 0.5f);
    });

    // ------------------------------------------------------------------ objects
    static void Trunk(Canvas c)
    {
        c.Ellipse(16, 42.5f, 10, 3, Shadow);
        c.Rect(14, 24, 4, 19, Bark); c.Rect(17, 24, 1, 19, BarkD);
        c.Rect(12, 41, 8, 2, Bark); c.Rect(18, 41, 2, 2, BarkD);
    }

    public static Sprite Tree(Biome biome = Biome.Tropical) => biome switch
    {
        Biome.Volcanic => DeadTree(),
        Biome.Jungle => LeafyTree("tree_jungle", "#1f4d26", "#2f6e33", "#4f9a45"),
        _ => LeafyTree("tree", "#2c6431", "#3e8a3e", "#6bb85c"),
    };

    static Sprite LeafyTree(string key, string dark, string mid, string light) => Get(key, () =>
    {
        var c = new Canvas(32, 46);
        Trunk(c);
        Color d = Hex(dark), m = Hex(mid), l = Hex(light);
        c.Ball(16, 21, 12, 8, d, d, m);
        c.Ball(10, 16, 8, 7, d, m, l);
        c.Ball(22, 15, 8, 7, d, m, l);
        c.Ball(16, 9, 9, 8, d, m, l);
        return c.ToSprite(16, 42);
    });

    static Sprite DeadTree() => Get("tree_dead", () =>
    {
        var c = new Canvas(32, 46);
        Color w = Hex("#5a4a40"), wd = Hex("#3e322b");
        c.Ellipse(16, 42.5f, 9, 2.5f, Shadow);
        c.Rect(14, 18, 4, 25, w); c.Rect(17, 18, 1, 25, wd); c.Rect(12, 41, 8, 2, w);
        c.Line(15, 26, 6, 14, w); c.Line(15, 27, 6, 15, wd); c.Line(8, 16, 4, 12, w);
        c.Line(17, 22, 26, 10, w); c.Line(17, 23, 26, 11, wd); c.Line(23, 14, 28, 13, w);
        c.Line(16, 18, 15, 6, w); c.Line(15, 10, 11, 5, w); c.Line(16, 12, 20, 4, wd);
        return c.ToSprite(16, 42);
    });

    public static Sprite FruitTree(bool fruit, Biome biome = Biome.Tropical) =>
        biome == Biome.Jungle ? Palm(fruit) : RoundFruitTree(fruit);

    static Sprite Palm(bool fruit) => Get(fruit ? "palm" : "palm_empty", () =>
    {
        var c = new Canvas(32, 46);
        c.Ellipse(16, 42.5f, 8, 2.5f, Shadow);
        for (int y = 12; y <= 42; y++)
        {
            int x = 15 + Mathf.RoundToInt(Mathf.Sin(y * 0.11f) * 2f);
            c.Rect(x, y, 3, 1, y % 3 == 0 ? BarkD : Hex("#9a7048"));
        }
        Color fd = Hex("#2f6e33"), fm = Hex("#4f9a45"), fl = Hex("#7cc95c");
        int[,] tips = { { 2, 16 }, { 5, 7 }, { 13, 2 }, { 22, 3 }, { 29, 9 }, { 30, 17 } };
        for (int i = 0; i < tips.GetLength(0); i++)
        {
            c.Line(16, 12, tips[i, 0], tips[i, 1], fd);
            c.Line(16, 11, tips[i, 0], tips[i, 1] - 1, i % 2 == 0 ? fm : fl);
            c.Line(16, 13, tips[i, 0], tips[i, 1] + 1, fm);
        }
        if (fruit)
        {
            Color y1 = Hex("#c9a822"), y2 = Hex("#f2d43a"), y3 = Hex("#fff19a");
            c.Ball(14, 16, 2, 3, y1, y2, y3); c.Ball(17, 17, 2, 3, y1, y2, y3); c.Ball(20, 16, 2, 3, y1, y2, y3);
        }
        return c.ToSprite(16, 42);
    });

    static Sprite RoundFruitTree(bool fruit) => Get(fruit ? "fruit_tree" : "fruit_tree_empty", () =>
    {
        var c = new Canvas(32, 46);
        Trunk(c);
        Color d = Hex("#2f5e2a"), m = Hex("#4c8f37"), l = Hex("#86c35a");
        c.Ball(16, 18, 14, 11, d, m, l);
        c.Ball(12, 12, 6, 5, m, m, l);
        if (fruit)
        {
            int[,] pos = { { 9, 16 }, { 15, 10 }, { 21, 13 }, { 19, 21 }, { 11, 23 }, { 24, 20 }, { 14, 17 } };
            for (int i = 0; i < pos.GetLength(0); i++)
                c.Ball(pos[i, 0], pos[i, 1], 2, 2, Hex("#c4501b"), Hex("#f08a24"), Hex("#ffd27a"));
        }
        return c.ToSprite(16, 42);
    });

    public static Sprite Stump() => Get("stump", () =>
    {
        var c = new Canvas(16, 12);
        c.Ellipse(8, 9.5f, 7, 2, Shadow);
        c.Ellipse(8, 7, 5, 3, BarkD);
        c.Rect(3, 4, 10, 3, Bark);
        c.Ellipse(8, 4, 5, 2, Hex("#c79a64"));
        c.Ellipse(8, 4, 2, 1, Hex("#a87a48"));
        return c.ToSprite(8, 9);
    });

    public static Sprite Bush(bool berries) => Get(berries ? "bush" : "bush_empty", () =>
    {
        var c = new Canvas(24, 18);
        c.Ellipse(12, 15.5f, 10, 2.5f, Shadow);
        Color d = Hex("#2c6431"), m = Hex("#3e8a3e"), l = Hex("#5fae4a");
        c.Ball(7, 11, 6, 5, d, m, l);
        c.Ball(17, 11, 6, 5, d, m, l);
        c.Ball(12, 8, 7, 6, d, m, l);
        if (berries)
        {
            int[,] pos = { { 6, 10 }, { 11, 6 }, { 16, 9 }, { 9, 13 }, { 15, 13 }, { 19, 11 } };
            for (int i = 0; i < pos.GetLength(0); i++)
            {
                c.Rect(pos[i, 0], pos[i, 1], 2, 2, Hex("#b8283f"));
                c.Set(pos[i, 0], pos[i, 1], Hex("#ff8aa0"));
            }
        }
        return c.ToSprite(12, 15);
    });

    public static Sprite Grass() => Get("grass", () =>
    {
        var c = new Canvas(14, 13);
        int[] xs = { 2, 4, 5, 7, 9, 10, 12 }, hs = { 6, 9, 7, 11, 8, 10, 5 }, lean = { -1, -1, 0, 0, 1, 1, 1 };
        Color g1 = Hex("#4a9a3c"), g2 = Hex("#7cc95c");
        c.Ellipse(7, 12, 6, 1.5f, Shadow);
        for (int i = 0; i < xs.Length; i++) c.Line(xs[i], 12, xs[i] + lean[i], 12 - hs[i], i % 2 == 0 ? g1 : g2);
        return c.ToSprite(7, 12);
    });

    public static Sprite Stick() => Get("stick", () =>
    {
        var c = new Canvas(18, 10);
        c.Ellipse(9, 8, 8, 1.5f, Shadow);
        c.Line(2, 7, 15, 3, Hex("#8a5a35"));
        c.Line(2, 8, 15, 4, Hex("#5a341c"));
        c.Line(9, 5, 11, 1, Hex("#8a5a35"));
        return c.ToSprite(9, 7);
    });

    public static Sprite Rock() => Get("rock", () =>
    {
        var c = new Canvas(16, 12);
        c.Ellipse(8, 10, 7, 2, Shadow);
        c.Ball(8, 6.5f, 6.5f, 4.5f, StoneD, StoneM, StoneL);
        return c.ToSprite(8, 9);
    });

    public static Sprite Campfire(int frame) => Get("campfire_" + frame, () =>
    {
        var c = new Canvas(20, 26);
        c.Ellipse(10, 22.5f, 9, 2.5f, Shadow);
        Color log = Hex("#6b3f22"), logD = Hex("#4a2a14");
        c.Line(3, 21, 16, 16, log); c.Line(3, 22, 16, 17, logD);
        c.Line(3, 16, 16, 21, log); c.Line(3, 17, 16, 22, logD);
        int[,] stones = { { 2, 21 }, { 5, 23 }, { 10, 24 }, { 15, 23 }, { 18, 21 } };
        for (int i = 0; i < stones.GetLength(0); i++)
            c.Ball(stones[i, 0], stones[i, 1], 2.2f, 1.6f, Hex("#4f565e"), Hex("#7d858e"), Hex("#aab2ba"));
        float h = frame == 0 ? 9f : 10.5f, sx = frame == 0 ? 10f : 9.5f;
        c.Ellipse(sx, 18 - h / 2, 4.5f, h / 2 + 1, Hex("#e8571c"));
        c.Ellipse(sx + 0.3f, 19.5f - h / 2 + 1, 3f, h / 2 - 0.5f, Hex("#ffa52a"));
        c.Ellipse(10, 17, 1.6f, 2.5f, Hex("#fff0a0"));
        c.Set(frame == 0 ? 6 : 13, frame == 0 ? 4 : 3, Hex("#ffd23c"));
        return c.ToSprite(10, 21);
    });

    public static Sprite Shelter() => Get("shelter", () =>
    {
        var c = new Canvas(40, 34);
        c.Ellipse(20, 30, 18, 4, Shadow);
        Color leafD = Hex("#5b7a2e"), leaf = Hex("#7a9a3a"), leafL = Hex("#a3bf55"), hole = Hex("#2a1a10");
        for (int y = 6; y <= 30; y++)
        {
            int hw = Mathf.RoundToInt((y - 6) * 0.72f);
            for (int x = 20 - hw; x <= 20 + hw; x++)
            {
                Color col = (y % 3 == 0) ? leafD : (x < 20 ? leafL : leaf);
                c.Set(x, y, col);
            }
        }
        for (int y = 17; y <= 30; y++)
        {
            int hw = Mathf.RoundToInt((y - 17) * 0.45f);
            for (int x = 20 - hw; x <= 20 + hw; x++) c.Set(x, y, hole);
        }
        c.Line(20, 2, 20, 8, Bark); c.Line(16, 3, 24, 9, BarkD);
        c.Rect(19, 12, 3, 1, RopeC);
        return c.ToSprite(20, 30);
    });

    /// Raft by upgrade level: 1 plain logs, 2 + bamboo rails, 3 + big striped woven sail, 4+ obsidian prow.
    public static Sprite Raft(int level = 1)
    {
        int lv = Mathf.Clamp(level, 1, 4);
        return Get(lv == 1 ? "raft" : $"raft_lv{lv}", () =>
        {
            var c = new Canvas(40, 36);
            c.Ellipse(18, 31, 16, 3, Shadow);
            for (int i = 0; i < 5; i++)
            {
                int x = 6 + i * 5;
                c.Rect(x, 19, 4, 12, Bark);
                c.Rect(x + 3, 19, 1, 12, BarkD);
                c.Ellipse(x + 2f, 19.5f, 2, 1.2f, Hex("#c79a64"));
            }
            c.Rect(5, 22, 26, 1, RopeC); c.Rect(5, 28, 26, 1, RopeC);
            if (lv >= 2) // bamboo rails
            {
                Color g = Hex("#6aa84f"), gd = Hex("#4a7f35");
                c.Rect(4, 18, 28, 1, g); c.Rect(4, 31, 28, 1, gd);
                for (int x = 4; x <= 31; x += 9) c.Rect(x, 16, 1, 3, g);
                c.Rect(4, 16, 28, 1, Hex("#a5d17a"));
            }
            if (lv >= 4) // obsidian prow
            {
                Color o1 = Hex("#120e18"), o2 = Hex("#2e2438"), o3 = Hex("#6a5a88");
                for (int y = 20; y <= 30; y++)
                {
                    int w = 5 - Mathf.Abs(y - 25) / 2;
                    for (int x = 31; x < 31 + w; x++) c.Set(x, y, x == 31 ? o3 : (y > 25 ? o1 : o2));
                }
            }
            c.Rect(17, 3, 2, 20, BarkD);
            for (int y = 4; y <= 18; y++) // sail
            {
                int w = Mathf.RoundToInt((y - 4) * (lv >= 3 ? 1.05f : 0.85f));
                for (int x = 19; x <= 19 + w; x++)
                {
                    Color col = x == 19 + w ? Hex("#cbbf9c") : Hex("#f0e6c8");
                    if (lv >= 3 && (y / 3) % 2 == 0) col = Hex("#d9534f"); // woven tribal sail with red stripes
                    c.Set(x, y, col);
                }
            }
            return c.ToSprite(18, 30);
        });
    }

    public static Sprite Bamboo() => Get("bamboo", () =>
    {
        var c = new Canvas(16, 34);
        c.Ellipse(8, 31.5f, 6, 1.5f, Shadow);
        Color g = Hex("#6aa84f"), gd = Hex("#4a7f35"), seg = Hex("#a5d17a"), leaf = Hex("#5a9a40");
        int[] xs = { 3, 7, 11 }, hs = { 26, 30, 22 };
        for (int i = 0; i < xs.Length; i++)
        {
            int topY = 31 - hs[i];
            c.Rect(xs[i], topY, 2, hs[i], g);
            c.Rect(xs[i] + 1, topY, 1, hs[i], gd);
            for (int y = topY + 4; y < 31; y += 6) c.Rect(xs[i], y, 2, 1, seg);
            c.Line(xs[i] + 1, topY + 2, xs[i] + 4, topY - 1, leaf);
            c.Line(xs[i], topY + 6, xs[i] - 3, topY + 4, leaf);
        }
        return c.ToSprite(8, 31);
    });

    public static Sprite Obsidian() => Get("obsidian", () =>
    {
        var c = new Canvas(16, 13);
        c.Ellipse(8, 11, 7, 2, Shadow);
        c.Ball(8, 7, 6, 4.5f, Hex("#120e18"), Hex("#2e2438"), Hex("#6a5a88"));
        c.Line(5, 4, 7, 6, Hex("#9a8ac0")); c.Set(10, 5, Hex("#c8bce8"));
        return c.ToSprite(8, 10);
    });

    public static Sprite Enemy(EnemyKind kind, int frame) => kind switch
    {
        EnemyKind.Snake => Snake(frame),
        EnemyKind.Boar => Boar(frame),
        _ => Crab(frame),
    };

    static Sprite Snake(int frame) => Get("snake_" + frame, () =>
    {
        var c = new Canvas(22, 11);
        c.Ellipse(11, 9, 9, 1.5f, Shadow);
        Color g = Hex("#4a8f2a"), gd = Hex("#2f5f1a"), belly = Hex("#a5c860");
        int headY = 5;
        for (int x = 2; x <= 16; x++)
        {
            int y = 6 + Mathf.RoundToInt(Mathf.Sin((x + frame * 3) * 0.6f) * 1.5f);
            c.Set(x, y - 1, g); c.Set(x, y, x % 3 == 0 ? gd : g); c.Set(x, y + 1, belly);
            if (x == 16) headY = y;
        }
        c.Ball(18, headY, 2.6f, 2f, gd, g, Hex("#7cc95c"));
        c.Set(19, headY - 1, Color.black);
        c.Set(21, headY + 1, Hex("#d8323c")); c.Set(21 - frame, headY + 2, Hex("#d8323c"));
        return c.ToSprite(11, 9);
    });

    static Sprite Boar(int frame) => Get("boar_" + frame, () =>
    {
        var c = new Canvas(24, 17);
        c.Ellipse(12, 15.5f, 10, 2, Shadow);
        Color d = Hex("#3a2214"), m = Hex("#6b4226"), l = Hex("#8a5a36");
        int[] legs = { 6, 9, 15, 18 };
        for (int i = 0; i < legs.Length; i++)
        {
            int lift = (i + frame) % 2;
            c.Rect(legs[i], 11, 2, 4 - lift, d);
        }
        c.Ball(11, 9, 9, 5, d, m, l);
        c.Line(4, 5, 16, 4, d);
        c.Ball(19, 9, 4, 3.5f, d, m, l);
        c.Rect(22, 9, 2, 2, Hex("#d98a8a"));
        c.Set(21, 11, Color.white); c.Set(22, 12, Color.white);
        c.Set(19, 7, Color.black);
        c.Set(17, 5, d); c.Set(18, 5, d);
        return c.ToSprite(12, 15);
    });

    // ------------------------------------------------------------------ sea
    /// Little white wave glint that floats on the open sea.
    public static Sprite Wave() => Get("wave", () =>
    {
        var c = new Canvas(8, 3);
        var w = new Color(1f, 1f, 1f, 0.7f);
        c.Set(0, 2, w); c.Set(1, 1, w); c.Set(2, 1, w); c.Set(3, 2, w); c.Set(4, 2, w); c.Set(5, 1, w); c.Set(6, 1, w); c.Set(7, 2, w);
        return c.ToSprite(4, 1.5f);
    });

    public static Sprite SeaCreature(SeaKind kind, int frame) => kind == SeaKind.Shark ? Shark(frame) : Jellyfish(frame);

    static Sprite Shark(int frame) => Get("shark_" + frame, () =>
    {
        var c = new Canvas(26, 16);
        c.Ellipse(13, 12, 11, 3, new Color(0.05f, 0.15f, 0.3f, 0.45f));   // body under the water
        Color fd = Hex("#3c4a58"), fm = Hex("#5f7080"), fl = Hex("#93a4b4");
        for (int y = 1; y <= 11; y++)                                      // dorsal fin
        {
            int x0 = 11 - (y - 1) / 3, x1 = 11 + (y - 1) * 2 / 3;
            for (int x = x0; x <= x1; x++) c.Set(x, y, x == x0 ? fl : x == x1 ? fd : fm);
        }
        var foam = new Color(1f, 1f, 1f, 0.85f);                           // ripple at the waterline
        int off = frame == 0 ? 0 : 1;
        c.Line(5 + off, 12, 9 + off, 11, foam); c.Line(15 - off, 11, 20 - off, 12, foam);
        c.Set(3 + off, 13, foam); c.Set(22 - off, 13, foam);
        return c.ToSprite(13, 12);
    });

    static Sprite Jellyfish(int frame) => Get("jellyfish_" + frame, () =>
    {
        var c = new Canvas(14, 16);
        c.Ellipse(7, 14, 5, 1.2f, new Color(0.05f, 0.15f, 0.3f, 0.35f));
        Color d = new Color(0.75f, 0.35f, 0.7f, 0.85f), m = new Color(0.95f, 0.55f, 0.9f, 0.85f), l = new Color(1f, 0.85f, 1f, 0.9f);
        float squash = frame == 0 ? 0f : 0.8f;
        c.Ball(7, 5 + squash * 0.5f, 5f + squash * 0.5f, 3.6f - squash * 0.5f, d, m, l);
        var t = new Color(0.95f, 0.6f, 0.9f, 0.7f);
        for (int i = 0; i < 4; i++)
        {
            int x = 3 + i * 2 + (i > 1 ? 1 : 0);
            for (int y = 8; y <= 13; y++) c.Set(x + ((y + i + frame) % 3 == 0 ? 1 : 0), y, t);
        }
        return c.ToSprite(7, 13);
    });

    /// Night crab. frame 0/1 = walk cycle.
    static Sprite Crab(int frame) => Get("crab_" + frame, () =>
    {
        var c = new Canvas(18, 13);
        c.Ellipse(9, 11, 7, 1.5f, Shadow);
        Color d = Hex("#9a2b18"), m = Hex("#e0522c"), l = Hex("#ff8a5c");
        int lift = frame == 0 ? 0 : 1;
        for (int i = 0; i < 3; i++)
        {
            int off = (i + frame) % 2;
            c.Line(5 - i, 8, 3 - i, 10 - off, d);
            c.Line(12 + i, 8, 14 + i, 10 - off, d);
        }
        c.Ball(9, 7, 5.5f, 3.5f, d, m, l);
        c.Ball(2.5f, 5 - lift, 2.2f, 2, d, m, l);
        c.Ball(15.5f, 5 - (1 - lift), 2.2f, 2, d, m, l);
        c.Rect(7, 2, 1, 2, d); c.Rect(11, 2, 1, 2, d);
        c.Set(7, 2, Color.black); c.Set(11, 2, Color.black);
        return c.ToSprite(9, 11);
    });

    /// Draws a 14x24 person: legs (or a skirt), torso, arms, head and hair.
    static void Person(Canvas c, int dir, int frame, Color skin, Color skinD, Color hair, Color top, Color topD,
                       Color legs, Color legsD, bool skirt, bool bareChest)
    {
        Color shoe = Hex("#2b2b2b");
        c.Ellipse(7, 22.5f, 5, 1.5f, Shadow);
        int lL = frame == 1 ? 1 : 0, lR = frame == 2 ? 1 : 0;
        c.Rect(4, 16, 2, 5 - lL, skirt ? skinD : legs); c.Rect(4, 21 - lL, 2, 1, shoe);
        c.Rect(8, 16, 2, 5 - lR, skirt ? skinD : legsD); c.Rect(8, 21 - lR, 2, 1, shoe);
        if (bareChest)
        {
            c.Rect(3, 10, 8, 7, skin); c.Rect(9, 10, 2, 7, skinD);
            if (dir == 0) { c.Set(4, 10, Color.white); c.Set(6, 11, Color.white); c.Set(7, 11, Color.white); c.Set(9, 10, Color.white); } // bone necklace
        }
        else { c.Rect(3, 10, 8, 7, top); c.Rect(9, 10, 2, 7, topD); }
        if (skirt) // grass skirt
            for (int x = 3; x <= 10; x++) c.Rect(x, 15, 1, 4 + (x % 2), x % 2 == 0 ? legs : legsD);
        c.Rect(2, 11 + (frame == 2 ? 1 : 0), 1, 5, skin);
        c.Rect(11, 11 + (frame == 1 ? 1 : 0), 1, 5, skinD);
        c.Rect(4, 3, 6, 7, skin); c.Rect(3, 4, 8, 5, skin); c.Rect(10, 4, 1, 5, skinD);
        if (dir == 0)
        {
            c.Rect(4, 2, 6, 2, hair); c.Rect(3, 3, 8, 2, hair); c.Set(3, 5, hair); c.Set(10, 5, hair);
            c.Set(5, 6, Hex("#2a1a10")); c.Set(8, 6, Hex("#2a1a10"));
        }
        else
        {
            c.Rect(4, 2, 6, 2, hair); c.Rect(3, 3, 8, 6, hair); c.Rect(4, 9, 6, 1, hair);
        }
    }

    static void TribalHeadband(Canvas c)
    {
        c.Rect(3, 4, 8, 1, Hex("#c0392b"));
        c.Set(5, 4, Hex("#f1c40f")); c.Set(8, 4, Hex("#f1c40f"));
        c.Rect(10, 0, 1, 4, Color.white); c.Set(10, 0, Hex("#2a1a10")); c.Set(11, 1, Color.white); // feather
    }

    /// The player. dir 0 = facing camera, 1 = facing away. frame 0 = idle, 1/2 = walk.
    /// hat / outfit change the look (null = nothing worn).
    public static Sprite Player(int dir, int frame, Item? hat = null, Item? outfit = null)
    {
        string key = $"player_{(dir == 0 ? "front" : "back")}_{frame}";
        if (hat != null) key += "_" + hat.Value.ToString().ToLowerInvariant();
        if (outfit != null) key += "_" + outfit.Value.ToString().ToLowerInvariant();
        return Get(key, () =>
        {
            var c = new Canvas(14, 24);
            Color shirt = Hex("#d9534f"), shirtD = Hex("#a83a36");
            if (outfit == Item.LeafTunic) { shirt = Hex("#4f9a3f"); shirtD = Hex("#3a7a30"); }
            Person(c, dir, frame, Hex("#f1c27d"), Hex("#d9a066"), Hex("#4a2c1a"), shirt, shirtD,
                   Hex("#3b4a7a"), Hex("#2c3860"), false, false);

            if (outfit == Item.LeafTunic)
            {
                Color vein = Hex("#7cc95c");
                c.Line(4, 11, 6, 14, vein); c.Line(9, 11, 7, 14, vein);
                for (int x = 3; x <= 10; x += 2) c.Set(x, 17, shirtD); // jagged leafy hem
            }
            else if (outfit == Item.HideVest)
            {
                Color v1 = Hex("#8a5a36"), v2 = Hex("#6b4226");
                if (dir == 0) { c.Rect(3, 10, 3, 7, v1); c.Rect(8, 10, 3, 7, v2); c.Set(4, 12, Hex("#c79a64")); c.Set(9, 14, Hex("#c79a64")); }
                else { c.Rect(3, 10, 8, 7, v1); c.Rect(9, 10, 2, 7, v2); c.Line(4, 11, 9, 15, v2); }
            }

            if (hat == Item.StrawHat)
            {
                Color s1 = Hex("#e8c66a"), s2 = Hex("#c9a24a");
                c.Rect(1, 3, 12, 1, s1); c.Rect(2, 4, 10, 1, new Color(0f, 0f, 0f, 0.25f));
                c.Rect(4, 0, 6, 3, s1); c.Rect(8, 0, 2, 3, s2);
                if (dir == 0) c.Rect(4, 2, 6, 1, Hex("#b5452c"));
            }
            else if (hat == Item.TribalHeadband) TribalHeadband(c);
            return c.ToSprite(7, 22);
        });
    }

    /// Tribe members. look: 0 weaver, 1 elder, 2 trader, 3+ villagers.
    public static Sprite Villager(int look, int frame) => Get($"villager_{look}_{frame}", () =>
    {
        var c = new Canvas(16, 26);
        var inner = new Canvas(14, 24);
        Color skin = Hex("#a86a3c"), skinD = Hex("#86522c"), black = Hex("#1a1010");
        Color grass = Hex("#c9b45a"), grassD = Hex("#a08c3a");
        switch (look)
        {
            case 0: // weaver: purple wrap, red headband
                Person(inner, 0, frame, skin, skinD, black, Hex("#7a3f8a"), Hex("#5a2a68"), grass, grassD, true, false);
                inner.Rect(3, 4, 8, 1, Hex("#c0392b"));
                break;
            case 1: // elder: white hair, long brown robe
                Person(inner, 0, frame, skin, skinD, Hex("#e8e8e8"), Hex("#6b4226"), Hex("#4a2c1a"), Hex("#6b4226"), Hex("#4a2c1a"), true, false);
                break;
            case 2: // trader: blue headband, bone necklace, satchel
                Person(inner, 0, frame, skin, skinD, black, skin, skinD, grass, grassD, true, true);
                inner.Rect(3, 4, 8, 1, Hex("#2e86c1"));
                inner.Rect(9, 13, 3, 3, Hex("#8a5a36"));
                break;
            default: // villager: orange wrap
                Person(inner, 0, frame, skin, skinD, black, Hex("#d9822b"), Hex("#b0661c"), grass, grassD, true, false);
                break;
        }
        // copy into a slightly wider canvas so the elder's staff fits
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 14; x++)
            {
                var col = inner.GetPixel(x, y);
                if (col.a > 0f) c.Set(x, y + 2, col);
            }
        if (look == 1) { c.Rect(14, 6, 1, 19, Hex("#8a5a36")); c.Rect(13, 5, 3, 2, Hex("#c79a64")); }
        return c.ToSprite(7, 24);
    });

    public static Sprite Hut() => Get("hut", () =>
    {
        var c = new Canvas(44, 42);
        c.Ellipse(22, 38, 20, 4, Shadow);
        Color plank = Hex("#9a7048"), plankD = Hex("#7a5434");
        c.Rect(8, 22, 28, 16, plank);
        for (int x = 8; x < 36; x += 4) c.Rect(x, 22, 1, 16, plankD);
        c.Rect(30, 22, 6, 16, plankD);
        c.Rect(19, 27, 7, 11, Hex("#2a1a10"));
        Color t1 = Hex("#c9a24a"), t2 = Hex("#a8843a"), t3 = Hex("#e0c070");
        for (int y = 3; y <= 25; y++)
        {
            int hw = Mathf.RoundToInt((y - 3) * 0.9f) + 2;
            for (int x = 22 - hw; x <= 22 + hw; x++)
                c.Set(x, y, y % 3 == 0 ? t2 : (x < 22 ? t3 : t1));
        }
        c.Rect(21, 0, 2, 4, Hex("#7a4b2a"));
        return c.ToSprite(22, 38);
    });

    public static Sprite Totem() => Get("totem", () =>
    {
        var c = new Canvas(14, 36);
        c.Ellipse(7, 33, 5, 1.5f, Shadow);
        Color w = Hex("#8a5a36"), wd = Hex("#6b4226");
        c.Rect(4, 4, 6, 30, w); c.Rect(8, 4, 2, 30, wd);
        c.Rect(0, 5, 14, 2, Hex("#c0392b")); c.Rect(0, 5, 3, 1, Hex("#f1c40f")); c.Rect(11, 5, 3, 1, Hex("#f1c40f"));
        for (int f = 0; f < 2; f++)
        {
            int y = 10 + f * 11;
            c.Rect(5, y, 1, 2, Color.white); c.Rect(8, y, 1, 2, Color.white);
            c.Set(5, y + 1, Color.black); c.Set(8, y + 1, Color.black);
            c.Rect(5, y + 4, 4, 1, f == 0 ? Hex("#c0392b") : Hex("#2e86c1"));
            c.Rect(4, y + 7, 6, 1, wd);
        }
        c.Rect(5, 1, 4, 3, Hex("#2e86c1"));
        return c.ToSprite(7, 33);
    });

    // ------------------------------------------------------------------ icons
    public static Sprite Icon(Item it) => it switch
    {
        Item.Fiber => Grass(),
        Item.Stick => Stick(),
        Item.Stone => Rock(),
        _ => Get("icon_" + it.ToString().ToLowerInvariant(), () => DrawIcon(it)),
    };

    static Sprite DrawIcon(Item it)
    {
        var c = new Canvas(16, 16);
        Color leaf = Hex("#4c8f37");
        var steam = new Color(1f, 1f, 1f, 0.6f);
        switch (it)
        {
            case Item.Wood:
                c.Rect(1, 6, 12, 6, Bark); c.Rect(1, 6, 12, 1, Hex("#9a6a3e")); c.Rect(1, 11, 12, 1, BarkD);
                c.Ellipse(13, 9, 2.6f, 3.2f, Hex("#c79a64")); c.Set(13, 9, Hex("#a87a48"));
                break;
            case Item.Berry:
                Color b1 = Hex("#7a1426"), b2 = Hex("#b8283f"), b3 = Hex("#ff8aa0");
                c.Ball(5, 10, 3, 3, b1, b2, b3); c.Ball(11, 11, 3, 3, b1, b2, b3); c.Ball(8, 6, 3, 3, b1, b2, b3);
                c.Line(8, 3, 10, 1, leaf);
                break;
            case Item.Fruit:
                c.Ball(8, 9, 5.5f, 5.5f, Hex("#c4501b"), Hex("#f08a24"), Hex("#ffd27a"));
                c.Line(8, 3, 9, 1, Bark); c.Ellipse(11.5f, 2.5f, 2, 1, leaf);
                break;
            case Item.CookedFruit:
                c.Ball(8, 10, 5.5f, 5f, Hex("#6b3410"), Hex("#b5651d"), Hex("#e0a050"));
                c.Line(5, 4, 5, 1, steam); c.Line(10, 3, 10, 0, steam);
                break;
            case Item.Rope:
                c.Ring(8, 8, 7, 1.6f, RopeC); c.Ring(8, 8, 5, 1.6f, Hex("#a88450")); c.Ring(8, 8, 3, 1.6f, RopeC);
                break;
            case Item.StoneAxe:
                c.Line(3, 14, 11, 4, Bark); c.Line(4, 14, 12, 4, BarkD);
                c.Ball(12, 4, 4, 3, StoneD, StoneM, StoneL);
                c.Rect(10, 4, 2, 2, RopeC);
                break;
            case Item.Spear:
                c.Line(1, 15, 12, 4, Bark); c.Line(2, 15, 13, 4, BarkD);
                c.Line(12, 4, 15, 0, StoneM); c.Line(13, 4, 15, 1, StoneD); c.Line(11, 3, 14, 0, StoneL);
                c.Rect(10, 5, 2, 2, RopeC);
                break;
            case Item.StoneKnife:
                c.Rect(2, 10, 5, 3, Bark); c.Rect(2, 12, 5, 1, BarkD);
                c.Line(6, 10, 14, 4, StoneM); c.Line(6, 11, 14, 5, StoneD); c.Line(7, 9, 13, 4, StoneL);
                break;
            case Item.RawFish:
            case Item.CookedFish:
                bool cooked = it == Item.CookedFish;
                Color fd = Hex(cooked ? "#7a4318" : "#4f7f9a"), fm = Hex(cooked ? "#c07a3a" : "#8fb8d0"), fl = Hex(cooked ? "#e8b070" : "#d6eef8");
                c.Ball(8.5f, 9, 5.5f, 3.2f, fd, fm, fl);
                c.Line(1, 6, 3, 9, fd); c.Line(1, 12, 3, 9, fd); c.Line(2, 7, 2, 11, fm);
                c.Set(12, 8, Color.black);
                if (cooked) { c.Line(6, 4, 6, 1, steam); c.Line(10, 4, 10, 1, steam); }
                break;
            case Item.CrabMeat:
                c.Ball(8, 9, 5, 4, Hex("#d97a7a"), Hex("#f4a0a0"), Hex("#ffd8d8"));
                c.Ball(12, 5, 2.5f, 2, Hex("#9a2b18"), Hex("#e0522c"), Hex("#ff8a5c"));
                break;
            case Item.RawMeat:
            case Item.CookedMeat:
                bool done = it == Item.CookedMeat;
                c.Ball(7, 9, 5.5f, 4.5f, Hex(done ? "#5a2a10" : "#9a2030"), Hex(done ? "#8a4a22" : "#d8505a"), Hex(done ? "#c07a40" : "#f4a0a0"));
                c.Line(11, 6, 15, 2, Hex("#f0ece0")); c.Set(15, 1, Hex("#f0ece0")); c.Set(14, 1, Hex("#f0ece0"));
                if (done) { c.Line(4, 4, 4, 1, steam); c.Line(8, 3, 8, 0, steam); }
                break;
            case Item.Bamboo:
                c.Line(4, 15, 7, 1, Hex("#6aa84f")); c.Line(5, 15, 8, 1, Hex("#4a7f35"));
                c.Line(9, 15, 12, 2, Hex("#6aa84f")); c.Line(10, 15, 13, 2, Hex("#4a7f35"));
                c.Set(6, 8, Hex("#a5d17a")); c.Set(11, 9, Hex("#a5d17a"));
                break;
            case Item.Obsidian:
                c.Line(3, 13, 8, 2, Hex("#2e2438")); c.Line(8, 2, 13, 12, Hex("#2e2438"));
                c.Ball(8, 9, 4.5f, 5, Hex("#120e18"), Hex("#2e2438"), Hex("#6a5a88"));
                c.Set(7, 5, Hex("#c8bce8"));
                break;
            case Item.WaterFlask:
                c.Rect(5, 3, 6, 12, Hex("#6aa84f")); c.Rect(9, 3, 2, 12, Hex("#4a7f35"));
                c.Rect(5, 8, 6, 1, Hex("#a5d17a")); c.Rect(6, 1, 4, 2, Hex("#c79a64"));
                c.Rect(4, 6, 1, 6, RopeC);
                break;
            case Item.ObsidianSpear:
                c.Line(1, 15, 12, 4, Bark); c.Line(2, 15, 13, 4, BarkD);
                c.Line(12, 4, 15, 0, Hex("#2e2438")); c.Line(13, 4, 15, 1, Hex("#120e18")); c.Line(11, 3, 14, 0, Hex("#6a5a88"));
                c.Rect(10, 5, 2, 2, RopeC);
                break;
            case Item.ObsidianAxe:
                c.Line(3, 14, 11, 4, Bark); c.Line(4, 14, 12, 4, BarkD);
                c.Ball(12, 4, 4, 3, Hex("#120e18"), Hex("#2e2438"), Hex("#6a5a88"));
                c.Rect(10, 4, 2, 2, RopeC);
                break;
            case Item.Hide:
                c.Ellipse(8, 8, 6, 6, Hex("#8a5a36")); c.Ellipse(8, 8, 4, 4, Hex("#a8744a"));
                c.Rect(1, 3, 2, 2, Hex("#8a5a36")); c.Rect(13, 3, 2, 2, Hex("#8a5a36"));
                c.Rect(1, 12, 2, 2, Hex("#8a5a36")); c.Rect(13, 12, 2, 2, Hex("#8a5a36"));
                break;
            case Item.Cloth:
                c.Rect(2, 4, 12, 9, Hex("#e0cfa0"));
                for (int y = 4; y < 13; y += 2) c.Rect(2, y, 12, 1, Hex("#c8b482"));
                for (int x = 3; x < 14; x += 3) c.Rect(x, 4, 1, 9, Hex("#d0bc8a"));
                c.Rect(2, 12, 12, 1, Hex("#c0392b"));
                break;
            case Item.StrawHat:
                c.Rect(1, 9, 14, 2, Hex("#e8c66a")); c.Rect(2, 11, 12, 1, Hex("#c9a24a"));
                c.Rect(4, 4, 8, 5, Hex("#e8c66a")); c.Rect(9, 4, 3, 5, Hex("#c9a24a")); c.Rect(4, 7, 8, 1, Hex("#b5452c"));
                break;
            case Item.TribalHeadband:
                c.Rect(1, 8, 14, 3, Hex("#c0392b")); c.Set(4, 9, Hex("#f1c40f")); c.Set(8, 9, Hex("#f1c40f")); c.Set(12, 9, Hex("#f1c40f"));
                c.Line(11, 8, 13, 1, Color.white); c.Line(12, 8, 14, 1, Hex("#dddddd")); c.Set(13, 1, Hex("#2a1a10"));
                break;
            case Item.LeafTunic:
                c.Rect(4, 3, 8, 11, Hex("#4f9a3f")); c.Rect(2, 3, 2, 5, Hex("#4f9a3f")); c.Rect(12, 3, 2, 5, Hex("#3a7a30"));
                c.Rect(6, 3, 4, 2, new Color(0, 0, 0, 0)); c.Line(5, 6, 7, 10, Hex("#7cc95c")); c.Line(10, 6, 8, 10, Hex("#7cc95c"));
                for (int x = 4; x < 12; x += 2) c.Set(x, 14, Hex("#3a7a30"));
                break;
            case Item.HideVest:
                c.Rect(3, 3, 4, 11, Hex("#8a5a36")); c.Rect(9, 3, 4, 11, Hex("#6b4226"));
                c.Set(5, 6, Hex("#c79a64")); c.Set(5, 9, Hex("#c79a64")); c.Set(10, 6, Hex("#c79a64")); c.Set(10, 9, Hex("#c79a64"));
                break;
            case Item.CookedCrab:
                c.Ball(8, 10, 5.5f, 3.5f, Hex("#8a2010"), Hex("#d0401c"), Hex("#ff7a4c"));
                c.Ball(2.5f, 7, 2, 2, Hex("#8a2010"), Hex("#d0401c"), Hex("#ff7a4c"));
                c.Ball(13.5f, 7, 2, 2, Hex("#8a2010"), Hex("#d0401c"), Hex("#ff7a4c"));
                c.Line(6, 4, 6, 1, steam); c.Line(10, 4, 10, 1, steam);
                break;
        }
        return c.ToSprite(8, 8);
    }
}
