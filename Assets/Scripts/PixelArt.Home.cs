using UnityEngine;

/// Art for the private island: farm plots and crops, fences, paths, decorations, the cottage,
/// farm animals, the fishing float, and icons for the farm / fishing / building items.
/// Same style as the rest of PixelArt: drawn in world pixels on the Detail grid, shaded blobs
/// lit from the top-left, and the automatic dark rim.
public static partial class PixelArt
{
    static Color Wood => Hex("#a8784a");
    static Color WoodD => Hex("#7a5232");
    static Color WoodL => Hex("#d0a46e");

    /// Every private-island sprite (for the art sheet / exporter).
    static void HomePlaceholders()
    {
        Item?[] seeds = { null, Item.CarrotSeed, Item.TomatoSeed, Item.WheatSeed };
        foreach (var s in seeds)
            for (int st = 0; st <= 3; st++)
            {
                if ((s == null) != (st == 0)) continue;
                Plot(s, st, false); Plot(s, st, true);
            }
        Fence(); StonePath(); Bench(); Scarecrow(); Signpost(); Bobber(); Doormat();
        Bed(); Table(); Chair(); Rug(); PlantPot(); Shelf(); Workbench();
        for (int l = 1; l <= 3; l++) { Cottage(l); RoomWall(l, false); RoomWall(l, true); for (int v = 0; v < 3; v++) FloorTile(l, v); }
        for (int v = 0; v < 3; v++) { Flowers(v); Chicken(v); Cow(v); }
    }

    // ------------------------------------------------------------------ farm plot and crops
    /// Tilled soil on one tile (darker when watered) with the crop at its growth stage:
    /// 0 empty, 1 seeds, 2 sprouts, 3 ripe.
    public static Sprite Plot(Item? seed, int stage, bool wet) => Get($"plot_{(seed?.ToString() ?? "none").ToLowerInvariant()}_{stage}_{(wet ? "wet" : "dry")}", () =>
    {
        var c = new Canvas(32, 34);
        const float cx = 16f, cy = 26f, rx = 13f, ry = 6.5f;
        Color soil = Hex(wet ? "#5c3b22" : "#8a5a34"), soilD = Hex(wet ? "#432a17" : "#6b4527"), soilL = Hex(wet ? "#77512f" : "#ad7746");
        for (int y = 0; y < c.FH; y++)
            for (int x = 0; x < c.FW; x++)
            {
                float wx = (x + 0.5f) / Detail - cx, wy = (y + 0.5f) / Detail - cy;
                float e = Mathf.Abs(wx) / rx + Mathf.Abs(wy) / ry;
                if (e > 1f) continue;
                float f = Mathf.Repeat(wy - 0.5f * wx, 3.2f); // furrows run along the tile's x axis
                Color col = f < 0.9f ? soilD : f < 1.7f ? soilL : soil;
                if (e > 0.88f) col = soilD;
                else if (Hash(x, y, 41) < 0.06f) col = Color.Lerp(col, soilD, 0.6f);
                c.SetFine(x, y, col);
            }
        if (stage <= 0 || seed == null) return c.ToSprite(cx, cy);

        // two rows of three plants, back row first
        Color g = Hex("#4f9a3f"), gL = Hex("#8fd06a"), gD = Hex("#2f6a28");
        foreach (float s in new[] { -1.6f, 1.6f })
            foreach (float t in new[] { -6f, 0f, 6f })
            {
                int px = Mathf.RoundToInt(cx + t), py = Mathf.RoundToInt(cy + s + 0.5f * t);
                if (stage == 1) { c.Set(px, py, Hex("#e6d0a0")); c.Set(px + 1, py, Hex("#b89a6a")); continue; }
                if (stage == 2)
                {
                    c.Line(px, py, px, py - 2, g); c.Set(px - 1, py - 2, gL); c.Set(px + 1, py - 3, gL);
                    continue;
                }
                switch (seed.Value)
                {
                    case Item.CarrotSeed:
                        c.Line(px, py - 1, px - 2, py - 5, g); c.Line(px, py - 1, px, py - 6, gL); c.Line(px, py - 1, px + 2, py - 5, gD);
                        c.Rect(px - 1, py - 1, 3, 2, Hex("#e8742a")); c.Set(px - 1, py - 1, Hex("#ffa860"));
                        break;
                    case Item.TomatoSeed:
                        c.Line(px, py, px, py - 3, gD);
                        c.Ball(px, py - 5, 3f, 3f, gD, g, gL);
                        c.Ball(px - 1.2f, py - 4.5f, 1.4f, 1.4f, Hex("#9a1a12"), Hex("#e03a2a"), Hex("#ff8a6a"));
                        c.Ball(px + 1.5f, py - 6f, 1.3f, 1.3f, Hex("#9a1a12"), Hex("#e03a2a"), Hex("#ff8a6a"));
                        break;
                    default: // wheat
                        Color stalk = Hex("#b8923a"), head = Hex("#e8c058"), headD = Hex("#b88a2a");
                        for (int k = -1; k <= 1; k++)
                        {
                            int hx = px + k * 2;
                            c.Line(px + k, py, hx, py - 7, stalk);
                            c.Ellipse(hx + 0.5f, py - 8.5f, 1.1f, 2.4f, headD);
                            c.Ellipse(hx + 0.2f, py - 8.8f, 0.7f, 2f, head);
                        }
                        break;
                }
            }
        return c.ToSprite(cx, cy);
    });

