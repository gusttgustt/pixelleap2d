using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// Builds the game for the web into docs/ so GitHub Pages can serve it straight from the repo.
/// Menu: Island > Build WebGL. Command line:
///   Unity.exe -batchmode -quit -projectPath . -executeMethod WebGLBuilder.Build
public static class WebGLBuilder
{
    const string OutputDir = "docs";
    const string VersionAsset = "Assets/Resources/BuildVersion.txt";
    static readonly string[] KeepFiles = { "server.txt" };

    [MenuItem("Island/Build WebGL (docs folder)")]
    public static void Build()
    {
        // Gzip with "decompression fallback": GitHub Pages can't send the headers compressed Unity
        // builds normally need, so the loader unpacks the files itself. Far smaller download.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.productName = "Island Survival";
        PlayerSettings.runInBackground = true;

        // Version stamp: baked into the game (Resources/BuildVersion.txt) and written next to it
        // (docs/version.txt). A running game that sees a different version.txt offers a reload.
        string version = System.DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        Directory.CreateDirectory("Assets/Resources");
        File.WriteAllText(VersionAsset, version);
        AssetDatabase.ImportAsset(VersionAsset, ImportAssetOptions.ForceUpdate);

        var scenes = new[] { "Assets/Scenes/SampleScene.unity" };
        // Files in docs/ that are not part of the build survive it (server.txt = the online server's
        // address, written by Server/online.js; without it the game on GitHub Pages can't connect).
        var kept = new System.Collections.Generic.Dictionary<string, byte[]>();
        foreach (var f in KeepFiles)
        {
            string p = Path.Combine(OutputDir, f);
            if (File.Exists(p)) kept[f] = File.ReadAllBytes(p);
        }
        if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = OutputDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        });

        // GitHub Pages runs Jekyll by default, which hides folders starting with "_". Turn it off.
        File.WriteAllText(Path.Combine(OutputDir, ".nojekyll"), "");
        File.WriteAllText(Path.Combine(OutputDir, "version.txt"), version);
        WritePage(version);
        foreach (var k in kept) File.WriteAllBytes(Path.Combine(OutputDir, k.Key), k.Value);

        var s = report.summary;
        Debug.Log($"[WebGLBuilder] {s.result} (version {version}): {s.totalSize / (1024f * 1024f):0.0} MB, {s.totalErrors} errors, {s.totalTime}");
        if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }

    /// Our own page (Assets/WebGLTemplates/Island/index.html) in place of Unity's default one:
    /// the game fills the browser window at any size. Fills in the template's {{{ ... }}} values.
    /// The build files keep the same names every build and GitHub Pages lets browsers cache them
    /// for 10 minutes, so a reload right after a deploy could mix an old file with a new one (the
    /// game then shows the wrong words in the wrong places). "?v=<version>" makes every build's
    /// files new addresses: a page always loads one matching set.
    static void WritePage(string version)
    {
        string v = "?v=" + version;
        string name = PlayerSettings.productName;
        string page = File.ReadAllText("Assets/WebGLTemplates/Island/index.html")
            .Replace("{{{ JSON.stringify(COMPANY_NAME) }}}", Quote(PlayerSettings.companyName))
            .Replace("{{{ JSON.stringify(PRODUCT_NAME) }}}", Quote(name))
            .Replace("{{{ JSON.stringify(PRODUCT_VERSION) }}}", Quote(PlayerSettings.bundleVersion))
            .Replace("{{{ PRODUCT_NAME }}}", name)
            .Replace("{{{ LOADER_FILENAME }}}", "docs.loader.js" + v)
            .Replace("{{{ DATA_FILENAME }}}", "docs.data.unityweb" + v)
            .Replace("{{{ FRAMEWORK_FILENAME }}}", "docs.framework.js.unityweb" + v)
            .Replace("{{{ CODE_FILENAME }}}", "docs.wasm.unityweb" + v);
        File.WriteAllText(Path.Combine(OutputDir, "index.html"), page);
        string unused = Path.Combine(OutputDir, "TemplateData"); // the default page's images
        if (Directory.Exists(unused)) Directory.Delete(unused, true);
    }

    static string Quote(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
