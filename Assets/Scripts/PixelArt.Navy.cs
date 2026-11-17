using UnityEngine;

/// The government's warships (เรือรัฐบาล) that hunt wanted sailors at sea (IslandGame.Navy.cs):
/// a three-masted frigate in navy colours - a black hull with a white gun-deck band and its row
/// of open gun ports, a copper bottom, white sails with the blue gull seal of the government,
/// and long blue-and-white pennants. Side view, bow to the right, pivot on the waterline.
/// Hand-made overrides: Assets/Resources/Sprites/navy_ship_0.png / navy_ship_1.png, cannonball.png.
public static partial class PixelArt
{
    const int NavyW = 88, NavyH = 76;

    public static Sprite NavyShip(int frame) => Get("navy_ship_" + (frame & 1), () =>
    {
        int f = frame & 1, wl = NavyH - 5;
        var c = new Canvas(NavyW, NavyH);
        c.Ellipse(NavyW * 0.45f, wl + 0.5f, NavyW * 0.42f, 2.8f, new Color(0.02f, 0.08f, 0.2f, 0.45f));
        float x0 = 5, x1 = 74, deck = wl - 15, br = 5, sr = 10;
        Color cloth = Hex("#f7f5ee"), shade = Hex("#c8cfdc"), blue = Hex("#1f4f9a"), blueD = Hex("#143466"), gold = Hex("#e8b840"), white = Hex("#eeeae0");

        // rigging behind the sails
        Rope(c, 43, 2, 22, 13); Rope(c, 43, 2, 61, 8); Rope(c, 61, 8, 86, deck - 13); Rope(c, 61, 18, 82, deck - 10); Rope(c, 23, 13, 6, deck - 12);
        Mast(c, 22, 12, (int)deck, 1);
        Mast(c, 42, 2, (int)deck, 2);
        Mast(c, 60, 8, (int)deck, 2);

        // mizzen: a spanker with a blue band
        AftSail(c, 22, 16, 12, 5, deck - 8, 14, cloth, shade, blue, 0.6f);
        c.Rect(8, (int)deck - 8, 14, 1, Bark);
        // head sails out to the bowsprit
        Jib(c, new Vector2(62, 12), new Vector2(85, deck - 13), new Vector2(62, deck - 10), cloth, shade);
        Jib(c, new Vector2(62, 22), new Vector2(80, deck - 10), new Vector2(62, deck - 8), cloth, shade);
        // main mast: three tiers, the government's gull seal on the course
        SquareSail(c, 43, 10, 14, 7, cloth, shade, Bark);
        SquareSail(c, 43, 20, 20, 10, cloth, shade, Bark, (u, v) => v > 0.78f && v < 0.88f ? blue : Color.clear);
        c.Rect(39, 31, 9, 2, Bark); c.Rect(39, 31, 9, 1, gold); // fighting top
        SquareSail(c, 43, 35, 26, 12, cloth, shade, Bark, (u, v) => Seal(u, v, 26f, 12f, blue, blueD, white, gold));
        // fore mast
        SquareSail(c, 61, 15, 12, 6, cloth, shade, Bark);
        SquareSail(c, 61, 24, 17, 9, cloth, shade, Bark);
        SquareSail(c, 61, 36, 21, 10, cloth, shade, Bark, (u, v) => v > 0.72f && v < 0.84f ? blue : Color.clear);
        c.Line(72, (int)deck - 5, 86, (int)deck - 13, BarkD); // bowsprit

        // pennants stream back from the mastheads (they ripple between the two frames)
        Pennant(c, 42, 0, 20 + f * 2, 3f, blue, white);
        Pennant(c, 60, 6, 11 - f, 2.4f, white, blue);
        Pennant(c, 22, 10, 7 + f, 2f, blue, blue);

        // the hull: black topsides, a white gun-deck band with open ports, copper below
        Color black = Hex("#1a1e2a"), blackL = Hex("#2c3346"), copper = Hex("#b8703a"), copperD = Hex("#7a4626");
        Hull(c, x0, x1, deck, wl, br, sr, false, (t, v, d) =>
        {
            if (d < 1f) return gold;
            if (d < 1.5f) return Hex("#8a6420");
            if (d > 4.2f && d < 7.6f) return Color.Lerp(white, Hex("#c9c6bc"), (d - 4.2f) / 6f);
            if (v > 0.88f) return ((int)(t * 34f) % 3) == 0 ? copperD : copper; // copper sheathing at the waterline
            Color b = d > 10.5f && d < 11.3f ? Hex("#d8d2c0") : black;
            return Mathf.Repeat(d, 2.4f) < 0.45f ? blackL : b;
        }, 0.1f);
        // gun ports on the white band: dark openings, red lids raised over them, muzzles peeking out
        for (int px = 20; px <= 66; px += 6)
        {
            int y = Mathf.RoundToInt(HullTop(px + 1, x0, x1, deck, br, sr)) + 5;
            c.Rect(px, y - 1, 3, 1, Hex("#a8282a"));
            c.Rect(px, y, 3, 2, Hex("#0c0c10"));
            c.Rect(px + 1, y, 1, 1, Hex("#4a505c"));
        }
        // stern: gilded windows and a lantern; the ensign flies from its staff
        int st = Mathf.RoundToInt(HullTop(12, x0, x1, deck, br, sr));
        c.Rect(6, st + 6, 15, 1, gold);
        for (int x = 7; x <= 17; x += 5) { c.Rect(x, st + 2, 3, 3, gold); c.Rect(x, st + 2, 2, 2, Hex("#ffd98a")); }
        c.Rect(7, st - 5, 1, 5, BarkD);
        Glow(c, 7.5f, st - 6.5f, 4f, Hex("#ffe08a"));
        Lantern(c, 7.5f, st - 6.5f, Hex("#ffd98a"));
        c.Rect(3, st - 14, 1, 10, BarkD);
        Ensign(c, 3, st - 14, 9, 6, f, blue, blueD, white);
        // a golden gull for a figurehead under the bowsprit
        float ft = HullTop(73, x0, x1, deck, br, sr);
        c.Line(72, Mathf.RoundToInt(ft) + 2, 77, Mathf.RoundToInt(ft) - 1, gold);
        c.Ball(78, ft - 1.5f, 1.6f, 1.3f, Hex("#a8781e"), gold, Hex("#fff0a0"));
        c.Line(76, Mathf.RoundToInt(ft) - 2, 74, Mathf.RoundToInt(ft) - 4, gold);
        // foam along the waterline and the bow wave
        var foam = new Color(1f, 1f, 1f, 0.85f);
        for (int x = 6; x < 76; x += 3) c.Set(x + f, wl + ((x / 3 + f) % 2), foam);
        c.Line(64, wl - 1, 75, wl + 1, foam); // the bow wave
        c.Set(76 + f, wl, foam); c.Set(78, wl + 1 - f, foam);
        return c.ToSprite(NavyW * 0.45f, wl);
    });

