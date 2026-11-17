using System.IO;
using UnityEditor;
using UnityEngine;

/// Editor menu "Island": export placeholder art for Aseprite, and manage the save file.
public static class IslandTools
{
    const string ExportDir = "Assets/Art/Placeholders";

    [MenuItem("Island/Export Placeholder Art")]
    static void ExportPlaceholderArt()
    {
        Directory.CreateDirectory(ExportDir);
        int count = 0;
        foreach (var kv in PixelArt.AllPlaceholders())
        {
            File.WriteAllBytes(Path.Combine(ExportDir, kv.Key + ".png"), kv.Value.texture.EncodeToPNG());
            count++;
        }
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Island",
            $"Exported {count} sprites to {ExportDir}.\n\n" +
            "Open them in Aseprite, repaint (keep the same canvas size), export as PNG and put the file in " +
            "Assets/Resources/Sprites/ with the same name. The game uses it automatically.", "OK");
        EditorUtility.RevealInFinder(ExportDir);
    }

    /// The four continent pictures from the sea chart, side by side at 4x, into IslandShots/continents.png.
    /// Command line: Unity.exe -batchmode -quit -projectPath . -executeMethod IslandTools.ExportContinentArt
    [MenuItem("Island/Export Continent Art")]
    public static void ExportContinentArt()
    {
        const int z = 4, gap = 2;
        var first = IslandGame.ContinentArt(0);
        int w = first.width, h = first.height, n = IslandGame.Continents;
        var sheet = new Texture2D((w * n + gap * (n - 1)) * z, h * z, TextureFormat.RGBA32, false);
        var px = new Color[sheet.width * sheet.height];
        for (int i = 0; i < px.Length; i++) px[i] = Color.black;
        for (int c = 0; c < n; c++)
        {
            var t = IslandGame.ContinentArt(c);
            for (int y = 0; y < h * z; y++)
                for (int x = 0; x < w * z; x++)
                    px[y * sheet.width + c * (w + gap) * z + x] = t.GetPixel(x / z, y / z);
        }
        sheet.SetPixels(px);
        sheet.Apply();
        Directory.CreateDirectory("IslandShots");
        File.WriteAllBytes("IslandShots/continents.png", sheet.EncodeToPNG());
        Debug.Log("Exported IslandShots/continents.png");
    }

    [MenuItem("Island/Open Save Folder")]
    static void OpenSaveFolder() => EditorUtility.RevealInFinder(SaveSystem.HasSave ? SaveSystem.SavePath : Application.persistentDataPath);

    [MenuItem("Island/Delete Save")]
    static void DeleteSave()
    {
        SaveSystem.Delete();
        Debug.Log("Island save deleted. Next Play starts a new island.");
    }
}
