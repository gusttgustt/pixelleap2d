using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// Menu "Island/Export Art Sheet": every generated sprite on one enlarged PNG
/// (Art/ArtSheet.png), for checking the art at a glance.
public static class ArtSheet
{
    [MenuItem("Island/Export Art Sheet")]
    public static void Export()
    {
        // draw every sprite the game makes: call each public sprite maker with sample arguments
        foreach (var m in typeof(PixelArt).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(Sprite)))
        {
            var ps = m.GetParameters();
            foreach (int v in new[] { 0, 1 })
            {
                var args = ps.Select(p => Sample(p, v)).ToArray();
                try { m.Invoke(null, args); } catch { }
            }
        }
        var all = PixelArt.AllPlaceholders().OrderBy(k => k.Key).Select(k => k.Value).ToList();
        const int scale = 2, cell = 200, cols = 14;
        int rows = (all.Count + cols - 1) / cols;
        var sheet = new Texture2D(cols * cell, rows * cell, TextureFormat.RGBA32, false);
        var bg = new Color(0.32f, 0.36f, 0.4f);
        sheet.SetPixels(Enumerable.Repeat(bg, sheet.width * sheet.height).ToArray());
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i].texture;
            int ox = (i % cols) * cell + 2, oy = sheet.height - (i / cols + 1) * cell + 2;
            int sw = Mathf.Min(t.width * scale, cell - 4), sh = Mathf.Min(t.height * scale, cell - 4);
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    var c = t.GetPixel(x / scale, y / scale);
                    if (c.a > 0f) sheet.SetPixel(ox + x, oy + y, Color.Lerp(bg, c, c.a));
                }
        }
        sheet.Apply();
        Directory.CreateDirectory("Art");
        File.WriteAllBytes("Art/ArtSheet.png", sheet.EncodeToPNG());
        File.WriteAllText("Art/ArtSheet.txt", string.Join("\n", PixelArt.AllPlaceholders().Keys.OrderBy(k => k)));
        Debug.Log($"Art sheet: {all.Count} sprites");
    }

    static object Sample(ParameterInfo p, int v)
    {
        var t = p.ParameterType;
        if (t == typeof(int)) return v;
        if (t == typeof(bool)) return v == 0;
        if (t.IsEnum) { var vals = System.Enum.GetValues(t); return vals.GetValue(v % vals.Length); }
        if (p.HasDefaultValue) return p.DefaultValue;
        return null;
    }
}
