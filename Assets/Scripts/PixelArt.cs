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
        for (int lv = 1; lv <= 5; lv++) Raft(lv);
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
    static string BiomeKey(Biome b) => b switch { Biome.Jungle => "jungle_", Biome.Volcanic => "volcanic_", Biome.Tribal => "tribal_", Biome.Mushroom => "mushroom_", Biome.Turtle => "turtle_", Biome.Sky => "sky_", Biome.Bubble => "bubble_", Biome.Hourglass => "hourglass_", Biome.Ice => "ice_", Biome.Sunken => "sunken_", _ => "" };

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
            (Biome.Sunken, true) => new[] { "#2f5a6a", "#244a58", "#4a7a8a", "#4a5a6a", "#3a4858", "#204452" },   // sea-floor weed
            (Biome.Sunken, false) => new[] { "#8a9ab0", "#7a8aa0", "#a8b8c8", "#6a7a90", "#566478", "#7a8aa0" }, // grey-blue silt
            (Biome.Ice, true) => new[] { "#eef4fa", "#d8e4f0", "#ffffff", "#a8b8c8", "#8898a8", "#c8d8e8" },   // snow
            (Biome.Ice, false) => new[] { "#a8d8f0", "#8ac4e4", "#d8f0ff", "#7aa8c8", "#5a88a8", "#8ac4e4" }, // sheet ice
            (Biome.Hourglass, true) => new[] { "#a8a050", "#928a42", "#c8c070", "#9a7a48", "#7a5e36", "#868038" },   // sun-dried grass
            (Biome.Hourglass, false) => new[] { "#ecd08a", "#dcbc72", "#f8e4ac", "#c8a060", "#a8844a", "#dcbc72" }, // golden dune sand
            (Biome.Bubble, true) => new[] { "#2f7a7a", "#266868", "#4fa0a0", "#5a4a5a", "#463a48", "#226060" },     // seagrass
            (Biome.Bubble, false) => new[] { "#e8c0b8", "#d4aaa2", "#f6dcd6", "#b88a84", "#9a6e68", "#d4aaa2" },  // pink coral sand
            (Biome.Sky, true) => new[] { "#7ad08a", "#62b874", "#a8eab4", "#c8c0d8", "#a8a0bc", "#58a868" },     // bright meadow on pale floating rock
            (Biome.Sky, false) => new[] { "#ece8f4", "#d8d2e6", "#ffffff", "#c8c0d8", "#a8a0bc", "#d8d2e6" },
            (Biome.Turtle, true) => new[] { "#6a8a3a", "#5a7630", "#8aaa52", "#5a6a34", "#46542a", "#4f6a2c" },  // olive shell plates
            (Biome.Turtle, false) => new[] { "#d8cc96", "#c4b682", "#ece2b4", "#a89c68", "#8a7e50", "#c4b682" },
            (Biome.Mushroom, true) => new[] { "#4a5a8a", "#3d4a78", "#6a7ab0", "#5a4a6a", "#463a54", "#3a4870" },  // blue-violet moss
            (Biome.Mushroom, false) => new[] { "#b8aec8", "#a498b8", "#d0c8dc", "#8a7e9e", "#6e6482", "#a498b8" }, // pale spore sand
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
        Biome.Mushroom => GiantMushroom(),
        Biome.Bubble => LeafyTree("tree_bubble", "#1f5a5a", "#2f8a7a", "#6ac8b0"), // sea-green trees: wood for fires down here
        Biome.Ice => SnowPine(),
        Biome.Sunken => LeafyTree("tree_sunken", "#143a40", "#1f5a5a", "#4a9a8a"),
        Biome.Hourglass => LeafyTree("tree_hourglass", "#8a5a1a", "#c8862a", "#f2c45a"), // amber autumn trees
        Biome.Sky => LeafyTree("tree_sky", "#c86a9a", "#f0a0c8", "#ffd8ea"), // blossom trees
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
        biome == Biome.Jungle ? Palm(fruit) : biome == Biome.Mushroom ? GiantMushroom() : biome == Biome.Bubble ? Kelp(true) : biome == Biome.Ice ? SnowPine() : RoundFruitTree(fruit);

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

    /// The boat by upgrade level: 1 log raft, 2 raft with bamboo rails, 3 canoe, 4 sailboat,
    /// 5+ two-masted ship with an obsidian prow. (Names: IslandGame.BoatName.)
    public static Sprite Raft(int level = 1)
    {
        int lv = Mathf.Clamp(level, 1, 5);
        return Get(lv == 1 ? "raft" : $"raft_lv{lv}", () =>
        {
            var c = new Canvas(40, 36);
            c.Ellipse(19, 31, 17, 3, Shadow);
            Color sail = Hex("#f0e6c8"), sailEdge = Hex("#cbbf9c"), red = Hex("#d9534f");
            if (lv <= 2)
            {
                for (int i = 0; i < 5; i++) // logs
                {
                    int x = 6 + i * 5;
                    c.Rect(x, 19, 4, 12, Bark);
                    c.Rect(x + 3, 19, 1, 12, BarkD);
                    c.Ellipse(x + 2f, 19.5f, 2, 1.2f, Hex("#c79a64"));
                }
                c.Rect(5, 22, 26, 1, RopeC); c.Rect(5, 28, 26, 1, RopeC);
                if (lv == 2) // bamboo rails
                {
                    Color g = Hex("#6aa84f"), gd = Hex("#4a7f35");
                    c.Rect(4, 18, 28, 1, g); c.Rect(4, 31, 28, 1, gd);
                    for (int x = 4; x <= 31; x += 9) c.Rect(x, 16, 1, 3, g);
                    c.Rect(4, 16, 28, 1, Hex("#a5d17a"));
                }
                c.Rect(17, 4, 2, 19, BarkD);
                for (int y = 5; y <= 18; y++) // small square-ish sail
                {
                    int w = Mathf.RoundToInt((y - 5) * 0.8f);
                    for (int x = 19; x <= 19 + w; x++) c.Set(x, y, x == 19 + w ? sailEdge : sail);
                }
                return c.ToSprite(18, 30);
            }

            // a real hull from level 3 on: rows narrow towards the keel, pointed bow on the right
            Color h1 = lv >= 4 ? Hex("#6b3f22") : Hex("#9a6a3a");   // hull
            Color h2 = lv >= 4 ? Hex("#4a2a16") : Hex("#74492a");   // shade
            Color rim = lv >= 4 ? Hex("#e8e0c8") : Hex("#c79a64");  // gunwale / painted stripe
            for (int y = 21; y <= 30; y++)
            {
                int inset = (y - 21) / 2;
                int x0 = 3 + inset + (y > 27 ? 1 : 0), x1 = 35 - inset - (y - 21) / 3;
                for (int x = x0; x <= x1; x++) c.Set(x, y, y >= 27 ? h2 : h1);
                c.Set(x1, y, h2);
            }
            c.Rect(2, 20, 34, 1, rim);          // gunwale
            for (int x = 36; x <= 38; x++) c.Set(x, 20 - (x - 36), rim); // bow sweeps up
            if (lv >= 4) c.Rect(5, 24, 28, 1, Hex("#2a6f9a")); // painted waterline stripe
            else for (int x = 8; x <= 30; x += 6) c.Set(x, 24, h2); // canoe ribs

            if (lv >= 5) // obsidian prow + second, smaller mast at the front
            {
                Color o1 = Hex("#120e18"), o2 = Hex("#2e2438"), o3 = Hex("#6a5a88");
                for (int y = 21; y <= 29; y++)
                    for (int x = 33 - (y - 21) / 3; x <= 36 - (y - 21) / 2; x++) c.Set(x, y, y < 24 ? o3 : (y > 26 ? o1 : o2));
                c.Rect(28, 9, 1, 12, BarkD);
                for (int y = 10; y <= 18; y++)
                    for (int x = 29; x <= 29 + (y - 10) * 0.7f; x++) c.Set(x, y, (y / 3) % 2 == 0 ? red : sail);
            }

            // main mast and sail
            int top = lv >= 4 ? 1 : 5;
            c.Rect(16, top, 2, 21 - top, BarkD);
            for (int y = top + 1; y <= 19; y++)
            {
                float slope = lv >= 4 ? 0.95f : 0.8f;
                int w = Mathf.RoundToInt((y - top - 1) * slope);
                for (int x = 18; x <= 18 + w; x++)
                {
                    Color col = x == 18 + w ? sailEdge : sail;
                    if (lv == 3 && (y / 3) % 2 == 0) col = red;                 // striped woven sail
                    if (lv >= 4 && y >= top + 8 && y <= top + 9) col = red;    // one bold band
                    c.Set(x, y, col);
                }
            }
            if (lv >= 4) { c.Rect(13, top, 3, 2, red); c.Set(12, top + 1, red); } // flag
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
        EnemyKind.Mimic => Mimic(frame),
        EnemyKind.SporeSpider => SporeSpider(frame),
        EnemyKind.ShroomKing => ShroomKing(frame),
        EnemyKind.Barnacle => Barnacle(frame),
        EnemyKind.HermitCrab => HermitCrab(frame),
        EnemyKind.StormCloud => StormCloud(frame),
        EnemyKind.ChimeBird => ChimeBird(frame),
        EnemyKind.LanternFish => LanternFish(frame),
        EnemyKind.StoneSquid => StoneSquid(frame),
        EnemyKind.WindupDoll => WindupDoll(frame),
        EnemyKind.TimeBeetle => TimeBeetle(frame),
        EnemyKind.Snowman => Snowman(frame),
        EnemyKind.Snowling => Snowling(frame),
        EnemyKind.FrostBat => FrostBat(frame),
        EnemyKind.Eel => Eel(frame),
        EnemyKind.DeepCrab => DeepCrab(frame),
        EnemyKind.Kraken => Kraken(frame),
        _ => Crab(frame),
    };

    // ------------------------------------------------------------------ Sunken City
    /// The drowned temple: broken columns, a domed roof covered in coral.
    public static Sprite SunkenTemple() => Get("sunken_temple", () =>
    {
        var c = new Canvas(72, 64);
        c.Ellipse(36, 60, 32, 4, Shadow);
        Color st = Hex("#9aa8b8"), stD = Hex("#6a7888"), moss = Hex("#3f8a6a");
        c.Rect(6, 52, 60, 8, st); c.Rect(6, 52, 60, 1, Hex("#c8d0dc"));         // steps
        for (int i = 0; i < 5; i++)
        {
            int x = 10 + i * 12, top = i == 3 ? 34 : 24;                          // one column snapped
            c.Rect(x, top, 6, 52 - top, st); c.Rect(x + 4, top, 2, 52 - top, stD);
            c.Rect(x - 1, top - 2, 8, 2, st);
        }
        for (int y = 6; y <= 22; y++) { int hw = 28 - (22 - y) * 3 / 2; if (hw > 0) c.Rect(36 - hw, y, hw * 2, 1, y % 3 == 0 ? stD : st); }
        c.Ball(20, 18, 3, 2, Hex("#a83a4a"), Hex("#e05a6a"), Hex("#ff9aa8"));     // coral growing on the roof
        c.Ball(50, 14, 3, 2, Hex("#c8862a"), Hex("#f2a840"), Hex("#ffd890"));
        c.Rect(12, 40, 3, 6, moss); c.Rect(58, 30, 2, 8, moss);
        c.Ball(36, 40, 4, 4, Hex("#1a7a70"), Hex("#2fb8a8"), Hex("#d8fff8"));     // glowing crest over the door
        c.Rect(32, 44, 8, 8, Hex("#1a2a3a"));
        return c.ToSprite(36, 60);
    });

    public static Sprite Relic(bool full) => Get(full ? "relic" : "relic_empty", () =>
    {
        var c = new Canvas(16, 22);
        c.Ellipse(8, 20, 6, 1.5f, Shadow);
        Color s = Hex("#8a98a8"), sD = Hex("#5a6878"), glow = Hex("#7ff5e6");
        c.Rect(3, 4, 10, 16, s); c.Rect(11, 4, 2, 16, sD); c.Ellipse(8, 4, 5, 2, s);
        if (full) for (int y = 7; y < 18; y += 3) { c.Rect(5, y, 5, 1, glow); c.Set(6 + y % 3, y + 1, glow); }
        else c.Line(4, 8, 11, 16, sD);
        return c.ToSprite(8, 20);
    });

    /// A giant clam breathing out bubbles of air.
    public static Sprite GiantClam() => Get("giant_clam", () =>
    {
        var c = new Canvas(30, 22);
        c.Ellipse(15, 19, 13, 2.5f, Shadow);
        Color d = Hex("#4a5a7a"), m = Hex("#7a8ab0"), l = Hex("#b8c8e8");
        c.Ball(15, 15, 13, 4.5f, d, m, l);
        c.Ball(15, 9, 12, 5, d, m, l);
        for (int x = 5; x <= 25; x += 4) c.Line(15, 13, x, 5, d);               // ridges on the shell
        c.Rect(5, 13, 20, 2, Hex("#e8a0b0"));                                   // soft lip
        c.Ball(15, 13, 2, 1.5f, Hex("#c8b8d0"), Hex("#f6f0f6"), Color.white);    // pearl
        return c.ToSprite(15, 19);
    });

    /// A school of little fish, drawn over everything (v = colour variant).
    public static Sprite FishSchool(int v) => Get("fish_school_" + v, () =>
    {
        var c = new Canvas(40, 20);
        Color f = v == 0 ? Hex("#f2c43a") : Hex("#8ad0f0");
        var r = new System.Random(7 + v);
        for (int i = 0; i < 9; i++)
        {
            int x = r.Next(2, 34), y = r.Next(2, 17);
            c.Rect(x, y, 4, 2, f); c.Set(x - 1, y, f); c.Set(x - 1, y + 1, f); c.Set(x + 3, y, Color.black);
        }
        return c.ToSprite(20, 10);
    });

    static Sprite Eel(int frame) => Get("eel_" + frame, () =>
    {
        var c = new Canvas(26, 12);
        c.Ellipse(13, 10.5f, 10, 1.5f, Shadow);
        Color d = Hex("#2a3a1a"), m = Hex("#5a7a2a"), spot = Hex("#ffe14a");
        int headY = 5;
        for (int x = 2; x <= 20; x++)
        {
            int y = 6 + Mathf.RoundToInt(Mathf.Sin((x + frame * 4) * 0.5f) * 2f);
            c.Set(x, y - 1, m); c.Set(x, y, d);
            if (x % 4 == 0) c.Set(x, y - 1, spot); // glowing spots
            if (x == 20) headY = y;
        }
        c.Ball(22, headY, 3, 2.2f, d, m, Hex("#9aba5a"));
        c.Set(23, headY - 1, Hex("#ffe14a"));
        if (frame == 1) { c.Line(18, headY - 4, 20, headY - 2, spot); c.Line(24, headY - 4, 22, headY - 2, spot); } // sparks
        return c.ToSprite(13, 10);
    });

    static Sprite DeepCrab(int frame) => Get("deep_crab_" + frame, () =>
    {
        var c = new Canvas(26, 18);
        c.Ellipse(13, 16, 11, 2, Shadow);
        Color d = Hex("#2a2a4a"), m = Hex("#4a4a8a"), l = Hex("#8a8ac8");
        for (int i = 0; i < 3; i++)
        {
            int off = (i + frame) % 2;
            c.Line(7 - i, 11, 4 - i, 15 - off, d); c.Line(18 + i, 11, 21 + i, 15 - off, d);
        }
        c.Ball(13, 9, 9, 5.5f, d, m, l);
        for (int x = 7; x <= 19; x += 3) c.Set(x, 6, Hex("#c8c8f0")); // spiky ridge
        c.Ball(3, 6 - frame, 3, 2.5f, d, m, l); c.Ball(23, 6 - (1 - frame), 3, 2.5f, d, m, l);
        c.Rect(10, 2, 1, 3, d); c.Rect(15, 2, 1, 3, d); c.Set(10, 2, Hex("#ff6a40")); c.Set(15, 2, Hex("#ff6a40"));
        return c.ToSprite(13, 16);
    });

    /// The Kraken: a huge squid head with curling tentacles.
    static Sprite Kraken(int frame) => Get("kraken_" + frame, () =>
    {
        var c = new Canvas(52, 48);
        c.Ellipse(26, 45, 20, 3, Shadow);
        Color d = Hex("#4a1a3a"), m = Hex("#8a2a5a"), l = Hex("#c85a8a"), sucker = Hex("#f0b8c8");
        for (int i = 0; i < 6; i++) // tentacles
        {
            float a = Mathf.PI * (0.1f + i * 0.16f);
            for (int t = 0; t < 18; t++)
            {
                float curl = Mathf.Sin(t * 0.35f + i + frame * 1.5f) * 3f;
                int x = Mathf.RoundToInt(26 + Mathf.Cos(a) * t * 1.3f + curl), y = Mathf.RoundToInt(28 + Mathf.Sin(a) * t * 0.9f);
                c.Rect(x, y, 3 - t / 8, 2, t % 2 == 0 ? m : d);
                if (t % 4 == 2) c.Set(x, y + 1, sucker);
            }
        }
        c.Ball(26, 18, 14, 13, d, m, l);                           // head
        c.Ball(20, 20, 3.5f, 3.5f, Hex("#c8a020"), Hex("#ffe14a"), Color.white); c.Ball(32, 20, 3.5f, 3.5f, Hex("#c8a020"), Hex("#ffe14a"), Color.white);
        c.Rect(20, 19, 1, 3, Color.black); c.Rect(32, 19, 1, 3, Color.black);
        c.Line(16, 15, 23, 17, d); c.Line(36, 15, 29, 17, d);      // angry brows
        return c.ToSprite(26, 45);
    });

    // ------------------------------------------------------------------ landmarks (every island its own)
    /// A broken ship lying on its side on the beach.
    public static Sprite Wreck() => Get("wreck", () =>
    {
        var c = new Canvas(56, 40);
        c.Ellipse(28, 35, 25, 4, Shadow);
        Color h = Hex("#6b4226"), hD = Hex("#4a2c1a"), hL = Hex("#8a5a36");
        for (int y = 18; y <= 34; y++)
        {
            int x0 = 6 + (34 - y) / 3, x1 = 50 - (34 - y) / 2;
            for (int x = x0; x <= x1; x++) c.Set(x, y, (x / 5) % 2 == 0 ? h : hL);
            c.Set(x1, y, hD);
        }
        for (int x = 14; x <= 40; x += 6) c.Rect(x, 22, 3, 8, Hex("#2a1a10")); // holes in the planks
        c.Line(22, 18, 34, 2, hD); c.Line(23, 18, 35, 2, h);                  // snapped mast
        for (int y = 4; y <= 12; y++) c.Rect(35, y, 8 - (y - 4) / 2, 1, y % 3 == 0 ? Hex("#cbbf9c") : Hex("#f0e6c8")); // torn sail
        c.Rect(36, 13, 2, 3, Hex("#f0e6c8"));
        return c.ToSprite(28, 35);
    });

    public static Sprite Chest(bool closed) => Get(closed ? "chest" : "chest_open", () =>
    {
        var c = new Canvas(18, 16);
        c.Ellipse(9, 14, 7, 1.5f, Shadow);
        Color w = Hex("#8a5a36"), wD = Hex("#6b4226"), gold = Hex("#f2c43a");
        c.Rect(2, 7, 14, 7, w); c.Rect(2, 7, 14, 1, wD); c.Rect(13, 7, 3, 7, wD);
        c.Rect(2, 10, 14, 1, gold); c.Rect(8, 9, 2, 3, gold);
        if (closed) { c.Rect(2, 3, 14, 4, w); c.Rect(2, 3, 14, 1, Hex("#a87a48")); c.Rect(2, 6, 14, 1, gold); }
        else { c.Rect(2, 1, 14, 3, wD); c.Ball(9, 7, 4, 1.5f, Hex("#b88a1a"), gold, Hex("#fff0a0")); } // lid up, gold inside
        return c.ToSprite(9, 14);
    });

    /// An old stone pillar, cracked and mossy (ruins).
    public static Sprite Pillar() => Get("pillar", () =>
    {
        var c = new Canvas(14, 34);
        c.Ellipse(7, 32, 5, 1.5f, Shadow);
        Color s = Hex("#b8b0a0"), sD = Hex("#908878"), moss = Hex("#5a8a3f");
        c.Rect(2, 29, 10, 3, s); c.Rect(3, 6, 8, 23, s); c.Rect(9, 6, 2, 23, sD); c.Rect(2, 4, 10, 3, s);
        for (int y = 8; y < 28; y += 5) c.Rect(4, y, 1, 3, sD);
        c.Line(6, 12, 8, 18, sD);                                        // crack
        c.Rect(3, 26, 3, 3, moss); c.Set(10, 5, moss); c.Set(4, 6, moss);
        c.Rect(7, 2, 4, 2, s); // broken top
        return c.ToSprite(7, 32);
    });

    /// A huge ancient tree towering over the island.
    public static Sprite GiantTree() => Get("giant_tree", () =>
    {
        var c = new Canvas(64, 80);
        c.Ellipse(32, 76, 22, 4, Shadow);
        Color b = Hex("#6b4226"), bD = Hex("#4a2c1a");
        for (int y = 36; y <= 76; y++) { int hw = 6 + (y > 66 ? (y - 66) : 0); c.Rect(32 - hw, y, hw * 2, 1, b); c.Rect(32 + hw - 3, y, 3, 1, bD); }
        c.Line(26, 74, 16, 78, b); c.Line(38, 74, 48, 78, b); // roots
        Color d = Hex("#1f4d26"), m = Hex("#2f6e33"), l = Hex("#5aa848");
        c.Ball(32, 30, 28, 18, d, d, m); c.Ball(18, 22, 14, 12, d, m, l); c.Ball(46, 20, 14, 12, d, m, l); c.Ball(32, 12, 16, 11, d, m, l);
        for (int i = 0; i < 9; i++) c.Set(10 + i * 5, 14 + (i * 7) % 20, Hex("#c8f08a")); // light through the leaves
        return c.ToSprite(32, 76);
    });

    /// A small stone shrine with a glowing offering bowl.
    public static Sprite Shrine() => Get("shrine", () =>
    {
        var c = new Canvas(28, 34);
        c.Ellipse(14, 31, 12, 2.5f, Shadow);
        Color s = Hex("#a8a098"), sD = Hex("#888078"), roof = Hex("#8a3020");
        c.Rect(4, 26, 20, 5, s); c.Rect(4, 26, 20, 1, Hex("#c8c0b8"));
        c.Rect(7, 12, 3, 14, sD); c.Rect(18, 12, 3, 14, sD);
        for (int y = 5; y <= 12; y++) { int hw = 14 - (12 - y); c.Rect(14 - hw, y, hw * 2, 1, y % 2 == 0 ? roof : Hex("#b5452c")); }
        c.Ball(14, 22, 4, 2.5f, Hex("#8a6a2a"), Hex("#c8a040"), Hex("#f0d890"));
        c.Ball(14, 19, 2, 2, Hex("#f2c43a"), Hex("#fff0a0"), Color.white); // the glow
        return c.ToSprite(14, 31);
    });

    /// The red bundle someone lost in the woods.
    public static Sprite LostBundle() => Get("lost_bundle", () =>
    {
        var c = new Canvas(14, 12);
        c.Ellipse(7, 10.5f, 5, 1.2f, Shadow);
        c.Ball(7, 7, 5, 3.5f, Hex("#8a1a20"), Hex("#d9534f"), Hex("#ff9a8a"));
        c.Rect(6, 2, 3, 3, Hex("#d9534f")); c.Rect(4, 2, 2, 1, Hex("#f2c43a")); c.Rect(9, 2, 2, 1, Hex("#f2c43a")); // knot
        return c.ToSprite(7, 10);
    });

    // ------------------------------------------------------------------ Ice island
    /// Snowy pine.
    static Sprite SnowPine() => Get("tree_pine_snow", () =>
    {
        var c = new Canvas(32, 46);
        c.Ellipse(16, 42.5f, 8, 2.5f, Shadow);
        c.Rect(15, 34, 3, 9, Bark); c.Rect(17, 34, 1, 9, BarkD);
        Color d = Hex("#1f4a3a"), m = Hex("#2f6a4f"), snow = Hex("#f4f8ff"), snowD = Hex("#c8d8e8");
        for (int tier = 0; tier < 4; tier++)
        {
            int top = 4 + tier * 8, bottom = top + 12, hw0 = 4 + tier * 3;
            for (int y = top; y <= bottom; y++)
            {
                int hw = (y - top) * hw0 / 12 + 1;
                for (int x = 16 - hw; x <= 16 + hw; x++) c.Set(x, y, x > 16 ? d : m);
                if (y == top + (y - top) % 3 && y < top + 4) c.Rect(16 - hw, y, hw * 2 + 1, 1, snow);
            }
            c.Rect(16 - hw0, bottom, hw0 * 2 + 1, 1, snowD); c.Rect(16 - hw0 + 1, bottom - 1, hw0 * 2 - 1, 1, snow); // snow on each branch layer
        }
        c.Rect(15, 2, 3, 3, snow);
        return c.ToSprite(16, 42);
    });

    public static Sprite IceCrystal(bool full) => Get(full ? "ice_crystal" : "ice_crystal_empty", () =>
    {
        var c = new Canvas(16, 18);
        c.Ellipse(8, 16, 5, 1.5f, Shadow);
        Color d = Hex("#6aa8d8"), m = Hex("#a8d8f8"), l = Color.white;
        if (!full) { c.Rect(6, 14, 4, 2, d); return c.ToSprite(8, 16); }
        for (int y = 3; y <= 16; y++) { int hw = Mathf.Min(3, (y - 3) / 2 + 1); c.Rect(6 - hw / 2, y, hw, 1, m); c.Set(6 + hw / 2, y, d); }
        for (int y = 7; y <= 16; y++) { int hw = Mathf.Min(2, (y - 7) / 2 + 1); c.Rect(11 - hw / 2, y, hw, 1, m); c.Set(11 + hw / 2, y, d); }
        c.Set(5, 5, l); c.Set(10, 9, l);
        return c.ToSprite(8, 16);
    });

    public static Sprite HotSpring() => Get("hot_spring", () =>
    {
        var c = new Canvas(32, 18);
        c.Ellipse(16, 10, 14, 6, Hex("#8a8a96"));
        c.Ellipse(16, 9.5f, 11.5f, 4.5f, Hex("#4ab8c8"));
        c.Ellipse(14, 8.5f, 6, 2, Hex("#8ae0e8"));
        for (int i = 0; i < 7; i++) c.Ball(4 + i * 4, 13 + (i % 2), 1.8f, 1.4f, Hex("#5a5a66"), Hex("#8a8a96"), Hex("#b8b8c4"));
        return c.ToSprite(16, 10);
    });

    /// Melting snowman: three snowballs, coal eyes, a carrot nose and drips.
    static Sprite Snowman(int frame) => Get("snowman_" + frame, () =>
    {
        var c = new Canvas(20, 28);
        c.Ellipse(10, 26, 7, 1.5f, new Color(0.6f, 0.8f, 1f, 0.5f)); // meltwater puddle
        Color d = Hex("#b8c8d8"), m = Hex("#e8f0f8"), l = Color.white;
        int b = frame;
        c.Ball(10, 20, 7, 6, d, m, l); c.Ball(10, 11 - b, 5, 4.5f, d, m, l); c.Ball(10, 5 - b, 3.6f, 3.4f, d, m, l);
        c.Set(9, 4 - b, Color.black); c.Set(11, 4 - b, Color.black); c.Rect(11, 5 - b, 3, 1, Hex("#f08a24"));
        c.Line(5, 11 - b, 1, 8 - b + frame * 2, Bark); c.Line(15, 11 - b, 19, 8 - b + (1 - frame) * 2, Bark); // stick arms
        c.Set(10, 10 - b, Color.black); c.Set(10, 13 - b, Color.black);
        c.Set(6, 23, Hex("#8ac0e8")); c.Set(14, 22, Hex("#8ac0e8")); c.Set(13, 15 - b, Hex("#8ac0e8")); // drips
        return c.ToSprite(10, 26);
    });

    static Sprite Snowling(int frame) => Get("snowling_" + frame, () =>
    {
        var c = new Canvas(12, 12);
        c.Ellipse(6, 10.5f, 4, 1, Shadow);
        int hop = frame == 1 ? 1 : 0;
        c.Ball(6, 7 - hop, 4.2f, 3.8f, Hex("#b8c8d8"), Hex("#e8f0f8"), Color.white);
        c.Set(5, 6 - hop, Color.black); c.Set(7, 6 - hop, Color.black); c.Set(8, 7 - hop, Hex("#f08a24"));
        return c.ToSprite(6, 10);
    });

    static Sprite FrostBat(int frame) => Get("frost_bat_" + frame, () =>
    {
        var c = new Canvas(22, 16);
        c.Ellipse(11, 15, 4, 1, Shadow);
        Color d = Hex("#2a4a6a"), m = Hex("#4a7aa8"), l = Hex("#a8d8f8");
        c.Ball(11, 7, 3.4f, 3f, d, m, l);
        int up = frame == 0 ? -3 : 2;
        c.Line(8, 7, 2, 4 + up, d); c.Line(2, 4 + up, 5, 9, d); c.Line(8, 8, 4, 9, m);
        c.Line(14, 7, 20, 4 + up, d); c.Line(20, 4 + up, 17, 9, d); c.Line(14, 8, 18, 9, m);
        c.Set(10, 6, Hex("#a8f0ff")); c.Set(12, 6, Hex("#a8f0ff")); c.Set(11, 9, Color.white); // icy eyes, fang
        c.Set(9, 4, d); c.Set(13, 4, d); // ears
        return c.ToSprite(11, 14);
    });

    // ------------------------------------------------------------------ Hourglass island
    static void Cog(Canvas c, float cx, float cy, float r, Color d, Color m)
    {
        for (int i = 0; i < 8; i++) // teeth
        {
            float a = i * Mathf.PI / 4f;
            c.Rect(Mathf.RoundToInt(cx + Mathf.Cos(a) * r) - 1, Mathf.RoundToInt(cy + Mathf.Sin(a) * r) - 1, 2, 2, d);
        }
        c.Ball(cx, cy, r - 0.5f, r - 0.5f, d, m, Hex("#f0d890"));
        c.Ball(cx, cy, r * 0.35f, r * 0.35f, d, d, d);
    }

    /// Time sand crystal: a little glowing hourglass-shaped crystal in the sand.
    public static Sprite SandCrystal(bool full) => Get(full ? "sand_crystal" : "sand_crystal_empty", () =>
    {
        var c = new Canvas(14, 16);
        c.Ellipse(7, 14, 5, 1.5f, Shadow);
        if (!full) { c.Rect(5, 12, 4, 2, Hex("#c8a050")); return c.ToSprite(7, 14); }
        Color d = Hex("#b8862a"), m = Hex("#f2c43a"), l = Hex("#fff0a0");
        for (int y = 2; y <= 13; y++) { int hw = Mathf.Abs(y - 7) / 2 + 1; c.Rect(7 - hw, y, hw * 2, 1, y < 7 ? m : d); }
        c.Set(6, 4, l); c.Set(7, 10, l); c.Rect(4, 1, 6, 1, Hex("#8a6a3a")); c.Rect(4, 14, 6, 1, Hex("#8a6a3a"));
        return c.ToSprite(7, 14);
    });

    /// A half-buried broken cog from an old clock.
    public static Sprite ClockRuin() => Get("clock_ruin", () =>
    {
        var c = new Canvas(28, 26);
        c.Ellipse(14, 23, 11, 2.5f, Shadow);
        Cog(c, 14, 13, 10, Hex("#6a4a20"), Hex("#b8862a"));
        c.Rect(3, 18, 22, 6, Hex("#d8c088")); // sand drifted over it
        return c.ToSprite(14, 23);
    });

    /// The great clock tower at the heart of the Hourglass island.
    public static Sprite ClockTower() => Get("clock_tower", () =>
    {
        var c = new Canvas(40, 72);
        c.Ellipse(20, 68, 17, 4, Shadow);
        Color st = Hex("#d8c8a8"), stD = Hex("#b0a080"), roof = Hex("#5a4a8a"), roofD = Hex("#3e3268");
        c.Rect(8, 22, 24, 46, st); c.Rect(26, 22, 6, 46, stD);
        for (int y = 26; y < 68; y += 6) c.Rect(8, y, 24, 1, stD);
        c.Rect(15, 56, 10, 12, Hex("#6b4226"));                   // door
        c.Ball(20, 32, 9, 9, Hex("#8a6a2a"), Hex("#f2e8c8"), Color.white); // clock face
        c.Line(20, 32, 20, 26, Color.black); c.Line(20, 32, 24, 34, Color.black);
        for (int i = 0; i < 12; i++) { float a = i * Mathf.PI / 6f; c.Set(Mathf.RoundToInt(20 + Mathf.Cos(a) * 7), Mathf.RoundToInt(32 + Mathf.Sin(a) * 7), Hex("#5a4a2a")); }
        for (int y = 2; y <= 22; y++) { int hw = (y - 2) / 2 + 2; c.Rect(20 - hw, y, hw * 2, 1, y % 3 == 0 ? roofD : roof); }
        c.Rect(19, 0, 2, 3, Hex("#f2c43a"));
        return c.ToSprite(20, 68);
    });

    /// Wind-up doll: frame 2 = frozen stiff (you're looking at it).
    static Sprite WindupDoll(int frame) => Get("windup_doll_" + frame, () =>
    {
        var c = new Canvas(16, 24);
        c.Ellipse(8, 22.5f, 5, 1.2f, Shadow);
        Color dress = Hex("#c0397a"), dressD = Hex("#8a1a50"), face = Hex("#f6e8e0");
        int leg = frame == 1 ? 1 : 0;
        c.Rect(5, 18, 2, 4 - leg, Hex("#3a2a2a")); c.Rect(9, 18, 2, 3 + leg, Hex("#3a2a2a"));
        for (int y = 11; y <= 18; y++) { int hw = (y - 11) / 2 + 3; c.Rect(8 - hw, y, hw * 2, 1, y % 2 == 0 ? dress : dressD); }
        c.Ball(8, 6, 4.5f, 4.5f, Hex("#c8b8b0"), face, Color.white);
        c.Rect(3, 2, 10, 2, Hex("#2a1a10")); c.Rect(3, 4, 2, 5, Hex("#2a1a10")); c.Rect(11, 4, 2, 5, Hex("#2a1a10")); // hair
        c.Set(6, 6, Color.black); c.Set(10, 6, Color.black);
        c.Rect(7, 9, 3, 1, frame == 2 ? Hex("#5a2a2a") : Hex("#c03030")); // a creepy little smile
        c.Rect(14, 12, 2, 1, Hex("#b8862a")); c.Rect(15, 10, 1, 5, Hex("#b8862a")); // wind-up key on its back
        if (frame == 2) { c.Set(6, 5, Hex("#ff4040")); c.Set(10, 5, Hex("#ff4040")); }
        return c.ToSprite(8, 22);
    });

    static Sprite TimeBeetle(int frame) => Get("time_beetle_" + frame, () =>
    {
        var c = new Canvas(18, 13);
        c.Ellipse(9, 11.5f, 7, 1.5f, Shadow);
        Color d = Hex("#5a3a10"), m = Hex("#b8862a"), l = Hex("#f2d06a");
        for (int i = 0; i < 3; i++) { int off = (i + frame) % 2; c.Line(5 + i * 3, 9, 4 + i * 3, 11 - off, d); c.Line(6 + i * 3, 9, 7 + i * 3, 11 - (1 - off), d); }
        c.Ball(9, 7, 6, 4, d, m, l);
        c.Line(9, 3, 9, 10, d);                         // wing seam
        c.Ring(9, 7, 2, 1, Hex("#fff0a0"));              // a clock face on its shell
        c.Ball(15, 7, 2, 2, d, d, m); c.Set(16, 6, Color.black);
        return c.ToSprite(9, 11);
    });

    // ------------------------------------------------------------------ Bubble island (under the sea)
    /// Deep water around the dome: dark blue with drifting light specks.
    public static Sprite DeepWater(int v) => Get("deep_water_" + v, () =>
    {
        var c = new Canvas(32, 16);
        Color baseCol = Hex("#0f3358"), ray = Hex("#174a78");
        c.Diamond(0, (x, r) => (x + r * 2 + v * 5) % 13 < 2 ? ray : baseCol);
        var rnd = new System.Random(71 + v * 13);
        for (int k = 0; k < 3; k++) { int x = rnd.Next(6, 26), y = rnd.Next(3, 13); if (InDiamond(x, y)) c.Set(x, y, new Color(0.6f, 0.85f, 1f, 0.7f)); }
        return c.ToSprite(16, 8);
    });

    /// Tall swaying kelp (fibre). Picked = short stubs.
    public static Sprite Kelp(bool full) => Get(full ? "kelp" : "kelp_empty", () =>
    {
        var c = new Canvas(20, 44);
        c.Ellipse(10, 41, 6, 1.5f, Shadow);
        Color d = Hex("#2a5a3a"), m = Hex("#3f8a4f"), l = Hex("#7cc07a");
        int top = full ? 4 : 32;
        for (int s = 0; s < 3; s++)
            for (int y = top + s * 3; y <= 41; y++)
            {
                int x = 6 + s * 4 + Mathf.RoundToInt(Mathf.Sin(y * 0.3f + s) * 2f);
                c.Set(x, y, s == 1 ? m : d); c.Set(x + 1, y, s == 1 ? l : m);
                if (full && y % 7 == s) { c.Set(x + 2, y, l); c.Set(x - 1, y + 1, m); } // little leaves
            }
        return c.ToSprite(10, 41);
    });

    public static Sprite CoralNode(bool full) => Get(full ? "coral" : "coral_empty", () =>
    {
        var c = new Canvas(18, 16);
        c.Ellipse(9, 14, 6, 1.5f, Shadow);
        Color d = Hex("#a83a4a"), m = Hex("#e05a6a"), l = Hex("#ff9aa8");
        if (!full) { c.Rect(7, 11, 4, 3, d); return c.ToSprite(9, 14); }
        c.Line(9, 14, 9, 4, m); c.Line(9, 9, 4, 4, m); c.Line(9, 8, 14, 3, m); c.Line(5, 6, 3, 2, d); c.Line(13, 5, 15, 1, d);
        c.Line(10, 14, 10, 6, d); c.Set(4, 4, l); c.Set(14, 3, l); c.Set(9, 4, l); c.Set(3, 2, l); c.Set(15, 1, l);
        c.Ball(13, 11, 2.5f, 2f, Hex("#c8862a"), Hex("#f2a840"), Hex("#ffd890")); // a little orange coral beside it
        return c.ToSprite(9, 14);
    });

    public static Sprite Oyster(bool full) => Get(full ? "oyster" : "oyster_empty", () =>
    {
        var c = new Canvas(16, 12);
        c.Ellipse(8, 10.5f, 6, 1.5f, Shadow);
        Color d = Hex("#5a5a6a"), m = Hex("#8a8a9a"), l = Hex("#c8c8d8");
        c.Ball(8, 8, 6, 3, d, m, l);
        if (full) { c.Ball(8, 5, 5.5f, 2.5f, d, m, l); c.Set(8, 7, Hex("#fff8f0")); c.Set(9, 7, Hex("#e8d8e8")); } // lid just open, a pearl glints
        else c.Rect(4, 6, 8, 1, Hex("#c87a8a"));
        return c.ToSprite(8, 10);
    });

    /// A ring of rocks with air bubbling up through the sand.
    public static Sprite AirVent() => Get("air_vent", () =>
    {
        var c = new Canvas(24, 18);
        c.Ellipse(12, 15, 9, 2, Shadow);
        c.Ellipse(12, 12, 8, 3.5f, Hex("#6a6a7a")); c.Ellipse(12, 11.5f, 5.5f, 2.2f, Hex("#2a4a6a"));
        for (int i = 0; i < 6; i++) c.Ball(6 + i * 2.5f, 12 + (i % 2), 1.4f, 1.2f, Hex("#4a4a5a"), Hex("#7a7a8a"), Hex("#a8a8b8"));
        Color b = new Color(0.85f, 0.97f, 1f, 0.9f);
        c.Ring(12, 6, 1.6f, 1, b); c.Ring(9, 2, 1.2f, 1, b); c.Ring(15, 3, 1f, 1, b);
        return c.ToSprite(12, 15);
    });

    /// Lantern fish: dark round fish with a glowing lure on a stalk.
    static Sprite LanternFish(int frame) => Get("lantern_fish_" + frame, () =>
    {
        var c = new Canvas(22, 20);
        c.Ellipse(11, 18, 6, 1.5f, Shadow);
        int bob = frame;
        Color d = Hex("#1a1a3a"), m = Hex("#3a3a6a"), l = Hex("#6a6aa0");
        c.Ball(10, 11 + bob, 7, 5.5f, d, m, l);
        c.Line(16, 10 + bob, 20, 8 + bob, d); c.Line(16, 13 + bob, 20, 15 + bob, d); // tail
        c.Rect(3, 12 + bob, 6, 1, Hex("#e8e8f0"));                                  // teeth
        for (int x = 3; x <= 8; x += 2) c.Set(x, 13 + bob, Hex("#e8e8f0"));
        c.Set(7, 9 + bob, Hex("#ffe14a"));                                          // eye
        c.Line(8, 6 + bob, 5, 2 + bob, d); c.Line(5, 2 + bob, 3, 3 + bob, d);       // stalk
        c.Ball(3, 4 + bob, 2.2f, 2.2f, Hex("#c8a020"), Hex("#ffe14a"), Color.white); // the lure
        return c.ToSprite(11, 18);
    });

    /// Stone squid: frame 2 = pretending to be a rock; 0/1 = tentacles out.
    static Sprite StoneSquid(int frame) => Get("stone_squid_" + frame, () =>
    {
        var c = new Canvas(20, 18);
        c.Ellipse(10, 16, 7, 1.5f, Shadow);
        Color d = Hex("#4a4a54"), m = Hex("#7a7a86"), l = Hex("#a8a8b4");
        if (frame == 2)
        {
            c.Ball(10, 11, 7, 4.5f, d, m, l);
            c.Set(7, 10, d); c.Set(12, 12, d);
            return c.ToSprite(10, 16);
        }
        for (int i = 0; i < 5; i++) // wriggling tentacles
            for (int y = 11; y <= 16; y++) c.Set(4 + i * 3 + ((y + i + frame) % 3 == 0 ? 1 : 0), y, i % 2 == 0 ? m : Hex("#8a6a7a"));
        c.Ball(10, 7, 6, 5, d, m, l);
        c.Rect(7, 7, 2, 2, Color.white); c.Rect(12, 7, 2, 2, Color.white);
        c.Set(8, 8, Color.black); c.Set(13, 8, Color.black);
        return c.ToSprite(10, 16);
    });

    // ------------------------------------------------------------------ towns (island prosperity)
    /// Stone house with a red tiled roof (towns and cities).
    public static Sprite House() => Get("house", () =>
    {
        var c = new Canvas(44, 44);
        c.Ellipse(22, 40, 20, 4, Shadow);
        Color st = Hex("#d8cdb8"), stD = Hex("#b0a48c"), line = Hex("#a0947c");
        c.Rect(8, 22, 28, 18, st); c.Rect(28, 22, 8, 18, stD);
        for (int y = 25; y < 40; y += 4) c.Rect(8, y, 28, 1, line);
        c.Rect(18, 29, 7, 11, Hex("#6b4226")); c.Set(23, 34, Hex("#f2c43a"));            // door
        c.Rect(10, 26, 5, 5, Hex("#7ab0d8")); c.Rect(30, 26, 4, 5, Hex("#5a90b8"));      // windows
        Color roof = Hex("#b5452c"), roofD = Hex("#8a3020"), roofL = Hex("#d86a4a");
        for (int y = 6; y <= 22; y++)
        {
            int hw = (y - 6) + 6;
            for (int x = 22 - hw; x <= 22 + hw; x++) if (x >= 2 && x < 42) c.Set(x, y, x > 22 ? roofD : (y % 3 == 0 ? roofL : roof));
        }
        c.Rect(30, 4, 4, 8, Hex("#8a7e70"));                                              // chimney
        return c.ToSprite(22, 40);
    });

    /// Street lamp (it lights up at night).
    public static Sprite Lamp() => Get("lamp", () =>
    {
        var c = new Canvas(10, 34);
        c.Ellipse(5, 32, 3, 1, Shadow);
        c.Rect(4, 8, 2, 24, Hex("#3a3a40")); c.Rect(3, 30, 4, 2, Hex("#3a3a40"));
        c.Rect(2, 2, 6, 6, Hex("#3a3a40")); c.Rect(3, 3, 4, 4, Hex("#ffd27a"));
        c.Rect(1, 1, 8, 1, Hex("#3a3a40"));
        return c.ToSprite(5, 32);
    });

    /// Vegetable patch: rows of crops (ripe = ready to pick).
    public static Sprite Farm(bool ripe) => Get(ripe ? "farm" : "farm_empty", () =>
    {
        var c = new Canvas(32, 18);
        Color soil = Hex("#6b4a2b"), soilD = Hex("#523720");
        for (int y = 4; y < 16; y++) { int hw = 14 - Mathf.Abs(y - 10) * 2; c.Rect(16 - hw, y, hw * 2, 1, y % 3 == 0 ? soilD : soil); }
        if (ripe)
            for (int i = 0; i < 6; i++)
            {
                int x = 7 + i * 3 + (i % 2), y = 7 + (i % 3) * 2;
                c.Line(x, y + 3, x, y, Hex("#4f9a3f")); c.Set(x - 1, y, Hex("#7cc95c")); c.Set(x + 1, y + 1, Hex("#7cc95c"));
                c.Set(x, y - 1, i % 2 == 0 ? Hex("#e0522c") : Hex("#f2c43a"));
            }
        return c.ToSprite(16, 10);
    });

    public static Sprite Well() => Get("well", () =>
    {
        var c = new Canvas(24, 30);
        c.Ellipse(12, 27, 10, 2.5f, Shadow);
        c.Ellipse(12, 20, 9, 4, Hex("#8a8a90")); c.Ellipse(12, 19, 7, 2.6f, Hex("#3a6a9a"));
        c.Rect(3, 20, 18, 6, Hex("#9a9aa0")); c.Rect(3, 22, 18, 1, Hex("#7a7a80"));
        c.Rect(4, 6, 2, 15, Bark); c.Rect(18, 6, 2, 15, Bark);
        for (int y = 2; y <= 7; y++) { int hw = 11 - (7 - y) * 2; c.Rect(12 - hw, y, hw * 2, 1, Hex("#8a3020")); }
        c.Rect(11, 8, 1, 7, RopeC); c.Rect(10, 14, 3, 3, Hex("#6b4226"));
        return c.ToSprite(12, 27);
    });

    /// Market stall with a striped awning.
    public static Sprite Stall() => Get("stall", () =>
    {
        var c = new Canvas(36, 32);
        c.Ellipse(18, 29, 16, 3, Shadow);
        c.Rect(5, 12, 2, 17, Bark); c.Rect(29, 12, 2, 17, Bark);
        c.Rect(4, 20, 28, 6, Hex("#9a7048")); c.Rect(4, 20, 28, 1, Hex("#c79a64"));
        for (int i = 0; i < 5; i++) c.Ball(8 + i * 5, 18, 2, 2, Hex("#c4501b"), Hex("#f08a24"), Hex("#ffd27a")); // goods
        for (int x = 2; x < 34; x++) for (int y = 6; y < 12; y++) c.Set(x, y, ((x / 4) % 2 == 0) ? Hex("#d9534f") : Hex("#f0e6c8"));
        for (int x = 2; x < 34; x += 4) c.Rect(x, 12, 2, 1, Hex("#d9534f"));
        return c.ToSprite(18, 29);
    });

    public static Sprite Fountain() => Get("fountain", () =>
    {
        var c = new Canvas(40, 32);
        c.Ellipse(20, 28, 18, 3.5f, Shadow);
        c.Ellipse(20, 22, 17, 6, Hex("#a8a8b0")); c.Ellipse(20, 21, 14, 4.5f, Hex("#5a9ad8"));
        c.Rect(18, 8, 4, 13, Hex("#c8c8d0")); c.Ellipse(20, 9, 6, 2, Hex("#a8a8b0"));
        var spray = new Color(0.8f, 0.92f, 1f, 0.9f);
        c.Line(20, 2, 14, 10, spray); c.Line(20, 2, 26, 10, spray); c.Line(20, 2, 20, 8, spray);
        return c.ToSprite(20, 28);
    });

    /// A statue of a famous sailor on a plinth (city plaza).
    public static Sprite Statue() => Get("statue", () =>
    {
        var c = new Canvas(20, 40);
        c.Ellipse(10, 37, 8, 2, Shadow);
        c.Rect(3, 28, 14, 9, Hex("#a8a8b0")); c.Rect(3, 28, 14, 1, Hex("#c8c8d0"));
        var inner = new Canvas(14, 24);
        Color s = Hex("#b8c0b0"), sd = Hex("#909888");
        Person(inner, 0, 0, s, sd, sd, s, sd, s, sd, false, false, 2);
        for (int y = 0; y < 24; y++) for (int x = 0; x < 14; x++) { var col = inner.GetPixel(x, y); if (col.a > 0f) c.Set(x + 3, y + 4, col); }
        c.Line(16, 6, 18, 2, sd); // raised arm with a spyglass
        return c.ToSprite(10, 37);
    });

    // ------------------------------------------------------------------ Sky island
    /// A tile of the cloud sea around the floating island.
    public static Sprite CloudTile(int v) => Get("cloud_tile_" + v, () =>
    {
        var c = new Canvas(32, 16);
        Color baseCol = new Color(0.86f, 0.92f, 1f), shade = new Color(0.74f, 0.82f, 0.95f);
        c.Diamond(0, (x, r) => r > 11 ? shade : baseCol);
        var rnd = new System.Random(31 + v * 17);
        for (int k = 0; k < 3; k++)
        {
            float cx = rnd.Next(8, 24), cy = rnd.Next(4, 11);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 32; x++)
                    if (InDiamond(x, y) && (x - cx) * (x - cx) / 16f + (y - cy) * (y - cy) / 5f < 1f) c.Set(x, y, Color.white);
        }
        return c.ToSprite(16, 8);
    });

    /// Cloud cotton: a puffy white plant (gives cloud cotton). Picked = bare twigs.
    public static Sprite CloudPlant(bool ripe) => Get(ripe ? "cloud_plant" : "cloud_plant_empty", () =>
    {
        var c = new Canvas(18, 18);
        c.Ellipse(9, 16, 6, 1.5f, Shadow);
        Color stem = Hex("#7a9a6a");
        c.Line(9, 16, 9, 9, stem); c.Line(9, 12, 5, 8, stem); c.Line(9, 11, 13, 7, stem);
        if (ripe)
        {
            Color d = Hex("#c8d8f0"), m = Hex("#eef4ff");
            c.Ball(5, 7, 3.5f, 3f, d, m, Color.white); c.Ball(13, 6, 3.5f, 3f, d, m, Color.white); c.Ball(9, 5, 4f, 3.5f, d, m, Color.white);
        }
        return c.ToSprite(9, 16);
    });

    /// Storm cloud: frame 2 = crackling before a charge.
    static Sprite StormCloud(int frame) => Get("storm_cloud_" + frame, () =>
    {
        var c = new Canvas(24, 20);
        c.Ellipse(12, 18, 7, 1.5f, Shadow);
        bool flash = frame == 2;
        Color d = flash ? Hex("#8a8aa0") : Hex("#3a3a50"), m = flash ? Hex("#d8d8f0") : Hex("#6a6a88"), l = flash ? Color.white : Hex("#9a9ab8");
        int bob = frame == 1 ? 1 : 0;
        c.Ball(8, 9 + bob, 6, 4.5f, d, m, l); c.Ball(15, 8 + bob, 6, 5f, d, m, l); c.Ball(12, 6 + bob, 5, 4f, d, m, l);
        c.Rect(8, 9 + bob, 1, 2, Color.black); c.Rect(14, 9 + bob, 1, 2, Color.black); // eyes
        c.Line(9, 12 + bob, 13, 12 + bob, Hex("#20202a"));
        Color bolt = Hex("#ffe14a");
        c.Line(11, 13 + bob, 9, 16, bolt); c.Line(9, 16, 12, 16, bolt); c.Line(12, 16, 10, 19, bolt);
        if (flash) { c.Line(3, 4, 6, 7, bolt); c.Line(20, 3, 18, 7, bolt); }
        return c.ToSprite(12, 18);
    });

    /// Chime bird: a little blue bird with a golden bell round its neck.
    static Sprite ChimeBird(int frame) => Get("chime_bird_" + frame, () =>
    {
        var c = new Canvas(18, 16);
        c.Ellipse(9, 14.5f, 5, 1.2f, Shadow);
        Color d = Hex("#2a5a9a"), m = Hex("#4f8ad8"), l = Hex("#a8d0ff");
        c.Ball(9, 8, 5, 3.6f, d, m, l);                      // body
        c.Ball(13, 5, 2.6f, 2.4f, d, m, l);                   // head
        c.Set(14, 4, Color.black); c.Rect(15, 5, 2, 1, Hex("#f2a020")); // eye, beak
        if (frame == 0) { c.Line(5, 6, 1, 2, d); c.Line(6, 6, 2, 3, m); }  // wing up
        else { c.Line(5, 9, 1, 12, d); c.Line(6, 9, 2, 11, m); }          // wing down
        c.Line(4, 9, 1, 10, d);                               // tail
        c.Ball(11, 10, 1.6f, 1.6f, Hex("#b88a1a"), Hex("#f2c43a"), Hex("#fff0a0")); // the bell
        c.Line(8, 12, 8, 14, Hex("#f2a020")); c.Line(10, 12, 10, 14, Hex("#f2a020"));
        return c.ToSprite(9, 14);
    });

    // ------------------------------------------------------------------ Giant Turtle island
    /// The turtle's head poking out of the sea (blink = eyes shut for a moment).
    public static Sprite TurtleHead(bool blink) => Get(blink ? "turtle_head_blink" : "turtle_head", () =>
    {
        var c = new Canvas(64, 48);
        Color d = Hex("#2f5a2a"), m = Hex("#4f8a3f"), l = Hex("#86c06a"), belly = Hex("#d8d090");
        c.Ellipse(30, 42, 26, 4, new Color(1f, 1f, 1f, 0.35f)); // foam ring where the neck meets the sea
        for (int y = 22; y <= 42; y++) c.Rect(18 + (y - 22) / 4, y, 18 - (y - 22) / 6, 1, y % 4 == 0 ? d : m); // neck
        c.Rect(26, 26, 6, 16, belly);
        c.Ball(32, 18, 16, 12, d, m, l);             // head
        c.Ball(44, 22, 6, 5, d, m, l);               // snout
        c.Set(48, 21, Hex("#1a2a10")); c.Set(48, 23, Hex("#1a2a10")); // nostrils
        if (blink) c.Rect(34, 13, 6, 1, Hex("#1a2a10"));
        else { c.Ball(37, 13, 3.5f, 3.5f, Hex("#c8c0a0"), Color.white, Color.white); c.Rect(38, 12, 2, 3, Hex("#1a1010")); }
        c.Line(38, 26, 47, 25, Hex("#1a2a10"));      // mouth
        for (int i = 0; i < 4; i++) c.Set(22 + i * 4, 10 + (i % 2), l); // spots
        return c.ToSprite(30, 42);
    });

    /// A big spiral shell lying on the beach (gives shell shards).
    public static Sprite Shell() => Get("shell", () =>
    {
        var c = new Canvas(16, 12);
        c.Ellipse(8, 10.5f, 6, 1.5f, Shadow);
        c.Ball(8, 6, 6, 4.5f, Hex("#c08a7a"), Hex("#f2c8b8"), Hex("#fff0e8"));
        c.Line(3, 6, 13, 6, Hex("#d8a090")); c.Line(4, 4, 12, 8, Hex("#d8a090"));
        c.Ring(8, 6, 2, 1, Hex("#c08a7a"));
        return c.ToSprite(8, 10);
    });

    /// Barnacle crawler: a cluster of grey cones scuttling on tiny legs.
    static Sprite Barnacle(int frame) => Get("barnacle_" + frame, () =>
    {
        var c = new Canvas(16, 12);
        c.Ellipse(8, 10.5f, 6, 1.5f, Shadow);
        Color d = Hex("#5a5a60"), m = Hex("#9a9aa0"), l = Hex("#d8d8e0"), mouth = Hex("#e05a8a");
        for (int i = 0; i < 4; i++) c.Line(4 + i * 3, 9, 3 + i * 3 + ((i + frame) % 2) * 2, 11, d); // legs
        int[,] cones = { { 5, 6 }, { 9, 5 }, { 12, 7 }, { 7, 8 } };
        for (int i = 0; i < cones.GetLength(0); i++)
        {
            c.Ball(cones[i, 0], cones[i, 1], 2.6f, 2.4f, d, m, l);
            c.Set(cones[i, 0], cones[i, 1] - 1, mouth);
        }
        c.Set(10, 3, Color.black); c.Set(12, 4, Color.black); // eye stalks
        return c.ToSprite(8, 10);
    });

    /// Hermit crab: frames 0/1 inside its huge shell, 2/3 after the shell broke.
    static Sprite HermitCrab(int frame) => Get("hermit_crab_" + frame, () =>
    {
        var c = new Canvas(22, 18);
        c.Ellipse(11, 16, 9, 2, Shadow);
        bool shell = frame < 2;
        int f = frame % 2;
        Color d = Hex("#8a2a18"), m = Hex("#e0602c"), l = Hex("#ffa070");
        for (int i = 0; i < 3; i++) { c.Line(5 - i, 12, 3 - i, 15 - (i + f) % 2, d); c.Line(14 + i, 12, 16 + i, 15 - (i + 1 - f) % 2, d); }
        c.Ball(17, 9 - f, 2.6f, 2.2f, d, m, l);          // big claw
        c.Rect(13, 6, 1, 3, d); c.Rect(15, 5, 1, 3, d);   // eye stalks
        c.Set(13, 5, Color.black); c.Set(15, 4, Color.black);
        if (shell)
        {
            c.Ball(9, 8, 8, 7, Hex("#6a4a5a"), Hex("#b088a0"), Hex("#f0d0e0")); // the spiral shell
            c.Ring(9, 8, 4, 1, Hex("#6a4a5a")); c.Ring(9, 8, 2, 1, Hex("#8a6a7a"));
        }
        else c.Ball(10, 11, 5, 3.5f, Hex("#c87a6a"), Hex("#f0a898"), Hex("#ffd8d0")); // soft pink body
        return c.ToSprite(11, 16);
    });

    // ------------------------------------------------------------------ special fruits
    /// (dark, mid, light) colours of each special fruit.
    static (Color d, Color m, Color l) FruitColors(Item it) => it switch
    {
        Item.FruitShadow => (Hex("#241040"), Hex("#5a3a9a"), Hex("#b8a0ff")),
        Item.FruitVine => (Hex("#1f5a1a"), Hex("#4fbf3a"), Hex("#c8ff8a")),
        Item.FruitMagnet => (Hex("#6a1010"), Hex("#d9534f"), Hex("#ffb0a0")),
        Item.FruitBubble => (Hex("#2a6a9a"), Hex("#8fd8ff"), Hex("#ffffff")),
        _ => (Hex("#8a6a00"), Hex("#f2d43a"), Hex("#fff6b0")),
    };

    /// The rare glowing tree: silver-white trunk, golden leaves, and big fruits of its island's power.
    public static Sprite GlowTree(bool fruit, Item kind) => Get(fruit ? "glowtree_" + kind.ToString().ToLowerInvariant() : "glowtree_empty", () =>
    {
        var c = new Canvas(32, 46);
        c.Ellipse(16, 42.5f, 10, 3, Shadow);
        Color tr = Hex("#e8e4d8"), trD = Hex("#b0a890");
        c.Rect(14, 24, 4, 19, tr); c.Rect(17, 24, 1, 19, trD);
        c.Rect(12, 41, 8, 2, tr);
        c.Ball(16, 18, 14, 11, Hex("#8a6a1a"), Hex("#d8b040"), Hex("#fff0a0"));
        c.Ball(11, 12, 6, 5, Hex("#d8b040"), Hex("#f2d06a"), Hex("#fff6c8"));
        var sparkle = new Color(1f, 1f, 1f, 0.9f);
        c.Set(5, 8, sparkle); c.Set(27, 6, sparkle); c.Set(24, 26, sparkle); c.Set(3, 20, sparkle);
        if (fruit)
        {
            var (d, m, l) = FruitColors(kind);
            c.Ball(10, 19, 3, 3, d, m, l); c.Ball(21, 14, 3, 3, d, m, l); c.Ball(17, 23, 3, 3, d, m, l);
        }
        return c.ToSprite(16, 42);
    });

    static void DrawFruitIcon(Canvas c, Item it)
    {
        var (d, m, l) = FruitColors(it);
        c.Ball(8, 9, 6, 6, d, m, l);
        c.Line(8, 3, 9, 1, Bark);
        c.Ellipse(11.5f, 2.5f, 2, 1, Hex("#4c8f37"));
        Color mark = it == Item.FruitBubble ? Hex("#2a6a9a") : Color.white;
        switch (it) // a symbol of the power on the fruit's skin
        {
            case Item.FruitDash: c.Line(9, 5, 6, 9, mark); c.Line(6, 9, 10, 9, mark); c.Line(10, 9, 7, 13, mark); break; // lightning
            case Item.FruitShadow: c.Ball(7, 9, 3, 3, Hex("#fff6c8"), Hex("#fff6c8"), Color.white); c.Ball(9, 8, 3, 3, m, m, m); break; // crescent moon
            case Item.FruitVine: c.Line(5, 12, 7, 7, mark); c.Line(7, 7, 10, 8, mark); c.Line(10, 8, 11, 11, mark); break; // curl
            case Item.FruitMagnet: c.Rect(5, 6, 2, 6, mark); c.Rect(10, 6, 2, 6, mark); c.Rect(5, 11, 7, 2, mark); break; // horseshoe
            case Item.FruitBubble: c.Ring(8, 9, 3, 1, mark); c.Set(7, 7, Color.white); break; // bubble
        }
    }

    // ------------------------------------------------------------------ Glowing Mushroom island
    /// Giant mushroom "tree": pale stalk, wide violet cap with glowing spots.
    static Sprite GiantMushroom() => Get("tree_mushroom", () =>
    {
        var c = new Canvas(32, 46);
        c.Ellipse(16, 42.5f, 9, 2.5f, Shadow);
        Color st = Hex("#d9cfe8"), stD = Hex("#a99cc0"), gill = Hex("#8a6fa8");
        for (int y = 16; y <= 42; y++)
        {
            int w = 3 + (y > 36 ? (y - 36) / 2 : 0);
            c.Rect(16 - w / 2 - 1, y, w + 1, 1, st);
            c.Set(16 + w / 2, y, stD);
        }
        c.Ball(16, 13, 15, 8, Hex("#3a1f5c"), Hex("#6a3fa0"), Hex("#9a6fd0"));
        c.Rect(4, 17, 24, 2, gill);
        Color glow = Hex("#7ff5e6"), glowL = Hex("#d8fff8");
        int[,] spots = { { 9, 10 }, { 16, 7 }, { 23, 11 }, { 13, 14 }, { 20, 15 }, { 6, 14 }, { 26, 15 } };
        for (int i = 0; i < spots.GetLength(0); i++)
        {
            c.Rect(spots[i, 0], spots[i, 1], 2, 2, glow);
            c.Set(spots[i, 0], spots[i, 1], glowL);
        }
        return c.ToSprite(16, 42);
    });

    /// Small glowing mushroom you can pick (gives glow spores). Picked = a dim stub.
    public static Sprite GlowShroom(bool ripe) => Get(ripe ? "glowshroom" : "glowshroom_empty", () =>
    {
        var c = new Canvas(16, 16);
        c.Ellipse(8, 14, 5, 1.5f, Shadow);
        if (!ripe)
        {
            c.Rect(7, 11, 2, 3, Hex("#8a7fa0"));
            return c.ToSprite(8, 14);
        }
        Color st = Hex("#e8f0f0"), cap = Hex("#2fb8a8"), capL = Hex("#7ff5e6"), capD = Hex("#1a7a70");
        c.Rect(5, 9, 2, 5, st); c.Rect(10, 10, 2, 4, st);
        c.Ball(6, 7, 4, 2.6f, capD, cap, capL);
        c.Ball(11, 9, 3, 2, capD, cap, capL);
        c.Set(5, 6, Color.white); c.Set(11, 8, Color.white);
        return c.ToSprite(8, 14);
    });

    /// Mimic: frame 2 = disguised (looks like a glowing mushroom, but redder), 0/1 = hopping.
    static Sprite Mimic(int frame) => Get("mimic_" + frame, () =>
    {
        var c = new Canvas(18, 18);
        c.Ellipse(9, 16, 6, 1.5f, Shadow);
        Color cap = Hex("#c0397a"), capL = Hex("#ff7ab8"), capD = Hex("#6a1a40"), st = Hex("#f0e0e8");
        if (frame == 2)
        {
            c.Rect(7, 10, 4, 6, st);
            c.Ball(9, 8, 6, 3.5f, capD, cap, capL);
            c.Set(6, 7, Color.white); c.Set(12, 8, Color.white);
            return c.ToSprite(9, 16);
        }
        int hop = frame == 1 ? 2 : 0;
        c.Rect(6, 9 - hop, 6, 6, st);
        c.Rect(5, 15 - hop, 2, 1, st); c.Rect(11, 15 - hop, 2, 1, st); // little feet
        c.Ball(9, 7 - hop, 7, 4, capD, cap, capL);
        c.Rect(7, 10 - hop, 1, 2, Color.black); c.Rect(10, 10 - hop, 1, 2, Color.black); // eyes
        for (int x = 7; x <= 11; x += 2) c.Set(x, 13 - hop, Hex("#ffffff"));          // teeth
        c.Rect(7, 12 - hop, 5, 1, Hex("#3a0a1a"));
        return c.ToSprite(9, 16);
    });

    static Sprite SporeSpider(int frame) => Get("spore_spider_" + frame, () =>
    {
        var c = new Canvas(20, 14);
        c.Ellipse(10, 12, 8, 1.5f, Shadow);
        Color d = Hex("#2a1a3a"), m = Hex("#4a2f6a"), l = Hex("#7a5aa0"), spot = Hex("#9bff6a");
        for (int i = 0; i < 4; i++)
        {
            int off = (i + frame) % 2;
            c.Line(8, 8, 2 + i, 11 - off - i % 2, d);
            c.Line(12, 8, 18 - i, 11 - (1 - off) - i % 2, d);
        }
        c.Ball(10, 7, 5, 3.5f, d, m, l);   // body
        c.Ball(14, 8, 2.5f, 2, d, m, l);   // head
        c.Set(15, 7, Hex("#ff4040")); c.Set(13, 7, Hex("#ff4040"));
        c.Set(8, 5, spot); c.Set(10, 4, spot); c.Set(11, 6, spot); // glowing spores on its back
        return c.ToSprite(10, 12);
    });

    /// The Mushroom King: a huge purple toadstool with a crown and angry eyes.
    static Sprite ShroomKing(int frame) => Get("shroom_king_" + frame, () =>
    {
        var c = new Canvas(40, 40);
        c.Ellipse(20, 37, 14, 3, Shadow);
        int bob = frame;
        Color st = Hex("#e8dcc8"), stD = Hex("#b8a890");
        c.Rect(13, 20 + bob, 14, 16 - bob, st); c.Rect(24, 20 + bob, 3, 16 - bob, stD);
        c.Rect(11, 34, 6, 3, st); c.Rect(23, 34, 6, 3, stD); // feet
        c.Rect(15, 25 + bob, 2, 3, Color.black); c.Rect(23, 25 + bob, 2, 3, Color.black);
        c.Line(14, 23 + bob, 17, 24 + bob, Color.black); c.Line(26, 23 + bob, 23, 24 + bob, Color.black); // angry brows
        c.Rect(17, 30 + bob, 6, 1, Hex("#5a2a2a"));
        c.Ball(20, 13 + bob, 19, 10, Hex("#3a0f4a"), Hex("#7a2a9a"), Hex("#b46ae0"));
        Color glow = Hex("#9bff6a");
        int[,] spots = { { 8, 12 }, { 15, 7 }, { 24, 6 }, { 31, 11 }, { 12, 17 }, { 28, 17 }, { 20, 13 } };
        for (int i = 0; i < spots.GetLength(0); i++) c.Ball(spots[i, 0], spots[i, 1] + bob, 2, 1.6f, glow, glow, Color.white);
        Color gold = Hex("#f2c43a"), goldD = Hex("#b88a1a");
        c.Rect(14, 1 + bob, 12, 3, gold); c.Rect(14, 3 + bob, 12, 1, goldD);
        c.Set(14, 0 + bob, gold); c.Set(19, 0 + bob, gold); c.Set(20, 0 + bob, gold); c.Set(25, 0 + bob, gold);
        c.Set(17, 2 + bob, Hex("#d9534f")); c.Set(22, 2 + bob, Hex("#3b7dd8"));
        return c.ToSprite(20, 37);
    });

    /// A drifting patch of poisonous spores on the ground.
    public static Sprite SporeCloud() => Get("spore_cloud", () =>
    {
        var c = new Canvas(28, 16);
        Color a = new Color(0.55f, 1f, 0.35f, 0.35f), b = new Color(0.75f, 1f, 0.5f, 0.55f);
        c.Ellipse(14, 9, 12, 5, a);
        c.Ellipse(9, 8, 5, 3, b); c.Ellipse(18, 10, 6, 3, b); c.Ellipse(14, 6, 4, 2.5f, b);
        var r = new System.Random(5);
        for (int i = 0; i < 10; i++) c.Set(r.Next(4, 24), r.Next(3, 13), new Color(0.9f, 1f, 0.7f, 0.9f));
        return c.ToSprite(14, 9);
    });

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
                       Color legs, Color legsD, bool skirt, bool bareChest, int hairStyle = 0)
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
            if (hairStyle == 1) { c.Rect(2, 4, 2, 7, hair); c.Rect(10, 4, 2, 7, hair); }   // long: falls past the shoulders
        }
        else
        {
            c.Rect(4, 2, 6, 2, hair); c.Rect(3, 3, 8, 6, hair); c.Rect(4, 9, 6, 1, hair);
            if (hairStyle == 1) c.Rect(3, 9, 8, 4, hair);
        }
        if (hairStyle == 2) // spiky
        {
            c.Set(3, 1, hair); c.Set(5, 0, hair); c.Set(5, 1, hair); c.Set(7, 0, hair); c.Set(8, 1, hair); c.Set(10, 1, hair); c.Set(4, 2, hair);
        }
        else if (hairStyle == 3) // bun on top
        {
            c.Rect(6, 0, 2, 1, hair); c.Rect(5, 1, 4, 1, hair);
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
    public static Sprite Player(int dir, int frame, Item? hat = null, Item? outfit = null, int look = 0)
    {
        string key = $"player_{(dir == 0 ? "front" : "back")}_{frame}";
        if (hat != null) key += "_" + hat.Value.ToString().ToLowerInvariant();
        if (outfit != null) key += "_" + outfit.Value.ToString().ToLowerInvariant();
        if (look != 0) key += "_l" + look; // look 0 keeps the plain names for hand-made art
        return Get(key, () =>
        {
            var c = new Canvas(14, 24);
            var l = CharacterLook.FromCode(look);
            Color shirt = l.Shirt, shirtD = l.ShirtDark;
            if (outfit == Item.LeafTunic) { shirt = Hex("#4f9a3f"); shirtD = Hex("#3a7a30"); }
            bool female = l.gender == 1;
            // girls: bare legs under a skirt in the "pants" colour (drawn below)
            Person(c, dir, frame, l.Skin, l.SkinDark, l.Hair, shirt, shirtD,
                   female ? l.Skin : l.Pant, female ? l.SkinDark : l.PantDark, false, false, l.style);
            if (female)
            {
                for (int y = 15; y <= 18; y++)
                {
                    int flare = (y - 15) / 2;
                    for (int x = 3 - flare; x <= 10 + flare; x++) c.Set(x, y, x >= 9 + flare ? l.PantDark : l.Pant);
                }
                if (dir == 0)
                {
                    Color lash = Hex("#2a1a10"), blush = new Color(1f, 0.55f, 0.55f, 0.6f);
                    c.Set(4, 5, lash); c.Set(9, 5, lash);   // lashes beside the eyes
                    c.Set(4, 7, blush); c.Set(9, 7, blush); // rosy cheeks
                }
            }

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
            case 4: // shipwright: tanned, blue overalls, red bandana
                Person(inner, 0, frame, Hex("#d8a070"), Hex("#b88050"), Hex("#4a2c1a"), Hex("#2e6aa8"), Hex("#1e4a78"), Hex("#2e6aa8"), Hex("#1e4a78"), false, false);
                inner.Rect(3, 3, 8, 2, Hex("#d9534f"));
                inner.Rect(5, 11, 4, 1, Hex("#f0e6c8"));
                break;
            case 5: // mushroom doctor: violet robe, big glowing mushroom hat
                Person(inner, 0, frame, Hex("#f1c27d"), Hex("#d0a060"), Hex("#e8e8e8"), Hex("#5a3a8a"), Hex("#3e2868"), Hex("#5a3a8a"), Hex("#3e2868"), true, false, 1);
                inner.Rect(1, 1, 12, 2, Hex("#2fb8a8")); inner.Rect(3, 0, 8, 1, Hex("#7ff5e6"));
                inner.Set(4, 1, Color.white); inner.Set(9, 2, Color.white);
                break;
            case 6: // lost child: little mushroom cap, yellow dress
                Person(inner, 0, frame, Hex("#f1c27d"), Hex("#d0a060"), Hex("#4a2c1a"), Hex("#e0b030"), Hex("#b08a20"), Hex("#f1c27d"), Hex("#d0a060"), false, false, 3);
                inner.Rect(2, 1, 10, 3, Hex("#c0397a")); inner.Set(4, 2, Color.white); inner.Set(9, 1, Color.white);
                for (int x = 3; x <= 10; x++) inner.Rect(x, 15, 1, 3, x % 2 == 0 ? Hex("#e0b030") : Hex("#b08a20"));
                break;
            case 7: // turtle keeper: old woman, sea-green shawl, little shell backpack
                Person(inner, 0, frame, Hex("#d8a070"), Hex("#b88050"), Hex("#e8e8e8"), Hex("#2a8a7a"), Hex("#1a6a5a"), Hex("#6b4226"), Hex("#4a2c1a"), true, false, 3);
                inner.Ball(11, 12, 3, 3, Hex("#4f6a2c"), Hex("#6a8a3a"), Hex("#8aaa52"));
                break;
            case 8: // wind sage: long white beard, sky-blue robe, a golden bell on his staff
                Person(inner, 0, frame, Hex("#f1c27d"), Hex("#d0a060"), Hex("#f0f0f0"), Hex("#4f8ad8"), Hex("#2a5a9a"), Hex("#4f8ad8"), Hex("#2a5a9a"), true, false);
                inner.Rect(5, 8, 4, 4, Hex("#f0f0f0"));
                inner.Ball(12, 4, 1.6f, 1.6f, Hex("#b88a1a"), Hex("#f2c43a"), Hex("#fff0a0"));
                break;
            case 9: // townsfolk: farmer in a straw hat
                Person(inner, 0, frame, Hex("#f1c27d"), Hex("#d0a060"), Hex("#4a2c1a"), Hex("#4f9a3f"), Hex("#3a7a30"), Hex("#6b4226"), Hex("#4a2c1a"), false, false);
                inner.Rect(1, 3, 12, 1, Hex("#e8c66a")); inner.Rect(4, 0, 6, 3, Hex("#e8c66a"));
                break;
            case 10: // townsfolk: woman in a blue dress
                Person(inner, 0, frame, Hex("#ffdbac"), Hex("#e0b890"), Hex("#1a1010"), Hex("#3b7dd8"), Hex("#2a5aa8"), Hex("#3b7dd8"), Hex("#2a5aa8"), true, false, 1);
                break;
            case 11: // townsfolk: sailor in a striped shirt
                Person(inner, 0, frame, Hex("#c68642"), Hex("#a86a30"), Hex("#2b2b2b"), Hex("#e8e8e8"), Hex("#c8c8c8"), Hex("#2a4a7a"), Hex("#1a3a5a"), false, false);
                for (int y = 11; y <= 16; y += 2) inner.Rect(3, y, 8, 1, Hex("#2a5aa8"));
                inner.Rect(3, 2, 8, 2, Hex("#2a4a7a"));
                break;
            case 12: // diver: dark wetsuit, goggles pushed up, pearl necklace
                Person(inner, 0, frame, Hex("#e0ac69"), Hex("#c08a50"), Hex("#2b2b2b"), Hex("#1e3a5a"), Hex("#122a44"), Hex("#1e3a5a"), Hex("#122a44"), false, false, 1);
                inner.Rect(3, 3, 8, 2, Hex("#5a9ad8")); inner.Set(5, 3, Color.white); inner.Set(8, 3, Color.white);
                for (int x = 4; x <= 9; x++) inner.Set(x, 10, Hex("#f6f0f6"));
                break;
            case 13: // clockmaker: brown waistcoat, brass goggles, white moustache
                Person(inner, 0, frame, Hex("#f1c27d"), Hex("#d0a060"), Hex("#d8d8d8"), Hex("#8a5a36"), Hex("#6b4226"), Hex("#3a3a40"), Hex("#2a2a30"), false, false);
                inner.Rect(3, 4, 8, 1, Hex("#b8862a")); inner.Rect(4, 3, 2, 2, Hex("#f2c43a")); inner.Rect(8, 3, 2, 2, Hex("#f2c43a"));
                inner.Rect(5, 8, 4, 1, Color.white);
                break;
            case 14: // snow hunter: thick fur parka with a white-trimmed hood
                Person(inner, 0, frame, Hex("#e0ac69"), Hex("#c08a50"), Hex("#4a2c1a"), Hex("#8a5a36"), Hex("#6b4226"), Hex("#4a3a2a"), Hex("#3a2a1a"), false, false);
                inner.Rect(2, 1, 10, 3, Hex("#f4f4f4")); inner.Rect(2, 3, 2, 6, Hex("#f4f4f4")); inner.Rect(10, 3, 2, 6, Hex("#f4f4f4"));
                inner.Rect(3, 16, 8, 1, Hex("#f4f4f4"));
                break;
            case 15: // archaeologist: brass diving helmet, khaki coat
                Person(inner, 0, frame, Hex("#f1c27d"), Hex("#d0a060"), Hex("#4a2c1a"), Hex("#b8a070"), Hex("#988050"), Hex("#6b5a3a"), Hex("#4a3a2a"), false, false);
                inner.Ball(7, 5, 5, 5, Hex("#8a6a20"), Hex("#c8a040"), Hex("#f0d890")); inner.Ball(7, 5, 2.5f, 2.5f, Hex("#3a6a9a"), Hex("#7ab0d8"), Color.white);
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
        if (IslandGame.IsPowerFruit(it)) { DrawFruitIcon(c, it); return c.ToSprite(8, 8); }
        switch (it)
        {
            case Item.AncientRelic:
                c.Rect(4, 2, 8, 12, Hex("#8a98a8")); c.Rect(10, 2, 2, 12, Hex("#5a6878"));
                for (int y = 4; y < 13; y += 3) c.Rect(5, y, 4, 1, Hex("#7ff5e6"));
                break;
            case Item.Trident:
                c.Line(2, 15, 11, 6, Hex("#c8a040")); c.Line(3, 15, 12, 6, Hex("#8a6a20"));
                c.Line(11, 6, 14, 1, Hex("#e8e8f0")); c.Line(9, 4, 10, 1, Hex("#e8e8f0")); c.Line(13, 8, 15, 6, Hex("#e8e8f0"));
                c.Rect(9, 5, 4, 2, Hex("#c8a040")); c.Set(11, 6, Hex("#7ff5e6"));
                break;
            case Item.IceShard:
                c.Line(4, 14, 8, 2, Hex("#a8d8f8")); c.Line(5, 14, 9, 2, Hex("#6aa8d8")); c.Line(9, 14, 12, 6, Hex("#a8d8f8")); c.Line(10, 14, 13, 6, Hex("#6aa8d8"));
                c.Set(7, 5, Color.white); c.Set(11, 8, Color.white);
                break;
            case Item.Fur:
                c.Ball(8, 9, 6.5f, 4.5f, Hex("#4a6a8a"), Hex("#8aa8c8"), Hex("#d8e8f8"));
                for (int x = 3; x <= 13; x += 2) c.Set(x, 13, Hex("#4a6a8a"));
                break;
            case Item.SpikedBoots:
                c.Rect(3, 4, 5, 8, Hex("#6b4226")); c.Rect(3, 10, 9, 3, Hex("#6b4226")); c.Rect(3, 4, 5, 1, Hex("#f4f4f4"));
                for (int x = 3; x <= 11; x += 2) c.Set(x, 13, Hex("#c8c8d0"));
                break;
            case Item.Gear:
                Cog(c, 8, 8, 6, Hex("#6a4a20"), Hex("#c8962a"));
                break;
            case Item.TimeSand:
                for (int y = 2; y <= 14; y++) { int hw = Mathf.Abs(y - 8) / 2 + 1; c.Rect(8 - hw, y, hw * 2, 1, y < 8 ? Hex("#f2c43a") : Hex("#b8862a")); }
                c.Rect(3, 1, 10, 1, Hex("#8a6a3a")); c.Rect(3, 15, 10, 1, Hex("#8a6a3a"));
                break;
            case Item.Sundial:
                c.Ball(8, 10, 7, 4, Hex("#8a8a90"), Hex("#c8c8d0"), Hex("#f0f0f8"));
                c.Line(8, 10, 8, 2, Hex("#b8862a")); c.Line(8, 2, 11, 9, Hex("#b8862a"));
                for (int i = 0; i < 6; i++) c.Set(3 + i * 2, 11 - (i % 2), Hex("#5a5a60"));
                break;
            case Item.Pearl:
                c.Ball(8, 8, 5, 5, Hex("#b8a8c0"), Hex("#f0e8f4"), Color.white);
                c.Set(6, 6, Color.white); c.Set(7, 6, Color.white);
                break;
            case Item.Coral:
                c.Line(8, 15, 8, 4, Hex("#e05a6a")); c.Line(8, 9, 3, 4, Hex("#e05a6a")); c.Line(8, 8, 13, 3, Hex("#e05a6a"));
                c.Line(9, 15, 9, 5, Hex("#a83a4a")); c.Set(3, 4, Hex("#ff9aa8")); c.Set(13, 3, Hex("#ff9aa8")); c.Set(8, 4, Hex("#ff9aa8"));
                break;
            case Item.DivingBell:
                c.Ball(8, 9, 6, 6, Hex("#8a6a20"), Hex("#c8a040"), Hex("#f0d890"));
                c.Rect(2, 12, 13, 3, Hex("#8a6a20")); c.Ball(8, 9, 2, 2, Hex("#3a6a9a"), Hex("#7ab0d8"), Color.white);
                c.Rect(7, 1, 2, 3, Hex("#5a5a60"));
                break;
            case Item.Feather:
                c.Line(3, 14, 12, 2, Hex("#c8a040"));
                for (int i = 0; i < 9; i++) { c.Line(4 + i, 13 - i * 4 / 3, 2 + i, 9 - i * 4 / 3, Hex("#4f8ad8")); c.Line(5 + i, 13 - i * 4 / 3, 8 + i, 12 - i * 4 / 3, Hex("#a8d0ff")); }
                break;
            case Item.CloudCotton:
                c.Ball(5, 9, 4, 3.5f, Hex("#c8d8f0"), Hex("#eef4ff"), Color.white);
                c.Ball(11, 9, 4, 3.5f, Hex("#c8d8f0"), Hex("#eef4ff"), Color.white);
                c.Ball(8, 6, 4, 3.5f, Hex("#c8d8f0"), Hex("#eef4ff"), Color.white);
                break;
            case Item.Balloon:
                c.Ball(8, 6, 6, 6, Hex("#a83a1e"), Hex("#e0602c"), Hex("#ffb070"));
                c.Rect(7, 1, 2, 10, Hex("#f2c43a"));
                c.Line(4, 10, 6, 13, RopeC); c.Line(12, 10, 10, 13, RopeC);
                c.Rect(5, 13, 6, 3, Bark);
                break;
            case Item.ShellShard:
                c.Ball(6, 9, 4, 3.5f, Hex("#c08a7a"), Hex("#f2c8b8"), Hex("#fff0e8"));
                c.Ball(11, 6, 3, 3, Hex("#c08a7a"), Hex("#f2c8b8"), Hex("#fff0e8"));
                c.Line(3, 9, 9, 9, Hex("#d8a090"));
                break;
            case Item.CanvasSail:
                c.Rect(4, 1, 1, 14, Bark);
                for (int y = 2; y <= 12; y++) c.Rect(5, y, Mathf.Max(1, 10 - Mathf.Abs(y - 7)), 1, (y / 2) % 2 == 0 ? Hex("#f0e6c8") : Hex("#d8cca8"));
                c.Rect(2, 14, 12, 1, RopeC);
                break;
            case Item.GlowSpore:
                c.Ball(6, 9, 3.5f, 3.5f, Hex("#1a7a70"), Hex("#2fb8a8"), Hex("#7ff5e6"));
                c.Ball(11, 6, 2.5f, 2.5f, Hex("#1a7a70"), Hex("#2fb8a8"), Hex("#d8fff8"));
                c.Ball(11, 12, 2, 2, Hex("#1a7a70"), Hex("#7ff5e6"), Color.white);
                break;
            case Item.Antidote:
                c.Rect(6, 1, 4, 3, Hex("#8a5a36"));                     // cork
                c.Ball(8, 10, 5, 5, Hex("#2a6a3a"), Hex("#4fbf6a"), Hex("#b8ffc8")); // green potion
                c.Rect(7, 4, 2, 2, Hex("#cfe8e0"));
                c.Set(6, 8, Color.white);
                break;
            case Item.MushroomLantern:
                c.Rect(7, 0, 2, 3, Bark); c.Rect(4, 2, 8, 1, Bark);      // handle
                c.Rect(4, 3, 8, 11, Hex("#3e2b1a")); c.Rect(5, 4, 6, 9, Hex("#7ff5e6"));
                c.Ball(8, 9, 2, 2, Hex("#2fb8a8"), Hex("#7ff5e6"), Color.white);
                c.Rect(4, 14, 8, 1, Bark);
                break;
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
