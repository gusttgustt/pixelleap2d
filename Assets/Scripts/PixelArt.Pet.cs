using UnityEngine;

/// Pets (IslandGame.Pet.cs): the four you can buy in town, drawn facing right.
/// frame 0 standing, 1-2 walking. Caught wild creatures use their own sprite.
public static partial class PixelArt
{
    public static Sprite Pet(int code, int frame)
    {
        if (code >= 100) return Enemy((EnemyKind)(code - 100), frame == 0 ? 0 : frame - 1);
        return code switch
        {
            1 => Cat(frame),
            2 => Rabbit(frame),
            3 => Parrot(frame),
            _ => Dog(frame),
        };
    }

    /// Four little legs that swing in pairs while walking.
    static void PetLegs(Canvas c, int[] xs, int top, int len, int frame, Color near, Color far, Color paw)
    {
        for (int i = 0; i < xs.Length; i++)
        {
            int lift = (frame == 1 && i % 2 == 0) || (frame == 2 && i % 2 == 1) ? 1 : 0;
            c.Rect(xs[i], top, 1, len - lift, i % 2 == 0 ? near : far);
            c.Set(xs[i], top + len - 1 - lift, paw);
        }
    }

    /// Puppy: tan with a darker ear and a wagging tail.
    public static Sprite Dog(int frame) => Get("pet_dog_" + frame, () =>
    {
        var c = new Canvas(18, 15);
        c.Ellipse(9, 13.5f, 6, 1.3f, Shadow);
        Color d = Hex("#9a6430"), m = Hex("#d89a52"), l = Hex("#f4c888"), ear = Hex("#6a4022");
        PetLegs(c, new[] { 5, 6, 11, 12 }, 9, 5, frame, m, d, ear);
        int wag = frame == 2 ? 1 : 0;
        c.Line(4, 8, 2, 5 + wag, d); c.Set(2, 4 + wag, m);                        // tail
        c.Ball(8.5f, 8, 5f, 3f, d, m, l);                                          // body
        c.Ellipse(9, 9.5f, 2.6f, 1.2f, l);                                         // belly
        int bob = frame == 0 ? 0 : 1;
        c.Ball(13.5f, 5 + bob, 3.2f, 2.8f, d, m, l);                               // head
        c.Ellipse(16, 6.5f + bob, 1.6f, 1.1f, l);                                  // muzzle
        c.Set(17, 6 + bob, Hex("#1a1010"));                                        // nose
        c.Set(14, 4 + bob, Hex("#1a1010"));                                        // eye
        c.Rect(11, 3 + bob, 2, 4, ear);                                            // floppy ear
        c.Rect(11, 8 + bob, 4, 1, Hex("#d0302a"));                                 // red collar
        c.Set(13, 9 + bob, Hex("#ffd84d"));                                        // tag
        return c.ToSprite(9, 13);
    });

    /// Kitten: grey tabby, tail held up.
    public static Sprite Cat(int frame) => Get("pet_cat_" + frame, () =>
    {
        var c = new Canvas(18, 16);
        c.Ellipse(9, 14.5f, 5.5f, 1.2f, Shadow);
        Color d = Hex("#5c5e68"), m = Hex("#9a9ca8"), l = Hex("#d4d6de"), stripe = Hex("#4a4c56");
        PetLegs(c, new[] { 5, 6, 11, 12 }, 11, 4, frame, m, d, l);
        int sway = frame == 1 ? 1 : 0;
        c.Line(4, 10, 2 + sway, 6, d); c.Line(2 + sway, 6, 3 + sway, 3, m);         // tail up
        c.Ball(8.5f, 9.5f, 4.6f, 2.6f, d, m, l);                                    // body
        c.Set(7, 8, stripe); c.Set(9, 8, stripe); c.Set(8, 7, stripe); c.Set(10, 7, stripe);
        int bob = frame == 0 ? 0 : 1;
        c.Ball(13.5f, 6.5f + bob, 3f, 2.6f, d, m, l);                               // head
        c.Set(11, 3 + bob, d); c.Set(12, 3 + bob, m); c.Set(11, 4 + bob, Hex("#e8a0a8")); // ears
        c.Set(15, 3 + bob, d); c.Set(16, 3 + bob, m); c.Set(15, 4 + bob, Hex("#e8a0a8"));
        c.Set(14, 6 + bob, Hex("#7ad04a")); c.Set(16, 6 + bob, Hex("#7ad04a"));    // green eyes
        c.Set(15, 7 + bob, Hex("#e8a0a8"));                                         // nose
        c.Rect(11, 9 + bob, 4, 1, Hex("#3a7ad8"));                                  // blue collar
        c.Set(13, 10 + bob, Hex("#ffd84d"));                                        // bell
        return c.ToSprite(9, 14);
    });

