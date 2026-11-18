using UnityEngine;

/// Pixel-art pictures of the four continents, side by side along the top of the sea chart, so
/// you can tell their weather apart at a glance: hot sun over palm beaches, snow falling on ice
/// mountains, a smoking volcano under a red sky, and islands floating in the sky over a dark deep
/// sea with sunken ruins. The still picture is drawn once into a small texture; the weather on
/// top of it (sun rays, snow, smoke and embers, drifting clouds and bubbles) moves every frame.
public partial class IslandGame
{
    const int ConW = 80, ConH = 38;
    static readonly Texture2D[] continentArt = new Texture2D[Continents];
    static readonly bool[][] continentOpen = new bool[Continents][]; // pixels still plain sky / sea
    static readonly int[] ContinentHorizon = { 16, 16, 16, 17 };
    static readonly string[] ContinentWeather = { "แดดจ้า ร้อนชื้น", "หิมะตก หนาวจัด", "ร้อนระอุ เถ้าถ่านปลิว", "ลมแรงบนฟ้า ทะเลลึกมืด" };

    static float ConNoise(int x, int y, int seed)
    {
        unchecked
        {
            int n = x * 374761393 + y * 668265263 + seed * 982451653;
            n = (n ^ (n >> 13)) * 1274126177;
            return ((n ^ (n >> 16)) & 0xffff) / 65535f;
        }
    }

