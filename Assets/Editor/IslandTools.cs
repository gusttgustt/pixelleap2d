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

    [MenuItem("Island/Open Save Folder")]
    static void OpenSaveFolder() => EditorUtility.RevealInFinder(SaveSystem.HasSave ? SaveSystem.SavePath : Application.persistentDataPath);

    [MenuItem("Island/Delete Save")]
    static void DeleteSave()
    {
        SaveSystem.Delete();
        Debug.Log("Island save deleted. Next Play starts a new island.");
    }
}