    /// The government's seal on a sail: a blue disc with a gold rim and a white gull in flight.
    static Color Seal(float u, float v, float w, float h, Color blue, Color blueD, Color white, Color gold)
    {
        float dx = (u - 0.5f) * w, dy = (v - 0.46f) * h, r = Mathf.Sqrt(dx * dx + dy * dy);
        if (r > 5f) return Color.clear;
        if (r > 4.3f) return gold;
        float wing = -Mathf.Abs(dx) * 0.55f + 1.2f + (Mathf.Abs(dx) > 2.2f ? (Mathf.Abs(dx) - 2.2f) * 0.9f : 0f); // a gull's M-shaped wings
        if (Mathf.Abs(dx) < 3.4f && Mathf.Abs(dy - wing) < 0.75f) return white;
        return dy > 2.4f ? blueD : blue;
    }

    /// A flag flying left from the staff at (x, y): blue with a white gull, rippling.
    static void Ensign(Canvas c, int x, int y, int w, int h, int frame, Color blue, Color blueD, Color white)
    {
        for (int i = 1; i <= w; i++)
        {
            int dy = Mathf.RoundToInt(Mathf.Sin(i * 0.8f + frame * 1.6f) * 0.7f * i / w);
            for (int j = 0; j < h; j++)
            {
                float u = i / (float)w, vv = j / (float)(h - 1);
                Color col = j == h - 1 ? blueD : blue;
                float gx = (u - 0.55f) * w, gy = (vv - 0.45f) * h;
                if (Mathf.Abs(gx) < 2.6f && Mathf.Abs(gy - (-Mathf.Abs(gx) * 0.5f + 0.6f)) < 0.6f) col = white;
                c.Set(x - i, y + j + dy, col);
            }
        }
    }

    /// A cannonball in flight.
    public static Sprite Cannonball() => Get("cannonball", () =>
    {
        var c = new Canvas(6, 6) { Rim = false };
        c.Ball(3, 3, 2.4f, 2.4f, Hex("#08080c"), Hex("#25262e"), Hex("#6a6e7a"));
        return c.ToSprite(3, 3);
    });
}
