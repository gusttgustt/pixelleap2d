using UnityEngine;
using UnityEngine.InputSystem;

/// Settings live behind one gear icon in the top-right corner (title screen and game): sound,
/// weather effects, full screen, zoom and the list of keys. Also the mouse on a computer:
/// left click does what E does, right click uses the fruit power (R), or the next move if it's recharging.
public partial class IslandGame
{
    bool settingsOpen;
    static Texture2D gearTex;
    bool mouseActionQueued, mouseActionHolding, mousePowerQueued;

    static Rect GearRect(float W) => new Rect(W - 40f, 6f, 32f, 28f); // title screen

    /// In the game: a round ⚙ in the bottom-right row with ☰, the bag and skills (IslandGame.Menu.cs).
    Vector2 SettingsCenter(float W, float H) => MenuCenter(W, H) + new Vector2(ShowTouch ? -54f : -162f, 0f);

    /// Title screen: under the gear. In the game: the middle of the screen, where no button sits
    /// under it (so its clicks can't reach the hotbar or the power buttons).
    Rect SettingsRect(float W, float H)
    {
        float h = 40f + 30f * SettingsRows + 26f;
        return title ? new Rect(W - 290f, 38f, 282f, h) : new Rect(W / 2f - 141f, Mathf.Max(8f, H / 2f - h / 2f - 40f), 282f, h);
    }

    int SettingsRows => title ? 2 : (touchUsed ? 4 : 5); // sound, full screen [, weather, zoom] [, keys]

    void ToggleSettings()
    {
        settingsOpen = !settingsOpen;
        if (settingsOpen) { craftOpen = false; bagOpen = false; skillsOpen = false; wantedOpen = false; crewOpen = false; helpOpen = false; menuOpen = false; whoOpen = false; }
        Sfx.Play("pickup", 0.3f);
    }

