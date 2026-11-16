using UnityEditor;

/// Hand-made sprites in Assets/Resources/Sprites import pixel-exact: no smoothing,
/// no compression, no mipmaps - so nobody has to set it by hand for every PNG.
public class SpriteImportSettings : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        string p = assetPath.Replace('\\', '/');
        if (p.StartsWith("Assets/Resources/Emoji/")) // chat stickers: smooth art shown small, so smooth + mipmapped
        {
            var ei = (TextureImporter)assetImporter;
            ei.filterMode = UnityEngine.FilterMode.Trilinear;
            ei.textureCompression = TextureImporterCompression.Uncompressed;
            ei.mipmapEnabled = true;
            ei.alphaIsTransparency = true;
            ei.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            return;
        }
        if (!p.StartsWith("Assets/Resources/Sprites/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.filterMode = UnityEngine.FilterMode.Point;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaIsTransparency = true;
    }
}
