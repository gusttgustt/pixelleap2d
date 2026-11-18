using UnityEngine;

/// The continent bosses (IslandGame.ContinentBoss.cs): four huge figures, each unlike anything
/// else in the game so they're known at a glance, and the materials and gear they leave behind.
public static partial class PixelArt
{
    // ------------------------------------------------------------------ the bosses
    /// The Tide Lord (continent 1): a giant sea king in a whirl of water, coral crown and trident.
    static Sprite TideLord(int frame) => Get("boss_tide_" + frame, () =>
    {
        var c = new Canvas(64, 64);
        int b = frame;
        c.Ellipse(32, 60, 26, 4, Shadow);
        // the whirlpool he stands in
        c.Ellipse(32, 56, 24, 6, Hex("#1f6a9a")); c.Ellipse(32, 55, 19, 4.5f, Hex("#3a9ad0")); c.Ellipse(32, 54, 12, 3, Hex("#8ad8ff"));
        for (int i = 0; i < 6; i++) c.Set(10 + i * 9, 55 + (i + b) % 2, Color.white);
        // body: a great blue-green torso and fish tail curling round
        c.Ball(32, 38 - b, 15, 15, Hex("#0f4a5a"), Hex("#1f8a8a"), Hex("#6ad8c8"));
        c.Ball(20, 50, 7, 5, Hex("#0f4a5a"), Hex("#1f8a8a"), Hex("#6ad8c8"));
        for (int y = 30; y < 50; y += 3) c.Rect(24, y - b, 16, 1, Hex("#156a6a")); // scales
        // arms
        c.Ball(14, 34 - b, 5, 7, Hex("#0f4a5a"), Hex("#1f8a8a"), Hex("#6ad8c8"));
        c.Ball(50, 34 - b, 5, 7, Hex("#0f4a5a"), Hex("#1f8a8a"), Hex("#6ad8c8"));
        // head, coral beard, glowing eyes
        c.Ball(32, 18 - b, 10, 9, Hex("#2a6a5a"), Hex("#4aa88a"), Hex("#9ae0c8"));
        c.Ball(32, 26 - b, 8, 5, Hex("#a83a4a"), Hex("#e05a6a"), Hex("#ff9aa8"));
        c.Rect(27, 16 - b, 3, 2, Hex("#fff36a")); c.Rect(34, 16 - b, 3, 2, Hex("#fff36a"));
        // crown
        Color gold = Hex("#f2c43a"), goldD = Hex("#b88a1a");
        c.Rect(23, 7 - b, 18, 3, gold); c.Rect(23, 9 - b, 18, 1, goldD);
        for (int i = 0; i < 4; i++) c.Rect(24 + i * 5, 3 - b + (i % 2), 2, 4, gold);
        c.Set(32, 8 - b, Hex("#3b7dd8"));
        // trident in his right hand
        c.Line(54, 44 - b, 60, 6 - b, Hex("#c8a040")); c.Line(55, 44 - b, 61, 6 - b, Hex("#8a6a20"));
        c.Line(58, 6 - b, 56, 0, Hex("#e8e8f0")); c.Line(61, 6 - b, 63, 0, Hex("#e8e8f0")); c.Line(60, 6 - b, 60, 0, Hex("#e8e8f0"));
        return c.ToSprite(32, 60);
    });

    /// The Frost Mirror Queen (continent 2): a tall figure in a gown of ice, mirror shards circling her.
    static Sprite FrostQueen(int frame) => Get("boss_frost_" + frame, () =>
    {
        var c = new Canvas(56, 68);
        int b = frame;
        c.Ellipse(28, 64, 20, 3.5f, Shadow);
        Color ice = Hex("#a8d8f8"), iceD = Hex("#5a98c8"), iceL = Hex("#eef8ff");
        // the gown: a wide cone of ice
        for (int y = 30; y <= 63; y++)
        {
            int hw = 4 + (y - 30) * 16 / 33;
            c.Rect(28 - hw, y - b, hw, 1, ice); c.Rect(28, y - b, hw, 1, iceD);
            if (y % 5 == 0) c.Rect(28 - hw + 2, y - b, 2, 1, iceL);
        }
        c.Rect(26, 30 - b, 4, 33, iceL); // a bright seam down the front
        // body, arms, pale face
        c.Ball(28, 26 - b, 7, 6, iceD, ice, iceL);
        c.Line(21, 26 - b, 12, 36 - b, iceD); c.Line(35, 26 - b, 44, 36 - b, iceD);
        c.Ball(28, 15 - b, 6, 6, Hex("#b8c8d8"), Hex("#e8f0f8"), Color.white);
        c.Rect(25, 14 - b, 2, 2, Hex("#2a6ad8")); c.Rect(30, 14 - b, 2, 2, Hex("#2a6ad8"));
        c.Rect(27, 18 - b, 3, 1, Hex("#6a8ab8"));
        // long white hair
        c.Rect(21, 11 - b, 3, 16, Hex("#f4f8ff")); c.Rect(32, 11 - b, 3, 16, Hex("#dce8f4"));
        // a crown of ice spikes
        for (int i = 0; i < 5; i++) c.Line(22 + i * 3, 9 - b, 22 + i * 3 + (i - 2), 1 + Mathf.Abs(i - 2) * 2 - b, i == 2 ? Color.white : ice);
        // mirror shards floating round her
        int[,] shards = { { 6, 20 }, { 49, 16 }, { 4, 44 }, { 51, 42 }, { 28, 4 } };
        for (int i = 0; i < shards.GetLength(0); i++)
        {
            int sx = shards[i, 0], sy = shards[i, 1] + ((i + b) % 2 == 0 ? -1 : 1);
            c.Line(sx, sy - 3, sx + 2, sy, iceL); c.Line(sx + 2, sy, sx, sy + 3, ice); c.Line(sx, sy + 3, sx - 2, sy, iceD); c.Line(sx - 2, sy, sx, sy - 3, ice);
            c.Set(sx, sy, Color.white);
        }
        return c.ToSprite(28, 64);
    });

