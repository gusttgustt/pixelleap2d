using System;
using UnityEngine;

/// The player's boat: one picture per look, growing with every upgrade (names: IslandGame.BoatName).
///   1 log raft, 2 big raft with a leaf shelter, 3 outrigger canoe with a crab-claw sail,
///   4 sailboat, 5 two-masted ship with an obsidian ram, 6 junk with battened sails (Lv 6-9),
///   7 galleon (Lv 10-19), 8 grand ocean ship (Lv 20+).
/// Side view, bow to the right; the pivot is the middle of the waterline.
/// Hand-made overrides: Assets/Resources/Sprites/raft.png, raft_lv2.png ... raft_lv8.png.
public static partial class PixelArt
{
    public const int BoatLooks = 8;

    /// Which look a boat of this upgrade level has.
    public static int BoatLook(int level) => level <= 1 ? 1 : level <= 5 ? level : level <= 9 ? 6 : level <= 19 ? 7 : 8;

    /// The first upgrade level that has this look.
    public static int BoatLookLevel(int look) => look <= 5 ? Mathf.Max(1, look) : look == 6 ? 6 : look == 7 ? 10 : 20;

    /// The boat of this upgrade level (its look).
    public static Sprite Raft(int level = 1) => Boat(BoatLook(level));

    static readonly int[] BoatW = { 0, 40, 46, 54, 58, 66, 76, 86, 96 };
    static readonly int[] BoatH = { 0, 34, 40, 46, 56, 62, 68, 80, 90 };
    static readonly int[] BoatDeckPx = { 0, 5, 6, 3, 9, 11, 11, 15, 17 }; // where the sailor stands, px above the waterline

    /// How high above the boat's pivot (the waterline) the sailor stands, in world units.
    public static float BoatDeck(int look) => BoatDeckPx[Mathf.Clamp(look, 1, BoatLooks)] / (float)PPU;

    /// The boat picture's width in world pixels.
    public static int BoatWidth(int look) => BoatW[Mathf.Clamp(look, 1, BoatLooks)];

    /// How tall the boat is above the waterline, in world units (for the "press E" prompt).
    public static float BoatHeight(int look) { int t = Mathf.Clamp(look, 1, BoatLooks); return (BoatH[t] - 4) / (float)PPU; }

    public static Sprite Boat(int look)
    {
        int t = Mathf.Clamp(look, 1, BoatLooks);
        return Get(t == 1 ? "raft" : $"raft_lv{t}", () =>
        {
            int W = BoatW[t], wl = BoatH[t] - 4;
            var c = new Canvas(W, BoatH[t]);
            c.Ellipse(W * 0.5f, wl + 0.5f, W * 0.45f, 2.6f, Shadow);
            switch (t)
            {
                case 1: BoatRaft(c, wl); break;
                case 2: BoatBigRaft(c, wl); break;
                case 3: BoatCanoe(c, wl); break;
                case 4: BoatSloop(c, wl); break;
                case 5: BoatBrig(c, wl); break;
                case 6: BoatJunk(c, wl); break;
                case 7: BoatGalleon(c, wl); break;
                default: BoatGrand(c, wl); break;
            }
            return c.ToSprite(W * 0.5f, wl);
        });
    }

    // ------------------------------------------------------------------ the eight boats

    static void BoatRaft(Canvas c, int wl)
    {
        // mast and sail first: the logs cover the mast's foot
        Mast(c, 18, 5, wl - 6, 2);
        SquareSail(c, 19, 7, 16, 11, Hex("#efe3c2"), Hex("#c9b88e"), Bark, (u, v) =>
            u > 0.58f && u < 0.8f && v > 0.45f && v < 0.72f ? (u < 0.6f || u > 0.78f || v < 0.47f || v > 0.7f ? Hex("#7a5a3a") : Hex("#b88a5a")) : Color.clear); // a patch
        Pennant(c, 18, 3.5f, 6, 2.2f, Hex("#d9534f"), Hex("#d9534f"));
        Ball(c, 8.5f, wl - 11.5f, 2.4f, 1.9f, Hex("#8a6a40"), Hex("#b89862"), Hex("#dcc08a")); // a sack of supplies
        c.Ball(12.6f, wl - 11.3f, 1.4f, 1.4f, Hex("#3a2414"), Hex("#5a3a20"), Hex("#8a5a34")); // coconut
        for (int i = 0; i < 4; i++) Log(c, 6 + (i % 2), 34 - (i == 2 ? 1 : 0), wl - 9 + i * 2.2f, 1.5f);
        Lash(c, 11, wl - 11, 11); Lash(c, 28, wl - 11, 11);
        // a paddle leaning on the side
        c.Line(31, wl - 14, 35, wl - 4, Bark);
        c.Ellipse(35.6f, wl - 2.6f, 1.2f, 2.2f, Hex("#b07c48"));
    }

