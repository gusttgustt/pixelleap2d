using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// Chat on phones: the on-screen "แชต" button opens a real HTML text box at the top of the page
/// (IslandNet.jslib) so the phone's own keyboard comes up - Unity's own text field can't raise it
/// in a web build. What's typed there is sent through the usual chat (SubmitChat).
public partial class IslandGame
{
    bool mobileChat; // the chat box open right now is the HTML one

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void IS_ChatOpen(string placeholder);
    [DllImport("__Internal")] static extern string IS_ChatPoll();
    [DllImport("__Internal")] static extern void IS_ChatClose();
#else
    static void IS_ChatOpen(string placeholder) { }
    static string IS_ChatPoll() => null;
    static void IS_ChatClose() { }
#endif

    /// The phone's chat button: the HTML box in a web build, Unity's own field anywhere else.
    void OpenMobileChat()
    {
        if (!Shared || chatOpen) return;
        OpenChat();
#if UNITY_WEBGL && !UNITY_EDITOR
        mobileChat = true;
        WebGLInput.captureAllKeyboardInput = false; // let the HTML box have the keys
        IS_ChatOpen("พิมพ์ข้อความถึงเพื่อนบนเกาะ...");
#endif
    }

    /// Every frame: has the HTML box been sent or cancelled?
    void PollMobileChat()
    {
        if (!mobileChat) return;
        if (!chatOpen || !Shared) { CloseMobileChat(); chatOpen = false; return; } // left the island meanwhile
        string text = IS_ChatPoll();
        if (text == null) return; // still typing
        CloseMobileChat();
        chatText = text;
        SubmitChat(); // empty = cancelled: just closes
    }

    // ------------------------------------------------------------------ chat on screen
    /// Recent chat and the typing box, placed for the device:
    /// - mouse / keyboard: a panel bottom-left, above the hotbar, with a "แชต (Enter)" button,
    ///   the text box and a send button.
    /// - touch: a narrow strip at the top centre, clear of the joystick and buttons at the bottom;
    ///   typing happens in the HTML box at the top of the page.
    void DrawChat(float W, float H)
    {
        bool touch = ShowTouch;
        bool open = chatOpen;
        float w = touch ? Mathf.Clamp(W - 440f, 260f, 480f) : Mathf.Clamp(W * 0.45f, 300f, 420f);
        var st = new GUIStyle(small) { wordWrap = true, richText = true, fontSize = touch ? 12 : 11 };

        // the lines to show, oldest first: all of them while typing, otherwise the fresh ones
        int max = open ? 8 : touch ? 4 : 6;
        var lines = new List<(string text, float a, float h, Texture2D sticker)>();
        float total = 0f, top = 0f;
        for (int i = chatLog.Count - 1; i >= 0 && lines.Count < max; i--)
        {
            var c = chatLog[i];
            float age = Time.time - c.t;
            if (!open && age > 15f) continue;
            float a = open ? 1f : Mathf.Clamp01((15f - age) / 3f);
            var sticker = StickerOf(c.text); // a sticker: the name, then a small picture (IslandGame.Sticker.cs)
            string line = string.IsNullOrEmpty(c.who) ? $"<i>{c.text}</i>"
                        : sticker != null ? $"<b><color=#a8dcff>{c.who}:</color></b>"
                        : $"<b><color=#a8dcff>{c.who}:</color></b> {c.text}";
            float h = sticker != null ? 34f : st.CalcHeight(new GUIContent(line), w - 14f);
            lines.Insert(0, (line, a, h, sticker));
            total += h;
            top = Mathf.Max(top, a);
        }

        bool desktopBox = open && !mobileChat;            // Unity's own text field (keyboard, or the editor)
        bool chatButton = !open && !touch && Shared && remotes.Count > 0 && !dead;
        const float rowH = 28f;
        float logH = lines.Count > 0 ? total + 8f : 0f;

        Rect logRect, rowRect;
        if (touch)
        {
            logRect = new Rect(W / 2f - w / 2f, 30f, w, logH);
            rowRect = new Rect(logRect.x, logRect.yMax + 4f, w, rowH);
        }
        else
        {
            float bottom = H - 120f; // above the hotbar and the hint line
            rowRect = new Rect(12f, bottom - rowH, w, rowH);
            logRect = new Rect(12f, (desktopBox || chatButton ? rowRect.y - 4f : bottom) - logH, w, logH);
        }

        if (lines.Count > 0)
        {
            Fill(logRect, new Color(0f, 0f, 0f, (open ? 0.55f : 0.35f) * top));
            float y = logRect.y + 4f;
            foreach (var l in lines)
            {
                if (l.sticker != null)
                {
                    float nameW = st.CalcSize(new GUIContent(l.text)).x;
                    ShadowLabel(new Rect(logRect.x + 7f, y + 8f, nameW + 4f, 18f), l.text, st, new Color(1f, 1f, 1f, l.a));
                    GUI.color = new Color(1f, 1f, 1f, l.a);
                    GUI.DrawTexture(new Rect(logRect.x + 11f + nameW, y + 1f, 32f, 32f), l.sticker, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                }
                else ShadowLabel(new Rect(logRect.x + 7f, y, w - 14f, l.h), l.text, st, new Color(1f, 1f, 1f, l.a));
                y += l.h;
            }
        }

        if (desktopBox)
        {
            var e = Event.current;
            bool justOpened = Time.time - chatOpenedAt < 0.2f;
            if (e.type == EventType.KeyDown && !justOpened)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { SubmitChat(); e.Use(); return; }
                if (e.keyCode == KeyCode.Escape) { chatOpen = false; e.Use(); return; }
            }
            Fill(rowRect, new Color(0f, 0f, 0f, 0.7f));
            Fill(new Rect(rowRect.x, rowRect.y, rowRect.width, 2f), new Color(0.9f, 0.75f, 0.3f, 0.9f));
            var field = new GUIStyle(GUI.skin.textField) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            GUI.SetNextControlName("chat");
            chatText = GUI.TextField(new Rect(rowRect.x + 4f, rowRect.y + 4f, w - 106f, rowH - 6f), chatText ?? "", 80, field);
            if (!stickerOpen) GUI.FocusControl("chat");
            if (string.IsNullOrEmpty(chatText))
                ShadowLabel(new Rect(rowRect.x + 10f, rowRect.y + 5f, w - 116f, rowH - 8f), "พิมพ์แล้วกด Enter ส่ง, Esc ยกเลิก", small, new Color(1f, 1f, 1f, 0.4f));
            StickerButton(new Rect(rowRect.xMax - 98f, rowRect.y + 3f, 32f, rowH - 5f)); // IslandGame.Sticker.cs
            var send = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
            send.normal.textColor = new Color(1f, 0.85f, 0.3f);
            if (GUI.Button(new Rect(rowRect.xMax - 62f, rowRect.y + 3f, 58f, rowH - 5f), "ส่ง", send)) SubmitChat();
        }
        else if (chatButton)
        {
            var b = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
            b.normal.textColor = new Color(0.7f, 0.9f, 1f);
            if (GUI.Button(new Rect(rowRect.x, rowRect.y + 2f, 110f, rowH - 4f), "แชต (Enter)", b)) OpenChat();
            StickerButton(new Rect(rowRect.x + 114f, rowRect.y + 2f, 32f, rowH - 4f));
        }
    }

    void CloseMobileChat()
    {
        if (!mobileChat) return;
        mobileChat = false;
        IS_ChatClose();
#if UNITY_WEBGL && !UNITY_EDITOR
        WebGLInput.captureAllKeyboardInput = true;
#endif
    }
}
