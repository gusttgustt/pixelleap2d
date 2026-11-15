using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

/// Web build only: every so often, reads version.txt next to the game page (written by
/// WebGLBuilder on each build). When it differs from the version this game was built with,
/// shows a banner: clicking it saves the game and reloads the page to get the new build.
public class UpdateChecker : MonoBehaviour
{
    const float CheckEvery = 90f;          // seconds between checks
    static string builtVersion;
    bool updateReady;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var go = new GameObject("UpdateChecker");
        DontDestroyOnLoad(go);
        go.AddComponent<UpdateChecker>();
#endif
    }

    void Start()
    {
        var asset = Resources.Load<TextAsset>("BuildVersion");
        builtVersion = asset != null ? asset.text.Trim() : "";
        if (builtVersion.Length > 0) StartCoroutine(CheckLoop());
    }

    IEnumerator CheckLoop()
    {
        var wait = new WaitForSecondsRealtime(CheckEvery);
        while (!updateReady)
        {
            yield return wait;
            using (var req = UnityWebRequest.Get(VersionUrl()))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) continue;
                string v = req.downloadHandler.text.Trim();
                if (IsVersion(v) && v != builtVersion) updateReady = true;
            }
        }
    }

    /// The folder the page lives in + version.txt, with a cache-buster so we never get a stale copy.
    static string VersionUrl()
    {
        string page = Application.absoluteURL ?? "";
        int q = page.IndexOfAny(new[] { '?', '#' });
        if (q >= 0) page = page.Substring(0, q);
        int slash = page.LastIndexOf('/');
        string dir = slash >= 0 ? page.Substring(0, slash + 1) : page + "/";
        return dir + "version.txt?t=" + System.DateTime.UtcNow.Ticks;
    }

    static bool IsVersion(string v)
    {
        if (v.Length < 8 || v.Length > 20) return false;
        foreach (char c in v) if (c < '0' || c > '9') return false;
        return true;
    }

    void OnGUI()
    {
        if (!updateReady) return;
        GUI.depth = -100; // on top of the game's HUD
        GameFont.Apply();
        float s = IslandGame.HudScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s;

        var r = new Rect(W / 2f - 190f, 34f, 380f, 34f);
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 4f);
        GUI.color = new Color(1f, 1f, 1f, pulse);
        var st = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
        st.normal.textColor = new Color(1f, 0.9f, 0.4f);
        st.hover.textColor = Color.white;
        if (GUI.Button(r, "มีอัปเดตใหม่! คลิกเพื่อโหลดเกมใหม่", st)) Reload();
        GUI.color = Color.white;
    }

    static void Reload()
    {
        if (IslandGame.I != null) IslandGame.I.SaveGame(); // keep progress across the reload
#if UNITY_WEBGL && !UNITY_EDITOR
        IS_ReloadPage();
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void IS_ReloadPage();
#endif
}

/// The Thai-capable font (Kanit, SIL OFL - see Resources/Fonts/Kanit-OFL.txt) for all on-screen text.
public static class GameFont
{
    static Font font;
    static bool tried;

    public static void Apply()
    {
        if (!tried) { tried = true; font = Resources.Load<Font>("Fonts/Kanit-Regular"); }
        if (font == null || GUI.skin.font == font) return;
        GUI.skin.font = font;
        // Thai vowels and tone marks sit above the line: let them draw outside a control's box
        // instead of being cut off (styles made later from these copy the setting).
        foreach (var st in new[] { GUI.skin.label, GUI.skin.button, GUI.skin.textField, GUI.skin.box })
            st.clipping = TextClipping.Overflow;
    }
}
