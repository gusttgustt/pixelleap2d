using UnityEngine;

/// The look of the title screen (the character creator): wooden frames and a wooden sign, stone
/// buttons, dark slots for typing, small pixel icons, the chosen character standing on a little
/// island, and the ready-made characters as framed portraits. Everything is drawn from flat
/// rectangles, so it stays crisp at any HUD scale and needs no art files.
public partial class IslandGame
{
    // ---- palette
    static readonly Color WoodDark = new Color(0.1f, 0.06f, 0.03f);
    static readonly Color Wood = new Color(0.37f, 0.23f, 0.12f);
    static readonly Color WoodLight = new Color(0.52f, 0.35f, 0.18f);
    static readonly Color WoodShade = new Color(0.25f, 0.15f, 0.07f);
    static readonly Color TitleGold = new Color(1f, 0.83f, 0.25f);

    /// A small, steady hash: the same speckles and grain every frame.
    static int Hash(int a, int b) { unchecked { int h = a * 73856093 ^ b * 19349663; h ^= h >> 13; h *= 0x5bd1e995; return (h ^ (h >> 15)) & 0xff; } }

    static Color Dim(Color c) => GUI.enabled ? c : new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f, c.a);

    // ------------------------------------------------------------------ wood
    /// One plank (horizontal when wide, vertical when tall) with a light edge, a dark edge and grain.
    static void Plank(Rect r, int seed)
    {
        bool across = r.width >= r.height;
        Fill(r, Wood);
        if (across)
        {
            Fill(new Rect(r.x, r.y, r.width, 2), WoodLight);
            Fill(new Rect(r.x, r.yMax - 2, r.width, 2), WoodShade);
            for (int i = 0, n = Mathf.Max(1, (int)(r.width / 18f)); i < n; i++)
            {
                int h = Hash(seed, i);
                float gx = r.x + i * 18f + (h % 9), gy = r.y + 3 + (h >> 4) % Mathf.Max(1, (int)r.height - 6);
                Fill(new Rect(gx, gy, 6 + h % 7, 1), WoodShade);
            }
        }
        else
        {
            Fill(new Rect(r.x, r.y, 2, r.height), WoodLight);
            Fill(new Rect(r.xMax - 2, r.y, 2, r.height), WoodShade);
            for (int i = 0, n = Mathf.Max(1, (int)(r.height / 18f)); i < n; i++)
            {
                int h = Hash(seed, i);
                float gy = r.y + i * 18f + (h % 9), gx = r.x + 3 + (h >> 4) % Mathf.Max(1, (int)r.width - 6);
                Fill(new Rect(gx, gy, 1, 6 + h % 7), WoodShade);
            }
        }
    }

    static void Nail(float cx, float cy)
    {
        Fill(new Rect(cx - 2, cy - 2, 4, 4), new Color(0.12f, 0.1f, 0.08f));
        Fill(new Rect(cx - 1, cy - 1, 2, 2), new Color(0.62f, 0.6f, 0.55f));
    }

    /// A frame of four planks `t` thick around `r` (its outside edge), with tied corner blocks.
    static void WoodFrame(Rect r, float t, int seed = 0)
    {
        Fill(new Rect(r.x - 1, r.y - 1, r.width + 2, 1), WoodDark); Fill(new Rect(r.x - 1, r.yMax, r.width + 2, 1), WoodDark);
        Fill(new Rect(r.x - 1, r.y, 1, r.height), WoodDark); Fill(new Rect(r.xMax, r.y, 1, r.height), WoodDark);
        Plank(new Rect(r.x, r.y, r.width, t), seed + 1);
        Plank(new Rect(r.x, r.yMax - t, r.width, t), seed + 2);
        Plank(new Rect(r.x, r.y + t, t, r.height - 2 * t), seed + 3);
        Plank(new Rect(r.xMax - t, r.y + t, t, r.height - 2 * t), seed + 4);
        // the inner edge, darker: the frame sits above what it holds
        Fill(new Rect(r.x + t, r.y + t, r.width - 2 * t, 1), WoodDark);
        Fill(new Rect(r.x + t, r.yMax - t - 1, r.width - 2 * t, 1), WoodDark);
        Fill(new Rect(r.x + t, r.y + t, 1, r.height - 2 * t), WoodDark);
        Fill(new Rect(r.xMax - t - 1, r.y + t, 1, r.height - 2 * t), WoodDark);
        float c = t + 2;
        foreach (var p in new[] { new Vector2(r.x - 1, r.y - 1), new Vector2(r.xMax - c + 1, r.y - 1), new Vector2(r.x - 1, r.yMax - c + 1), new Vector2(r.xMax - c + 1, r.yMax - c + 1) })
        {
            Fill(new Rect(p.x, p.y, c, c), WoodDark);
            Fill(new Rect(p.x + 1, p.y + 1, c - 2, c - 2), WoodLight);
            Fill(new Rect(p.x + 1, p.y + c / 2f - 1, c - 2, 2), WoodShade); // rope wrapped across the corner
            if (t >= 8) Nail(p.x + c / 2f, p.y + c / 2f + 3);
        }
    }

    /// A signboard of planks laid across, nailed at the ends.
    static void WoodBoard(Rect r, int seed = 0)
    {
        Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), WoodDark);
        int rows = Mathf.Max(1, Mathf.RoundToInt(r.height / 16f));
        float ph = r.height / rows;
        for (int i = 0; i < rows; i++)
        {
            Plank(new Rect(r.x, r.y + i * ph, r.width, ph), seed + i * 7);
            if (i > 0) Fill(new Rect(r.x, r.y + i * ph, r.width, 1), WoodDark);
            Nail(r.x + 9, r.y + i * ph + ph / 2f);
            Nail(r.xMax - 9, r.y + i * ph + ph / 2f);
        }
    }

    /// A few leaves trailing from a frame corner (dx/dy = which way the vine grows).
    static void Vine(float x, float y, int dx, int dy)
    {
        var leaf = new Color(0.33f, 0.62f, 0.22f);
        var leafD = new Color(0.2f, 0.42f, 0.14f);
        for (int i = 0; i < 6; i++)
        {
            float px = x + dx * i * 5f, py = y + dy * (i * 7f + (i % 2) * 3f);
            Fill(new Rect(px, py, 3, 6), leafD);
            Fill(new Rect(px + (i % 2 == 0 ? 3 : -4), py + 1, 4, 3), leaf);
        }
    }

    // ------------------------------------------------------------------ stone buttons and slots
    /// A stone button: speckled, lit from above, a gold rim under the pointer. Draws the icon (if
    /// any) and the words, and returns true when clicked.
    bool StoneButton(Rect r, string text, Color tint, int fontSize = 14, string[] icon = null, Color? iconCol = null)
    {
        bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
        bool down = hover && UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
        Color body = Dim(hover ? Color.Lerp(tint, Color.white, 0.12f) : tint);
        Fill(r, hover ? new Color(1f, 0.82f, 0.3f) : new Color(0.08f, 0.07f, 0.07f));
        var inner = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
        Fill(inner, body);
        Fill(new Rect(inner.x, inner.y, inner.width, 2), Color.Lerp(body, Color.white, 0.28f));
        Fill(new Rect(inner.x, inner.yMax - 3, inner.width, 3), Color.Lerp(body, Color.black, 0.35f));
        int seed = (int)r.x * 31 + (int)r.y;
        for (int i = 0, n = (int)(r.width * r.height / 90f); i < n; i++)
        {
            int h = Hash(seed, i), h2 = Hash(i, seed);
            float sx = inner.x + 3 + h * (inner.width - 8) / 255f, sy = inner.y + 4 + h2 * (inner.height - 10) / 255f;
            Fill(new Rect(Mathf.Round(sx), Mathf.Round(sy), 2, 2), Color.Lerp(body, (h & 1) == 0 ? Color.black : Color.white, 0.16f));
        }
        float lift = down ? 1f : 0f;
        var words = new GUIStyle(labelCenter) { fontSize = fontSize };
        if (icon != null)
        {
            float ps = Mathf.Max(2f, Mathf.Floor((r.height - 14f) / icon.Length));
            float iw = icon[0].Length * ps, ih = icon.Length * ps;
            float tw = words.CalcSize(new GUIContent(Lang.English ? Lang.Tr(text) : text)).x;
            float total = iw + 12 + tw, sx = r.center.x - total / 2f;
            PixelIcon(sx, r.center.y - ih / 2f + lift, ps, icon, iconCol ?? Color.white, new Color(0.25f, 0.18f, 0.1f), TitleGold);
            ShadowLabel(new Rect(sx + iw + 12, r.y + lift, tw + 4, r.height), text, new GUIStyle(label) { fontSize = fontSize, alignment = TextAnchor.MiddleLeft }, Dim(Color.white));
        }
        else ShadowLabel(new Rect(r.x, r.y + lift, r.width, r.height), text, words, Dim(Color.white));
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    static GUIStyle slotStyle, slotCenter;
    static GUIStyle SlotStyle(bool center)
    {
        if (slotStyle == null)
        {
            Texture2D Tex(Color fill, Color edge)
            {
                var t = new Texture2D(6, 6, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[36];
                for (int y = 0; y < 6; y++) for (int x = 0; x < 6; x++)
                    px[y * 6 + x] = x == 0 || y == 0 || x == 5 || y == 5 ? edge : y == 4 ? Color.Lerp(fill, Color.black, 0.4f) : fill;
                t.SetPixels(px); t.Apply();
                return t;
            }
            slotStyle = new GUIStyle(GUI.skin.textField) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
            slotStyle.padding = new RectOffset(10, 8, 2, 2);
            slotStyle.normal.background = Tex(new Color(0.07f, 0.06f, 0.05f, 0.96f), new Color(0.33f, 0.24f, 0.14f));
            slotStyle.hover.background = Tex(new Color(0.09f, 0.08f, 0.07f, 0.97f), new Color(0.55f, 0.4f, 0.24f));
            slotStyle.focused.background = Tex(new Color(0.09f, 0.08f, 0.07f, 0.98f), new Color(1f, 0.82f, 0.3f));
            slotStyle.onNormal.background = slotStyle.normal.background; slotStyle.onFocused.background = slotStyle.focused.background;
            slotStyle.border = new RectOffset(2, 2, 2, 2);
            slotStyle.normal.textColor = slotStyle.hover.textColor = slotStyle.focused.textColor = Color.white;
            slotCenter = new GUIStyle(slotStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }
        return center ? slotCenter : slotStyle;
    }

    /// A dark slot to type into, with an optional icon block on the left and a faded hint while empty.
    string Slot(Rect r, string value, int max, string[] icon, string hint, bool password = false, bool center = false)
    {
        var field = r;
        if (icon != null)
        {
            var ib = new Rect(r.x, r.y, r.height + 4, r.height);
            Fill(ib, new Color(0.08f, 0.07f, 0.07f));
            Fill(new Rect(ib.x + 2, ib.y + 2, ib.width - 4, ib.height - 4), Dim(new Color(0.3f, 0.3f, 0.32f)));
            Fill(new Rect(ib.x + 2, ib.y + 2, ib.width - 4, 2), new Color(0.44f, 0.44f, 0.46f));
            float ps = Mathf.Max(2f, Mathf.Floor((r.height - 10f) / icon.Length));
            PixelIcon(ib.center.x - icon[0].Length * ps / 2f, ib.center.y - icon.Length * ps / 2f, ps, icon,
                new Color(0.9f, 0.9f, 0.92f), new Color(0.25f, 0.25f, 0.28f), TitleGold);
            field = new Rect(ib.xMax + 4, r.y, r.width - ib.width - 4, r.height);
        }
        var st = SlotStyle(center);
        value = password ? GUI.PasswordField(field, value ?? "", '*', max, st) : GUI.TextField(field, value ?? "", max, st);
        if (string.IsNullOrEmpty(value) && hint != null)
        {
            var h = new GUIStyle(small) { alignment = center ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, fontSize = 14 };
            CrispGUI.Label(new Rect(field.x + (center ? 0 : 10), field.y, field.width - (center ? 0 : 10), field.height), hint, Tinted(h, new Color(1f, 1f, 1f, 0.38f)));
        }
        return value;
    }

    static GUIStyle Tinted(GUIStyle s, Color c) { s.normal.textColor = c; return s; }

    /// A gold heading with a dark edge.
    void Heading(Rect r, string text, TextAnchor align = TextAnchor.MiddleLeft) =>
        ShadowLabel(r, text, new GUIStyle(label) { fontSize = 15, alignment = align }, TitleGold);

    // ------------------------------------------------------------------ pixel icons
    // X = main colour, o = second colour, k = accent
    static readonly string[] IconPerson = {
        "..XXXX..", ".XXXXXX.", ".XXXXXX.", ".XXXXXX.", "..XXXX..", "........", ".XXXXXX.", "XXXXXXXX", "XXXXXXXX" };
    static readonly string[] IconLock = {
        "..XXXX..", ".X....X.", ".X....X.", ".X....X.", "XXXXXXXX", "XXXooXXX", "XXXooXXX", "XXXXXXXX", "XXXXXXXX" };
    static readonly string[] IconPen = {
        "......XX", ".....XkX", "....XXX.", "...XXX..", "..XXX...", ".XXX....", "ooX.....", "oo......" };
    static readonly string[] IconGlobe = {
        "...XXXXX...", ".XX..X..XX.", ".X..X.X..X.", "X..X...X..X", "XXXXXXXXXXX", "X..X...X..X",
        "X..X...X..X", "XXXXXXXXXXX", "X..X...X..X", ".X..X.X..X.", ".XX..X..XX.", "...XXXXX..." };
    static readonly string[] IconDoor = {
        "XXXXXXXXX", "XoooooooX", "XoXXXXXoX", "XoX...XoX", "XoXXXXXoX", "XoooooooX", "XooooookX",
        "XoooooooX", "XoXXXXXoX", "XoX...XoX", "XoXXXXXoX", "XoooooooX", "XXXXXXXXX" };
    static readonly string[] IconTurn = {
        "..XXXX..", ".X....X.", "X......X", "X....XXX", "X.....X.", ".X......", "..XXX..." };

    static void PixelIcon(float x, float y, float ps, string[] rows, Color main, Color second, Color accent)
    {
        x = Mathf.Round(x); y = Mathf.Round(y);
        for (int j = 0; j < rows.Length; j++)
            for (int i = 0; i < rows[j].Length; i++)
            {
                char ch = rows[j][i];
                if (ch == '.') continue;
                Fill(new Rect(x + i * ps, y + j * ps, ps, ps), Dim(ch == 'X' ? main : ch == 'o' ? second : accent));
            }
    }

    // ------------------------------------------------------------------ the stage
    /// Sky, sea and a small grassy island inside `r`; returns where the character's feet go.
    static Vector2 IslandStage(Rect r)
    {
        const float p = 4f; // one art pixel
        // sky fading into a lighter horizon
        int bands = 6;
        float seaY = r.y + r.height * 0.42f;
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)(bands - 1);
            Fill(new Rect(r.x, r.y + (seaY - r.y) * i / bands, r.width, (seaY - r.y) / bands + 1),
                Color.Lerp(new Color(0.34f, 0.62f, 0.9f), new Color(0.68f, 0.86f, 0.98f), t));
        }
        // clouds drifting slowly
        float drift = (Time.time * 4f) % (r.width + 60f);
        for (int c = 0; c < 2; c++)
        {
            float cx = r.x - 40f + (drift + c * (r.width * 0.55f + 30f)) % (r.width + 60f), cy = r.y + 16 + c * 26;
            var white = new Color(1f, 1f, 1f, 0.95f);
            GUI.BeginClip(r);
            Fill(new Rect(cx - r.x, cy - r.y + 6, 44, 8), white);
            Fill(new Rect(cx - r.x + 8, cy - r.y, 20, 8), white);
            Fill(new Rect(cx - r.x + 22, cy - r.y + 2, 14, 6), white);
            Fill(new Rect(cx - r.x + 2, cy - r.y + 13, 40, 2), new Color(0.75f, 0.85f, 0.95f));
            GUI.EndClip();
        }
        // sea, darker toward the front, with glinting wave dashes
        Fill(new Rect(r.x, seaY, r.width, r.yMax - seaY), new Color(0.13f, 0.42f, 0.7f));
        Fill(new Rect(r.x, seaY, r.width, 3), new Color(0.5f, 0.75f, 0.92f));
        for (int row = 0; seaY + 8 + row * 10 < r.yMax; row++)
        {
            float wy = seaY + 8 + row * 10;
            Fill(new Rect(r.x, wy, r.width, 10), Color.Lerp(new Color(0.15f, 0.46f, 0.74f), new Color(0.06f, 0.26f, 0.52f), row / 10f));
            for (int k = 0; k < 4; k++)
            {
                int h = Hash(row, k);
                float wx = r.x + ((h * 3 + row * 17 + Time.time * (6 + row)) % (r.width - 12));
                Fill(new Rect(Mathf.Round(wx), wy + 3, 8 + h % 6, 2), new Color(0.6f, 0.82f, 0.96f, 0.7f));
            }
        }
        // the island, in layers from the back: foam, a dirt cliff, the grassy top
        float cxI = r.center.x, topY = r.yMax - 64f, rx = r.width * 0.42f, ry = 22f;
        float bob = Mathf.Round(Mathf.Sin(Time.time * 1.6f) * 1.5f); // the foam breathes
        void Ellipse(float ecx, float ecy, float erx, float ery, int layer)
        {
            int rows = Mathf.CeilToInt(ery * 2 / p);
            for (int j = 0; j < rows; j++)
            {
                float y = ecy - ery + j * p, dy = (y + p / 2f - ecy) / ery;
                float half = Mathf.Round(erx * Mathf.Sqrt(Mathf.Max(0f, 1f - dy * dy)) / p) * p;
                if (half <= 0) continue;
                Color c = layer == 0 ? new Color(0.86f, 0.95f, 1f, 0.92f)
                        : layer == 1 ? (j % 2 == 0 ? new Color(0.5f, 0.35f, 0.2f) : new Color(0.43f, 0.29f, 0.16f))
                        : j == 0 ? new Color(0.5f, 0.78f, 0.3f) : new Color(0.36f, 0.64f, 0.22f);
                Fill(new Rect(cxI - half, y, half * 2, p), c);
                if (layer == 0) continue;
                for (int k = -(int)(half / p); k < (int)(half / p); k++)
                {
                    int h = Hash(j + layer * 50, k);
                    if (layer == 1 && h < 40) Fill(new Rect(cxI + k * p, y, p, p), new Color(0.62f, 0.46f, 0.28f));
                    else if (layer == 2 && h < 34) Fill(new Rect(cxI + k * p, y, p, p), new Color(0.27f, 0.52f, 0.17f));
                    else if (layer == 2 && h > 238) Fill(new Rect(cxI + k * p, y, p, p), new Color(0.56f, 0.82f, 0.36f));
                }
                if (layer == 2) // the rim of the grass, darker
                {
                    Fill(new Rect(cxI - half, y, p, p), new Color(0.24f, 0.46f, 0.15f));
                    Fill(new Rect(cxI + half - p, y, p, p), new Color(0.24f, 0.46f, 0.15f));
                }
            }
        }
        Ellipse(cxI, topY + 22, rx + 10 + bob, ry + 2, 0);
        Ellipse(cxI, topY + 16, rx - 2, ry, 1);
        Ellipse(cxI, topY, rx, ry, 2);
        // rocks and tufts
        void Rock(float x, float y, float s)
        {
            Fill(new Rect(x, y + p, s * p, (s - 1) * p), new Color(0.38f, 0.38f, 0.4f));
            Fill(new Rect(x + p, y, (s - 2) * p, p), new Color(0.55f, 0.55f, 0.57f));
            Fill(new Rect(x + p, y + p, (s - 3) * p, p), new Color(0.66f, 0.66f, 0.68f));
            Fill(new Rect(x, y + s * p - p, s * p, p), new Color(0.25f, 0.25f, 0.27f));
        }
        void Tuft(float x, float y)
        {
            var g = new Color(0.22f, 0.5f, 0.15f);
            Fill(new Rect(x, y - 8, 2, 8), g); Fill(new Rect(x + 3, y - 12, 2, 12), g); Fill(new Rect(x + 6, y - 7, 2, 7), g);
            Fill(new Rect(x + 3, y - 12, 2, 3), new Color(0.5f, 0.8f, 0.3f));
        }
        Rock(cxI - rx + 10, topY - 6, 5);
        Rock(cxI + rx - 34, topY - 2, 6);
        Rock(cxI + rx * 0.35f, topY + ry - 18, 4);
        Tuft(cxI - rx * 0.55f, topY - ry + 14);
        Tuft(cxI + rx * 0.5f, topY - ry + 12);
        Tuft(cxI - rx * 0.3f, topY + ry - 4);
        // a soft shadow where the character stands
        Fill(new Rect(cxI - 26, topY + 2, 52, 4), new Color(0f, 0f, 0f, 0.22f));
        Fill(new Rect(cxI - 18, topY + 6, 36, 2), new Color(0f, 0f, 0f, 0.16f));
        return new Vector2(cxI, topY + 6);
    }

    /// Draws a sprite with its bottom-centre at `feet`, `height` tall.
    static void SpriteStanding(Sprite s, Vector2 feet, float height)
    {
        if (s == null) return;
        var tr = s.textureRect;
        float w = height * tr.width / tr.height;
        var uv = new Rect(tr.x / s.texture.width, tr.y / s.texture.height, tr.width / s.texture.width, tr.height / s.texture.height);
        GUI.DrawTextureWithTexCoords(new Rect(Mathf.Round(feet.x - w / 2f), Mathf.Round(feet.y - height), w, height), s.texture, uv);
    }

    /// The head and shoulders of a sprite, filling a square.
    static void SpritePortrait(Sprite s, Rect r)
    {
        if (s == null) return;
        var tr = s.textureRect;
        float cw = tr.width * 0.74f, ch = cw * r.height / r.width;
        var crop = new Rect(tr.x + (tr.width - cw) / 2f, tr.yMax - ch - tr.height * 0.02f, cw, ch);
        var uv = new Rect(crop.x / s.texture.width, crop.y / s.texture.height, crop.width / s.texture.width, crop.height / s.texture.height);
        GUI.DrawTextureWithTexCoords(r, s.texture, uv);
    }

    // ------------------------------------------------------------------ portraits
    /// The ready-made characters as framed portraits in a row; the picked one glows gold.
    void DrawPortraitCards(float x, float y, float w, float h)
    {
        int n = CharacterLook.Presets.Length - 1;
        const float gap = 12f;
        float cw = (w - gap * (n - 1)) / n;
        Vector2 mouse = Event.current.mousePosition;
        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 4f);
        for (int i = 1; i <= n; i++)
        {
            var r = new Rect(Mathf.Round(x + (i - 1) * (cw + gap)), y, Mathf.Round(cw), h);
            bool picked = look.preset == i, hover = GUI.enabled && r.Contains(mouse);
            if (picked)
            {
                Fill(new Rect(r.x - 5, r.y - 5, r.width + 10, r.height + 10), new Color(1f, 0.85f, 0.3f, 0.25f * pulse));
                Fill(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(1f, 0.88f, 0.35f, 0.9f));
            }
            else if (hover) Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(1f, 1f, 1f, 0.5f));
            WoodFrame(r, 6, i * 11);
            var inner = new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12);
            Fill(inner, picked ? new Color(0.2f, 0.27f, 0.42f) : new Color(0.12f, 0.16f, 0.27f));
            Fill(new Rect(inner.x, inner.y + inner.height * 0.6f, inner.width, inner.height * 0.4f), picked ? new Color(0.17f, 0.22f, 0.36f) : new Color(0.1f, 0.13f, 0.22f));
            int code = new CharacterLook { preset = i, gender = 1, style = 1 }.Code;
            var sprite = PixelArt.Player(0, 0, null, null, code);
            GUI.color = picked || hover ? Color.white : new Color(0.82f, 0.82f, 0.86f);
            SpritePortrait(sprite, new Rect(inner.x + 2, inner.y + 2, inner.width - 4, inner.height - 4));
            GUI.color = Color.white;
            if (CrispGUI.Button(r, GUIContent.none, GUIStyle.none) && !picked)
            {
                look = new CharacterLook { preset = i, gender = 1, style = 1 };
                Sfx.Play("pickup", 0.3f);
            }
        }
    }
}
