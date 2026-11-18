using UnityEngine;

/// The later continents' own creatures (IslandGame.Monsters.cs): stranger the further you go.
public static partial class PixelArt
{
    /// A snow ghost with a mirror for a face.
    static Sprite MirrorWraith(int frame) => Get("mirror_wraith_" + frame, () =>
    {
        var c = new Canvas(24, 30);
        int b = frame;
        c.Ellipse(12, 28, 7, 1.6f, Shadow);
        Color w = Hex("#e8f4ff"), wD = Hex("#a8c8e8");
        for (int y = 8; y <= 26; y++) { int hw = 4 + (y - 8) / 3; c.Rect(12 - hw, y - b, hw * 2, 1, y % 4 == 0 ? wD : w); }
        for (int x = 4; x <= 20; x += 4) c.Rect(x, 26 - b + (x / 4 + b) % 2, 2, 2, w); // ragged hem
        c.Ball(12, 9 - b, 6, 6, wD, w, Color.white);
        // the mirror face
        c.Ellipse(12, 9 - b, 4, 4.5f, Hex("#5a7a9a")); c.Ellipse(12, 9 - b, 3, 3.5f, Hex("#c8e8ff"));
        c.Line(10, 7 - b, 13, 11 - b, Color.white);
        c.Line(4, 15 - b, 1, 20 - b, wD); c.Line(20, 15 - b, 23, 20 - b, wD); // reaching arms
        return c.ToSprite(12, 28);
    });

    /// A two-headed ice mammoth with frozen tusks.
    static Sprite FrostMammoth(int frame) => Get("frost_mammoth_" + frame, () =>
    {
        var c = new Canvas(40, 30);
        int b = frame;
        c.Ellipse(20, 28, 15, 2.5f, Shadow);
        Color f = Hex("#8a6a5a"), fD = Hex("#5a4038"), fL = Hex("#b8988a"), ice = Hex("#c8ecff");
        c.Rect(9, 20, 4, 8 - b, fD); c.Rect(15, 20, 4, 8, f); c.Rect(23, 20, 4, 8 - b, fD); c.Rect(29, 20, 4, 8, f); // legs
        c.Ball(21, 16, 13, 8, fD, f, fL);
        for (int x = 10; x < 33; x += 3) c.Rect(x, 9, 2, 3, Hex("#e8f4ff")); // frost on its back
        // two heads
        for (int h = 0; h < 2; h++)
        {
            int hx = 6 + h * 8, hy = 14 + h * 2 - b;
            c.Ball(hx, hy, 5, 5, fD, f, fL);
            c.Line(hx - 2, hy + 3, hx - 4, hy + 9, fD); // trunk
            c.Line(hx + 1, hy + 3, hx - 3, hy + 6, ice); c.Line(hx + 2, hy + 3, hx - 2, hy + 7, ice); // tusks
            c.Set(hx - 1, hy - 1, Hex("#4ac8ff"));
        }
        return c.ToSprite(20, 28);
    });

    /// A lava salamander, glowing cracks along its back.
    static Sprite LavaSalamander(int frame) => Get("lava_salamander_" + frame, () =>
    {
        var c = new Canvas(34, 18);
        int b = frame;
        c.Ellipse(17, 16, 13, 1.8f, Shadow);
        Color d = Hex("#3a1410"), m = Hex("#7a2a14"), l = Hex("#c84a1a"), lava = Hex("#ffa83a");
        c.Ball(16, 11, 10, 4, d, m, l);
        c.Ball(27, 9 - b, 5, 3.5f, d, m, l); // head
        c.Line(6, 11, 1, 8 + b * 2, m); c.Line(6, 12, 2, 10 + b * 2, d); // tail
        for (int x = 9; x < 24; x += 3) c.Set(x, 7 + (x % 2), lava);
        c.Rect(10, 14, 2, 3 - b, d); c.Rect(20, 14, 2, 2 + b, d); // legs
        c.Set(28, 8 - b, Hex("#ffe06a")); c.Rect(30, 10 - b, 3, 1, lava); // eye, a lick of flame
        return c.ToSprite(17, 16);
    });

    /// A golem of cooling magma.
    static Sprite MagmaGolem(int frame) => Get("magma_golem_" + frame, () =>
    {
        var c = new Canvas(32, 36);
        int b = frame;
        c.Ellipse(16, 34, 11, 2.2f, Shadow);
        Color d = Hex("#2a1a16"), m = Hex("#4a2e26"), l = Hex("#7a4a3a"), lava = Hex("#ff7a2a");
        c.Rect(9, 26, 5, 8, d); c.Rect(18, 26, 5, 8, m);
        c.Ball(16, 18 - b, 10, 9, d, m, l);
        c.Ball(4, 18 - b, 4, 7, d, m, l); c.Ball(28, 18 - b, 4, 7, d, m, l); // fists
        c.Ball(16, 7 - b, 6, 5, d, m, l);
        c.Rect(13, 6 - b, 2, 2, lava); c.Rect(18, 6 - b, 2, 2, lava);
        c.Line(10, 14 - b, 16, 20 - b, lava); c.Line(16, 20 - b, 22, 15 - b, lava); c.Line(16, 20 - b, 16, 25 - b, Hex("#ffd06a"));
        return c.ToSprite(16, 34);
    });

