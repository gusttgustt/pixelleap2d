using UnityEngine;

/// How the player's character looks, chosen on the title screen. Packed into one int
/// ("code") so it's easy to save and to send to other players. Code 0 = the default look.
public struct CharacterLook
{
    public int skin, style, hair, shirt, pants, gender; // gender 0 = male, 1 = female

    public static readonly string[] Skins = { "#f1c27d", "#ffdbac", "#e0ac69", "#c68642", "#8d5524" };
    public static readonly string[] Styles = { "สั้น", "ยาว", "ตั้งชี้", "มวย" };
    public static readonly string[] Hairs = { "#4a2c1a", "#1a1010", "#d8b04a", "#a83a1e", "#e8e8e8", "#5a7ad8" };
    public static readonly string[] Shirts = { "#d9534f", "#3b7dd8", "#4f9a3f", "#e0b030", "#7a3f8a", "#e8e8e8", "#d9822b", "#2aa198" };
    public static readonly string[] Pants = { "#3b4a7a", "#6b4226", "#2b2b2b", "#6b6b6b", "#3a5a2a", "#a08c5a" };
    public static readonly string[] Genders = { "ชาย", "หญิง" };

    // gender is the last digit, so every look saved before it existed reads back as male
    public int Code => skin + Skins.Length * (style + Styles.Length * (hair + Hairs.Length * (shirt + Shirts.Length * (pants + Pants.Length * gender))));

    public static CharacterLook FromCode(int code)
    {
        code = Mathf.Max(0, code);
        var l = new CharacterLook();
        l.skin = code % Skins.Length; code /= Skins.Length;
        l.style = code % Styles.Length; code /= Styles.Length;
        l.hair = code % Hairs.Length; code /= Hairs.Length;
        l.shirt = code % Shirts.Length; code /= Shirts.Length;
        l.pants = code % Pants.Length; code /= Pants.Length;
        l.gender = code % Genders.Length;
        return l;
    }

    public static CharacterLook Random() => new CharacterLook
    {
        gender = UnityEngine.Random.Range(0, Genders.Length),
        skin = UnityEngine.Random.Range(0, Skins.Length),
        style = UnityEngine.Random.Range(0, Styles.Length),
        hair = UnityEngine.Random.Range(0, Hairs.Length),
        shirt = UnityEngine.Random.Range(0, Shirts.Length),
        pants = UnityEngine.Random.Range(0, Pants.Length),
    };

    static Color C(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    static Color Dark(Color c, float k = 0.2f) => Color.Lerp(c, Color.black, k);

    public Color Skin => C(Skins[skin]);
    public Color SkinDark => Dark(Skin, 0.15f);
    public Color Hair => C(Hairs[hair]);
    public Color Shirt => C(Shirts[shirt]);
    public Color ShirtDark => Dark(Shirt);
    public Color Pant => C(Pants[pants]);
    public Color PantDark => Dark(Pant);
}