    static void BoatBigRaft(Canvas c, int wl)
    {
        Mast(c, 26, 4, wl - 7, 2);
        Color red = Hex("#c9463d");
        SquareSail(c, 27, 6, 19, 13, Hex("#f2e6c6"), Hex("#cbb98e"), Bark, (u, v) => ((int)(v * 5f) & 1) == 1 ? red : Color.clear);
        Pennant(c, 26, 2.5f, 8, 2.4f, Hex("#ffd23c"), red);
        // bamboo rail along the back of the deck
        Color g = Hex("#6aa84f"), gd = Hex("#4a7f35"), gl = Hex("#a5d17a");
        c.Rect(17, wl - 16, 25, 1, g);
        for (int x = 17; x <= 41; x += 8) { c.Rect(x, wl - 16, 1, 4, gd); c.Set(x, wl - 16, gl); }
        for (int x = 19; x <= 41; x += 4) c.Set(x, wl - 16, gl);
        // leaf shelter at the back
        Color leafD = Hex("#5b7a2e"), leaf = Hex("#7a9a3a"), leafL = Hex("#a3bf55");
        c.Rect(7, wl - 18, 8, 6, Hex("#2a1a10"));
        c.Rect(6, wl - 19, 1, 8, BarkD); c.Rect(15, wl - 19, 1, 8, BarkD);
        for (int y = wl - 24; y <= wl - 18; y++)
        {
            int k = y - (wl - 24), x0 = 10 - k, x1 = 12 + k;
            for (int x = x0; x <= x1; x++) c.Set(x, y, k % 2 == 0 ? (x < 11 ? leafL : leaf) : leafD);
        }
        // a lantern on a pole at the bow
        c.Rect(40, wl - 22, 1, 10, BarkD);
        Glow(c, 40.5f, wl - 20.5f, 4f, Hex("#ffe08a"));
        Lantern(c, 40.5f, wl - 20.5f, Hex("#ffd36a"));
        // two layers of logs
        for (int i = 0; i < 5; i++) Log(c, 5 + (i % 2), 40 - (i % 3 == 1 ? 1 : 0), wl - 11 + i * 2.3f, 1.6f);
        Lash(c, 10, wl - 13, 13); Lash(c, 22, wl - 13, 13); Lash(c, 35, wl - 13, 13);
        c.Ball(29.5f, wl - 13.5f, 1.8f, 1.6f, Hex("#5a3a20"), Hex("#8a5a34"), Hex("#b07a48")); // water jar
        c.Rect(28, wl - 15, 3, 1, Hex("#5a3a20"));
    }

    static void BoatCanoe(Canvas c, int wl)
    {
        // crab-claw sail, woven in red and cream stripes
        Vector2 tack = new Vector2(38, wl - 9), A = new Vector2(17, 4), B = new Vector2(8, wl - 20);
        Vector2 mid = (A + B) * 0.5f, bite = mid + (mid - tack).normalized * 4.5f;
        Color cream = Hex("#efe0bd"), rust = Hex("#b5523a"), creamD = Hex("#cdb98e"), rustD = Hex("#8e3a28");
        Mast(c, 26, 9, wl - 6, 1);
        Tri(c, tack, A, B, (x, y) =>
        {
            var p = new Vector2(x, y);
            if ((p - bite).magnitude < 7.2f) return Color.clear;     // the claw's notch
            float r = (p - tack).magnitude;
            bool stripe = Mathf.Repeat(r, 6f) < 3f, shade = (p - B).magnitude < 9f;
            return stripe ? (shade ? rustD : rust) : (shade ? creamD : cream);
        });
        c.LineFine((int)(tack.x * Detail), (int)(tack.y * Detail), (int)(A.x * Detail), (int)(A.y * Detail), BarkD, 2);
        c.LineFine((int)(tack.x * Detail), (int)(tack.y * Detail), (int)(B.x * Detail), (int)(B.y * Detail), BarkD, 2);
        Pennant(c, 17, 3, 6, 2f, Hex("#ffd23c"), Hex("#ffd23c"));

        // dug-out hull with a carved zigzag band
        float x0 = 5, x1 = 47, deck = wl - 6;
        Color wood = Hex("#a5693a"), woodL = Hex("#c8894e"), woodD = Hex("#6e4222"), zigA = Hex("#f0dcb0"), zigB = Hex("#b8432f");
        Hull(c, x0, x1, deck, wl - 0.5f, 6, 5, true, (t, v, d) =>
        {
            if (d < 0.6f) return woodL;
            if (d > 1.6f && d < 3.2f)
            {
                float tri = Mathf.Abs(Mathf.Repeat(t * (x1 - x0), 3f) - 1.5f);
                return d - 1.6f < tri ? zigA : zigB;
            }
            return Color.Lerp(wood, woodD, Mathf.Clamp01(v * 1.1f - 0.3f));
        });
        c.Ring(48.2f, wl - 13f, 1.8f, 0.8f, woodD); c.Ring(4f, wl - 11.5f, 1.6f, 0.8f, woodD); // carved curls on the prow and stern
        // the outrigger float in front, on two booms
        c.Line(18, wl - 7, 19, wl - 1, BarkD); c.Line(33, wl - 7, 34, wl - 1, BarkD);
        Log(c, 13, 38, wl - 0.6f, 1.2f, Hex("#7a5034"), Hex("#4e321e"), Hex("#9a6a44"));
    }