    /// The Magma Hydra (continent 3): three fire-serpent heads on long necks over a cracked lava body.
    static Sprite MagmaHydra(int frame) => Get("boss_hydra_" + frame, () =>
    {
        var c = new Canvas(72, 64);
        int b = frame;
        c.Ellipse(36, 60, 30, 4, Shadow);
        Color rock = Hex("#3a1a14"), rockM = Hex("#6a2a1a"), rockL = Hex("#a84a2a"), lava = Hex("#ff8a2a"), lavaL = Hex("#ffe06a");
        // the body: a heap of cooling lava with glowing cracks
        c.Ball(36, 48, 24, 12, rock, rockM, rockL);
        c.Line(18, 46, 28, 52, lava); c.Line(28, 52, 40, 47, lava); c.Line(40, 47, 52, 53, lava); c.Line(30, 42, 36, 47, lavaL);
        // three necks and heads (the middle one highest)
        int[] hx = { 14, 36, 58 }, hy = { 20, 10, 20 };
        for (int h = 0; h < 3; h++)
        {
            int bob = (h + b) % 2;
            int x0 = 24 + h * 12, y0 = 42;
            for (int s = 0; s <= 10; s++)
            {
                float t = s / 10f;
                int x = Mathf.RoundToInt(Mathf.Lerp(x0, hx[h], t)), y = Mathf.RoundToInt(Mathf.Lerp(y0, hy[h] + 6, t)) - bob;
                c.Ball(x, y, 3.5f, 3.5f, rock, rockM, rockL);
            }
            int x1 = hx[h], y1 = hy[h] - bob;
            c.Ball(x1, y1, 7, 5.5f, rock, rockM, rockL);
            c.Rect(x1 - 4, y1 - 1, 2, 2, lavaL); c.Rect(x1 + 2, y1 - 1, 2, 2, lavaL); // eyes
            c.Rect(x1 - 3, y1 + 3, 7, 1, lava);                                      // a burning mouth
            c.Line(x1 - 5, y1 - 4, x1 - 7, y1 - 9, rockL); c.Line(x1 + 5, y1 - 4, x1 + 7, y1 - 9, rockL); // horns
            if (b == 1) { c.Set(x1, y1 + 5, lavaL); c.Set(x1 - 1, y1 + 6, lava); c.Set(x1 + 1, y1 + 7, lava); } // flames dripping
        }
        return c.ToSprite(36, 60);
    });