    /// Bunny: white with long ears, hops rather than walks.
    public static Sprite Rabbit(int frame) => Get("pet_rabbit_" + frame, () =>
    {
        var c = new Canvas(16, 17);
        c.Ellipse(8, 15.5f, 4.8f, 1.2f, Shadow);
        Color d = Hex("#c0b8b0"), m = Hex("#f0ece6"), l = Color.white, pink = Hex("#f0a8b8");
        int hop = frame == 1 ? 2 : frame == 2 ? 1 : 0;
        c.Ellipse(4, 14 - hop, 2.2f, 1f, d);                                        // back foot
        c.Ball(3.5f, 10 - hop, 1.6f, 1.6f, d, l, l);                                // cotton tail
        c.Ball(7.5f, 11 - hop, 4.2f, 3.2f, d, m, l);                                // body
        c.Rect(10, 13 - hop, 2, 2, d);                                              // front paws
        c.Ball(11.5f, 7.5f - hop, 2.8f, 2.6f, d, m, l);                             // head
        c.Rect(9, 1 - hop, 2, 5, m); c.Rect(9, 2 - hop, 1, 3, pink);                // ears
        c.Rect(12, 1 - hop, 2, 5, d); c.Rect(12, 2 - hop, 1, 3, pink);
        c.Set(13, 7 - hop, Hex("#1a1010"));                                         // eye
        c.Set(14, 8 - hop, pink);                                                   // nose
        return c.ToSprite(8, 15);
    });

    /// Parrot: green with a red head and a curved beak; it hops along the ground.
    public static Sprite Parrot(int frame) => Get("pet_parrot_" + frame, () =>
    {
        var c = new Canvas(14, 18);
        c.Ellipse(7, 16.5f, 3.6f, 1f, Shadow);
        Color d = Hex("#1e7a32"), m = Hex("#3cb84a"), l = Hex("#8ae878");
        Color red = Hex("#e0382a"), redL = Hex("#ff7a5a"), yellow = Hex("#ffd84d"), blue = Hex("#3a7ad8");
        int hop = frame == 1 ? 2 : frame == 2 ? 1 : 0;
        c.Line(6, 13 - hop, 6, 16 - hop, Hex("#8a7a6a")); c.Line(8, 13 - hop, 8, 16 - hop, Hex("#8a7a6a")); // legs
        c.Line(4, 11 - hop, 2, 16 - hop, blue); c.Line(5, 12 - hop, 3, 17 - hop, d); // tail feathers
        c.Ball(6.5f, 10 - hop, 3.2f, 4f, d, m, l);                                  // body
        c.Line(4, 8 - hop, 5, 13 - hop, d); c.Set(5, 12 - hop, blue);               // wing
        c.Ball(8.5f, 5 - hop, 2.8f, 2.6f, Hex("#9a1a12"), red, redL);               // head
        c.Set(9, 4 - hop, Color.white); c.Set(10, 4 - hop, Hex("#1a1010"));         // eye
        c.Rect(11, 5 - hop, 2, 1, yellow); c.Set(12, 6 - hop, Hex("#c89a20"));      // beak
        c.Set(11, 6 - hop, Hex("#3a3030"));
        return c.ToSprite(7, 16);
    });

    /// Icon for the catching net (Item.PetNet).
    static bool DrawPetIcon(Canvas c, Item it)
    {
        if (it != Item.PetNet) return false;
        Color stick = Hex("#8a5a2e"), rope = Hex("#e8d8a8"), ropeD = Hex("#b8a070");
        c.Line(2, 15, 7, 9, stick); c.Line(3, 15, 8, 9, Hex("#6a4022"));          // handle
        for (int y = 2; y <= 10; y++)                                              // mesh, inside the hoop
        {
            float hw = Mathf.Sqrt(Mathf.Max(0f, 4.2f * 4.2f - (y - 6) * (y - 6)));
            for (int x = Mathf.CeilToInt(10.5f - hw); x <= Mathf.FloorToInt(10.5f + hw); x++)
                if ((x + y) % 2 == 0) c.Set(x, y, (x + y) % 4 == 0 ? rope : ropeD);
        }
        c.Ring(10.5f, 6, 4.6f, 0.8f, Hex("#7a5a30"));                              // hoop
        return true;
    }
}
