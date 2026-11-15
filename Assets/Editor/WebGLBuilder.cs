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

    [MenuItem("Island/Build WebGL (docs folder)")]
    public static void Build()
    {
        // GitHub Pages can't send the headers compressed Unity builds need, so ship uncompressed.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.productName = "Island Survival";
        PlayerSettings.runInBackground = true;

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

        var s = report.summary;
        Debug.Log($"[WebGLBuilder] {s.result}: {s.totalSize / (1024f * 1024f):0.0} MB, {s.totalErrors} errors, {s.totalTime}");
        if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }
}
