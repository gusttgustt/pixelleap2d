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
        WritePage();

        var s = report.summary;
        Debug.Log($"[WebGLBuilder] {s.result} (version {version}): {s.totalSize / (1024f * 1024f):0.0} MB, {s.totalErrors} errors, {s.totalTime}");
        if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }

    /// Our own page (Assets/WebGLTemplates/Island/index.html) in place of Unity's default one:
    /// the game fills the browser window at any size. Fills in the template's {{{ ... }}} values.
    static void WritePage()
    {
        string name = PlayerSettings.productName;
        string page = File.ReadAllText("Assets/WebGLTemplates/Island/index.html")
            .Replace("{{{ JSON.stringify(COMPANY_NAME) }}}", Quote(PlayerSettings.companyName))
            .Replace("{{{ JSON.stringify(PRODUCT_NAME) }}}", Quote(name))
            .Replace("{{{ JSON.stringify(PRODUCT_VERSION) }}}", Quote(PlayerSettings.bundleVersion))
            .Replace("{{{ PRODUCT_NAME }}}", name)
            .Replace("{{{ LOADER_FILENAME }}}", "docs.loader.js")
            .Replace("{{{ DATA_FILENAME }}}", "docs.data.unityweb")
            .Replace("{{{ FRAMEWORK_FILENAME }}}", "docs.framework.js.unityweb")
            .Replace("{{{ CODE_FILENAME }}}", "docs.wasm.unityweb");
        File.WriteAllText(Path.Combine(OutputDir, "index.html"), page);
        string unused = Path.Combine(OutputDir, "TemplateData"); // the default page's images
        if (Directory.Exists(unused)) Directory.Delete(unused, true);
    }

    static string Quote(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