    // ------------------------------------------------------------------ building pieces
    /// One fence piece along the tile's x axis; flipped it runs along y. Pieces on neighbouring
    /// tiles share their end posts, so a row of them reads as one fence.
    public static Sprite Fence() => Get("fence", () =>
    {
        var c = new Canvas(32, 30);
        const int gx0 = 8, gy0 = 18, gx1 = 24, gy1 = 26; // ground line from the back post to the front post
        c.Line(gx0 + 1, gy0 + 1, gx1 + 1, gy1 + 1, Shadow);
        void Post(int x, int y)
        {
            c.Rect(x - 1, y - 12, 3, 12, Wood); c.Rect(x + 1, y - 12, 1, 12, WoodD); c.Rect(x - 1, y - 12, 1, 12, WoodL);
            c.Rect(x - 1, y - 13, 3, 1, WoodL);
        }
        Post(gx0, gy0);
        foreach (int h in new[] { 4, 9 })
        {
            c.Line(gx0, gy0 - h - 1, gx1, gy1 - h - 1, WoodL);
            c.Line(gx0, gy0 - h, gx1, gy1 - h, Wood);
            c.Line(gx0, gy0 - h + 1, gx1, gy1 - h + 1, WoodD);
        }
        Post(16, 22); // middle post
        Post(gx1, gy1);
        return c.ToSprite(16, 22);
    });

    /// Cobblestones filling a tile, flat on the ground.
    public static Sprite StonePath() => Get("stone_path", () =>
    {
        var c = new Canvas(32, 16) { Rim = false };
        int n = 16 * Detail;
        for (int r = 0; r < n; r++)
            for (int x = 0; x < 32 * Detail; x++)
            {
                if (!InDiamondFine(x, r, 0.94f)) continue;
                // diamond-shaped cells aligned with the tile
                float u = (x + 2f * r) / 11f, v = (x - 2f * r + 200f) / 11f;
                int cu = Mathf.FloorToInt(u), cv = Mathf.FloorToInt(v);
                float fu = u - cu, fv = v - cv;
                bool mortar = fu < 0.14f || fv < 0.14f;
                float h = Hash(cu, cv, 77);
                Color stone = Color.Lerp(Hex("#8e8a84"), Hex("#b8b2a6"), h);
                if (fu + fv < 0.55f) stone = Color.Lerp(stone, Color.white, 0.18f);      // lit edge
                else if (fu + fv > 1.55f) stone = Color.Lerp(stone, Hex("#5a5650"), 0.35f); // shaded edge
                c.SetFine(x, r, mortar ? Hex("#5e5a52") : stone);
            }
        return c.ToSprite(16, 8);
    });

    /// A little bed of flowers (three colour mixes).
    public static Sprite Flowers(int v) => Get("flowers_" + v, () =>
    {
        var c = new Canvas(24, 20);
        c.Ellipse(12, 15, 10, 4, Hex("#5a3a22")); c.Ellipse(12, 14.5f, 8.5f, 3, Hex("#6e4a2c"));
        Color[][] mixes =
        {
            new[] { Hex("#f06a9a"), Hex("#ffd84a"), Hex("#ffffff") },
            new[] { Hex("#9a6af0"), Hex("#f0a0e0"), Hex("#ffd84a") },
            new[] { Hex("#e8442a"), Hex("#ff9a3a"), Hex("#fff0a0") },
        };
        var mix = mixes[v % mixes.Length];
        Color g = Hex("#4f9a3f"), gD = Hex("#2f6a28"), gL = Hex("#8fd06a");
        float[,] at = { { 6, 12 }, { 11, 11 }, { 17, 12 }, { 8, 15 }, { 14, 15 }, { 19, 15 }, { 12, 17 } };
        for (int i = 0; i < at.GetLength(0); i++)
        {
            int x = (int)at[i, 0], y = (int)at[i, 1];
            c.Line(x, y, x, y - 4, gD); c.Set(x - 1, y - 2, gL); c.Set(x + 1, y - 1, g);
            Color p = mix[i % 3];
            c.Ball(x, y - 5, 1.8f, 1.6f, Color.Lerp(p, Color.black, 0.35f), p, Color.Lerp(p, Color.white, 0.5f));
            c.Set(x, y - 5, Hex("#ffe14a"));
        }
        return c.ToSprite(12, 15);
    });

    /// Wooden garden bench along the tile's x axis.
    public static Sprite Bench() => Get("bench", () =>
    {
        var c = new Canvas(30, 24);
        c.Ellipse(15, 18, 12, 3, Shadow);
        // legs
        foreach (var (i, j) in new[] { (1, 0), (1, 4), (17, 0), (17, 4) })
        {
            int x = 5 + i - j, y = 9 + (i + j) / 2;
            c.Rect(x, y + 1, 1, 5, WoodD);
        }
        // seat: planks along x, 5 px deep along y
        for (int i = 0; i <= 18; i++)
            for (int j = 0; j <= 4; j++)
            {
                int x = 5 + i - j, y = 9 + Mathf.FloorToInt((i + j) * 0.5f);
                c.Set(x, y, j % 2 == 0 ? WoodL : Wood);
                if (j == 4) c.Set(x, y + 1, WoodD);
            }
        // backrest along the back edge
        for (int i = 0; i <= 18; i++)
        {
            int x = 5 + i, y = 9 + i / 2;
            c.Set(x, y - 4, WoodL); c.Set(x, y - 3, Wood); c.Set(x, y - 6, Wood); c.Set(x, y - 5, WoodD);
            if (i % 6 == 0) c.Rect(x, y - 6, 1, 6, WoodD);
        }
        return c.ToSprite(15, 17);
    });

