using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// Thai / English. The game is written in Thai; English comes from Resources/Lang/en.txt, a list of
/// every Thai string in the scripts with its translation (Tools/lang_extract.py finds them all).
/// Nothing in the game code changes: every word on screen goes through CrispGUI, and CrispGUI asks
/// Tr() for the English just before drawing. A line in en.txt is
///     E <TAB> thai <TAB> english        a whole string, exactly as written in the code
///     T <TAB> thai {0} <TAB> english {0} an interpolated string: {0}, {1}... stand for the holes
/// Text made of several pieces (rich-text tags, lines, a name glued to a word) is taken apart and
/// each piece translated; what still has no English shows in Thai.
public static class Lang
{
    public static bool English { get; private set; }
    /// >0 while drawing what players wrote (chat, names): left exactly as it is.
    public static int Raw;

    public static void Set(bool english)
    {
        English = english;
        PlayerPrefs.SetString("lang", english ? "en" : "th");
        PlayerPrefs.Save();
        cache.Clear();
        SetPageLanguage(english);
    }

    /// Start of the game: the language picked last time (Thai until someone picks English: the
    /// players so far are Thai, and many of them have browsers set to English).
    public static void Init()
    {
        English = PlayerPrefs.GetString("lang", "") == "en";
        Load();
        SetPageLanguage(English);
    }

    /// Thai or English, picked here and now (for the rare line written in both, e.g. the language button).
    public static string Pick(string thai, string english) => English ? english : thai;

    public static GUIContent Content(string s) => new GUIContent(Tr(s));

    static bool HasThai(string s)
    {
        foreach (char c in s) if (c >= '฀' && c <= '๿') return true;
        return false;
    }

    // ------------------------------------------------------------------ the dictionary
    class Pattern { public Regex re; public string en; public string anchor; public int literal; }
    static readonly Dictionary<string, string> exact = new Dictionary<string, string>();
    static readonly List<Pattern> patterns = new List<Pattern>();
    static List<KeyValuePair<string, string>> pieces; // exact entries, longest first (for glued text)
    static bool loaded;

