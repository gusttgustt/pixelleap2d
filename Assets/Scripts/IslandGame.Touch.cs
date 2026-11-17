using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// On-screen controls for phones and tablets: a joystick on the left, an action button (E)
/// and a few small buttons on the right. They appear after the first touch on the screen.
/// Multi-touch comes from the Input System's Touchscreen, so you can walk and act at once.
public partial class IslandGame
{
    enum TouchBtn { None, Stick, Action, Eat, Drink, Craft, Skills, Power, PowerAtk, Bag, Chat, Sticker, Awaken, Who }

    /// Joystick direction (length 0..1), read by PlayerController.ReadInput.
    public static Vector2 TouchMove;
    static bool touchUsed;                    // a finger has touched the screen: show the controls
    bool touchActionHeld, touchActionPressed; // E button: held now / went down this frame
    readonly HashSet<TouchBtn> touchTapped = new HashSet<TouchBtn>(); // small buttons tapped this frame
    readonly Dictionary<int, TouchBtn> fingerOn = new Dictionary<int, TouchBtn>(); // touchId -> what it grabbed
    Vector2 stickKnob;                        // knob offset from the stick centre (GUI units)

    const float StickR = 58f, ActionR = 46f, SmallR = 25f;
    bool ShowTouch => touchUsed && !title && (player != null || voyage != null);

    // --- layout, in HUD units (see HudScale); y grows downwards like IMGUI
    static float GuiW => Screen.width / HudScale;
    static float GuiH => Screen.height / HudScale;
    static Vector2 StickCenter => new Vector2(104f, GuiH - 132f);
    static Vector2 ActionCenter => new Vector2(GuiW - 82f, GuiH - 120f);
    static Vector2 SmallCenter(TouchBtn b) => b switch
    {
        TouchBtn.Eat => new Vector2(GuiW - 172f, GuiH - 74f),
        TouchBtn.Drink => new Vector2(GuiW - 180f, GuiH - 140f),
        TouchBtn.Power => new Vector2(GuiW - 120f, GuiH - 200f),
        TouchBtn.PowerAtk => new Vector2(GuiW - 52f, GuiH - 212f),
        TouchBtn.Awaken => new Vector2(GuiW - 86f, GuiH - 266f), // level 60 (IslandGame.Awaken.cs)
        // menus in an arc just above-right of the stick: the left side of the screen above them
        // stays free for the status text and the quest steps (they used to cover it)
        TouchBtn.Bag => new Vector2(198f, GuiH - 152f),
        TouchBtn.Craft => new Vector2(176f, GuiH - 212f),
        // online only, on the right between the E side and the hotbar (IslandGame.ChatMobile.cs / Sticker.cs)
        TouchBtn.Chat => new Vector2(GuiW - 196f, GuiH - 210f),
        TouchBtn.Sticker => new Vector2(GuiW - 254f, GuiH - 164f),
        TouchBtn.Who => new Vector2(GuiW - 262f, GuiH - 232f), // who's online + channels (phones have no O key, IslandGame.Who.cs)
        _ => new Vector2(112f, GuiH - 240f), // Skills
    };

    bool WhoTouchShown => Online && sailT <= 0f && sleepT <= 0f && (!dead || downed);