    static Color Rgb(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

    /// The still picture of continent c (the editor's Island > Export Continent Art saves them).
    public static Texture2D ContinentArt(int c)
    {
        if (continentArt[c] != null) return continentArt[c];
        const int w = ConW, h = ConH;
        var px = new Color[w * h];
        var open = new bool[w * h];
        int hz = ContinentHorizon[c];
        var rng = new System.Random(c * 101 + 7);

        // y goes down from the top here; texture rows go up, so flip on write
        void P(int x, int y, Color col)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = (h - 1 - y) * w + x;
            px[i] = col.a >= 1f ? col : Color.Lerp(px[i], new Color(col.r, col.g, col.b, 1f), col.a);
            open[i] = false;
        }
        float N(int x, int y) => ConNoise(x, y, c);
        bool Blob(int x, int y, float cx, float cy, float rx, float ry, float wob = 0.25f)
        {
            float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
            return dx * dx + dy * dy < 1f + (N(x, y) - 0.5f) * wob;
        }
        void FillBlob(float cx, float cy, float rx, float ry, Color col, float wob = 0.25f)
        {
            for (int y = Mathf.FloorToInt(cy - ry - 1); y <= cy + ry + 1; y++)
                for (int x = Mathf.FloorToInt(cx - rx - 1); x <= cx + rx + 1; x++)
                    if (Blob(x, y, cx, cy, rx, ry, wob)) P(x, y, col);
        }

        // ---- sky and sea
        Color skyTop, skyLow, seaTop, seaLow;
        switch (c)
        {
            case 0: skyTop = Rgb(0.28f, 0.6f, 0.95f); skyLow = Rgb(0.72f, 0.9f, 1f); seaTop = Rgb(0.25f, 0.8f, 0.82f); seaLow = Rgb(0.08f, 0.48f, 0.7f); break;
            case 1: skyTop = Rgb(0.5f, 0.58f, 0.7f); skyLow = Rgb(0.84f, 0.88f, 0.94f); seaTop = Rgb(0.3f, 0.45f, 0.58f); seaLow = Rgb(0.1f, 0.18f, 0.3f); break;
            case 2: skyTop = Rgb(0.16f, 0.06f, 0.09f); skyLow = Rgb(0.9f, 0.4f, 0.16f); seaTop = Rgb(0.36f, 0.14f, 0.12f); seaLow = Rgb(0.07f, 0.04f, 0.07f); break;
            default: skyTop = Rgb(0.42f, 0.62f, 1f); skyLow = Rgb(0.84f, 0.9f, 1f); seaTop = Rgb(0.12f, 0.42f, 0.72f); seaLow = Rgb(0.02f, 0.05f, 0.18f); break;
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color col = y < hz ? Color.Lerp(skyTop, skyLow, y / (float)(hz - 1))
                                   : Color.Lerp(seaTop, seaLow, (y - hz) / (float)(h - 1 - hz));
                if (y >= hz && ConNoise(x / 3, y, c + 9) > 0.86f) col = Color.Lerp(col, Color.white, 0.16f); // ripples
                if (y == hz) col = Color.Lerp(col, Color.white, 0.12f);
                int i = (h - 1 - y) * w + x;
                px[i] = col;
                open[i] = true;
            }

        switch (c)
        {
            case 0: // ---- tropical: blazing sun, turquoise shallows, palm beaches
            {
                for (int y = 0; y < 14; y++)
                    for (int x = 58; x < w; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(68f, 6f));
                        if (d < 4.2f) P(x, y, Rgb(1f, 0.92f, 0.4f));
                        else if (d < 5.6f) P(x, y, Rgb(1f, 0.97f, 0.72f, 0.75f));
                        else if (d < 8f) P(x, y, Rgb(1f, 0.95f, 0.7f, 0.25f));
                    }
                FillBlob(16f, 6.5f, 7f, 2f, Rgb(1f, 1f, 1f), 0.1f);
                FillBlob(20f, 5f, 4.5f, 2.2f, Rgb(1f, 1f, 1f), 0.1f);
                for (int x = 10; x < 24; x++) if (Blob(x, 8, 16f, 6.5f, 7f, 2f, 0.1f)) P(x, 8, Rgb(0.86f, 0.93f, 1f));

                FillBlob(34f, 26f, 26f, 9f, Rgb(0.45f, 0.93f, 0.85f, 0.8f));
                FillBlob(66f, 30f, 10f, 4.5f, Rgb(0.45f, 0.93f, 0.85f, 0.8f));
                FillBlob(34f, 26f, 22f, 7f, Rgb(0.96f, 0.88f, 0.6f));
                FillBlob(66f, 30f, 7f, 2.8f, Rgb(0.96f, 0.88f, 0.6f));
                for (int y = 17; y < 34; y++)
                    for (int x = 10; x < 58; x++)
                        if (Blob(x, y, 34f, 25.5f, 18.5f, 5f, 0.3f)) P(x, y, N(x, y) > 0.72f ? Rgb(0.2f, 0.55f, 0.2f) : Rgb(0.3f, 0.7f, 0.28f));
                FillBlob(66f, 29.6f, 4f, 1.4f, Rgb(0.3f, 0.7f, 0.28f), 0.1f);

                void Palm(int bx, int by, int hgt, int lean)
                {
                    int tx = bx, ty = by;
                    for (int i = 0; i < hgt; i++)
                    {
                        tx = bx + Mathf.RoundToInt(lean * (i / (float)hgt) * (i / (float)hgt));
                        ty = by - i;
                        P(tx, ty, i % 2 == 0 ? Rgb(0.52f, 0.34f, 0.18f) : Rgb(0.42f, 0.27f, 0.14f));
                    }
                    var leaf = Rgb(0.16f, 0.58f, 0.2f); var leafHi = Rgb(0.38f, 0.8f, 0.3f);
                    for (int k = 1; k <= 4; k++)
                    {
                        int droop = k >= 3 ? k - 2 : 0;
                        P(tx - k, ty + droop, k == 1 ? leafHi : leaf); P(tx + k, ty + droop, k == 1 ? leafHi : leaf);
                        if (k <= 3) { P(tx - k, ty - 1 + droop, leafHi); P(tx + k, ty - 1 + droop, leafHi); }
                    }
                    P(tx, ty - 1, leafHi); P(tx, ty - 2, leaf);
                    P(tx - 1, ty + 1, Rgb(0.4f, 0.25f, 0.1f)); P(tx + 1, ty + 1, Rgb(0.4f, 0.25f, 0.1f));
                }
                Palm(24, 25, 9, -3); Palm(35, 24, 11, 2); Palm(46, 26, 8, 3); Palm(66, 30, 6, 1);
                // a little hut with a straw roof
                for (int y = 26; y <= 28; y++) for (int x = 28; x <= 31; x++) P(x, y, Rgb(0.6f, 0.42f, 0.24f));
                for (int y = 23; y <= 25; y++) for (int x = 28 - (y - 23); x <= 31 + (y - 23); x++) P(x, y, Rgb(0.86f, 0.72f, 0.36f));
                P(29, 28, Rgb(0.3f, 0.18f, 0.1f)); P(29, 27, Rgb(0.3f, 0.18f, 0.1f));
                break;
            }
            case 1: // ---- ice: snow peaks, an ice shelf, icebergs, overcast cold sky
            {
                for (int y = 0; y < hz; y++)
                    for (int x = 0; x < w; x++)
                        if (N(x, y) > 0.965f) P(x, y, Rgb(1f, 1f, 1f, 0.8f));
                void Peak(int cx, int top, int hw, int baseY)
                {
                    for (int y = top; y <= baseY; y++)
                    {
                        float half = (y - top) / (float)(baseY - top) * hw;
                        for (int x = Mathf.FloorToInt(cx - half); x <= cx + half; x++)
                        {
                            bool shade = x > cx + (y - top) * 0.15f;
                            Color col = shade ? Rgb(0.7f, 0.79f, 0.92f) : Rgb(0.97f, 0.99f, 1f);
                            if (y > top + 4 && N(x, y) > 0.86f) col = shade ? Rgb(0.42f, 0.47f, 0.58f) : Rgb(0.58f, 0.63f, 0.72f);
                            P(x, y, col);
                        }
                    }
                }
                Peak(56, 6, 13, 22); Peak(22, 5, 14, 22); Peak(38, 1, 16, 22);
                FillBlob(38f, 25f, 31f, 4f, Rgb(0.55f, 0.78f, 0.93f));
                FillBlob(38f, 23f, 30f, 3.6f, Rgb(0.93f, 0.97f, 1f));
                for (int x = 9; x < 70; x++) if (N(x, 27) > 0.6f && !Blob(x, 27, 38f, 23f, 30f, 3.6f)) P(x, 26, Rgb(0.42f, 0.66f, 0.85f, 0.7f));
                void Berg(int x0, int y0, int wd, int ht)
                {
                    for (int r = 0; r < ht; r++)
                        for (int x = x0 + (ht - 1 - r) / 2; x < x0 + wd - (ht - 1 - r) / 2; x++)
                            P(x, y0 - (ht - 1 - r), r == 0 ? Rgb(1f, 1f, 1f) : x > x0 + wd / 2 ? Rgb(0.62f, 0.82f, 0.95f) : Rgb(0.85f, 0.95f, 1f));
                    for (int x = x0; x < x0 + wd; x++) P(x, y0 + 1, Rgb(0.2f, 0.36f, 0.52f, 0.8f));
                }
                Berg(4, 31, 7, 3); Berg(68, 30, 8, 4); Berg(56, 35, 5, 2); Berg(17, 35, 6, 3);
                // penguins on the shelf
                foreach (int x in new[] { 28, 30, 47 }) { P(x, 21, Rgb(0.08f, 0.1f, 0.14f)); P(x, 22, Rgb(0.08f, 0.1f, 0.14f)); P(x + 1, 22, Rgb(0.95f, 0.95f, 0.95f)); P(x + 1, 21, Rgb(1f, 0.7f, 0.2f)); }
                break;
            }
            case 2: // ---- volcano: red sky, a smoking cone with lava rivers, a dark ashy sea
            {
                for (int y = 0; y < hz; y++)
                    for (int x = 20; x < 60; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y * 1.4f), new Vector2(40f, 7f));
                        if (d < 18f) P(x, y, Rgb(1f, 0.55f, 0.2f, (1f - d / 18f) * 0.45f));
                    }
                for (int y = 0; y < hz; y++) for (int x = 0; x < w; x++) if (N(x, y) > 0.975f) P(x, y, Rgb(0.95f, 0.6f, 0.3f, 0.7f)); // ash
                for (int y = 6; y <= 28; y++)
                {
                    float half = 4f + (y - 6) / 22f * 27f + (N(0, y) - 0.5f) * 2f;
                    for (int x = Mathf.FloorToInt(40 - half); x <= 40 + half; x++)
                    {
                        bool shade = x > 40 + (y - 6) * 0.35f;
                        Color col = shade ? Rgb(0.16f, 0.11f, 0.11f) : Rgb(0.28f, 0.19f, 0.17f);
                        if (N(x, y) > 0.84f) col = Rgb(0.38f, 0.26f, 0.22f);
                        P(x, y, col);
                    }
                }
                for (int x = 36; x <= 44; x++) { P(x, 6, Rgb(1f, 0.82f, 0.35f)); P(x, 7, Rgb(1f, 0.5f, 0.12f)); }
                foreach (int start in new[] { 37, 41, 44 })
                {
                    int x = start, dir = start < 40 ? -1 : start > 42 ? 1 : 0;
                    for (int y = 8; y <= 28; y++)
                    {
                        int step = rng.Next(4);
                        if (step == 0) x += dir == 0 ? (rng.Next(2) * 2 - 1) : dir;
                        if (step == 1 && dir != 0) x += dir;
                        P(x, y, rng.Next(4) == 0 ? Rgb(1f, 0.85f, 0.35f) : Rgb(1f, 0.45f, 0.1f));
                        P(x + 1, y, Rgb(0.75f, 0.2f, 0.07f));
                    }
                    for (int k = -2; k <= 2; k++) P(x + k, 29, Rgb(1f, 0.5f, 0.15f, 0.7f));
                }
                for (int y = hz; y < h; y++) for (int x = 24; x < 58; x++) if (N(x, y) > 0.8f) P(x, y, Rgb(0.95f, 0.42f, 0.15f, 0.55f)); // lava glow on the water
                FillBlob(8f, 31f, 6f, 2.5f, Rgb(0.2f, 0.14f, 0.14f));
                FillBlob(73f, 33f, 6f, 2.2f, Rgb(0.2f, 0.14f, 0.14f));
                P(7, 29, Rgb(1f, 0.5f, 0.15f)); P(74, 31, Rgb(1f, 0.5f, 0.15f));
                break;
            }
            default: // ---- sky and deep sea: floating islands above, sunken ruins far below
            {
                FillBlob(42f, 13.5f, 7f, 1.6f, Rgb(1f, 1f, 1f, 0.9f), 0.1f);
                FillBlob(8f, 15f, 6f, 1.4f, Rgb(1f, 1f, 1f, 0.9f), 0.1f);
                void Floater(int cx, int top, int hw, int depth)
                {
                    for (int r = 1; r <= depth; r++)
                    {
                        float half = hw * (1f - r / (float)(depth + 1)) + (N(cx, r) - 0.5f);
                        for (int x = Mathf.FloorToInt(cx - half); x <= cx + half; x++)
                            P(x, top + r, x > cx ? Rgb(0.4f, 0.28f, 0.2f) : N(x, r) > 0.8f ? Rgb(0.62f, 0.5f, 0.4f) : Rgb(0.56f, 0.4f, 0.28f));
                    }
                    for (int x = cx - hw; x <= cx + hw; x++) { P(x, top, Rgb(0.36f, 0.8f, 0.36f)); P(x, top - 1, N(x, top) > 0.6f ? Rgb(0.26f, 0.66f, 0.28f) : Rgb(0f, 0f, 0f, 0f)); }
                    P(cx - 2, top - 1, Rgb(0.2f, 0.55f, 0.24f)); P(cx - 2, top - 2, Rgb(0.26f, 0.66f, 0.28f)); P(cx - 3, top - 2, Rgb(0.26f, 0.66f, 0.28f)); P(cx - 2, top - 3, Rgb(0.38f, 0.8f, 0.36f));
                }
                Floater(20, 6, 9, 6); Floater(55, 4, 7, 5); Floater(71, 11, 5, 3);
                for (int y = 8; y < hz; y++) P(28, y, y % 2 == 0 ? Rgb(0.78f, 0.92f, 1f) : Rgb(1f, 1f, 1f)); // waterfall off the edge
                P(27, hz, Rgb(1f, 1f, 1f)); P(29, hz, Rgb(1f, 1f, 1f));
                for (int y = hz + 1; y < hz + 12; y++) // light shafts into the deep
                    for (int x = 0; x < w; x++)
                        if ((x + y * 2) % 13 < 2) P(x, y, Rgb(0.6f, 0.85f, 1f, 0.12f * (1f - (y - hz) / 12f)));
                for (int y = 35; y < h; y++) for (int x = 0; x < w; x++) P(x, y, N(x, y) > 0.6f ? Rgb(0.12f, 0.16f, 0.26f) : Rgb(0.08f, 0.11f, 0.2f));
                foreach (var (cx, ht) in new[] { (48, 9), (55, 6), (62, 8) }) // sunken columns
                {
                    for (int y = 35 - ht; y < 35; y++) { P(cx, y, Rgb(0.22f, 0.32f, 0.48f)); P(cx + 1, y, Rgb(0.16f, 0.24f, 0.38f)); }
                    for (int x = cx - 1; x <= cx + 2; x++) P(x, 34 - ht, Rgb(0.26f, 0.38f, 0.55f));
                }
                for (int x = 47; x <= 64; x++) P(x, 25, Rgb(0.24f, 0.35f, 0.52f)); // the lintel across the first and last
                foreach (int sx in new[] { 6, 10, 26, 33, 72 }) // seaweed
                    for (int y = 34; y > 34 - 4 - sx % 4; y--) P(sx + ((y / 2) % 2), y, Rgb(0.12f, 0.5f, 0.38f));
                FillBlob(19f, 34.5f, 2.5f, 1.5f, Rgb(0.95f, 0.45f, 0.6f), 0.1f);
                FillBlob(70f, 34.5f, 2f, 1.3f, Rgb(1f, 0.6f, 0.4f), 0.1f);
                foreach (var (fx, fy) in new[] { (36, 24), (39, 26), (14, 28) }) { P(fx, fy, Rgb(1f, 0.88f, 0.35f)); P(fx - 1, fy, Rgb(0.95f, 0.6f, 0.2f)); }
                break;
            }
        }

        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        t.SetPixels(px);
        t.Apply();
        continentArt[c] = t;
        continentOpen[c] = open;
        return t;
    }

    /// Is this spot of the picture still plain sea (no land, ice or ruins drawn on it)?
    static bool ConSea(int c, float x, float y)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        if (ix < 0 || iy < 0 || ix >= ConW || iy >= ConH || iy <= ContinentHorizon[c]) return false;
        return continentOpen[c][(ConH - 1 - iy) * ConW + ix];
    }

    /// One continent's picture with its weather moving on it, its name, and how far you've been.
    /// Returns true when it was clicked.
    bool ContinentCard(Rect r, int c, bool on, string sub)
    {
        var tex = ContinentArt(c);
        float s = Mathf.Max(r.width / ConW, r.height / ConH); // fill the card, cropping the overflow
        var img = new Rect((r.width - ConW * s) / 2f, (r.height - ConH * s) / 2f, ConW * s, ConH * s);
        float t = Time.unscaledTime;
        bool hover = r.Contains(Event.current.mousePosition);
        void Box(float x, float y, float bw, float bh, Color col) => Fill(new Rect(img.x + x * s, img.y + y * s, bw * s, bh * s), col);

        CrispGUI.BeginGroup(r);
        GUI.DrawTexture(img, tex, ScaleMode.StretchToFill);
        switch (c)
        {
            case 0: // the sun burns and the water sparkles
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f + t * 0.3f, len = 6.8f + Mathf.Sin(t * 3f + k) * 0.9f;
                    Box(68f + Mathf.Cos(a) * len - 0.5f, 6f + Mathf.Sin(a) * len - 0.5f, 1f, 1f, Rgb(1f, 0.95f, 0.55f, 0.85f));
                }
                for (int i = 0; i < 14; i++)
                {
                    float gx = Mathf.Repeat(i * 17.3f + t * 1.2f, ConW), gy = 17f + (i * 7) % 21;
                    float a = 0.5f + 0.5f * Mathf.Sin(t * 3f + i * 1.7f);
                    if (ConSea(c, gx, gy) && ConSea(c, gx + 2f, gy)) Box(gx, gy, 2f, 1f, Rgb(1f, 1f, 1f, 0.6f * a));
                }
                break;
            case 1: // snow falls, blown sideways
                for (int i = 0; i < 34; i++)
                {
                    float sp = 3f + i % 4;
                    float fy = Mathf.Repeat(i * 11.3f + t * sp, ConH + 2f) - 1f;
                    float fx = Mathf.Repeat(i * 23.7f + Mathf.Sin(t * 0.8f + i) * 2f - t * 2f, ConW);
                    float sz = i % 5 == 0 ? 1.5f : 1f;
                    Box(fx, fy, sz, sz, Rgb(1f, 1f, 1f, 0.9f));
                }
                break;
            case 2: // smoke pours out of the crater, embers fly, the lava glows
                Box(36f, 5f, 9f, 3f, Rgb(1f, 0.75f, 0.3f, 0.25f + 0.2f * Mathf.Sin(t * 4f)));
                for (int i = 0; i < 8; i++)
                {
                    float k = Mathf.Repeat(t * 0.3f + i / 8f, 1f);
                    float sz = 2f + k * 5f;
                    float sx = 40f + Mathf.Sin(i * 2.1f + k * 3f) * 2f + k * 9f, sy = 5f - k * 13f;
                    Box(sx - sz / 2f, sy - sz / 2f, sz, sz, Rgb(0.32f - k * 0.1f, 0.27f - k * 0.08f, 0.27f - k * 0.08f, 0.8f * (1f - k)));
                }
                for (int i = 0; i < 10; i++)
                {
                    float k = Mathf.Repeat(t * 0.7f + i * 0.137f, 1f);
                    float ex = 40f + ((i % 5) - 2) * 3f * k + Mathf.Sin(t * 5f + i) * 0.5f, ey = 6f - k * 11f + k * k * 6f;
                    Box(ex, ey, 1f, 1f, Rgb(1f, 0.6f + 0.3f * Mathf.Sin(t * 9f + i), 0.2f, 1f - k));
                }
                break;
            default: // clouds drift past the floating islands, bubbles rise from the deep
                for (int i = 0; i < 3; i++)
                {
                    float cx = Mathf.Repeat(i * 31f + t * (1.2f + i * 0.5f), ConW + 16f) - 12f, cy = 1f + i * 5f;
                    Box(cx, cy + 1f, 9f, 2f, Rgb(1f, 1f, 1f, 0.8f));
                    Box(cx + 2f, cy, 5f, 1f, Rgb(1f, 1f, 1f, 0.8f));
                }
                Box(28f, 8f + Mathf.Repeat(t * 9f, 8f), 1f, 2f, Rgb(1f, 1f, 1f, 0.9f));
                for (int i = 0; i < 16; i++)
                {
                    float k = Mathf.Repeat(t * 0.16f + i * 0.0625f + (i * 0.37f) % 0.05f, 1f);
                    float by = 36f - k * 18f, bx = (i * 13.7f) % ConW + Mathf.Sin(t * 2f + i) * 0.8f;
                    Box(bx, by, i % 4 == 0 ? 1.5f : 1f, i % 4 == 0 ? 1.5f : 1f, Rgb(0.8f, 0.95f, 1f, 0.75f * (1f - k * 0.5f)));
                }
                break;
        }
        if (!on) Fill(new Rect(0, 0, r.width, r.height), new Color(0f, 0f, 0f, hover ? 0.12f : 0.32f));

        // number and weather up top, name and progress along the bottom
        bool narrow = r.width < 150f;
        var top = new GUIStyle(small) { fontSize = narrow ? 9 : 10, richText = true, wordWrap = false };
        string tag = $"<b>{c + 1}</b>  {ContinentWeather[c]}";
        float tw = Mathf.Min(r.width - 8f, top.CalcSize(Lang.Content(tag)).x + 10f);
        Fill(new Rect(4, 4, tw, 16), new Color(0f, 0f, 0f, 0.65f));
        ShadowLabel(new Rect(9, 4, tw - 6, 16), tag, top, new Color(1f, 0.95f, 0.8f));
        float lh = Mathf.Min(34f, r.height * 0.42f);
        Fill(new Rect(0, r.height - lh, r.width, lh), new Color(0f, 0f, 0f, 0.55f));
        var name = new GUIStyle(smallCenter) { fontSize = narrow ? 11 : 13, richText = true, wordWrap = false, fontStyle = on ? FontStyle.Bold : FontStyle.Normal };
        var subSt = new GUIStyle(smallCenter) { fontSize = narrow ? 8 : 10, richText = true, wordWrap = false };
        ShadowLabel(new Rect(2, r.height - lh, r.width - 4, lh * 0.55f), ContinentNames[c], name, on ? new Color(1f, 0.9f, 0.5f) : Color.white);
        ShadowLabel(new Rect(2, r.height - lh * 0.5f, r.width - 4, lh * 0.45f), sub, subSt);
        CrispGUI.EndGroup();

        if (on)
        {
            Outline(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(1f, 0.85f, 0.3f, 0.75f + 0.25f * Mathf.Sin(t * 5f)));
            Fill(new Rect(r.x, r.yMax + 2, r.width, 3), ContinentColor[c]);
        }
        return CrispGUI.Button(r, GUIContent.none, GUIStyle.none);
    }
}