    /// A straw scarecrow (crops around it grow faster).
    public static Sprite Scarecrow() => Get("scarecrow", () =>
    {
        var c = new Canvas(24, 38);
        c.Ellipse(12, 35, 6, 1.6f, Shadow);
        c.BarkRect(11, 12, 2, 23, Bark, BarkD);
        c.Rect(3, 16, 18, 2, Bark); c.Rect(3, 17, 18, 1, BarkD);
        Color straw = Hex("#e8c66a"), strawD = Hex("#b8943a");
        c.Line(2, 17, 0, 20, straw); c.Line(21, 17, 23, 20, strawD); // straw hands
        c.Rect(7, 15, 10, 10, Hex("#c0392b")); c.Rect(13, 15, 4, 10, Hex("#962a1e"));      // shirt
        c.Rect(9, 18, 3, 3, Hex("#3a6aa8"));                                               // patch
        c.Line(8, 25, 7, 28, straw); c.Line(12, 25, 12, 29, strawD); c.Line(15, 25, 16, 28, straw);
        c.Ball(12, 10, 4, 4, Hex("#9a7a48"), Hex("#c8a46a"), Hex("#e8cc94"));                // sack head
        c.Set(10, 10, Hex("#2a1a10")); c.Set(14, 10, Hex("#2a1a10")); c.Line(10, 12, 14, 12, Hex("#6a4a28"));
        c.Rect(5, 6, 14, 2, straw); c.Rect(8, 2, 8, 4, straw); c.Rect(13, 2, 3, 4, strawD); c.Rect(8, 5, 8, 1, Hex("#b5452c"));
        return c.ToSprite(12, 35);
    });

    /// Your house on the private island, by level: 1 log cabin with a thatched roof, 2 a bigger
    /// timber house with a shingle roof and a porch lamp, 3 a stone house with a slate roof.
    public static Sprite Cottage(int level) => Get("cottage_" + Mathf.Clamp(level, 1, 3), () =>
    {
        int L = Mathf.Clamp(level, 1, 3);
        int W = L == 1 ? 58 : L == 2 ? 66 : 74;
        var c = new Canvas(W, 66);
        int cx = W / 2, ground = 59;
        int left = 8, front = W - 28, wallTop = L == 1 ? 37 : 34; // front face from left to front, side face to W-8
        c.Ellipse(cx, ground + 1, W / 2 - 4, 5, Shadow);
        // walls: front face lit, side face shaded
        for (int y = wallTop; y < ground; y++)
        {
            int k = (y - wallTop) % 4;
            Color f, s;
            if (L == 3) // stone blocks
            {
                bool mortar = k == 3;
                int off = ((y - wallTop) / 4) % 2 == 0 ? 0 : 3;
                f = mortar ? Hex("#8a8478") : Hex("#c8c0b0");
                s = mortar ? Hex("#6a645a") : Hex("#9a9284");
                c.Rect(left, y, front - left, 1, f); c.Rect(front, y, W - 8 - front, 1, s);
                if (!mortar) for (int x = left + off; x < W - 8; x += 6) c.Set(x, y, x < front ? Hex("#8a8478") : Hex("#6a645a"));
                continue;
            }
            f = k == 3 ? WoodD : k == 0 ? WoodL : Wood;
            s = k == 3 ? Hex("#5a3a22") : Hex("#8a6038");
            if (L == 2) { f = k == 3 ? Hex("#8a5a34") : k == 0 ? Hex("#e8c08a") : Hex("#d0a06a"); s = k == 3 ? Hex("#6a4426") : Hex("#a87848"); }
            c.Rect(left, y, front - left, 1, f); c.Rect(front, y, W - 8 - front, 1, s);
        }
        c.Rect(front - 1, wallTop, 1, ground - wallTop, Hex("#4a3020"));
        // door and windows
        int dx = left + (front - left) / 2 - 4;
        c.Rect(dx, ground - 15, 9, 15, Hex("#4a2a18")); c.Rect(dx + 1, ground - 14, 7, 14, L == 3 ? Hex("#5a3a6a") : Hex("#7a4a2a"));
        c.Rect(dx + 4, ground - 14, 1, 14, Hex("#4a2a18")); c.Set(dx + 7, ground - 7, Hex("#f2c43a"));
        foreach (int wx in new[] { left + 2, front + 4 })
        {
            int wy = ground - 17;
            c.Rect(wx, wy, 7, 6, Hex("#4a3020"));
            c.Rect(wx + 1, wy + 1, 5, 4, wx < front ? Hex("#ffe08a") : Hex("#d8b860"));
            c.Rect(wx + 3, wy + 1, 1, 4, Hex("#4a3020")); c.Rect(wx + 1, wy + 3, 5, 1, Hex("#4a3020"));
            c.Rect(wx, wy + 6, 7, 2, Hex("#6e4a2c"));
            c.Set(wx + 1, wy + 5, Hex("#f06a9a")); c.Set(wx + 3, wy + 5, Hex("#ffd84a")); c.Set(wx + 5, wy + 5, Hex("#f06a9a"));
        }
        if (L >= 2) // porch lamp
        {
            c.Rect(dx + 11, ground - 16, 3, 4, Hex("#3a3a40")); c.Rect(dx + 12, ground - 15, 1, 2, Hex("#ffd27a"));
        }
        // roof
        Color r, rD, rL;
        if (L == 1) { r = Hex("#c89a4a"); rD = Hex("#9a7030"); rL = Hex("#e6c274"); }
        else if (L == 2) { r = Hex("#b5452c"); rD = Hex("#8a3020"); rL = Hex("#d86a4a"); }
        else { r = Hex("#4a5a7a"); rD = Hex("#323e58"); rL = Hex("#6a7c9e"); }
        int peak = 6;
        for (int y = peak; y <= wallTop + 1; y++)
        {
            int hw = (y - peak) + 4;
            for (int x = cx - hw; x <= cx + hw; x++)
            {
                if (x < 1 || x > W - 2) continue;
                Color col = x > cx ? rD : r;
                if (L == 1) { if (x <= cx && Hash(x, y / 2, 9) < 0.25f) col = rL; if ((y - peak) % 5 == 4) col = Color.Lerp(col, rD, 0.6f); }
                else
                {
                    bool row = (y - peak) % 3 == 2;
                    int off = ((y - peak) / 3) % 2 == 0 ? 0 : 2;
                    if (row) col = Color.Lerp(col, Color.black, 0.25f);
                    else if ((x + off) % 4 == 0) col = Color.Lerp(col, Color.black, 0.15f);
                    else if (x <= cx && (y - peak) % 3 == 0) col = rL;
                }
                c.Set(x, y, col);
            }
        }
        c.Rect(1, wallTop + 2, W - 2, 1, Color.Lerp(rD, Color.black, 0.3f)); // eave
        int chx = cx + 10;
        c.Rect(chx, peak + 2, 5, 12, StoneM); c.Rect(chx + 3, peak + 2, 2, 12, StoneD); c.Rect(chx - 1, peak + 1, 7, 2, StoneL); // chimney
        if (L == 3) { c.Rect(cx - 2, peak + 10, 5, 5, Hex("#4a3020")); c.Rect(cx - 1, peak + 11, 3, 3, Hex("#ffe08a")); } // attic window
        return c.ToSprite(cx, ground - 1);
    });

