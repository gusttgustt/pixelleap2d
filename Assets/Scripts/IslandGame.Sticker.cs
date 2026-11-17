using System.Collections.Generic;
using UnityEngine;

/// Chat stickers: three sets of 12 (อาคะ, โมโมะ and ยูรุ, Resources/Emoji/<set>_<name>.png, 128 px).
/// A sticker travels as an ordinary chat message ":aka_laugh:" - the server needs nothing new,
/// and an older game simply shows the code as text. Opened from the "สติกเกอร์" button next to
/// the chat (mouse) or the round sticker button above the chat button (touch).
public partial class IslandGame
{
    static readonly string[] StickerSets = { "aka", "momo", "yoru" };
    static readonly string[] StickerSetNames = { "อาคะ", "โมโมะ", "ยูรุ" };
    static readonly string[] StickerNames = { "laugh", "smug", "tongue", "cry", "angry", "shock", "love", "thumbsup", "sleepy", "facepalm", "please", "wave" };
    static readonly string[] StickerLabels = { "ขำกลิ้ง", "ยียวน", "แลบลิ้น", "ร้องไห้", "โกรธ", "ตกใจ", "หัวใจ", "เยี่ยม", "ง่วง", "หมดแรง", "ขอร้อง", "โบกมือ" };
    static readonly Dictionary<string, Texture2D> stickerTex = new Dictionary<string, Texture2D>();

    bool stickerOpen;  // the sticker panel is showing
    int stickerSet;    // which set's tab is open
    float stickerSentAt = -10f;

    static Texture2D StickerTex(string key)
    {
        if (stickerTex.TryGetValue(key, out var t)) return t;
        t = Resources.Load<Texture2D>("Emoji/" + key);
        stickerTex[key] = t;
        return t;
    }

    /// The picture for a chat message that is a sticker (":aka_laugh:"), or null for plain text.
    static Texture2D StickerOf(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 6 || text[0] != ':' || text[text.Length - 1] != ':') return null;
        string key = text.Substring(1, text.Length - 2);
        int cut = key.IndexOf('_');
        if (cut < 0 || System.Array.IndexOf(StickerSets, key.Substring(0, cut)) < 0 || System.Array.IndexOf(StickerNames, key.Substring(cut + 1)) < 0) return null;
        return StickerTex(key);
    }

    bool CanSticker => Shared && !title && (!dead || downed);

    void ToggleStickers()
    {
        if (!CanSticker) return;
        stickerOpen = !stickerOpen;
    }

    void SendSticker(string key)
    {
        stickerOpen = false;
        if (!CanSticker || Time.time - stickerSentAt < 0.6f) return; // no machine-gun stickers
        stickerSentAt = Time.time;
        string text = ":" + key + ":";
        Send(new NetMsg { t = "chat", text = text });
        AddChat(playerName, text);
        myChat = text;
        myChatUntil = Time.time + 6f;
        if (chatOpen) { CloseMobileChat(); chatOpen = false; chatText = ""; } // picked from inside the chat: done
        Sfx.Play("pickup", 0.3f);
    }

    /// Small button with a sticker face on it that opens / closes the panel (mouse layout).
    void StickerButton(Rect r)
    {
        if (CrispGUI.Button(r, GUIContent.none)) ToggleStickers();
        var face = StickerTex("aka_laugh");
        if (face != null) GUI.DrawTexture(new Rect(r.x + 3f, r.y + 1f, r.width - 6f, r.height - 2f), face, ScaleMode.ScaleToFit);
    }

    /// The panel: tabs for the two sets, a 6 x 2 grid, the hovered sticker's name, a close button.
    /// Centred on screen, clear of the joystick and the buttons at the bottom.
    void DrawStickerPanel(float W, float H)
    {
        if (!stickerOpen) return;
        if (!CanSticker) { stickerOpen = false; return; }
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { stickerOpen = false; e.Use(); return; }

        const int cols = 6;
        const float cell = 58f, gap = 4f, pad = 10f, tabH = 28f;
        float gridW = cols * cell + (cols - 1) * gap;
        float pw = gridW + pad * 2f, ph = pad + tabH + 8f + 2f * cell + gap + 26f + pad;
        var r = new Rect(Mathf.Round(W / 2f - pw / 2f), Mathf.Round(Mathf.Max(70f, H / 2f - ph / 2f - 30f)), pw, ph);

        Fill(new Rect(r.x + 3f, r.y + 4f, r.width, r.height), new Color(0f, 0f, 0f, 0.3f)); // shadow
        Fill(r, new Color(0.1f, 0.08f, 0.06f, 0.94f));
        Fill(new Rect(r.x, r.y, r.width, 2f), new Color(1f, 0.85f, 0.3f, 0.9f));

        // tabs
        var tab = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
        float tx = r.x + pad;
        for (int s = 0; s < StickerSets.Length; s++)
        {
            bool on = s == stickerSet;
            tab.normal.textColor = on ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 1f, 1f, 0.6f);
            var tr = new Rect(tx, r.y + pad, 92f, tabH);
            if (on) Fill(new Rect(tr.x, tr.yMax - 3f, tr.width, 3f), new Color(1f, 0.85f, 0.3f));
            if (CrispGUI.Button(tr, StickerSetNames[s], tab)) stickerSet = s;
            tx += 98f;
        }
        var close = new GUIStyle(GUI.skin.button) { fontSize = 12 };
        if (CrispGUI.Button(new Rect(r.xMax - pad - 56f, r.y + pad, 56f, tabH), "ปิด", close)) { stickerOpen = false; return; }

        // grid
        string hover = null;
        float gy = r.y + pad + tabH + 8f;
        for (int i = 0; i < StickerNames.Length; i++)
        {
            int cx = i % cols, cy = i / cols;
            var c = new Rect(r.x + pad + cx * (cell + gap), gy + cy * (cell + gap), cell, cell);
            bool over = c.Contains(e.mousePosition);
            Fill(c, over ? new Color(1f, 0.85f, 0.3f, 0.25f) : new Color(1f, 1f, 1f, 0.06f));
            string key = StickerSets[stickerSet] + "_" + StickerNames[i];
            var tex = StickerTex(key);
            if (tex != null) GUI.DrawTexture(new Rect(c.x + 3f, c.y + 3f, cell - 6f, cell - 6f), tex, ScaleMode.ScaleToFit);
            if (over) hover = StickerLabels[i];
            if (CrispGUI.Button(c, GUIContent.none, GUIStyle.none)) { SendSticker(key); return; }
        }
        ShadowLabel(new Rect(r.x, gy + 2f * cell + gap + 4f, r.width, 20f), hover ?? "แตะสติกเกอร์เพื่อส่งให้เพื่อนบนเกาะ", smallCenter,
            hover != null ? new Color(1f, 0.9f, 0.6f) : new Color(1f, 1f, 1f, 0.55f));

        // clicks inside the panel stop here (not on the world underneath)
        if (r.Contains(e.mousePosition) && (e.type == EventType.MouseDown || e.type == EventType.MouseUp)) e.Use();
    }
}
