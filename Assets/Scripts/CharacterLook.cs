using UnityEngine;

/// How the player's character looks, chosen on the title screen. Packed into one int
/// ("code") so it's easy to save and to send to other players. Code 0 = the default look.
public struct CharacterLook
{
    public int skin, style, hair, shirt, pants, gender; // gender 0 = male, 1 = female
    /// 0 = build your own from the colours below; 1+ = a ready-made character (Presets[preset]).
    public int preset;

    /// Ready-made characters: shown name + sprite name in Resources/Sprites
    /// (hero_<id>_front_<frame>.png / hero_<id>_back_<frame>.png, frame 0 idle, 1-4 walk).
    public static readonly string[] Presets = { "ปรับแต่งเอง", "สาวหมวกเบเร่ต์ชมพู", "สาวหมวกเบเร่ต์แดง", "สาวผีเสื้อม่วง" };
    public static readonly string[] PresetIds = { null, "pinkberet", "redberet", "gothbutterfly" };
    /// Art pixels per world pixel: 2 = drawn at double detail (64 px art shown at the
    /// same size as 32 px art), so it is ready for a higher-detail world later.
    public static readonly int[] PresetDetail = { 1, 2, 2, 2 };
    /// For now players pick one of the ready-made characters (no build-your-own look).
    /// Set to true to bring back building a look from colours too.
    public const bool CanChoose = false;
    public const int DefaultPreset = 1;
    public static CharacterLook Default => new CharacterLook { preset = DefaultPreset, gender = 1, style = 1 };
    /// Codes from here up are ready-made characters, far above any build-your-own code.
    const int PresetBase = 1000000;

    public static readonly string[] Skins = { "#f1c27d", "#ffdbac", "#e0ac69", "#c68642", "#8d5524" };
    public static readonly string[] Styles = { "สั้น", "ยาว", "ตั้งชี้", "มวย" };
    public static readonly string[] Hairs = { "#4a2c1a", "#1a1010", "#d8b04a", "#a83a1e", "#e8e8e8", "#5a7ad8" };
    public static readonly string[] Shirts = { "#d9534f", "#3b7dd8", "#4f9a3f", "#e0b030", "#7a3f8a", "#e8e8e8", "#d9822b", "#2aa198" };
    public static readonly string[] Pants = { "#3b4a7a", "#6b4226", "#2b2b2b", "#6b6b6b", "#3a5a2a", "#a08c5a" };
    public static readonly string[] Genders = { "ชาย", "หญิง" };

    // gender is the last digit, so every look saved before it existed reads back as male
    public int Code => preset > 0 ? PresetBase + preset : CustomCode;
    int CustomCode => skin + Skins.Length * (style + Styles.Length * (hair + Hairs.Length * (shirt + Shirts.Length * (pants + Pants.Length * gender))));

    public static CharacterLook FromCode(int code)
    {
        code = Mathf.Max(0, code);
        var l = new CharacterLook();
        if (code >= PresetBase)
        {
            int p = code - PresetBase;
            l.preset = p < Presets.Length ? p : 0;
            l.gender = 1; l.style = 1;
            return l;
        }
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

    /// Sprite id of a ready-made character, or null for build-your-own.
    public string PresetId => preset > 0 && preset < PresetIds.Length ? PresetIds[preset] : null;
    public int PresetScale => preset > 0 && preset < PresetDetail.Length ? PresetDetail[preset] : 1;

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