    // ------------------------------------------------------------------ inside the house
    /// Wooden floorboards filling one tile (stone flags in the stone house).
    public static Sprite FloorTile(int level, int v) => Get($"floor_{Mathf.Clamp(level, 1, 3)}_{v}", () =>
    {
        var c = new Canvas(32, 16) { Rim = false };
        int L = Mathf.Clamp(level, 1, 3);
        Color a = L == 3 ? Hex("#a89c8a") : L == 2 ? Hex("#c08a5a") : Hex("#a8784a");
        Color b = Color.Lerp(a, Color.black, 0.12f), seam = Color.Lerp(a, Color.black, 0.4f), hi = Color.Lerp(a, Color.white, 0.15f);
        int n = 16 * Detail;
        for (int r = 0; r < n; r++)
            for (int x = 0; x < 32 * Detail; x++)
            {
                if (!InDiamondFine(x, r, 1f)) continue;
                float wx = (x + 0.5f) / Detail - 16f, wy = (r + 0.5f) / Detail - 8f;
                float s = wy - 0.5f * wx + 40f; // boards run along the tile's x axis
                int plank = Mathf.FloorToInt(s / 4f);
                float f = s / 4f - plank;
                Color col = (plank + v) % 2 == 0 ? a : b;
                if (L == 3) { float t = wy + 0.5f * wx + 40f; if (Mathf.Repeat(t, 8f) < 0.6f) col = seam; }
                else if (Hash(plank, Mathf.FloorToInt((wx + 0.5f * wy) / 9f), 13 + v) < 0.12f && Mathf.Repeat(wx + 0.5f * wy, 9f) < 0.5f) col = seam; // board ends
                if (f < 0.12f) col = seam;
                else if (f < 0.25f) col = hi;
                c.SetFine(x, r, col);
            }
        return c.ToSprite(16, 8);
    });

    /// A back wall panel standing on a tile's edge (flip it for the other wall), with a window sometimes.
    public static Sprite RoomWall(int level, bool window) => Get($"room_wall_{Mathf.Clamp(level, 1, 3)}_{(window ? 1 : 0)}", () =>
    {
        const int H = 40;
        var c = new Canvas(32, H + 18) { Rim = false };
        int L = Mathf.Clamp(level, 1, 3);
        Color paper = L == 3 ? Hex("#d8d0e8") : L == 2 ? Hex("#e8d8b0") : Hex("#c8a070");
        Color paperD = Color.Lerp(paper, Color.black, 0.12f), trim = L == 3 ? Hex("#6a5a8a") : Hex("#6b4226");
        // bottom edge from (0, H+8) to (16, H+16): the edge shared with the room tile in front
        for (int x = 0; x < 16 * Detail; x++)
        {
            float wx = (x + 0.5f) / Detail;
            float bottom = H + 8 + wx * 0.5f;
            for (int y = Mathf.FloorToInt((bottom - H) * Detail); y < Mathf.CeilToInt(bottom * Detail); y++)
            {
                float wy = (y + 0.5f) / Detail, up = bottom - wy; // height above the floor
                Color col = ((int)(wx / 3f)) % 2 == 0 ? paper : paperD;             // striped wallpaper
                if (L == 1) col = Mathf.Repeat(up, 4f) < 0.6f ? Hex("#8a6038") : paper; // log wall
                if (up < 3f) col = trim;                                               // skirting board
                else if (up > H - 2.5f) col = Color.Lerp(trim, Color.white, 0.15f);    // top trim
                if (window && wx > 4f && wx < 12f && up > 16f && up < 30f)
                {
                    col = up > 22.5f && up < 23.5f || Mathf.Abs(wx - 8f) < 0.5f ? trim : Hex("#9ad0f0");
                    if (wx < 7f && up > 25f) col = Hex("#c8e8ff");
                }
                c.SetFine(x, y, col);
            }
        }
        return c.ToSprite(16, H + 8);
    });

