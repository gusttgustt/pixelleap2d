using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// Sharp text for a GUI that is scaled with GUI.matrix. The HUD is laid out for a small virtual
/// screen (820 x 540) and scaled up to the real one; IMGUI draws glyphs at the style's font size
/// and then stretches them, so on a big or high-DPI screen the text came out blurry. These stand-ins
/// for GUI.Label / GUI.Button draw the text at the real pixel size instead: the same place and
/// size on screen, but the font rendered at fontSize x scale. Backgrounds, clicks and layout are
/// left to IMGUI as before.
public static class CrispGUI
{
    /// The smallest text size drawn (in HUD units): tiny 8-9 px labels were hard to read on phones.
    public const int MinFontSize = 10;

    static readonly Regex SizeTag = new Regex(@"<size=(\d+)>");
    static readonly Dictionary<(string, int), string> sizedText = new Dictionary<(string, int), string>();
    static readonly GUIContent tmp = new GUIContent();

    public static void Label(Rect r, string text) => Label(r, text, GUI.skin.label);
    public static void Label(Rect r, string text, GUIStyle st) { tmp.text = text; tmp.image = null; tmp.tooltip = null; Label(r, tmp, st); }
    public static void Label(Rect r, GUIContent c) => Label(r, c, GUI.skin.label);
    public static void Label(Rect r, Texture t) => GUI.Label(r, t);
    public static void Label(Rect r, Texture t, GUIStyle st) => GUI.Label(r, t, st);

    public static void Label(Rect r, GUIContent c, GUIStyle st)
    {
        c = Translated(c);
        int size = st.fontSize, fit = st.wordWrap ? 0 : FitSize(r.width, c, st);
        if (fit > 0) st.fontSize = fit; // just for this line (the style is shared): put back below
        if (!Draw(r, c, st, false)) GUI.Label(r, c, st);
        st.fontSize = size;
    }

    // ---- Thai / English (Lang.cs): every word is translated here, just before it's drawn
    static readonly GUIContent translated = new GUIContent();
    static GUIContent Translated(GUIContent c)
    {
        if (!Lang.English || string.IsNullOrEmpty(c.text)) return c;
        string t = Lang.Tr(c.text);
        if (ReferenceEquals(t, c.text)) return c;
        translated.text = t; translated.image = c.image; translated.tooltip = c.tooltip;
        return translated;
    }

    /// English runs longer than Thai: a line wider than its box is drawn smaller to fit (down to
    /// 75% of its size; past that it overflows as before). Thai text is left alone. Returns the size
    /// to draw at, or 0 for "as it is". The caller sets it on the style and puts it back after, so no
    /// style is copied; the measured widths are kept, since the same lines come every frame.
    static readonly Dictionary<(string, int, FontStyle, bool), float> widths = new Dictionary<(string, int, FontStyle, bool), float>();
    static int FitSize(float width, GUIContent c, GUIStyle st)
    {
        if (!Lang.English || string.IsNullOrEmpty(c.text) || width < 8f || c.image != null) return 0;
        int size = st.fontSize > 0 ? st.fontSize : 13;
        var key = (c.text, size, st.fontStyle, st.richText);
        if (!widths.TryGetValue(key, out float w))
        {
            if (widths.Count > 3000) widths.Clear(); // counters and timers make new lines all the time
            w = st.CalcSize(c).x;
            widths[key] = w;
        }
        if (w <= width + 1f) return 0;
        int want = Mathf.Max(Mathf.CeilToInt(size * 0.75f), Mathf.FloorToInt(size * (width + 1f) / w));
        return want < size ? want : 0;
    }

    public static bool Button(Rect r, string text) => Button(r, text, GUI.skin.button);
    public static bool Button(Rect r, string text, GUIStyle st) => Button(r, new GUIContent(text), st);
    public static bool Button(Rect r, GUIContent c) => Button(r, c, GUI.skin.button);
    public static bool Button(Rect r, Texture t) => GUI.Button(r, t);
    public static bool Button(Rect r, Texture t, GUIStyle st) => GUI.Button(r, t, st);

    public static bool Button(Rect r, GUIContent c, GUIStyle st)
    {
        c = Translated(c);
        int size = st.fontSize, fit = FitSize(r.width - st.padding.horizontal, c, st);
        if (fit > 0) st.fontSize = fit; // just for this button: put back below
        try
        {
            if (string.IsNullOrEmpty(c.text) || !Scaled(out _)) return GUI.Button(r, c, st);
            var plain = c.image != null ? new GUIContent(c.image, c.tooltip) : GUIContent.none;
            bool clicked = GUI.Button(r, plain, st); // the box and the click, as before
            if (c.image == null) Draw(r, c, st, true); // then the words on top, sharp
            return clicked;
        }
        finally { st.fontSize = size; }
    }