    TouchBtn HitTest(Vector2 p)
    {
        if (WhoTouchShown && (p - SmallCenter(TouchBtn.Who)).magnitude < SmallR * 1.3f) return TouchBtn.Who;
        if (whoOpen) return TouchBtn.None; // the list is open: taps go to its buttons, not the controls under it
        if (dead)
        {
            if (downed && Shared && (p - SmallCenter(TouchBtn.Chat)).magnitude < SmallR * 1.3f) return TouchBtn.Chat;
            if (downed && Shared && (p - SmallCenter(TouchBtn.Sticker)).magnitude < SmallR * 1.3f) return TouchBtn.Sticker;
            return TouchBtn.None;
        }
        if ((p - ActionCenter).magnitude < ActionR * 1.15f) return TouchBtn.Action;
        if (dialog != null) return TouchBtn.None; // talking: only E (it closes / goes on); taps reach the dialog's own buttons
        // the small buttons first: the menu arc sits close to the stick's grab area
        foreach (var b in new[] { TouchBtn.Eat, TouchBtn.Drink, TouchBtn.Craft, TouchBtn.Skills, TouchBtn.Power, TouchBtn.PowerAtk, TouchBtn.Bag })
            if ((b != TouchBtn.Power && b != TouchBtn.PowerAtk || power != Power.None) && (p - SmallCenter(b)).magnitude < SmallR * 1.3f) return b;
        if (CanAwaken && (p - SmallCenter(TouchBtn.Awaken)).magnitude < SmallR * 1.3f) return TouchBtn.Awaken;
        if (Shared && (p - SmallCenter(TouchBtn.Chat)).magnitude < SmallR * 1.3f) return TouchBtn.Chat;
        if (Shared && (p - SmallCenter(TouchBtn.Sticker)).magnitude < SmallR * 1.3f) return TouchBtn.Sticker;
        if ((p - StickCenter).magnitude < StickR * 1.5f) return TouchBtn.Stick;
        return TouchBtn.None;
    }

    /// Called first thing in Update: reads every finger and turns it into game input.
    void UpdateTouch()
    {
        TouchMove = Vector2.zero;
        touchActionPressed = false;
        touchTapped.Clear();
        bool actionNow = false;
        var ts = Touchscreen.current;
        if (ts == null) { touchActionHeld = false; fingerOn.Clear(); ApplyMouseInput(); return; }

        var seen = new HashSet<int>();
        float s = HudScale;
        foreach (var t in ts.touches)
        {
            if (!t.press.isPressed) continue;
            touchUsed = true;
            int id = t.touchId.ReadValue();
            seen.Add(id);
            Vector2 sp = t.position.ReadValue();
            var p = new Vector2(sp.x / s, (Screen.height - sp.y) / s);

            if (!fingerOn.TryGetValue(id, out var btn))
            {
                btn = ShowTouch ? HitTest(p) : TouchBtn.None; // new finger: what did it land on?
                fingerOn[id] = btn;
                if (btn == TouchBtn.Action) touchActionPressed = true;
                else if (btn != TouchBtn.None && btn != TouchBtn.Stick) touchTapped.Add(btn);
            }
            if (btn == TouchBtn.Stick)
            {
                stickKnob = Vector2.ClampMagnitude(p - StickCenter, StickR);
                Vector2 v = stickKnob / StickR;
                if (v.magnitude < 0.2f) v = Vector2.zero; // small dead zone
                TouchMove = new Vector2(v.x, -v.y);       // GUI y is down, world y is up
            }
            if (btn == TouchBtn.Action) actionNow = true;
        }
        // fingers that lifted let go of what they held
        var gone = new List<int>();
        foreach (var kv in fingerOn) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (var id in gone)
        {
            if (fingerOn[id] == TouchBtn.Stick) stickKnob = Vector2.zero;
            fingerOn.Remove(id);
        }
        touchActionHeld = actionNow;
        ApplyMouseInput(); // left click = E, right click = power (IslandGame.Settings.cs)

        PollMobileChat(); // the phone's chat box was sent / cancelled (IslandGame.ChatMobile.cs)
        if (touchTapped.Contains(TouchBtn.Sticker) && !title) ToggleStickers(); // also while typing or knocked out (IslandGame.Sticker.cs)
        if (chatOpen || title) return;
        if (touchTapped.Contains(TouchBtn.Who)) { ToggleWho(); return; } // who's online and the channel buttons
        if (touchTapped.Contains(TouchBtn.Chat)) { OpenMobileChat(); return; } // works while knocked out too
        if (dead || Busy) return;
        if (touchTapped.Contains(TouchBtn.Power)) UsePower();
        if (touchTapped.Contains(TouchBtn.PowerAtk)) UsePowerAttack();
        if (touchTapped.Contains(TouchBtn.Awaken)) UseAwaken();
        if (touchTapped.Contains(TouchBtn.Bag)) ToggleBag();
        if (touchTapped.Contains(TouchBtn.Eat)) EatBest();
        if (touchTapped.Contains(TouchBtn.Drink)) DrinkFlask();
        if (touchTapped.Contains(TouchBtn.Craft)) ToggleCraft();
        if (touchTapped.Contains(TouchBtn.Skills)) { skillsOpen = !skillsOpen; craftOpen = false; bagOpen = false; }
    }