    /// Iso box helper: fills a top surface along the tile's x axis (len) and y axis (depth),
    /// starting at (ox, oy) for the back corner, raised by `lift` pixels.
    static void IsoTop(Canvas c, int ox, int oy, int len, int depth, Func2 col)
    {
        for (int i = 0; i <= len; i++)
            for (int j = 0; j <= depth; j++)
                c.Set(ox + i - j, oy + Mathf.FloorToInt((i + j) * 0.5f), col(i, j));
    }
    delegate Color Func2(int i, int j);

    public static Sprite Bed() => Get("furn_bed", () =>
    {
        var c = new Canvas(34, 28);
        c.Ellipse(17, 21, 13, 3, Shadow);
        IsoTop(c, 12, 11, 18, 8, (i, j) => WoodD);                                  // frame side
        for (int i = 0; i <= 18; i++) for (int k = 1; k <= 3; k++) c.Set(12 + i - 8, 11 + (i + 8) / 2 + k, WoodD); // front side
        for (int j = 0; j <= 8; j++) for (int k = 1; k <= 3; k++) c.Set(12 + 18 - j, 11 + (18 + j) / 2 + k, Hex("#5a3a22"));
        IsoTop(c, 12, 9, 18, 8, (i, j) => i < 5 ? (j > 1 && j < 7 ? Color.white : Hex("#e8e4dc")) // pillow on the mattress
                                         : (j + i) % 5 == 0 ? Hex("#c05a7a") : Hex("#e07a9a")); // pink quilt
        for (int k = 0; k <= 8; k++) c.Rect(12 - k, 1 + k / 2, 1, 10, k % 4 == 0 ? WoodD : Wood); // headboard
        return c.ToSprite(17, 19);
    });

    public static Sprite Table() => Get("furn_table", () =>
    {
        var c = new Canvas(28, 24);
        c.Ellipse(14, 19, 10, 2.5f, Shadow);
        foreach (var (lx, ly) in new[] { (5, 11), (17, 17), (21, 11), (9, 7) }) c.Rect(lx, ly, 1, 7, WoodD);
        IsoTop(c, 12, 5, 12, 8, (i, j) => (i + j) % 4 == 0 ? WoodD : (j < 2 ? WoodL : Wood));
        for (int i = 0; i <= 12; i++) c.Set(12 + i - 8, 5 + (i + 8) / 2 + 1, Hex("#5a3a22"));
        c.Ball(14, 8, 1.6f, 1.4f, Hex("#2a5a90"), Hex("#4a8ad0"), Hex("#a0c8f0")); // a cup
        return c.ToSprite(14, 17);
    });

    public static Sprite Chair() => Get("furn_chair", () =>
    {
        var c = new Canvas(18, 24);
        c.Ellipse(9, 20, 6, 1.8f, Shadow);
        foreach (var (lx, ly) in new[] { (4, 15), (10, 18), (13, 15) }) c.Rect(lx, ly, 1, 4, WoodD);
        IsoTop(c, 9, 12, 5, 5, (i, j) => j < 2 ? WoodL : Wood);
        for (int i = 0; i <= 5; i++) { c.Rect(9 + i, 3 + i / 2, 1, 10, i % 2 == 0 ? Wood : WoodD); }
        c.Rect(9, 5, 6, 1, WoodL);
        return c.ToSprite(9, 18);
    });

    public static Sprite Rug() => Get("furn_rug", () =>
    {
        var c = new Canvas(32, 16) { Rim = false };
        int n = 16 * Detail;
        for (int r = 0; r < n; r++)
            for (int x = 0; x < 32 * Detail; x++)
            {
                if (!InDiamondFine(x, r, 0.9f)) continue;
                float half = 16f * Detail, d = Mathf.Abs(x + 0.5f - half) / half + Mathf.Abs(r + 0.5f - half / 2f) / (half / 2f);
                Color col = d > 0.78f ? Hex("#f2c43a") : d > 0.66f ? Hex("#8a2030") : d > 0.36f ? Hex("#c0394a") : d > 0.26f ? Hex("#f2c43a") : Hex("#2a5a90");
                if (d > 0.8f && ((x / 3 + r / 3) % 2 == 0)) col = Hex("#e8a020"); // fringe
                c.SetFine(x, r, col);
            }
        return c.ToSprite(16, 8);
    });

    public static Sprite PlantPot() => Get("furn_plant", () =>
    {
        var c = new Canvas(16, 26);
        c.Ellipse(8, 23, 5, 1.5f, Shadow);
        c.Rect(4, 16, 8, 7, Hex("#c0603a")); c.Rect(9, 16, 3, 7, Hex("#9a4a2a")); c.Rect(3, 15, 10, 2, Hex("#d87a4a"));
        c.Leaves(8, 9, 5.5f, 6, Hex("#2f6a28"), Hex("#4f9a3f"), Hex("#8fd06a"));
        return c.ToSprite(8, 22);
    });

    public static Sprite Shelf() => Get("furn_shelf", () =>
    {
        var c = new Canvas(22, 32);
        c.Ellipse(11, 29, 8, 2, Shadow);
        c.Rect(2, 2, 18, 27, WoodD); c.Rect(3, 3, 16, 25, Hex("#4a2c18"));
        Color[] books = { Hex("#c0392b"), Hex("#2e86c1"), Hex("#27ae60"), Hex("#f2c43a"), Hex("#8e44ad") };
        for (int s = 0; s < 3; s++)
        {
            int sy = 3 + s * 8;
            c.Rect(3, sy + 7, 16, 1, Wood);
            for (int b = 0, bx = 4; bx < 17; b++)
            {
                int bw = 1 + (b + s) % 2, bh = 4 + (b * 3 + s) % 3;
                c.Rect(bx, sy + 7 - bh, bw, bh, books[(b + s * 2) % books.Length]);
                bx += bw + (b % 3 == 2 ? 2 : 0);
            }
        }
        c.Rect(2, 2, 18, 1, WoodL);
        return c.ToSprite(11, 28);
    });

