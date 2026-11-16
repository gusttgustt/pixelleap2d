using UnityEditor;

/// Hand-made sprites in Assets/Resources/Sprites import pixel-exact: no smoothing,
/// no compression, no mipmaps - so nobody has to set it by hand for every PNG.
public class SpriteImportSettings : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith("Assets/Resources/Sprites/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.filterMode = UnityEngine.FilterMode.Point;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaIsTransparency = true;
    }
}