    static bool Scaled(out float s)
    {
        s = GUI.matrix.m00;
        return Mathf.Abs(s - 1f) > 0.01f && Mathf.Abs(GUI.matrix.m11 - s) < 0.001f && GUI.matrix.m01 == 0f && GUI.matrix.m10 == 0f;
    }

    // Inside a group or scroll view the scale can't be taken out of the matrix (the clipping goes
    // wrong), so the words are kept until the group closes and drawn then, at the top level,
    // clipped to where the group was. Positions are tracked here in top-level (virtual) units:
    // the open groups' origins, and their visible rects.
    struct Deferred { public Rect at; public GUIContent content; public GUIStyle style; public bool textOnly; }
    static readonly List<Deferred> deferred = new List<Deferred>();
    static readonly List<Rect> clips = new List<Rect>();       // visible rect of each open group
    static readonly List<Vector2> origins = new List<Vector2>(); // where its (0,0) is

    /// Start of a GUI pass: forget anything left over (a group left open by an early return).
    public static void Reset() { clips.Clear(); origins.Clear(); deferred.Clear(); }

    static Vector2 Origin => origins.Count > 0 ? origins[origins.Count - 1] : Vector2.zero;

    public static void BeginGroup(Rect r) { Push(r, r.position); GUI.BeginGroup(r); }
    public static void EndGroup() { GUI.EndGroup(); Pop(); }
    public static Vector2 BeginScrollView(Rect view, Vector2 pos, Rect content, bool alwaysH, bool alwaysV)
    {
        var p = GUI.BeginScrollView(view, pos, content, alwaysH, alwaysV);
        Push(view, view.position + content.position - p);
        return p;
    }
    public static void EndScrollView() { GUI.EndScrollView(); Pop(); }

    static void Push(Rect visible, Vector2 originOffset)
    {
        Vector2 o = Origin;
        var r = new Rect(visible.position + o, visible.size);
        if (clips.Count > 0)
        {
            var p = clips[clips.Count - 1];
            r = Rect.MinMaxRect(Mathf.Max(r.xMin, p.xMin), Mathf.Max(r.yMin, p.yMin), Mathf.Min(r.xMax, p.xMax), Mathf.Min(r.yMax, p.yMax));
        }
        clips.Add(r);
        origins.Add(o + originOffset);
    }

    static void Pop()
    {
        if (clips.Count == 0) return;
        var clip = clips[clips.Count - 1];
        clips.RemoveAt(clips.Count - 1);
        origins.RemoveAt(origins.Count - 1);
        if (clips.Count > 0 || deferred.Count == 0) return;
        if (Event.current.type == EventType.Repaint && Scaled(out float s) && clip.width > 0f && clip.height > 0f)
        {
            var m = GUI.matrix;
            GUI.matrix = m * Matrix4x4.Scale(new Vector3(1f / s, 1f / s, 1f));
            var local = Times(clip, s);
            GUI.BeginGroup(local);
            foreach (var d in deferred)
            {
                var r = Times(d.at, s);
                r.position -= local.position;
                Paint(r, d.content, d.style, d.textOnly, s);
            }
            GUI.EndGroup();
            GUI.matrix = m;
        }
        deferred.Clear();
    }

    static Rect Times(Rect r, float s) => new Rect(r.x * s, r.y * s, r.width * s, r.height * s);

    /// Draws the text of a label or button at screen resolution. False if it can't (no scaling).
    static bool Draw(Rect r, GUIContent c, GUIStyle st, bool textOnly)
    {
        if (string.IsNullOrEmpty(c.text) || c.image != null || !Scaled(out float s)) return false;
        if (Event.current.type != EventType.Repaint) return true; // labels only paint
        var at = new Rect(r.position + Origin, r.size);
        if (clips.Count > 0) // in a group: later (a copy of the style as it is now, colours and all)
        {
            var clip = clips[clips.Count - 1];
            if (at.xMax < clip.xMin - 100f || at.xMin > clip.xMax + 100f || at.yMax < clip.yMin - 30f || at.yMin > clip.yMax + 30f) return true; // scrolled away
            deferred.Add(new Deferred { at = at, content = new GUIContent(c.text, c.tooltip), style = new GUIStyle(st), textOnly = textOnly });
            return true;
        }
        // with the scale taken out of the matrix, one unit is one screen pixel
        var m = GUI.matrix;
        GUI.matrix = m * Matrix4x4.Scale(new Vector3(1f / s, 1f / s, 1f));
        Paint(Times(at, s), c, st, textOnly, s);
        GUI.matrix = m;
        return true;
    }