    public static Sprite Workbench() => Get("furn_workbench", () =>
    {
        var c = new Canvas(32, 28);
        c.Ellipse(16, 23, 12, 3, Shadow);
        foreach (var (lx, ly) in new[] { (4, 14), (18, 21), (24, 14), (10, 10) }) c.Rect(lx, ly, 2, 7, WoodD);
        IsoTop(c, 13, 8, 14, 8, (i, j) => (i * 3 + j) % 7 == 0 ? WoodD : (j < 2 ? WoodL : Wood));
        for (int i = 0; i <= 14; i++) for (int k = 1; k <= 2; k++) c.Set(13 + i - 8, 8 + (i + 8) / 2 + k, Hex("#5a3a22"));
        c.Line(9, 11, 15, 14, StoneL); c.Line(9, 12, 15, 15, StoneM);                     // saw blade
        c.Rect(15, 14, 2, 2, Bark);
        c.Rect(19, 9, 4, 2, StoneM); c.Line(21, 11, 23, 15, Bark);                          // hammer
        c.Rect(13, 7, 3, 3, RopeC);                                                          // rope coil
        return c.ToSprite(16, 21);
    });

    /// Doormat at the way out of the room.
    public static Sprite Doormat() => Get("doormat", () =>
    {
        var c = new Canvas(32, 16);
        int n = 16 * Detail;
        for (int r = 0; r < n; r++)
            for (int x = 0; x < 32 * Detail; x++)
            {
                if (!InDiamondFine(x, r, 0.62f)) continue;
                c.SetFine(x, r, ((x + r) / 3) % 2 == 0 ? Hex("#8a6a3a") : Hex("#a8844a"));
            }
        c.Line(13, 9, 19, 12, Hex("#fff0c0")); c.Line(17, 12, 19, 12, Hex("#fff0c0")); c.Line(19, 10, 19, 12, Hex("#fff0c0")); // arrow out
        return c.ToSprite(16, 8);
    });

    /// Wooden signpost.
    public static Sprite Signpost() => Get("signpost", () =>
    {
        var c = new Canvas(20, 28);
        c.Ellipse(10, 25, 5, 1.5f, Shadow);
        c.BarkRect(9, 9, 2, 16, Bark, BarkD);
        c.Rect(2, 3, 16, 8, WoodD); c.Rect(3, 4, 14, 6, WoodL); c.Rect(3, 9, 14, 1, Wood);
        c.Rect(5, 5, 9, 1, Hex("#5a3a22")); c.Rect(5, 7, 6, 1, Hex("#5a3a22"));
        return c.ToSprite(10, 25);
    });

    // ------------------------------------------------------------------ farm animals
    /// Hen. frame 0 standing, 1-2 walking.
    public static Sprite Chicken(int frame) => Get("chicken_" + frame, () =>
    {
        var c = new Canvas(16, 16);
        c.Ellipse(8, 14, 4.5f, 1.3f, Shadow);
        Color leg = Hex("#e8a020");
        int a = frame == 1 ? 1 : 0, b = frame == 2 ? 1 : 0;
        c.Line(7, 11, 7 - a, 14, leg); c.Line(9, 11, 9 + b, 14, leg);
        int bob = frame == 0 ? 0 : 1;
        c.Ball(4, 7, 2, 2.4f, Hex("#b8b0a8"), Hex("#ece6de"), Color.white);                 // tail
        c.Ball(8, 9, 4.4f, 3.4f, Hex("#c8c0b6"), Hex("#f6f2ea"), Color.white);              // body
        c.Line(6, 9, 9, 10, Hex("#c8c0b6"));                                               // wing
        c.Ball(11, 5 + bob, 2.3f, 2.3f, Hex("#c8c0b6"), Hex("#f6f2ea"), Color.white);       // head
        c.Set(11, 2 + bob, Hex("#e0382a")); c.Set(12, 2 + bob, Hex("#e0382a")); c.Set(10, 3 + bob, Hex("#e0382a"));
        c.Set(13, 5 + bob, Hex("#f0a020")); c.Set(13, 6 + bob, Hex("#d08010"));
        c.Set(12, 7 + bob, Hex("#e0382a"));                                                 // wattle
        c.Set(11, 5 + bob, Hex("#1a1010"));
        return c.ToSprite(8, 14);
    });