    /// The Void Titan (continent 4): a dark giant with a black hole for a heart and stars in its skin.
    static Sprite VoidTitan(int frame) => Get("boss_void_" + frame, () =>
    {
        var c = new Canvas(64, 70);
        int b = frame;
        c.Ellipse(32, 66, 24, 4, Shadow);
        Color d = Hex("#120a24"), m = Hex("#2e1a5a"), l = Hex("#6a4ab8");
        // legs and a huge body
        c.Rect(22, 50 - b, 7, 16 + b, d); c.Rect(35, 50 - b, 7, 16 + b, m);
        c.Ball(32, 38 - b, 18, 16, d, m, l);
        // arms like pillars, glowing fists
        c.Ball(10, 40 - b, 6, 11, d, m, l); c.Ball(54, 40 - b, 6, 11, d, m, l);
        c.Ball(10, 52 - b, 5, 4, Hex("#4a1a8a"), Hex("#a86aff"), Hex("#f0d8ff")); c.Ball(54, 52 - b, 5, 4, Hex("#4a1a8a"), Hex("#a86aff"), Hex("#f0d8ff"));
        // the black hole in its chest: a ring of light round nothing
        c.Ellipse(32, 38 - b, 9, 9, Hex("#c8a0ff")); c.Ellipse(32, 38 - b, 7.5f, 7.5f, Hex("#7a3aff")); c.Ellipse(32, 38 - b, 5.5f, 5.5f, Color.black);
        // head with horns and one great eye
        c.Ball(32, 16 - b, 10, 9, d, m, l);
        c.Line(23, 12 - b, 16, 2 - b, l); c.Line(41, 12 - b, 48, 2 - b, l);
        c.Ellipse(32, 16 - b, 4.5f, 3, Hex("#ffe06a")); c.Ellipse(32, 16 - b, 1.5f, 2.5f, Color.black);
        // stars in its skin
        var r = new System.Random(77);
        for (int i = 0; i < 18; i++) c.Set(r.Next(16, 48), r.Next(24, 52) - b, (i + b) % 3 == 0 ? Color.white : Hex("#c8b8ff"));
        return c.ToSprite(32, 66);
    });

    // ------------------------------------------------------------------ materials and gear icons
    static bool DrawBossIcon(Canvas c, Item it)
    {
        switch (it)
        {
            case Item.TideScale: Scale(c, Hex("#0f4a5a"), Hex("#1f8a8a"), Hex("#9ae0d8")); return true;
            case Item.StormPearl: c.Ball(8, 8, 5.5f, 5.5f, Hex("#2a4a8a"), Hex("#5a8ad8"), Hex("#e0f0ff")); c.Line(6, 4, 9, 8, Hex("#fff36a")); c.Line(9, 8, 7, 12, Hex("#fff36a")); return true;
            case Item.MirrorIce: Shard(c, Hex("#5a98c8"), Hex("#a8d8f8"), Color.white); return true;
            case Item.QueenTear: c.Ball(8, 10, 4.5f, 4.5f, Hex("#3a6ad8"), Hex("#8ab8ff"), Color.white); c.Line(8, 2, 6, 7, Hex("#8ab8ff")); c.Line(8, 2, 10, 7, Hex("#8ab8ff")); c.Set(7, 9, Color.white); return true;
            case Item.LavaCore: c.Ball(8, 8, 6, 6, Hex("#3a1a14"), Hex("#6a2a1a"), Hex("#a84a2a")); c.Ball(8, 8, 3.5f, 3.5f, Hex("#c84a1a"), Hex("#ff8a2a"), Hex("#ffe06a")); return true;
            case Item.HydraFang: c.Line(5, 2, 11, 14, Hex("#f0e8d8")); c.Line(6, 2, 12, 14, Hex("#c8b8a0")); c.Line(4, 3, 10, 14, Hex("#fff8f0")); c.Set(11, 13, Hex("#ff8a2a")); c.Set(12, 14, Hex("#ffe06a")); return true;
            case Item.VoidShard: Shard(c, Hex("#120a24"), Hex("#4a2a8a"), Hex("#c8a0ff")); return true;
            case Item.StarEye: c.Ball(8, 8, 6, 4.5f, Hex("#2e1a5a"), Hex("#6a4ab8"), Hex("#c8b8ff")); c.Ellipse(8, 8, 2.5f, 2.5f, Hex("#ffe06a")); c.Ellipse(8, 8, 1, 2, Color.black); return true;

            case Item.TideBlade: Sword(c, Hex("#3a9ad0"), Hex("#c8f0ff"), Hex("#f2c43a")); return true;
            case Item.FrostLance: Lance(c, Hex("#5a98c8"), Hex("#eef8ff")); return true;
            case Item.MagmaAxe: Axe(c, Hex("#6a2a1a"), Hex("#ff8a2a"), Hex("#ffe06a")); return true;
            case Item.VoidScythe: Scythe(c, Hex("#2e1a5a"), Hex("#a86aff"), Hex("#f0d8ff")); return true;

            case Item.TideMail: Armor(c, Hex("#0f4a5a"), Hex("#1f8a8a"), Hex("#9ae0d8"), Hex("#f2c43a")); return true;
            case Item.FrostRobe: Armor(c, Hex("#5a98c8"), Hex("#a8d8f8"), Hex("#eef8ff"), Color.white); return true;
            case Item.MagmaPlate: Armor(c, Hex("#3a1a14"), Hex("#6a2a1a"), Hex("#a84a2a"), Hex("#ff8a2a")); return true;
            case Item.VoidCloak: Armor(c, Hex("#120a24"), Hex("#2e1a5a"), Hex("#6a4ab8"), Hex("#c8a0ff")); return true;

            case Item.TideCharm: Amulet(c, Hex("#c8a040"), Hex("#5a8ad8"), Hex("#e0f0ff")); return true;
            case Item.FrostRing: c.Ellipse(8, 10, 5, 4, Hex("#a8d8f8")); c.Ellipse(8, 10, 3, 2.2f, new Color(0, 0, 0, 0)); c.Ellipse(8, 10, 3, 2.2f, Hex("#203040")); Shard(c, Hex("#5a98c8"), Hex("#eef8ff"), Color.white, 8, 4, 2); return true;
            case Item.MagmaAmulet: Amulet(c, Hex("#6a2a1a"), Hex("#ff8a2a"), Hex("#ffe06a")); return true;
            case Item.VoidEye: c.Ball(8, 9, 6.5f, 5, Hex("#120a24"), Hex("#4a2a8a"), Hex("#c8a0ff")); c.Ellipse(8, 9, 3, 3, Hex("#ffe06a")); c.Ellipse(8, 9, 1, 2.5f, Color.black); c.Line(2, 3, 5, 5, Hex("#c8a0ff")); c.Line(14, 3, 11, 5, Hex("#c8a0ff")); return true;
        }
        return false;
    }