    static void BoatSloop(Canvas c, int wl)
    {
        float x0 = 5, x1 = 51, deck = wl - 10;
        Color cloth = Hex("#f6f1e2"), shade = Hex("#cfc6ad"), red = Hex("#d0483e");
        // rigging and sails
        Rope(c, 32, 5, 56, deck - 5); Rope(c, 31, 5, 6, deck - 2);
        Mast(c, 30, 4, (int)deck, 2);
        AftSail(c, 30, 7, 7, 0, deck - 8, 21, cloth, shade, red, 0.78f);
        c.Rect(9, (int)deck - 8, 21, 1, Bark); // boom
        Jib(c, new Vector2(33, 8), new Vector2(55, deck - 5), new Vector2(33, deck - 6), cloth, shade);
        c.Line(50, (int)deck - 3, 56, (int)deck - 5, BarkD); // bowsprit
        Pennant(c, 30, 2.5f, 8, 2.4f, red, red);
        // planked hull: white topsides, a blue stripe and a navy bottom
        Color white = Hex("#eef0ea"), whiteD = Hex("#c2cad0"), blue = Hex("#2f6fa8"), navy = Hex("#23456e"), rail = Hex("#b8834e");
        Hull(c, x0, x1, deck, wl, 4, 2, false, (t, v, d) =>
        {
            if (d < 0.8f) return rail;
            if (d < 1.3f) return Dk(rail, 0.45f);
            if (d > 2.8f && d < 4f) return d < 3.3f ? Lt(blue, 0.3f) : blue;
            if (v < 0.64f) return Color.Lerp(white, whiteD, v * 1.2f);
            return Mathf.Repeat(d, 2.5f) < 0.5f ? Dk(navy, 0.3f) : navy;
        });
        // a little cabin with two round windows
        c.Rect(15, (int)deck - 5, 14, 5, Hex("#e8dcc0"));
        c.Rect(15, (int)deck - 5, 14, 1, Hex("#f8f0dc"));
        c.Rect(14, (int)deck - 7, 16, 2, Hex("#8a5a32"));
        c.Rect(14, (int)deck - 7, 16, 1, Hex("#a8744a"));
        foreach (float wx in new[] { 19f, 25f }) Porthole(c, wx, deck - 2.6f, Hex("#8a5a32"), Hex("#9fd6f0"));
    }

    static void BoatBrig(Canvas c, int wl)
    {
        float x0 = 5, x1 = 57, deck = wl - 12;
        Color cloth = Hex("#f4ecd4"), shade = Hex("#cdbf9c"), red = Hex("#c9463d"), redD = Hex("#9a3028");
        Rope(c, 23, 4, 6, deck - 3); Rope(c, 23, 5, 41, 12); Rope(c, 41, 13, 64, deck - 9);
        Mast(c, 22, 3, (int)deck, 2);
        Mast(c, 40, 11, (int)deck, 2);
        AftSail(c, 22, 8, 4, 8, deck - 6, 16, cloth, shade, red, 0.12f);
        c.Rect(6, (int)deck - 6, 16, 1, Bark);
        Jib(c, new Vector2(43, 14), new Vector2(63, deck - 9), new Vector2(43, deck - 6), cloth, shade);
        AftSail(c, 40, 15, 12, 6, deck - 5, 13, red, redD, cloth, 0.14f);
        c.Rect(27, (int)deck - 5, 13, 1, Bark);
        c.Line(55, (int)deck - 4, 64, (int)deck - 9, BarkD); // bowsprit
        Pennant(c, 22, 1.5f, 10, 2.6f, Hex("#3a78c0"), Hex("#f4ecd4"));
        Pennant(c, 40, 9.5f, 6, 2.2f, red, red);
        // varnished hull with a cream stripe and brass portholes
        Color wood = Hex("#7a4a28"), woodD = Hex("#4a2a16"), cream = Hex("#e9d9a8"), rail = Hex("#c8925a");
        Hull(c, x0, x1, deck, wl, 4, 4, false, (t, v, d) =>
        {
            if (d < 0.8f) return rail;
            if (d < 1.3f) return Dk(rail, 0.5f);
            if (d > 2.2f && d < 3.4f) return cream;
            Color b = v > 0.72f ? woodD : wood;
            return Mathf.Repeat(d, 2.5f) < 0.5f ? Dk(b, 0.3f) : Color.Lerp(b, woodD, v * 0.4f);
        });
        for (float px = 14; px <= 46; px += 8) Porthole(c, px, deck + 6, Hex("#d8b04a"), Hex("#22384f"));
        // obsidian ram on the bow
        Color o1 = Hex("#120e18"), o2 = Hex("#2e2438"), o3 = Hex("#6a5a88");
        Tri(c, new Vector2(49, deck + 4), new Vector2(62, wl - 4), new Vector2(51, wl - 0.5f), (x, y) =>
            y < deck + 6.5f ? o3 : Hash((int)(x * 4), (int)(y * 4), 3) < 0.08f ? o3 : y > wl - 3 ? o1 : o2);
    }