    /// Dairy cow. frame 0 standing, 1-2 walking.
    public static Sprite Cow(int frame) => Get("cow_" + frame, () =>
    {
        var c = new Canvas(32, 26);
        c.Ellipse(15, 23, 11, 2.2f, Shadow);
        Color hide = Hex("#f4f0ea"), hideD = Hex("#c8c0b6"), spot = Hex("#2a2622"), hoof = Hex("#4a3a30");
        int[] legX = { 7, 10, 19, 22 };
        for (int i = 0; i < 4; i++)
        {
            int lift = (frame == 1 && i % 2 == 0) || (frame == 2 && i % 2 == 1) ? 1 : 0;
            c.Rect(legX[i], 15, 2, 7 - lift, i % 2 == 0 ? hide : hideD); c.Rect(legX[i], 21 - lift, 2, 1, hoof);
        }
        c.Line(5, 10, 4, 17, hideD); c.Rect(3, 17, 2, 2, spot);                             // tail
        c.Ball(14, 11, 9.5f, 5.5f, hideD, hide, Color.white);                               // body
        c.Ellipse(10, 9, 2.8f, 2.2f, spot); c.Ellipse(17, 12, 3.2f, 2.2f, spot); c.Ellipse(13, 14.5f, 1.6f, 1f, spot);
        c.Ellipse(15, 16, 2f, 1.2f, Hex("#f0a8b0"));                                        // udder
        c.Ball(25, 8, 3.8f, 3.4f, hideD, hide, Color.white);                                // head
        c.Ellipse(27.5f, 10.5f, 2.2f, 1.7f, Hex("#f0a8b0")); c.Set(27, 10, Hex("#8a4a50")); c.Set(29, 10, Hex("#8a4a50"));
        c.Set(25, 7, Hex("#1a1010"));
        c.Set(22, 4, Hex("#e8e0c8")); c.Set(23, 4, Hex("#e8e0c8")); c.Set(27, 4, Hex("#e8e0c8")); c.Set(28, 4, Hex("#e8e0c8")); // horns
        c.Set(21, 6, spot); c.Set(29, 6, spot);                                             // ears
        return c.ToSprite(15, 22);
    });

    /// The fishing float bobbing on the water.
    public static Sprite Bobber() => Get("bobber", () =>
    {
        var c = new Canvas(8, 10);
        c.Ellipse(4, 8, 3, 1, new Color(1f, 1f, 1f, 0.45f)); // ripple
        c.Ball(4, 5, 2.2f, 2.4f, Hex("#9a1a12"), Hex("#e03a2a"), Hex("#ff8a6a"));
        c.Rect(2, 3, 5, 2, Color.white);
        c.Line(4, 2, 4, 0, Hex("#3a3a40"));
        return c.ToSprite(4, 7);
    });