    /// A floating jellyfish of cloud and lightning.
    static Sprite SkyJelly(int frame) => Get("sky_jelly_" + frame, () =>
    {
        var c = new Canvas(26, 34);
        int b = frame * 2;
        c.Ellipse(13, 32, 6, 1.4f, Shadow);
        c.Ball(13, 9 - b, 10, 7, Hex("#8a9ad8"), Hex("#c8d4ff"), Color.white);
        c.Rect(4, 12 - b, 18, 2, Hex("#a8b8f0"));
        for (int t = 0; t < 5; t++)
        {
            int x = 5 + t * 4;
            for (int y = 14; y < 26; y++) c.Set(x + ((y + t + frame) / 3) % 2, y - b, (y + t) % 4 == 0 ? Hex("#fff36a") : Hex("#c8d4ff"));
        }
        c.Set(9, 8 - b, Hex("#3a4a8a")); c.Set(16, 8 - b, Hex("#3a4a8a"));
        return c.ToSprite(13, 32);
    });

    // ------------------------------------------------------------------ the sunken prison (IslandGame.Prison.cs)
    /// At sea: an iron grate in the middle of a whirlpool.
    public static Sprite PrisonGate() => Get("prison_gate", () =>
    {
        var c = new Canvas(44, 44) { Rim = false };
        c.Ellipse(22, 22, 21, 21, new Color(0.1f, 0.25f, 0.45f, 0.55f));
        c.Ellipse(22, 22, 16, 16, new Color(0.08f, 0.18f, 0.35f, 0.75f));
        for (int i = 0; i < 4; i++) c.Ellipse(22 + (i % 2 == 0 ? 6 : -6), 22 + (i < 2 ? 5 : -5), 3, 1.2f, new Color(0.8f, 0.95f, 1f, 0.6f)); // foam swirls
        c.Rect(13, 13, 18, 18, Hex("#2a2e38"));
        for (int x = 15; x <= 29; x += 3) c.Rect(x, 13, 1, 18, Hex("#7a8090"));
        for (int y = 15; y <= 29; y += 4) c.Rect(13, y, 18, 1, Hex("#7a8090"));
        c.Rect(13, 13, 18, 1, Hex("#a8b0c0")); c.Rect(21, 20, 3, 4, Hex("#c8a040")); // a lock
        return c.ToSprite(22, 22);
    });

    /// Stairs down into the dark, flat on the floor.
    public static Sprite PrisonStairs() => Get("prison_stairs", () =>
    {
        var c = new Canvas(36, 20) { Rim = false };
        c.Ellipse(18, 10, 16, 8, Hex("#2a3040"));
        for (int i = 0; i < 4; i++) c.Rect(8 + i * 2, 5 + i * 3, 20 - i * 4, 2, Color.Lerp(Hex("#7a8498"), Hex("#151820"), i / 3f));
        c.Ellipse(18, 16, 6, 2.5f, Hex("#0a0c12"));
        return c.ToSprite(18, 10);
    });

    /// A cracked bit of wall with a faint light behind it.
    public static Sprite PrisonSecretWall() => Get("prison_secret", () =>
    {
        var c = new Canvas(24, 30);
        c.Ellipse(12, 28, 9, 2, Shadow);
        c.Rect(3, 6, 18, 22, Hex("#3a4258")); c.Rect(3, 6, 18, 2, Hex("#5a6478"));
        for (int y = 10; y < 28; y += 5) c.Rect(3, y, 18, 1, Hex("#2a3044")); // brick rows
        c.Line(10, 8, 13, 15, Hex("#0e1018")); c.Line(13, 15, 11, 21, Hex("#0e1018")); c.Line(11, 21, 14, 27, Hex("#0e1018"));
        c.Set(12, 14, Hex("#ffe8a0")); c.Set(12, 19, Hex("#ffe8a0")); // light through the crack
        return c.ToSprite(12, 28);
    });

    /// A floating ball of many eyes.
    static Sprite StarWatcher(int frame) => Get("star_watcher_" + frame, () =>
    {
        var c = new Canvas(28, 32);
        int b = frame;
        c.Ellipse(14, 30, 7, 1.6f, Shadow);
        c.Ball(14, 14 - b, 11, 11, Hex("#1a0e3a"), Hex("#3e2a7a"), Hex("#7a5ac8"));
        c.Ellipse(14, 14 - b, 5, 4, Color.white); c.Ellipse(14, 14 - b, 2.5f, 3, Hex("#ffb83a")); c.Ellipse(14, 14 - b, 1, 2, Color.black);
        int[,] eyes = { { 6, 8 }, { 22, 8 }, { 5, 19 }, { 23, 19 }, { 14, 4 }, { 10, 23 }, { 18, 23 } };
        for (int i = 0; i < eyes.GetLength(0); i++)
        {
            int x = eyes[i, 0], y = eyes[i, 1] - b;
            c.Ellipse(x, y, 2, 1.6f, Color.white); c.Set(x, y, (i + frame) % 3 == 0 ? Hex("#ff4a4a") : Color.black);
        }
        for (int i = 0; i < 4; i++) c.Line(8 + i * 4, 24 - b, 7 + i * 4 + frame, 29, Hex("#3e2a7a")); // dangling stalks
        return c.ToSprite(14, 30);
    });
}