    /// Start of OnGUI: a tap on one of our controls must not also click the HUD underneath
    /// (the browser turns the first finger into mouse clicks too).
    void TouchSwallowEvents()
    {
        if (!ShowTouch) return;
        var e = Event.current;
        if (e.type != EventType.MouseDown && e.type != EventType.MouseUp && e.type != EventType.MouseDrag) return;
        if (HitTest(e.mousePosition) != TouchBtn.None) e.Use();
    }

    /// End of OnGUI: draw the controls on top of everything.
    void DrawTouchControls()
    {
        if (!ShowTouch) return;
        if (WhoTouchShown && (dialog == null || dead))
            TouchCircle(SmallCenter(TouchBtn.Who), SmallR, $"CH{channel}", new Color(0.6f, 1f, 0.65f), whoOpen, 12);
        if (whoOpen) return; // the list covers the middle: keep the controls out of its way
        if (dialog != null && !dead) // talking: only the E button, so the dialog's choices aren't covered
        {
            TouchCircle(ActionCenter, ActionR, "E", new Color(1f, 0.85f, 0.3f), touchActionHeld, 22);
            return;
        }
        if (Shared && (!dead || downed) && sailT <= 0f) // chat with friends on the island (IslandGame.ChatMobile.cs)
        {
            TouchCircle(SmallCenter(TouchBtn.Chat), SmallR, "แชต", new Color(0.7f, 0.9f, 1f), chatOpen, 12);
            TouchCircle(SmallCenter(TouchBtn.Sticker), SmallR, null, new Color(1f, 0.75f, 0.85f), stickerOpen, 0);
            var face = StickerTex("aka_laugh");
            var c0 = SmallCenter(TouchBtn.Sticker);
            if (face != null) GUI.DrawTexture(new Rect(c0.x - SmallR + 5f, c0.y - SmallR + 5f, SmallR * 2f - 10f, SmallR * 2f - 10f), face, ScaleMode.ScaleToFit);
        }
        if (dead || sailT > 0f || sleepT > 0f) return;

        // joystick
        var c = StickCenter;
        TouchCircle(c, StickR, null, new Color(1f, 1f, 1f, 0.5f), false, 0);
        TouchCircle(c + stickKnob, 24f, null, new Color(1f, 1f, 1f, 0.85f), fingerOn.ContainsValue(TouchBtn.Stick), 0);

        // action button: shows what E would do right now
        TouchCircle(ActionCenter, ActionR, "E", new Color(1f, 0.85f, 0.3f), touchActionHeld, 22);
        if (power != Power.None) // dim while recharging
            TouchCircle(SmallCenter(TouchBtn.Power), SmallR + 3f, "พลัง", new Color(1f, 0.85f, 0.3f, PowerReady >= 1f ? 1f : 0.35f), PowerReady >= 1f, 12);
        if (power != Power.None)
            TouchCircle(SmallCenter(TouchBtn.PowerAtk), SmallR + 3f, "ท่าพิเศษ", new Color(1f, 0.55f, 0.35f, PowerAttackReady >= 1f ? 1f : 0.35f), PowerAttackReady >= 1f, 10);
        if (CanAwaken)
            TouchCircle(SmallCenter(TouchBtn.Awaken), SmallR + 3f, Awakened ? $"{awakenT:0}" : "ปลุกพลัง", Color.Lerp(PowerColor(power), Color.white, 0.2f) * new Color(1f, 1f, 1f, AwakenReady >= 1f ? 1f : 0.35f), Awakened, 10);
        TouchCircle(SmallCenter(TouchBtn.Bag), SmallR, "กระเป๋า", new Color(0.95f, 0.85f, 0.6f), bagOpen, 10);
        TouchCircle(SmallCenter(TouchBtn.Eat), SmallR, "กิน", new Color(1f, 0.7f, 0.35f), false, 12);
        TouchCircle(SmallCenter(TouchBtn.Drink), SmallR, "ดื่ม", new Color(0.55f, 0.85f, 1f), false, 12);
        TouchCircle(SmallCenter(TouchBtn.Craft), SmallR, "สร้าง", new Color(0.75f, 1f, 0.7f), craftOpen, 11);
        TouchCircle(SmallCenter(TouchBtn.Skills), SmallR, "สกิล", new Color(0.8f, 0.7f, 1f), skillsOpen, 11);
    }

