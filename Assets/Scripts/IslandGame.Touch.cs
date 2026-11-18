using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// On-screen controls for phones and tablets: a joystick on the left, an action button (E)
/// and a few small buttons on the right. They appear after the first touch on the screen.
/// Multi-touch comes from the Input System's Touchscreen, so you can walk and act at once.
public partial class IslandGame
{
    enum TouchBtn { None, Stick, Action, Eat, Drink, Craft, Skills, Power, PowerAtk, Bag, Chat, Sticker, Awaken, Who, Menu }

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
        // every pair of buttons is at least two tap-radii (2 x 1.3 x SmallR = 65) apart, so a
        // finger on one can never land on its neighbour (the ⚙ and ☰ above Awaken included)
        TouchBtn.Eat => new Vector2(GuiW - 172f, GuiH - 74f),
        TouchBtn.Drink => new Vector2(GuiW - 182f, GuiH - 144f),
        TouchBtn.Power => new Vector2(GuiW - 120f, GuiH - 200f),
        TouchBtn.PowerAtk => new Vector2(GuiW - 52f, GuiH - 212f),
        TouchBtn.Awaken => new Vector2(GuiW - 92f, GuiH - 272f), // level 60 (IslandGame.Awaken.cs)
        // menus in an arc just above-right of the stick: the left side of the screen above them
        // stays free for the status text and the quest steps (they used to cover it)
        TouchBtn.Bag => new Vector2(198f, GuiH - 152f),
        TouchBtn.Craft => new Vector2(182f, GuiH - 220f),
        // online only, on the right between the E side and the hotbar (IslandGame.ChatMobile.cs / Sticker.cs)
        TouchBtn.Chat => new Vector2(GuiW - 196f, GuiH - 210f),
        TouchBtn.Sticker => new Vector2(GuiW - 254f, GuiH - 164f),
        TouchBtn.Who => new Vector2(GuiW - 32f, 32f), // who's online + channels, top right corner (phones have no O key, IslandGame.Who.cs)
        _ => new Vector2(112f, GuiH - 240f), // Skills
    };

    bool WhoTouchShown => Online && sailT <= 0f && sleepT <= 0f && (!dead || downed);

    Vector2 TouchCenter(TouchBtn b) => b switch
    {
        TouchBtn.Stick => StickCenter,
        TouchBtn.Action => ActionCenter,
        TouchBtn.Menu => MenuCenter(GuiW, GuiH),
        _ => SmallCenter(b),
    };

    /// How far from its centre a finger still counts as on the button.
    static float TapRadius(TouchBtn b) => b switch
    {
        TouchBtn.Stick => StickR * 1.5f,
        TouchBtn.Action => ActionR * 1.15f,
        TouchBtn.Menu => MenuR * 1.3f,
        _ => SmallR * 1.3f,
    };

    /// The one rule for whether a touch control is on screen. Drawing and tapping both ask it,
    /// so an invisible button can never swallow a tap meant for something under it.
    bool TouchShown(TouchBtn b)
    {
        if (!ShowTouch || b == TouchBtn.None) return false;
        if (b == TouchBtn.Menu) return MenuShown && !PanelCovers(TouchCenter(b), TapRadius(b)); // ☰ (IslandGame.Menu.cs)
        if (menuOpen) return false; // the open menu: taps go to its buttons
        if (b == TouchBtn.Who) return WhoTouchShown && (dialog == null || dead) && !PanelCovers(TouchCenter(b), TapRadius(b));
        if (whoOpen) return false; // the list covers the middle: taps go to its buttons
        if (dialog != null && !dead) return b == TouchBtn.Action; // talking: only E, so the dialog's choices stay free
        if (b == TouchBtn.Chat || b == TouchBtn.Sticker)
            return Shared && (!dead || downed) && sailT <= 0f && !PanelCovers(TouchCenter(b), TapRadius(b));
        if (dead || sailT > 0f || sleepT > 0f) return false;
        if (settingsOpen || wantedOpen || helpOpen) return false; // a big panel in the middle: its own buttons only
        bool on = b switch
        {
            TouchBtn.Power or TouchBtn.PowerAtk => power != Power.None,
            TouchBtn.Awaken => CanAwaken,
            TouchBtn.Stick or TouchBtn.Action or TouchBtn.Eat or TouchBtn.Drink or TouchBtn.Craft or TouchBtn.Skills or TouchBtn.Bag => true,
            _ => false,
        };
        return on && !PanelCovers(TouchCenter(b), TapRadius(b));
    }

    // the open bag / crafting / skills / build panel: touch controls that would sit on it step aside
    // (each panel has its own X), so they neither hide its rows nor catch the taps meant for them
    int panelFrame = -1;
    readonly List<Rect> openPanels = new List<Rect>();

    bool PanelCovers(Vector2 c, float r)
    {
        if (panelFrame != Time.frameCount)
        {
            panelFrame = Time.frameCount;
            openPanels.Clear();
            float W = GuiW, H = GuiH;
            if (bagOpen) openPanels.Add(BagPanelRect(W, H));
            if (craftOpen) openPanels.Add(CraftPanelRect(W, H, out _));
            if (skillsOpen) openPanels.Add(SkillsPanelRect(W));
            if (buildOpen && !Busy) openPanels.Add(BuildPanelRect(W, H));
        }
        foreach (var p in openPanels)
        {
            var q = new Vector2(Mathf.Clamp(c.x, p.xMin, p.xMax), Mathf.Clamp(c.y, p.yMin, p.yMax));
            if ((q - c).magnitude < r) return true;
        }
        return false;
    }

    // tested in this order; the stick's wide grab area goes last so the arc of small buttons by it wins
    static readonly TouchBtn[] HitOrder =
    {
        TouchBtn.Menu, TouchBtn.Who, TouchBtn.Action, TouchBtn.Eat, TouchBtn.Drink, TouchBtn.Craft, TouchBtn.Skills,
        TouchBtn.Power, TouchBtn.PowerAtk, TouchBtn.Bag, TouchBtn.Awaken, TouchBtn.Chat, TouchBtn.Sticker, TouchBtn.Stick,
    };

    TouchBtn HitTest(Vector2 p)
    {
        // the nearest shown button whose tap circle holds the finger (not just the first in the list)
        TouchBtn best = TouchBtn.None; float bestD = float.MaxValue;
        foreach (var b in HitOrder)
        {
            if (!TouchShown(b)) continue;
            float d = (p - TouchCenter(b)).magnitude, r = TapRadius(b);
            if (d >= r) continue;
            if (b == TouchBtn.Stick && best != TouchBtn.None) break; // a small button by the stick wins
            float rel = d / r;
            if (rel < bestD) { bestD = rel; best = b; }
        }
        return best;
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
        if (touchTapped.Contains(TouchBtn.Menu)) { ToggleMenu(); return; } // the ☰ menu (IslandGame.Menu.cs)
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
        if (TouchShown(TouchBtn.Menu)) DrawBurger(MenuCenter(GuiW, GuiH), menuOpen); // ☰ (IslandGame.Menu.cs)
        if (TouchShown(TouchBtn.Who))
            TouchCircle(SmallCenter(TouchBtn.Who), SmallR, $"CH{channel}", new Color(0.6f, 1f, 0.65f), whoOpen, 12);
        if (TouchShown(TouchBtn.Chat)) // chat with friends on the island (IslandGame.ChatMobile.cs)
            TouchCircle(SmallCenter(TouchBtn.Chat), SmallR, "แชต", new Color(0.7f, 0.9f, 1f), chatOpen, 12);
        if (TouchShown(TouchBtn.Sticker))
        {
            TouchCircle(SmallCenter(TouchBtn.Sticker), SmallR, null, new Color(1f, 0.75f, 0.85f), stickerOpen, 0);
            var face = StickerTex("aka_laugh");
            var c0 = SmallCenter(TouchBtn.Sticker);
            if (face != null) GUI.DrawTexture(new Rect(c0.x - SmallR + 5f, c0.y - SmallR + 5f, SmallR * 2f - 10f, SmallR * 2f - 10f), face, ScaleMode.ScaleToFit);
        }

        if (TouchShown(TouchBtn.Stick))
        {
            var c = StickCenter;
            TouchCircle(c, StickR, null, new Color(1f, 1f, 1f, 0.5f), false, 0);
            TouchCircle(c + stickKnob, 24f, null, new Color(1f, 1f, 1f, 0.85f), fingerOn.ContainsValue(TouchBtn.Stick), 0);
        }
        // action button: shows what E would do right now
        if (TouchShown(TouchBtn.Action)) TouchCircle(ActionCenter, ActionR, "E", new Color(1f, 0.85f, 0.3f), touchActionHeld, 22);
        // the power buttons: dimmed with the seconds left while recharging
        if (TouchShown(TouchBtn.Power))
            TouchCircle(SmallCenter(TouchBtn.Power), SmallR + 3f, PowerReady >= 1f ? "พลัง" : $"{Mathf.CeilToInt(powerCd)}",
                new Color(1f, 0.85f, 0.3f, PowerReady >= 1f ? 1f : 0.35f), PowerReady >= 1f, 12);
        if (TouchShown(TouchBtn.PowerAtk))
            TouchCircle(SmallCenter(TouchBtn.PowerAtk), SmallR + 3f, PowerAttackReady >= 1f ? "ท่าพิเศษ" : $"{Mathf.CeilToInt(powerAtkCd)}",
                new Color(1f, 0.55f, 0.35f, PowerAttackReady >= 1f ? 1f : 0.35f), PowerAttackReady >= 1f, PowerAttackReady >= 1f ? 10 : 12);
        if (TouchShown(TouchBtn.Awaken))
            TouchCircle(SmallCenter(TouchBtn.Awaken), SmallR + 3f, Awakened ? $"{awakenT:0}" : AwakenReady >= 1f ? "ปลุกพลัง" : $"{Mathf.CeilToInt(awakenCd)}",
                Color.Lerp(PowerColor(power), Color.white, 0.2f) * new Color(1f, 1f, 1f, AwakenReady >= 1f ? 1f : 0.35f), Awakened, Awakened || AwakenReady < 1f ? 12 : 10);
        if (TouchShown(TouchBtn.Bag)) TouchCircle(SmallCenter(TouchBtn.Bag), SmallR, "กระเป๋า", new Color(0.95f, 0.85f, 0.6f), bagOpen, 10);
        if (TouchShown(TouchBtn.Eat)) TouchCircle(SmallCenter(TouchBtn.Eat), SmallR, "กิน", new Color(1f, 0.7f, 0.35f), false, 12);
        if (TouchShown(TouchBtn.Drink)) TouchCircle(SmallCenter(TouchBtn.Drink), SmallR, "ดื่ม", new Color(0.55f, 0.85f, 1f), false, 12);
        if (TouchShown(TouchBtn.Craft)) TouchCircle(SmallCenter(TouchBtn.Craft), SmallR, "สร้าง", new Color(0.75f, 1f, 0.7f), craftOpen, 11);
        if (TouchShown(TouchBtn.Skills)) TouchCircle(SmallCenter(TouchBtn.Skills), SmallR, Lang.Pick("ตัวละคร", "Skills"), new Color(0.8f, 0.7f, 1f), skillsOpen, 10);
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