    // ------------------------------------------------------------------ icons
    static bool DrawHomeIcon(Canvas c, Item it)
    {
        Color g = Hex("#4f9a3f"), gL = Hex("#8fd06a"), gD = Hex("#2f6a28");
        var steam = new Color(1f, 1f, 1f, 0.6f);
        switch (it)
        {
            case Item.CarrotSeed:
            case Item.TomatoSeed:
            case Item.WheatSeed:
                Color band = it == Item.CarrotSeed ? Hex("#e8742a") : it == Item.TomatoSeed ? Hex("#e03a2a") : Hex("#e8c058");
                c.Rect(3, 2, 10, 13, Hex("#c8b48a")); c.Rect(4, 3, 8, 11, Hex("#eee2c4")); c.Rect(4, 3, 8, 3, band);
                c.Rect(3, 1, 10, 1, Hex("#a8946a"));
                c.Ball(8, 10, 2.5f, 2.5f, Color.Lerp(band, Color.black, 0.35f), band, Color.Lerp(band, Color.white, 0.5f));
                c.Line(8, 7, 8, 6, g); c.Set(7, 6, gL);
                return true;
            case Item.Carrot:
                for (int y = 5; y <= 14; y++) { int hw = Mathf.Max(1, Mathf.RoundToInt((14 - y) * 0.35f + 0.6f)); c.Rect(8 - hw, y, hw * 2, 1, y % 3 == 0 ? Hex("#c45a1a") : Hex("#f08a30")); }
                c.Set(7, 7, Hex("#ffc080"));
                c.Line(8, 5, 5, 1, g); c.Line(8, 5, 8, 0, gL); c.Line(8, 5, 11, 1, gD);
                return true;
            case Item.Tomato:
                c.Ball(8, 9, 5.5f, 5f, Hex("#9a1a12"), Hex("#e03a2a"), Hex("#ff8a6a"));
                c.Set(8, 4, g); c.Set(7, 4, gL); c.Set(9, 4, gD); c.Set(8, 3, gD); c.Set(6, 5, g); c.Set(10, 5, g);
                return true;
            case Item.Wheat:
                for (int k = -1; k <= 1; k++)
                {
                    c.Line(8, 15, 8 + k * 3, 6, Hex("#b8923a"));
                    c.Ellipse(8.5f + k * 3, 4.5f, 1.3f, 3.2f, Hex("#b88a2a")); c.Ellipse(8.2f + k * 3, 4.2f, 0.8f, 2.6f, Hex("#e8c058"));
                }
                c.Rect(6, 11, 5, 2, Hex("#c0392b"));
                return true;
            case Item.Egg:
                c.Ball(8, 9, 4.3f, 5.3f, Hex("#c8b49a"), Hex("#fbf0e0"), Color.white);
                return true;
            case Item.Milk:
                c.Rect(5, 5, 7, 10, Hex("#e6e8ee")); c.Rect(10, 5, 2, 10, Hex("#c0c4cc"));
                c.Rect(6, 2, 5, 3, Hex("#e6e8ee")); c.Rect(6, 1, 5, 1, Hex("#2e86c1"));
                c.Rect(5, 9, 7, 3, Hex("#2e86c1")); c.Set(6, 6, Color.white);
                return true;
            case Item.CookedEgg:
                c.Ellipse(8, 9, 6.5f, 5f, Hex("#e8e0d0")); c.Ellipse(7.5f, 8.5f, 5.5f, 4f, Hex("#fbf6ec"));
                c.Ball(8, 8.5f, 2.5f, 2.3f, Hex("#c87a08"), Hex("#f6b820"), Hex("#ffe080"));
                c.Line(4, 3, 4, 1, steam); c.Line(12, 3, 12, 1, steam);
                return true;
            case Item.Salad:
                c.Ball(5, 8, 3, 2.5f, gD, g, gL); c.Ball(10, 7, 3.2f, 2.6f, gD, g, gL); c.Ball(8, 9, 3, 2f, gD, gL, Hex("#c8f0a0"));
                c.Ball(6, 8, 1.3f, 1.3f, Hex("#9a1a12"), Hex("#e03a2a"), Hex("#ff8a6a")); c.Rect(10, 8, 2, 1, Hex("#f08a30"));
                c.Ellipse(8, 11, 7, 3.5f, Hex("#8a5a35")); c.Ellipse(8, 10.5f, 6, 1.5f, Hex("#c79a64"));
                c.Ball(5, 8, 1.5f, 1.5f, gD, g, gL); c.Ball(11, 8, 1.2f, 1.2f, Hex("#9a1a12"), Hex("#e03a2a"), Hex("#ff8a6a"));
                return true;
            case Item.Tuna:
            case Item.GoldenFish:
                bool gold = it == Item.GoldenFish;
                Color fd = Hex(gold ? "#a8780a" : "#1a3a6a"), fm = Hex(gold ? "#f2c43a" : "#3a6aa8"), fl = Hex(gold ? "#fff0a0" : "#b0d0ee");
                c.Ball(8.5f, 9, 6.3f, 3.6f, fd, fm, fl);
                c.Line(1, 5, 3, 9, fd); c.Line(1, 13, 3, 9, fd); c.Line(2, 6, 2, 12, fm);
                c.Line(7, 5, 10, 4, fd); c.Set(13, 8, Color.black);
                if (gold) { c.Set(4, 2, Color.white); c.Set(13, 3, Color.white); c.Set(12, 13, Hex("#fff0a0")); }
                else c.Line(5, 10, 11, 10, Hex("#d0e4f4"));
                return true;
            case Item.Hoe:
                c.Line(2, 15, 11, 5, Bark); c.Line(3, 15, 12, 5, BarkD);
                c.Rect(10, 2, 5, 3, StoneM); c.Rect(10, 4, 5, 1, StoneD); c.Rect(13, 5, 2, 2, StoneD); c.Set(10, 2, StoneL);
                return true;
            case Item.WateringCan:
                c.Ball(7, 10, 5, 4, Hex("#2a5a90"), Hex("#4a8ad0"), Hex("#a0c8f0"));
                c.Line(11, 9, 15, 4, Hex("#2a5a90")); c.Rect(14, 3, 2, 2, Hex("#4a8ad0"));
                c.Ring(6, 6, 3, 1.2f, Hex("#2a5a90"));
                c.Set(15, 7, Hex("#9ad0ff")); c.Set(14, 9, Hex("#9ad0ff"));
                return true;
            case Item.FishingRod:
                c.Line(2, 15, 13, 2, Bark); c.Line(3, 15, 14, 2, BarkD);
                c.Ring(5, 12, 2, 1.2f, Hex("#5a5a60"));
                for (int y = 3; y <= 11; y++) c.SetFine(28, y * Detail, Hex("#e0e0e8"));
                c.Set(14, 11, Hex("#c8c8d0")); c.Set(13, 12, Hex("#c8c8d0"));
                c.Ball(14, 7, 1.2f, 1.2f, Hex("#9a1a12"), Hex("#e03a2a"), Hex("#ff8a6a"));
                return true;
            case Item.Hammer:
                c.Line(3, 15, 9, 6, Bark); c.Line(4, 15, 10, 6, BarkD);
                c.Rect(6, 2, 9, 4, StoneM); c.Rect(6, 5, 9, 1, StoneD); c.Rect(6, 2, 9, 1, StoneL); c.Rect(13, 2, 2, 4, StoneD);
                return true;
            case Item.Fertilizer:
                c.Rect(3, 4, 10, 11, Hex("#8a6a3a")); c.Rect(10, 4, 3, 11, Hex("#6a4e28")); c.Rect(3, 3, 10, 2, Hex("#a8844a"));
                c.Ellipse(8, 4, 4, 1.5f, Hex("#4a3018"));
                c.Line(6, 8, 6, 12, g); c.Set(5, 9, gL); c.Set(7, 10, gL); c.Rect(4, 13, 8, 1, Hex("#5a4020"));
                return true;
            case Item.AnimalFeed:
                c.Ellipse(8, 11, 6.5f, 3.5f, Hex("#8a5a35")); c.Ellipse(8, 10.5f, 5.5f, 1.8f, Hex("#e8c058"));
                for (int k = 0; k < 6; k++) c.Set(4 + k * 2 - (k % 2), 9 + k % 2, Hex("#b88a2a"));
                c.Line(5, 9, 4, 3, Hex("#b8923a")); c.Line(10, 9, 12, 3, Hex("#b8923a")); c.Ellipse(4, 3, 1, 2, Hex("#e8c058")); c.Ellipse(12, 3, 1, 2, Hex("#e8c058"));
                return true;
            case Item.Plank:
                c.Rect(1, 4, 14, 4, WoodL); c.Rect(1, 7, 14, 1, WoodD); c.Rect(5, 5, 4, 1, Wood);
                c.Rect(2, 9, 13, 4, Wood); c.Rect(2, 12, 13, 1, WoodD); c.Rect(8, 10, 5, 1, WoodD);
                return true;
        }
        return false;
    }
}