    /// With a mouse (no touch screen): two big round buttons in the bottom-right corner for the
    /// special power (R) and the special move (T), dimmed with a countdown while recharging.
    void DrawMousePowerButtons(float W, float H)
    {
        if (ShowTouch || title || dead || (player == null && voyage == null) || power == Power.None) return;
        if (sailT > 0f || sleepT > 0f || homeT > 0f || dialog != null || craftOpen || skillsOpen || bagOpen || buildOpen) return;
        var atk = new Vector2(W - 64f, H - 150f);
        var pow = new Vector2(W - 152f, H - 132f);
        MouseRoundButton(pow, 32f, "พลัง", "R", PowerReady, PowerCooldown, new Color(1f, 0.85f, 0.3f), () => UsePower());
        MouseRoundButton(atk, 40f, "ท่าพิเศษ", "T", PowerAttackReady, PowerAttackCooldown, new Color(1f, 0.55f, 0.35f), () => UsePowerAttack());
        if (CanAwaken) // level 60 (IslandGame.Awaken.cs)
            MouseRoundButton(new Vector2(W - 118f, H - 224f), 30f, Awakened ? $"{awakenT:0} วิ" : "ปลุกพลัง", "Y", AwakenReady, AwakenRecharge,
                Color.Lerp(PowerColor(power), Color.white, 0.2f), UseAwaken);
    }

    void MouseRoundButton(Vector2 c, float r, string text, string key, float ready, float cooldown, Color col, System.Action act)
    {
        var e = Event.current;
        bool over = (e.mousePosition - c).magnitude < r;
        if (over && e.type == EventType.MouseDown && e.button == 0)
        {
            e.Use();
            if (!chatOpen && !Busy) act();
        }
        bool ok = ready >= 1f;
        TouchCircle(c, r, null, new Color(col.r, col.g, col.b, ok ? 1f : 0.35f), ok && over, 0);
        var st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = r > 35f ? 13 : 12, fontStyle = FontStyle.Bold };
        string sub = ok ? $"[{key}]" : $"{Mathf.CeilToInt((1f - ready) * cooldown)} วิ";
        ShadowLabel(new Rect(c.x - r, c.y - r, r * 2f, r * 2f - 12f), text, st, ok ? col : new Color(col.r, col.g, col.b, 0.6f));
        ShadowLabel(new Rect(c.x - r, c.y - r + 16f, r * 2f, r * 2f - 12f), sub, new GUIStyle(st) { fontSize = 11 }, new Color(1f, 1f, 1f, ok ? 0.85f : 0.6f));
    }

    static Texture2D ringTex, discTex;

    static void TouchCircle(Vector2 c, float r, string text, Color col, bool lit, int fontSize)
    {
        if (discTex == null) { discTex = MakeCircle(false); ringTex = MakeCircle(true); }
        var rect = new Rect(c.x - r, c.y - r, r * 2f, r * 2f);
        GUI.color = new Color(0f, 0f, 0f, lit ? 0.55f : 0.35f);
        GUI.DrawTexture(rect, discTex);
        GUI.color = new Color(col.r, col.g, col.b, lit ? 1f : 0.8f * col.a + 0.1f);
        GUI.DrawTexture(rect, ringTex);
        GUI.color = Color.white;
        if (text == null) return;
        var st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = fontSize, fontStyle = FontStyle.Bold };
        ShadowLabel(rect, text, st, col);
    }

    /// 64px anti-aliased circle (filled disc or 4px ring), drawn once.
    static Texture2D MakeCircle(bool ring)
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        float R = n / 2f - 1f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude;
                float a = Mathf.Clamp01(R - d + 0.5f);
                if (ring) a *= Mathf.Clamp01(d - (R - 4f) + 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}