    static void BoatJunk(Canvas c, int wl)
    {
        float x0 = 4, x1 = 66, deck = wl - 12;
        Color cloth = Hex("#c9522c"), clothL = Hex("#e67a46"), batten = Hex("#6a3820");
        Mast(c, 24, 18, (int)deck, 1);
        Mast(c, 38, 3, (int)deck, 2);
        Mast(c, 56, 17, (int)deck, 1);
        JunkSail(c, 13, 27, 20, 22, deck - 19, cloth, clothL, batten);
        JunkSail(c, 26, 45, 4, 9, deck - 4, cloth, clothL, batten);
        JunkSail(c, 49, 62, 18, 21, deck - 4, cloth, clothL, batten);
        Pennant(c, 38, 1, 13, 2.6f, Hex("#ffd23c"), Hex("#d83a2a"));
        // dark red hull with an ochre band, a painted eye on the bow
        Color red = Hex("#8a3424"), dark = Hex("#2e1c14"), ochre = Hex("#d8a03a");
        Hull(c, x0, x1, deck, wl, 4, 9, false, (t, v, d) =>
        {
            if (d < 1f) return ochre;
            if (d < 1.5f) return Dk(ochre, 0.55f);
            if (d > 3.4f && d < 4.6f) return ochre;
            Color b = v > 0.6f ? dark : red;
            return Mathf.Repeat(d, 2.4f) < 0.5f ? Dk(b, 0.3f) : b;
        }, 0.12f);
        c.Ellipse(58, deck + 3.5f, 2.2f, 1.6f, Hex("#d83a2a"));
        c.Ellipse(58, deck + 3.5f, 1.7f, 1.2f, Color.white);
        c.Ellipse(58.4f, deck + 3.5f, 0.8f, 0.9f, Hex("#101010"));
        c.Rect(2, (int)deck + 1, 2, 9, Hex("#4a2a16")); // rudder
        // the stern cabin: red walls, glowing lattice windows, an upturned tiled roof
        float top = HullTop(12, x0, x1, deck, 4, 9);
        int cb = Mathf.RoundToInt(top), ct = cb - 8;
        c.Rect(6, ct, 13, 8, Hex("#c83c2a"));
        for (int x = 6; x <= 18; x += 6) c.Rect(x, ct, 1, 8, Hex("#6a1e14"));
        c.Rect(8, ct + 2, 3, 3, Hex("#ffd27a")); c.Rect(13, ct + 2, 3, 3, Hex("#ffd27a"));
        c.Set(9, ct + 3, Hex("#c88a3a")); c.Set(14, ct + 3, Hex("#c88a3a"));
        c.Rect(4, ct - 3, 17, 3, Hex("#3a2a2a"));
        for (int x = 4; x <= 20; x += 2) c.Set(x, ct - 1, Hex("#5a4444"));
        c.Rect(4, ct - 3, 17, 1, ochre);
        c.Set(3, ct - 4, ochre); c.Set(21, ct - 4, ochre);
        // red paper lanterns at the stern and the bow
        Lantern(c, 4.5f, ct + 1.5f, Hex("#e03a2a"), true);
        c.Line(60, (int)deck - 6, 60, (int)deck - 3, BarkD);
        Lantern(c, 60.5f, deck - 6.5f, Hex("#e03a2a"), true);
    }

    static void BoatGalleon(Canvas c, int wl)
    {
        float x0 = 5, x1 = 74, deck = wl - 16, br = 6, sr = 12;
        Color cloth = Hex("#efe6cc"), shade = Hex("#c9bc98"), red = Hex("#b8302a"), gold = Hex("#e0b040");
        Rope(c, 41, 2, 20, 15); Rope(c, 41, 2, 60, 10); Rope(c, 60, 10, 84, deck - 12); Rope(c, 21, 15, 6, deck - 12);
        Mast(c, 20, 14, (int)deck - 8, 1);
        Mast(c, 40, 2, (int)deck, 2);
        Mast(c, 59, 9, (int)deck, 2);
        AftSail(c, 20, 18, 13, 5, deck - 15, 12, cloth, shade);
        Jib(c, new Vector2(62, 12), new Vector2(83, deck - 12), new Vector2(62, deck - 9), cloth, shade);
        SquareSail(c, 41, 19, 18, 11, cloth, shade, Bark);
        c.Rect(37, 33, 8, 2, Bark); c.Rect(37, 33, 8, 1, Hex("#a8744a")); // crow's nest
        SquareSail(c, 41, 37, 24, 12, cloth, shade, Bark, (u, v) =>
            Mathf.Abs(u - 0.5f) < 0.05f || Mathf.Abs(v - 0.45f) < 0.08f ? red : Color.clear);
        SquareSail(c, 60, 23, 13, 9, cloth, shade, Bark);
        SquareSail(c, 60, 38, 18, 11, cloth, shade, Bark);
        c.Line(72, (int)deck - 6, 84, (int)deck - 12, BarkD); // bowsprit
        Pennant(c, 40, 0, 15, 2.8f, red, red);
        Pennant(c, 59, 7.5f, 8, 2.2f, red, gold);
        Pennant(c, 20, 12.5f, 6, 2f, red, red);
        // dark timber hull, ochre wales, a row of gun ports
        Color wood = Hex("#5a3420"), woodD = Hex("#2e1a10"), ochre = Hex("#c8963a");
        Hull(c, x0, x1, deck, wl, br, sr, false, (t, v, d) =>
        {
            if (d < 1f) return gold;
            if (d < 1.5f) return Dk(gold, 0.5f);
            if ((d > 4.6f && d < 5.6f) || (d > 9.4f && d < 10.4f)) return ochre;
            Color b = v > 0.76f ? woodD : wood;
            return Mathf.Repeat(d, 2.3f) < 0.5f ? Dk(b, 0.35f) : b;
        }, 0.1f);
        for (int px = 24; px <= 66; px += 7)
        {
            int y = Mathf.RoundToInt(HullTop(px + 1, x0, x1, deck, br, sr)) + 6;
            c.Rect(px, y - 1, 2, 1, Hex("#a8302a"));
            c.Rect(px, y, 2, 2, Hex("#15100c"));
        }
        // stern castle windows and its lantern
        int st = Mathf.RoundToInt(HullTop(12, x0, x1, deck, br, sr));
        c.Rect(6, st + 6, 15, 1, gold);
        for (int x = 7; x <= 17; x += 5) { c.Rect(x, st + 2, 3, 3, gold); c.Rect(x, st + 2, 2, 2, Hex("#ffd27a")); }
        c.Rect(7, st - 5, 1, 5, BarkD);
        Glow(c, 7.5f, st - 6.5f, 4f, Hex("#ffe08a"));
        Lantern(c, 7.5f, st - 6.5f, Hex("#ffd27a"));
    }