    /// The words, with the font, padding and <size> tags scaled up to screen pixels.
    static void Paint(Rect local, GUIContent c, GUIStyle st, bool textOnly, float s)
    {
        int size = st.fontSize > 0 ? st.fontSize : st.font != null ? st.font.fontSize : GUI.skin.font != null ? GUI.skin.font.fontSize : 13;
        size = Mathf.Max(size, MinFontSize); // nothing too small to read on a phone
        int oldSize = st.fontSize;
        var p = st.padding; // a live view of the style's padding: copy the numbers, not the object
        var oldPad = new RectOffset(p.left, p.right, p.top, p.bottom);
        var oldOffset = st.contentOffset;
        Texture2D n = null, h = null, act = null, f = null, on = null, onH = null, onA = null, onF = null;
        if (textOnly) // a button's words: no box (it's already drawn), the same colour it has now
        {
            n = st.normal.background; h = st.hover.background; act = st.active.background; f = st.focused.background;
            on = st.onNormal.background; onH = st.onHover.background; onA = st.onActive.background; onF = st.onFocused.background;
            st.normal.background = st.hover.background = st.active.background = st.focused.background = null;
            st.onNormal.background = st.onHover.background = st.onActive.background = st.onFocused.background = null;
        }
        st.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * s));
        st.padding = new RectOffset(Mathf.RoundToInt(oldPad.left * s), Mathf.RoundToInt(oldPad.right * s), Mathf.RoundToInt(oldPad.top * s), Mathf.RoundToInt(oldPad.bottom * s));
        st.contentOffset = oldOffset * s;
        string text = c.text;
        string shown = st.richText && text.IndexOf("<size=", System.StringComparison.Ordinal) >= 0 ? Sized(text, s) : text;
        // The font has no bold face, so Unity smears the regular one into a blurry "bold". Draw
        // the regular face twice, a screen pixel or so apart, instead: thick and still sharp.
        var oldStyle = st.fontStyle;
        bool bold = oldStyle is FontStyle.Bold or FontStyle.BoldAndItalic || (st.richText && shown.IndexOf("<b>", System.StringComparison.Ordinal) >= 0);
        if (bold)
        {
            st.fontStyle = oldStyle == FontStyle.BoldAndItalic ? FontStyle.Italic : oldStyle == FontStyle.Bold ? FontStyle.Normal : oldStyle;
            if (st.richText) shown = Unbold(shown);
        }
        var draw = shown == text ? c : new GUIContent(shown, c.tooltip);
        float dx = bold ? Mathf.Max(1f, Mathf.Round(s * 0.6f)) : 0f;
        for (int i = 0; i < (bold ? 2 : 1); i++)
        {
            var at = i == 0 ? local : new Rect(local.x + dx, local.y, local.width, local.height);
            if (textOnly) st.Draw(at, draw, false, false, false, false); // no hover glow on the words
            else GUI.Label(at, draw, st);
        }

        st.fontStyle = oldStyle;
        st.fontSize = oldSize; st.padding = oldPad; st.contentOffset = oldOffset;
        if (textOnly)
        {
            st.normal.background = n; st.hover.background = h; st.active.background = act; st.focused.background = f;
            st.onNormal.background = on; st.onHover.background = onH; st.onActive.background = onA; st.onFocused.background = onF;
        }
    }

    static readonly Dictionary<string, string> unbolded = new Dictionary<string, string>();
    static string Unbold(string text)
    {
        if (unbolded.TryGetValue(text, out var done)) return done;
        if (unbolded.Count > 2000) unbolded.Clear();
        done = text.Replace("<b>", "").Replace("</b>", "");
        unbolded[text] = done;
        return done;
    }

    /// Rich text with <size=N> tags, the sizes scaled too (cached: the same lines come every frame).
    static string Sized(string text, float s)
    {
        int key = Mathf.RoundToInt(s * 100f);
        if (sizedText.TryGetValue((text, key), out var done)) return done;
        if (sizedText.Count > 2000) sizedText.Clear();
        done = SizeTag.Replace(text, mt => $"<size={Mathf.RoundToInt(int.Parse(mt.Groups[1].Value) * s)}>");
        sizedText[(text, key)] = done;
        return done;
    }
}