    static void Scale(Canvas c, Color d, Color m, Color l)
    {
        c.Ball(8, 8, 6.5f, 6, d, m, l);
        for (int y = 4; y <= 12; y += 3) c.Line(3, y, 13, y + 1, d);
        c.Set(5, 5, Color.white);
    }

    static void Shard(Canvas c, Color d, Color m, Color l, int cx = 8, int top = 1, int half = 4)
    {
        for (int y = 0; y <= 13; y++)
        {
            int hw = Mathf.Max(0, half - Mathf.Abs(y - 6) * half / 7);
            if (top + y > 15) break;
            c.Rect(cx - hw, top + y, hw, 1, m); c.Rect(cx, top + y, hw + 1, 1, d);
        }
        c.Set(cx - 1, top + 3, l); c.Set(cx - 1, top + 4, l);
    }

    static void Sword(Canvas c, Color blade, Color edge, Color hilt)
    {
        c.Line(3, 13, 12, 3, blade); c.Line(4, 13, 13, 3, edge); c.Line(12, 3, 14, 1, edge);
        c.Line(2, 10, 6, 14, hilt); c.Line(1, 14, 3, 12, Hex("#5a3a1a")); c.Set(4, 12, Hex("#ff6a6a"));
    }

    static void Lance(Canvas c, Color shaft, Color tip)
    {
        c.Line(2, 15, 11, 6, shaft); c.Line(3, 15, 12, 6, Hex("#2a4a6a"));
        c.Line(11, 6, 15, 0, tip); c.Line(12, 6, 15, 2, tip); c.Line(10, 4, 13, 8, tip); c.Set(13, 3, Color.white);
    }

    static void Axe(Canvas c, Color handle, Color head, Color glow)
    {
        c.Line(3, 15, 11, 3, Hex("#3a1a14")); c.Line(4, 15, 12, 3, handle);
        c.Ball(11, 5, 4.5f, 4, Hex("#3a1a14"), head, glow); c.Ball(14, 6, 2, 3.5f, Hex("#3a1a14"), head, glow);
        c.Set(10, 4, Color.white);
    }

    static void Scythe(Canvas c, Color shaft, Color blade, Color edge)
    {
        c.Line(4, 15, 9, 1, shaft); c.Line(5, 15, 10, 1, Hex("#120a24"));
        c.Line(9, 1, 15, 3, blade); c.Line(15, 3, 14, 7, blade); c.Line(10, 2, 14, 4, edge);
        c.Set(4, 12, edge);
    }

    static void Armor(Canvas c, Color d, Color m, Color l, Color trim)
    {
        c.Rect(3, 3, 10, 11, m); c.Rect(10, 3, 3, 11, d); c.Rect(1, 3, 3, 5, m); c.Rect(12, 3, 3, 5, d);
        c.Rect(6, 3, 4, 2, d); c.Rect(3, 9, 10, 1, trim); c.Set(5, 6, l); c.Set(6, 6, l); c.Rect(7, 11, 2, 2, trim);
    }

    static void Amulet(Canvas c, Color chain, Color gem, Color glow)
    {
        c.Line(3, 1, 8, 8, chain); c.Line(13, 1, 8, 8, chain);
        c.Ball(8, 10, 4, 4.5f, Color.Lerp(gem, Color.black, 0.5f), gem, glow); c.Set(7, 8, Color.white);
    }
}