    static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var b = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                b.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
            }
            else b.Append(s[i]);
        }
        return b.ToString();
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        var asset = Resources.Load<TextAsset>("Lang/en");
        if (asset == null) { Debug.LogWarning("Lang: Resources/Lang/en.txt not found"); return; }
        foreach (var raw in asset.text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 3 || line[0] == '#') continue;
            var f = line.Split('\t');
            if (f.Length < 3 || f[2].Length == 0) continue;
            string th = Unescape(f[1]), en = Unescape(f[2]);
            if (f[0] == "E") exact[th] = en;
            else if (f[0] == "T") AddPattern(th, en);
        }
        pieces = new List<KeyValuePair<string, string>>();
        foreach (var kv in exact) if (kv.Key.Trim().Length >= 2 && HasThai(kv.Key) && kv.Key.IndexOf('<') < 0) pieces.Add(new KeyValuePair<string, string>(kv.Key.Trim(), kv.Value.Trim()));
        pieces.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
        patterns.Sort((a, b) => b.literal.CompareTo(a.literal)); // the most specific first
    }

    static readonly Regex Hole = new Regex(@"\{(\d+)\}");

    static void AddPattern(string th, string en)
    {
        // literal text between the holes; "{{" and "}}" are plain braces
        var re = new StringBuilder("^");
        string anchor = "";
        int literal = 0, last = 0;
        var holes = Hole.Matches(th);
        foreach (Match m in holes)
        {
            string lit = th.Substring(last, m.Index - last).Replace("{{", "{").Replace("}}", "}");
            re.Append(Regex.Escape(lit)).Append("(.*?)");
            literal += lit.Length;
            if (HasThai(lit) && lit.Length > anchor.Length) anchor = lit;
            last = m.Index + m.Length;
        }
        string tail = th.Substring(last).Replace("{{", "{").Replace("}}", "}");
        re.Append(Regex.Escape(tail)).Append('$');
        literal += tail.Length;
        if (HasThai(tail) && tail.Length > anchor.Length) anchor = tail;
        if (anchor.Length == 0) return; // nothing Thai to recognise it by
        patterns.Add(new Pattern { re = new Regex(re.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant), en = en, anchor = anchor, literal = literal });
    }

    // ------------------------------------------------------------------ translating
    static readonly Dictionary<string, string> cache = new Dictionary<string, string>();

    /// The English for a piece of Thai text on screen (as it is when not English, or when it has no Thai).
    public static string Tr(string s)
    {
        if (!English || Raw > 0 || string.IsNullOrEmpty(s) || !HasThai(s)) return s;
        if (cache.TryGetValue(s, out var done)) return done;
        if (cache.Count > 4000) cache.Clear(); // counters and timers make new lines all the time
        done = Whole(s, 0) ?? Parts(s);
        cache[s] = done;
        return done;
    }

    /// The whole string as written in the code: exactly, or one of the interpolated templates.
    static string Whole(string s, int depth)
    {
        if (exact.TryGetValue(s, out var en)) return en;
        if (depth > 4) return null;
        foreach (var p in patterns)
        {
            if (s.IndexOf(p.anchor, System.StringComparison.Ordinal) < 0) continue;
            var m = p.re.Match(s);
            if (!m.Success) continue;
            var args = new object[m.Groups.Count - 1];
            for (int i = 1; i < m.Groups.Count; i++)
            {
                string v = m.Groups[i].Value;
                args[i - 1] = HasThai(v) ? (Whole(v, depth + 1) ?? (depth == 0 ? Parts(v) : v)) : v;
            }
            try { return string.Format(p.en, args); } catch (System.FormatException) { return null; }
        }
        return null;
    }

    static readonly Regex Splitter = new Regex(@"(<[^<>]+>|\n)");

    /// Several pieces: rich-text tags and line breaks stay, each bit of text is translated.
    static string Parts(string s)
    {
        var bits = Splitter.Split(s);
        if (bits.Length == 1) return Glued(s);
        var b = new StringBuilder(s.Length + 16);
        foreach (var bit in bits)
        {
            if (bit.Length == 0) continue;
            if (bit[0] == '<' || bit == "\n" || !HasThai(bit)) { b.Append(bit); continue; }
            b.Append(Whole(bit, 1) ?? Glued(bit));
        }
        return b.ToString();
    }

    /// Text glued together in code ("ดาเมจ " + n, a name and a word): the known Thai phrases in it,
    /// longest first, each swapped for its English, with spaces where words now touch.
    static string Glued(string s)
    {
        string lead = s.Substring(0, s.Length - s.TrimStart().Length), trail = s.Substring(s.TrimEnd().Length);
        string core = s.Trim();
        if (core.Length == 0) return s;
        if (exact.TryGetValue(core, out var en)) return lead + en + trail;
        if (pieces == null) return s;
        string cur = core;
        foreach (var kv in pieces)
        {
            if (kv.Key.Length > cur.Length) continue;
            int at = cur.IndexOf(kv.Key, System.StringComparison.Ordinal);
            if (at < 0) continue;
            while (at >= 0)
            {
                string rep = kv.Value;
                if (at > 0 && char.IsLetterOrDigit(cur[at - 1]) && rep.Length > 0 && char.IsLetterOrDigit(rep[0])) rep = " " + rep;
                int end = at + kv.Key.Length;
                if (end < cur.Length && char.IsLetterOrDigit(cur[end]) && rep.Length > 0 && char.IsLetterOrDigit(rep[rep.Length - 1])) rep += " ";
                cur = cur.Substring(0, at) + rep + cur.Substring(end);
                at = cur.IndexOf(kv.Key, at + rep.Length, System.StringComparison.Ordinal);
            }
            if (!HasThai(cur)) break;
        }
        return lead + cur + trail;
    }

    // ------------------------------------------------------------------ the web page around the game
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void IS_SetLang(string lang);
    static void SetPageLanguage(bool english) { try { IS_SetLang(english ? "en" : "th"); } catch { } }
#else
    static void SetPageLanguage(bool english) { }
#endif
}