    static void BoatGrand(Canvas c, int wl)
    {
        float x0 = 5, x1 = 82, deck = wl - 18, br = 7, sr = 13;
        Color cloth = Hex("#fbfaf4"), shade = Hex("#cdd6e4"), gold = Hex("#f0c040"), blue = Hex("#2a5aa0");
        Rope(c, 47, 1, 24, 19); Rope(c, 47, 1, 67, 8); Rope(c, 67, 8, 94, deck - 15); Rope(c, 67, 18, 88, deck - 12); Rope(c, 25, 19, 6, deck - 13);
        Mast(c, 24, 19, (int)deck - 8, 1);
        Mast(c, 46, 2, (int)deck, 2);
        Mast(c, 66, 8, (int)deck, 2);
        AftSail(c, 24, 23, 18, 5, deck - 16, 14, cloth, shade, blue, 0.85f);
        Jib(c, new Vector2(69, 10), new Vector2(93, deck - 15), new Vector2(69, deck - 12), cloth, shade);
        Jib(c, new Vector2(69, 20), new Vector2(87, deck - 12), new Vector2(69, deck - 10), cloth, shade);
        // three tiers of sails on the main and the fore masts; a gold star on the main course
        SquareSail(c, 47, 13, 15, 8, cloth, shade, Bark);
        SquareSail(c, 47, 25, 22, 11, cloth, shade, Bark);
        c.Rect(43, 38, 9, 2, Bark); c.Rect(43, 38, 9, 1, gold); // crow's nest
        SquareSail(c, 47, 41, 28, 14, cloth, shade, Bark, (u, v) =>
        {
            float dx = (u - 0.5f) * 28f, dy = (v - 0.45f) * 14f, r = Mathf.Sqrt(dx * dx + dy * dy);
            if (r > 4.6f) return Color.clear;
            if (r > 4f) return gold;
            float a = Mathf.Atan2(dy, dx);
            return r < 1.8f + 1.6f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2.5f - Mathf.PI * 0.5f * 0f)), 3f) ? gold : blue;
        });
        SquareSail(c, 67, 18, 12, 7, cloth, shade, Bark);
        SquareSail(c, 67, 29, 17, 10, cloth, shade, Bark);
        SquareSail(c, 67, 42, 22, 12, cloth, shade, Bark, (u, v) => v > 0.74f && v < 0.84f ? blue : Color.clear);
        c.Line(80, (int)deck - 8, 94, (int)deck - 15, BarkD); // bowsprit
        Pennant(c, 46, 0, 19, 3f, gold, blue);
        Pennant(c, 66, 6.5f, 10, 2.4f, blue, gold);
        Pennant(c, 24, 17.5f, 7, 2f, blue, blue);
        // white and gold topsides over a navy bottom, gold-rimmed round ports
        Color white = Hex("#f4f1e8"), whiteD = Hex("#c9ccd6"), navy = Hex("#1f3a6a"), navyD = Hex("#142446");
        Hull(c, x0, x1, deck, wl, br, sr, false, (t, v, d) =>
        {
            if (d < 1f) return gold;
            if (d < 1.4f) return Hex("#9a6a20");
            if (d > 6.6f && d < 7.3f) return gold;
            if (d < 6.6f) return Color.Lerp(white, whiteD, d / 10f);
            Color b = v > 0.86f ? navyD : navy;
            return Mathf.Repeat(d, 2.3f) < 0.5f ? Dk(b, 0.3f) : b;
        }, 0.1f);
        for (float px = 26; px <= 72; px += 7.5f)
            Porthole(c, px, HullTop(px, x0, x1, deck, br, sr) + 4f, gold, Hex("#2a5a9a"));
        // stern castle: tall glowing windows, a gilded gallery and two lanterns
        int st = Mathf.RoundToInt(HullTop(12, x0, x1, deck, br, sr));
        for (int x = 7; x <= 19; x += 4) { c.Rect(x, st + 2, 3, 4, gold); c.Rect(x, st + 2, 2, 3, Hex("#ffe29a")); }
        c.Rect(6, st + 7, 17, 1, gold);
        for (int x = 7; x <= 22; x += 2) c.Set(x, st + 8, Hex("#c8962a"));
        foreach (int lx in new[] { 6, 16 })
        {
            c.Rect(lx, st - 4, 1, 4, Hex("#9a6a20"));
            Glow(c, lx + 0.5f, st - 5.5f, 4f, Hex("#ffe08a"));
            Lantern(c, lx + 0.5f, st - 5.5f, Hex("#ffe29a"));
        }
        // a stern flag and a golden figurehead under the bowsprit
        c.Rect(4, st - 12, 1, 8, Hex("#9a6a20"));
        Pennant(c, 4.5f, st - 12, 4, 3f, blue, blue);
        float ft = HullTop(81, x0, x1, deck, br, sr);
        c.Line(79, Mathf.RoundToInt(ft) + 1, 84, Mathf.RoundToInt(ft) - 2, gold);
        c.Ball(85, ft - 2.5f, 1.6f, 1.6f, Hex("#a8781e"), gold, Hex("#fff0a0"));
    }

    // ------------------------------------------------------------------ boat drawing kit

    static float Cf(int f) => (f + 0.5f) / Detail;
    static float Sq(float u) => u <= 0f ? 0f : u >= 1f ? 1f : u * u;
    static Color Dk(Color c, float k) => new Color(c.r * (1f - k), c.g * (1f - k), c.b * (1f - k), c.a);
    static Color Lt(Color c, float k) { var o = Color.Lerp(c, Color.white, k); o.a = c.a; return o; }
    static void Ball(Canvas c, float x, float y, float rx, float ry, Color d, Color m, Color l) => c.Ball(x, y, rx, ry, d, m, l);

    /// The gunwale's height at x: the sheer rises towards the stern and the bow.
    static float HullTop(float x, float x0, float x1, float deck, float bowRise, float sternRise)
    {
        float t = (x - x0) / (x1 - x0);
        return deck - sternRise * Sq(1f - t / 0.25f) - bowRise * Sq((t - 0.7f) / 0.3f);
    }

    /// A hull in side view from x0 (stern) to x1 (bow): the keel sweeps up into the bow, the stern
    /// is a short flat transom (or a second point for canoes). paint(t along, v down from the gunwale
    /// relative to the hull depth, d = px below the gunwale) colours it.
    static void Hull(Canvas c, float x0, float x1, float deck, float keel, float bowRise, float sternRise, bool pointyStern,
                     Func<float, float, float, Color> paint, float sternCut = 0.12f)
    {
        float L = x1 - x0, D = keel - deck;
        for (int fx = Mathf.FloorToInt(x0 * Detail); fx <= Mathf.CeilToInt(x1 * Detail); fx++)
        {
            float wx = Cf(fx), t = (wx - x0) / L;
            if (t < 0f || t > 1f) continue;
            float top = HullTop(wx, x0, x1, deck, bowRise, sternRise);
            float lift = Mathf.Pow(Mathf.Clamp01((t - 0.55f) / 0.45f), 1.7f) * 0.95f
                       + (pointyStern ? Mathf.Pow(Mathf.Clamp01((0.4f - t) / 0.4f), 1.7f) * 0.9f : Mathf.Clamp01((sternCut - t) / sternCut) * 0.45f);
            float bottom = keel - (keel - top) * Mathf.Min(0.96f, lift);
            for (int fy = Mathf.FloorToInt(top * Detail); fy < Mathf.CeilToInt(bottom * Detail); fy++)
            {
                float wy = Cf(fy);
                if (wy < top || wy > bottom) continue;
                c.SetFine(fx, fy, paint(t, (wy - top) / D, wy - top));
            }
        }
    }

    /// One log lying lengthways, its sawn end facing us on the left.
    static void Log(Canvas c, float xa, float xb, float cy, float r, Color? mid = null, Color? dark = null, Color? light = null)
    {
        Color m = mid ?? Bark, d = dark ?? BarkD, l = light ?? Hex("#9a6438");
        for (int fy = Mathf.FloorToInt((cy - r) * Detail); fy < Mathf.CeilToInt((cy + r) * Detail); fy++)
            for (int fx = Mathf.FloorToInt(xa * Detail); fx < Mathf.CeilToInt(xb * Detail); fx++)
            {
                float wy = Cf(fy), wx = Cf(fx), dy = (wy - cy) / r;
                if (Mathf.Abs(dy) > 1f) continue;
                float rx = r * 0.6f, ex = (wx - xb + rx) / rx; // round far end
                if (ex > 0f && ex * ex + dy * dy > 1f) continue;
                Color col = dy < -0.4f ? l : dy > 0.45f ? d : m;
                if (Hash(fx / 3, fy, 11) < 0.12f) col = d; // grain
                c.SetFine(fx, fy, col);
            }
        c.Ellipse(xa, cy, r * 0.65f, r, Hex("#d8b07a"));
        c.Ellipse(xa, cy, r * 0.3f, r * 0.45f, Hex("#a87a48"));
    }

    /// A rope lashing wrapped over the logs.
    static void Lash(Canvas c, int x, int y, int h)
    {
        c.Rect(x, y, 1, h, RopeC);
        for (int j = 0; j < h; j += 2) c.Set(x, y + j, Hex("#9a7a48"));
    }

    static void Mast(Canvas c, int x, int top, int bottom, int w)
    {
        c.Rect(x, top, w, bottom - top, BarkD);
        for (int fy = top * Detail; fy < bottom * Detail; fy++) c.SetFine(x * Detail, fy, Bark);
    }

    static void Rope(Canvas c, float x0, float y0, float x1, float y1) =>
        c.LineFine(Mathf.RoundToInt(x0 * Detail), Mathf.RoundToInt(y0 * Detail), Mathf.RoundToInt(x1 * Detail), Mathf.RoundToInt(y1 * Detail), Hex("#3a2a20"));

    /// A square sail hanging from its yard, bellied out by the wind and lit from the top-left.
    /// emblem(u, v) may paint over it (clear = plain cloth).
    static void SquareSail(Canvas c, float cx, float top, float w, float h, Color cloth, Color shade, Color yard, Func<float, float, Color> emblem = null)
    {
        float x0 = cx - w / 2f, belly = Mathf.Max(1f, h * 0.16f);
        for (int fx = Mathf.FloorToInt(x0 * Detail); fx < Mathf.CeilToInt((x0 + w) * Detail); fx++)
        {
            float u = (Cf(fx) - x0) / w;
            if (u < 0f || u > 1f) continue;
            float s = Mathf.Sin(u * Mathf.PI), yt = top + 0.3f * belly * s, yb = top + h + belly * s;
            for (int fy = Mathf.FloorToInt(yt * Detail); fy < Mathf.CeilToInt(yb * Detail); fy++)
            {
                float wy = Cf(fy);
                if (wy < yt || wy > yb) continue;
                float v = (wy - yt) / (yb - yt), sh = u * 0.65f + v * 0.45f;
                Color col = sh > 0.82f ? shade : sh > 0.7f ? (((fx + fy) & 1) == 0 ? shade : cloth) : cloth;
                if (u < 0.12f && v < 0.5f) col = Lt(cloth, 0.4f);
                if (Mathf.Repeat(u * w, w / 4f) < 0.5f && u > 0.1f && u < 0.9f) col = Color.Lerp(col, shade, 0.5f); // seams
                if (emblem != null) { var e = emblem(u, v); if (e.a > 0f) col = sh > 0.82f ? Dk(e, 0.2f) : e; }
                if (v > 0.92f) col = Dk(shade, 0.12f); // hem
                c.SetFine(fx, fy, col);
            }
        }
        c.Rect(Mathf.RoundToInt(x0 - 1), Mathf.RoundToInt(top) - 1, Mathf.RoundToInt(w + 2), 1, yard);
    }

    /// A fore-and-aft sail behind (left of) mast x: luff on the mast from luffTop down to the foot,
    /// a gaff out to the peak (gaffW px left, at peakY; 0 = a plain triangle), the foot reaching footW px left.
    static void AftSail(Canvas c, float mx, float luffTop, float peakY, float gaffW, float foot, float footW,
                        Color cloth, Color shade, Color band = default, float bandV = -1f)
    {
        for (int fx = Mathf.FloorToInt((mx - footW) * Detail); fx < Mathf.CeilToInt(mx * Detail); fx++)
        {
            float k = mx - Cf(fx);
            if (k < 0f || k > footW) continue;
            float yt;
            if (gaffW > 0f && k <= gaffW) yt = luffTop + (peakY - luffTop) * (k / gaffW);
            else
            {
                float q = (k - gaffW) / Mathf.Max(0.01f, footW - gaffW);
                yt = peakY + (foot - peakY) * q - 2.2f * Mathf.Sin(q * Mathf.PI) * (1f - q * 0.3f); // the leech bellies out
            }
            for (int fy = Mathf.FloorToInt(yt * Detail); fy < Mathf.CeilToInt(foot * Detail); fy++)
            {
                float wy = Cf(fy);
                if (wy < yt || wy > foot) continue;
                float v = (wy - yt) / Mathf.Max(0.01f, foot - yt), r = k / footW;
                Color col = r > 0.82f ? shade : r > 0.7f ? (((fx + fy) & 1) == 0 ? shade : cloth) : r < 0.12f ? Lt(cloth, 0.35f) : cloth;
                if (Mathf.Repeat(wy, 3f) < 0.5f) col = Color.Lerp(col, shade, 0.5f); // seams
                if (bandV >= 0f && Mathf.Abs((wy - peakY) / Mathf.Max(1f, foot - peakY) - bandV) < 0.08f) col = r > 0.8f ? Dk(band, 0.2f) : band;
                c.SetFine(fx, fy, col);
            }
        }
        if (gaffW > 0f) c.LineFine(Mathf.RoundToInt(mx * Detail), Mathf.RoundToInt(luffTop * Detail), Mathf.RoundToInt((mx - gaffW) * Detail), Mathf.RoundToInt(peakY * Detail), Bark, 2);
    }

    /// A triangular head sail from the mast to the bowsprit.
    static void Jib(Canvas c, Vector2 head, Vector2 tack, Vector2 clew, Color cloth, Color shade) =>
        Tri(c, head, tack, clew, (x, y) =>
        {
            float r = (x - clew.x) / Mathf.Max(1f, tack.x - clew.x);
            Color col = r > 0.75f ? shade : r < 0.2f ? Lt(cloth, 0.3f) : cloth;
            return Mathf.Repeat(y, 3f) < 0.5f ? Color.Lerp(col, shade, 0.5f) : col;
        });

    /// A junk's battened lug sail: straight top and foot, a fan-shaped leech on the left.
    static void JunkSail(Canvas c, float left, float right, float topL, float topR, float bottom, Color cloth, Color clothL, Color batten)
    {
        for (int fy = Mathf.FloorToInt(Mathf.Min(topL, topR) * Detail); fy < Mathf.CeilToInt(bottom * Detail); fy++)
        {
            float wy = Cf(fy), v = Mathf.Clamp01((wy - topL) / (bottom - topL));
            float edge = left - (right - left) * 0.22f * Mathf.Sin(v * Mathf.PI); // fan-shaped leech
            for (int fx = Mathf.FloorToInt(edge * Detail); fx < Mathf.CeilToInt(right * Detail); fx++)
            {
                float wx = Cf(fx);
                if (wx < edge || wx > right) continue;
                float top = topL + (topR - topL) * (wx - left) / (right - left);
                if (wy < top) continue;
                float pv = Mathf.Repeat(wy - top, 4f) / 4f, u = (wx - edge) / (right - edge);
                Color col = pv < 0.14f || wy > bottom - 0.6f ? batten : Color.Lerp(clothL, cloth, pv);
                if (u > 0.85f && pv >= 0.14f) col = Dk(col, 0.15f);
                c.SetFine(fx, fy, col);
            }
        }
        c.LineFine(Mathf.RoundToInt(left * Detail), Mathf.RoundToInt(topL * Detail), Mathf.RoundToInt(right * Detail), Mathf.RoundToInt(topR * Detail), batten, 2);
    }

    /// A pennant streaming back (left) from the masthead at (x, y).
    static void Pennant(Canvas c, float x, float y, float len, float h, Color a, Color b)
    {
        for (int fx = Mathf.FloorToInt((x - len) * Detail); fx < Mathf.CeilToInt(x * Detail); fx++)
        {
            float k = (x - Cf(fx)) / len;
            if (k < 0f || k > 1f) continue;
            float half = h * 0.5f * (1f - k * 0.8f), cy = y + h * 0.5f + Mathf.Sin(k * 7f + 0.5f) * 0.7f * k;
            for (int fy = Mathf.FloorToInt((cy - half) * Detail); fy < Mathf.CeilToInt((cy + half) * Detail); fy++)
            {
                float wy = Cf(fy);
                if (Mathf.Abs(wy - cy) > half) continue;
                Color col = Mathf.Repeat(k * len, 4f) < 2f ? a : b;
                c.SetFine(fx, fy, wy > cy ? Dk(col, 0.18f) : col);
            }
        }
    }

    static void Tri(Canvas c, Vector2 a, Vector2 b, Vector2 d, Func<float, float, Color> paint)
    {
        float Cross(Vector2 p, Vector2 q, Vector2 r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
        float area = Cross(a, b, d);
        if (Mathf.Abs(area) < 0.01f) return;
        int fx0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, d.x)) * Detail), fx1 = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, d.x)) * Detail);
        int fy0 = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, d.y)) * Detail), fy1 = Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, d.y)) * Detail);
        for (int fy = fy0; fy < fy1; fy++)
            for (int fx = fx0; fx < fx1; fx++)
            {
                var p = new Vector2(Cf(fx), Cf(fy));
                float w0 = Cross(b, d, p) / area, w1 = Cross(d, a, p) / area, w2 = Cross(a, b, p) / area;
                if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                var col = paint(p.x, p.y);
                if (col.a > 0f) c.SetFine(fx, fy, col);
            }
    }

    static void Porthole(Canvas c, float x, float y, Color rim, Color glass)
    {
        c.Ellipse(x, y, 1.4f, 1.4f, rim);
        c.Ellipse(x, y, 0.8f, 0.8f, glass);
        c.SetFine(Mathf.FloorToInt((x - 0.4f) * Detail), Mathf.FloorToInt((y - 0.4f) * Detail), Lt(glass, 0.7f));
    }

    static void Glow(Canvas c, float x, float y, float r, Color col)
    {
        for (int i = 3; i >= 1; i--) c.Ellipse(x, y, r * i / 3f, r * i / 3f, new Color(col.r, col.g, col.b, 0.14f));
    }

    /// A small hanging lantern (paper = round red one, else a framed glass one).
    static void Lantern(Canvas c, float x, float y, Color light, bool paper = false)
    {
        Color frame = paper ? Hex("#d8a03a") : Hex("#2a2018");
        if (paper)
        {
            c.Ellipse(x, y, 1.5f, 1.8f, light);
            c.Ellipse(x - 0.4f, y - 0.4f, 0.6f, 0.8f, Lt(light, 0.4f));
        }
        else
        {
            c.Ellipse(x, y, 1.1f, 1.5f, light);
            c.Ellipse(x - 0.2f, y - 0.3f, 0.5f, 0.7f, Lt(light, 0.6f));
        }
        c.Rect(Mathf.RoundToInt(x - 1.5f), Mathf.RoundToInt(y - 2.3f), 2, 1, frame);
        c.Rect(Mathf.RoundToInt(x - 1.5f), Mathf.RoundToInt(y + 1.4f), 2, 1, frame);
    }
}