    /// The gear button (drawn first, so it gets its clicks even under other things).
    void DrawGearButton(float W, float H)
    {
        if (!title)
        {
            var c = SettingsCenter(W, H);
            var e = Event.current;
            bool lit = (e.mousePosition - c).magnitude < MenuR;
            if (lit && e.type == EventType.MouseDown && e.button == 0) { e.Use(); if (!chatOpen) ToggleSettings(); }
            var col = new Color(0.82f, 0.88f, 1f);
            TouchCircle(c, MenuR, null, col, settingsOpen || lit, 0);
            if (gearTex == null) gearTex = MakeGear();
            GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - 13f, c.y - 13f, 26f, 26f), gearTex);
            GUI.color = Color.white;
            if (Sfx.Muted) Fill(new Rect(c.x + 10f, c.y - 16f, 6f, 6f), new Color(1f, 0.4f, 0.35f)); // sound is off: a small red dot
            return;
        }
        var r = GearRect(W);
        bool over = r.Contains(Event.current.mousePosition);
        Fill(r, settingsOpen ? new Color(1f, 0.85f, 0.3f, 0.95f) : new Color(0f, 0f, 0f, over ? 0.6f : 0.42f));
        if (gearTex == null) gearTex = MakeGear();
        GUI.color = settingsOpen ? new Color(0.18f, 0.12f, 0.05f) : new Color(1f, 1f, 1f, over ? 1f : 0.85f);
        GUI.DrawTexture(new Rect(r.x + 5f, r.y + 3f, 22f, 22f), gearTex);
        GUI.color = Color.white;
        if (Sfx.Muted) Fill(new Rect(r.xMax - 9f, r.y + 3f, 6f, 6f), new Color(1f, 0.4f, 0.35f)); // sound is off: a small red dot
        if (CrispGUI.Button(r, GUIContent.none, GUIStyle.none)) ToggleSettings();
    }

    /// The panel, drawn last so it's on top. Nothing with buttons is open under it: opening it
    /// closes the other panels, and opening one of them (keys, talking) closes it.
    void DrawSettingsPanel(float W, float H)
    {
        if (!settingsOpen) return;
        if (!title && (craftOpen || bagOpen || skillsOpen || wantedOpen || buildOpen || dialog != null)) { settingsOpen = false; return; }
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { settingsOpen = false; e.Use(); return; }
        var box = SettingsRect(W, H);
        Fill(box, new Color(0.06f, 0.07f, 0.1f, 0.95f));
        Fill(new Rect(box.x, box.y, box.width, 3f), new Color(1f, 0.85f, 0.3f));
        ShadowLabel(new Rect(box.x + 12, box.y + 8, 160, 22), "ตั้งค่า", label, new Color(1f, 0.85f, 0.3f));
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
        if (CrispGUI.Button(new Rect(box.xMax - 58, box.y + 8, 50, 22), "ปิด", btn)) { settingsOpen = false; return; }

        var name = new GUIStyle(small) { fontSize = 13 };
        float y = box.y + 40f, x = box.x + 12f, bx = box.xMax - 110f;
        void Toggle(string what, bool on, System.Action flip)
        {
            ShadowLabel(new Rect(x, y + 3, 160, 20), what, name);
            var st = new GUIStyle(btn);
            st.normal.textColor = on ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.6f, 0.55f);
            st.hover.textColor = st.normal.textColor;
            if (CrispGUI.Button(new Rect(bx, y, 98, 24), on ? "เปิด" : "ปิด", st)) flip();
            y += 30f;
        }

        Toggle("เสียง  <size=11><color=#aaaaaa>(M)</color></size>", !Sfx.Muted, Sfx.ToggleMute);
        Toggle("เต็มจอ", Screen.fullScreen, () => Screen.fullScreen = !Screen.fullScreen);
        if (!title)
        {
            Toggle("เอฟเฟกต์สภาพอากาศ", WeatherFx, () =>
            {
                bool was = WeatherFx;
                WeatherFx = !was;
                Hint(was ? "ปิดเอฟเฟกต์สภาพอากาศแล้ว (อากาศยังมีผลกับตัวละครเหมือนเดิม)" : "เปิดเอฟเฟกต์สภาพอากาศแล้ว", 2.5f);
            });

            ShadowLabel(new Rect(x, y + 3, 160, 20), "ซูม  <size=11><color=#aaaaaa>(ล้อเมาส์)</color></size>", name);
            if (CrispGUI.Button(new Rect(bx, y, 30, 24), "−", btn) && zoom > 1) zoomOffset--;
            ShadowLabel(new Rect(bx + 30, y + 2, 38, 20), $"x{zoom}", new GUIStyle(smallCenter) { fontSize = 13 });
            if (CrispGUI.Button(new Rect(bx + 68, y, 30, 24), "+", btn) && zoom < 8) zoomOffset++;
            y += 30f;

            if (!touchUsed)
            {
                ShadowLabel(new Rect(x, y + 3, 160, 20), "ปุ่มควบคุมทั้งหมด", name);
                if (CrispGUI.Button(new Rect(bx, y, 98, 24), helpOpen ? "ซ่อน (F1)" : "ดู (F1)", btn)) helpOpen = !helpOpen;
                y += 30f;
            }
        }
        string foot = !title && !touchUsed ? "เมาส์: คลิกซ้าย = E · คลิกขวา = พลัง (R)" : BuildTimeText() ?? "";
        ShadowLabel(new Rect(x, y + 2, box.width - 24, 18), foot, new GUIStyle(small) { fontSize = 11 }, new Color(1f, 1f, 1f, 0.6f));

        // clicks on the panel stop here (they must not count as a click on the world behind it)
        if (box.Contains(e.mousePosition) && (e.type == EventType.MouseDown || e.type == EventType.MouseUp)) e.Use();
    }

    /// End of OnGUI: a click that landed on no button is a click on the world.
    void MouseWorldClick()
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown || touchUsed || title) return;
        if (e.button != 0 && e.button != 1) return;
        if (player == null && voyage == null) return;
        if (dead || chatOpen || Busy || craftOpen || skillsOpen || bagOpen || buildOpen || wantedOpen
            || settingsOpen || helpOpen || crewOpen || stickerOpen || whoOpen || menuOpen) return; // a click on an open panel isn't a click on the world
        if (e.button == 0) mouseActionQueued = true; // like pressing E (held: like holding E)
        else mousePowerQueued = true;                // like pressing R
        e.Use();
    }

    /// Start of Update (from UpdateTouch): the mouse clicks from the last GUI pass become input.
    void ApplyMouseInput()
    {
        var m = Mouse.current;
        if (mouseActionQueued)
        {
            mouseActionQueued = false;
            touchActionPressed = true;
            mouseActionHolding = true;
        }
        if (mouseActionHolding)
        {
            if (m != null && m.leftButton.isPressed && !chatOpen) touchActionHeld = true;
            else mouseActionHolding = false;
        }
        if (mousePowerQueued)
        {
            mousePowerQueued = false;
            if (!title && !dead && !chatOpen && !Busy && (player != null || voyage != null)) UsePowerChain();
        }
    }

    /// A white gear, 64 px, drawn once (tinted when drawn).
    static Texture2D MakeGear()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - n / 2f, dy = y + 0.5f - n / 2f;
                float d = Mathf.Sqrt(dx * dx + dy * dy), ang = Mathf.Atan2(dy, dx);
                float tooth = Mathf.Clamp01((Mathf.Cos(ang * 8f) - 0.15f) * 3f); // 8 teeth
                float outer = Mathf.Lerp(21f, 30f, tooth);
                float a = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - 9.5f + 0.5f); // ring with a hole
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
