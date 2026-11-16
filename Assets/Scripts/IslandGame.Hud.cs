using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// Visual updates (camera, water, day/night) and the on-screen UI (IMGUI - fine for a prototype).
public partial class IslandGame
{
    readonly List<(string text, Vector3 pos, float t0, Color col)> popups = new List<(string, Vector3, float, Color)>();
    string hint;
    float hintUntil;
    Vector2 craftScroll;
    GUIStyle label, labelCenter, small, smallRight, smallCenter, titleCenter;
    int zoomOffset;

    /// GUI scale that fits a 820 x 540 layout on any screen: tall, wide, small or huge.
    public static float HudScale => Mathf.Max(0.1f, Mathf.Min(Screen.height / 540f, Screen.width / 820f));

    // ------------------------------------------------------------------ visuals
    void UpdateCamera()
    {
        var m = Mouse.current;
        if (m != null && !craftOpen && !skillsOpen && !bagOpen) // the wheel scrolls the panels while they're open
        {
            float s = m.scroll.ReadValue().y;
            if (s > 0.1f && zoom < 8) zoomOffset++;
            else if (s < -0.1f && zoom > 1) zoomOffset--;
        }
        // Default zoom follows the screen size (window resized, fullscreen, phone...); the wheel adjusts it.
        zoom = Mathf.Clamp(Mathf.Clamp(Mathf.RoundToInt(Screen.height / 300f), 2, 6) + zoomOffset, 1, 8);
        // Integer zoom keeps every pixel the same size on screen.
        cam.orthographicSize = Screen.height / (2f * PixelArt.PPU * zoom);
        Vector3 goal;
        if (voyage != null) goal = (Vector3)(voyage.raft + voyage.vel * 0.8f) + Vector3.up * 0.3f; // look ahead while paddling
        else if (player != null) goal = player.transform.position + Vector3.up * 0.3f;
        else return;
        camPos = Vector3.Lerp(camPos, goal, 1f - Mathf.Exp(-6f * Time.deltaTime));
        float u = 1f / (PixelArt.PPU * zoom); // snap to screen pixels
        cam.transform.position = new Vector3(Mathf.Round(camPos.x / u) * u, Mathf.Round(camPos.y / u) * u, -10f);
    }

    void UpdateWater(float dt)
    {
        waterTimer += dt;
        if (waterTimer < 0.8f) return;
        waterTimer = 0f;
        waterFrame ^= 1;
        foreach (var w in water) w.sr.sprite = PixelArt.Water(waterFrame ^ w.parity, w.fresh);
    }

    void UpdateDayNight(float dt)
    {
        if (!dead) clock += dt * TimeScale; // the Hourglass island races through its days
        float c = Mathf.Cos((Phase - 0.5f) * Mathf.PI * 2f);
        daylight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((c + 0.3f) / 0.6f));
        Color night = new Color(0.45f, 0.55f, 1f), dusk = new Color(1f, 0.7f, 0.5f);
        if (biome == Biome.Mushroom && voyage == null) daylight = Mathf.Min(daylight, 0.42f); // the mushroom canopy keeps it dim
        if (biome == Biome.Bubble && voyage == null) daylight = Mathf.Clamp(daylight, 0.38f, 0.6f); // deep under the sea
        Color col = Color.Lerp(night, Color.white, daylight);
        if (biome == Biome.Mushroom && voyage == null) col = Color.Lerp(col, new Color(0.7f, 0.55f, 1f), 0.35f);
        if (biome == Biome.Bubble && voyage == null) col = Color.Lerp(col, new Color(0.5f, 0.85f, 1f), 0.45f);
        col = Color.Lerp(col, dusk, (1f - Mathf.Abs(daylight - 0.5f) * 2f) * 0.6f);
        float intensity = Mathf.Lerp(0.22f, 1f, daylight);
        if (sun != null) { sun.color = col; sun.intensity = intensity; }
        Color bg = (biome == Biome.Sky && voyage == null ? new Color(0.62f, 0.8f, 0.98f)
                  : biome == Biome.Bubble && voyage == null ? new Color(0.06f, 0.2f, 0.38f) : PixelArt.WaterColor) * col * intensity;
        bg.a = 1f;
        cam.backgroundColor = bg;
    }

    void Hint(string text, float seconds) { hint = text; hintUntil = Time.time + seconds; }

    void Popup(string text, Vector3 pos, Color col)
    {
        int recent = 0;
        foreach (var p in popups) if (Time.time - p.t0 < 0.3f) recent++;
        popups.Add((text, pos + Vector3.up * 0.22f * recent, Time.time, col));
    }

    string Prompt(WorldNode n) => n.kind switch
    {
        NodeKind.Tree => HasAxe ? $"ตัดต้นไม้ ({n.hits}/{ChopsNeeded})" : "เขย่าต้นไม้",
        NodeKind.Bamboo => "ตัดไม้ไผ่",
        NodeKind.Obsidian => "เก็บออบซิเดียน",
        NodeKind.FruitTree => "เก็บผลไม้",
        NodeKind.Bush => "เก็บเบอร์รี่",
        NodeKind.Grass => "ตัดหญ้า",
        NodeKind.Stick => "เก็บกิ่งไม้",
        NodeKind.Rock => "เก็บหิน",
        NodeKind.Campfire => "ใช้กองไฟ",
        NodeKind.GlowShroom => "เก็บเห็ดเรืองแสง",
        NodeKind.GlowTree => "เก็บผลไม้เรืองแสง",
        NodeKind.Shell => "เก็บเปลือกหอย",
        NodeKind.CloudPlant => "เก็บปุยเมฆ",
        NodeKind.Drop => $"เก็บ{Name(n.dropItem)} x{n.dropCount}",
        NodeKind.Well => "ดื่มน้ำจากบ่อ",
        NodeKind.Fountain => "ดื่มน้ำพุ",
        NodeKind.Farm => "เก็บผักผลไม้",
        NodeKind.Kelp => "เก็บสาหร่าย",
        NodeKind.CoralNode => "เก็บปะการัง",
        NodeKind.Oyster => "งัดหอยมุก",
        NodeKind.AirVent => "หายใจที่ช่องอากาศ",
        NodeKind.SandCrystal => "เก็บทรายเวลา",
        NodeKind.Shelter => IsNight ? "นอน" : "นอน (กลางคืนเท่านั้น)",
        NodeKind.Raft => $"เปิดแผนที่ทะเล ({BoatName(raftLevel)} Lv {raftLevel})",
        _ => "",
    };

    // ------------------------------------------------------------------ HUD
    void InitStyles()
    {
        if (label != null) return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, richText = true };
        label.normal.textColor = Color.white;
        labelCenter = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        small = new GUIStyle(label) { fontSize = 11, fontStyle = FontStyle.Normal };
        smallRight = new GUIStyle(small) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
        smallCenter = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
        titleCenter = new GUIStyle(labelCenter) { fontSize = 28 };
    }

    static void ShadowLabel(Rect r, string text, GUIStyle style, Color? col = null)
    {
        Color c = col ?? Color.white;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.75f * c.a);
        GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
        style.normal.textColor = c;
        GUI.Label(r, text, style);
        style.normal.textColor = Color.white;
    }

    static void Fill(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    void Bar(Rect r, float v, Color col, string text)
    {
        Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0f, 0f, 0f, 0.55f));
        if (v < 0.25f) col = Color.Lerp(col, Color.white, 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 8f))); // blink when low
        Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), col);
        ShadowLabel(new Rect(r.x, r.y - 2, r.width, r.height + 4), text, smallCenter);
    }

    static void Icon(Rect r, Sprite s) => GUI.DrawTexture(r, s.texture, ScaleMode.ScaleToFit);

    Vector2 ToGui(Vector3 worldPos, float scale)
    {
        Vector3 sp = cam.WorldToScreenPoint(worldPos);
        return new Vector2(sp.x / scale, (Screen.height - sp.y) / scale);
    }

    void OnGUI()
    {
        if (!title && player == null && voyage == null) return;
        GameFont.Apply();
        InitStyles();
        TouchSwallowEvents();
        float s = HudScale; // the HUD is designed for a virtual screen at least 820 x 540
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;
        DrawSoundButton(W);
        if (title)
        {
            // keep the creator box in the middle of tall screens (phones held upright, tall windows)
            Fill(new Rect(0, 0, W, H), new Color(0.02f, 0.08f, 0.15f, 0.55f));
            GUI.matrix *= Matrix4x4.Translate(new Vector3(0f, (H - 540f) / 2f, 0f));
            DrawTitle(W, 540f);
            return;
        }
        if (player != null) DrawPlayerTags(s);
        DrawOnlineInfo(W, H);
        DrawQuestTracker(W);

        if (hurtT > 0f) Fill(new Rect(0, 0, W, H), new Color(0.8f, 0f, 0f, hurtT * 0.6f));

        Bar(new Rect(14, 14, 180, 14), health / MaxHealth, new Color(0.85f, 0.25f, 0.25f), $"พลังชีวิต {Mathf.CeilToInt(health)}/{MaxHealth:0}");
        Bar(new Rect(14, 34, 180, 14), hunger / 100f, new Color(0.95f, 0.6f, 0.2f), $"ความอิ่ม {Mathf.CeilToInt(hunger)}");
        Bar(new Rect(14, 54, 180, 14), thirst / 100f, new Color(0.3f, 0.65f, 0.95f), $"น้ำ {Mathf.CeilToInt(thirst)}");
        Bar(new Rect(14, 74, 180, 10), xp / (float)XpToNext(level), new Color(0.6f, 0.45f, 0.95f), $"Lv {level}   XP {xp}/{XpToNext(level)}");
        string where = voyage != null ? $"กลางทะเล กำลังไปเกาะที่ {destIsland}" : $"เกาะที่ {island}: {BiomeName}  <size=11>Lv {IslandLevel} · {ProsperityName(Prosperity)}</size>";
        ShadowLabel(new Rect(14, 88, 400, 20), $"{where}   วันที่ {Day} - {(IsNight ? "กลางคืน" : "กลางวัน")}", label);
        string goal = voyage != null ? "เป้าหมาย: พายไปทางตะวันออก (D) จนถึงฝั่ง"
                    : raftLevel == 0 ? "เป้าหมาย: สร้างแพแล้วล่องไปเกาะถัดไป"
                    : raftLevel < farthest ? $"เป้าหมาย: คุยกับช่างต่อเรือ เพื่ออัปเกรดเรือเป็น Lv {farthest} แล้วไปเกาะใหม่"
                    : $"เป้าหมาย: {BoatName(raftLevel)} Lv {raftLevel} พร้อมแล้ว - ไปเกาะใหม่ที่ {farthest + 1}";
        ShadowLabel(new Rect(14, 106, 400, 18), goal, small, new Color(1f, 1f, 1f, 0.75f));
        if (voyage != null) DrawVoyageBar(W);
        float infoY = 122f;
        if (hat != null || outfit != null)
        {
            var worn = new List<string>();
            if (hat != null) worn.Add(Name(hat.Value));
            if (outfit != null) worn.Add(Name(outfit.Value));
            ShadowLabel(new Rect(14, infoY, 320, 18), "สวมอยู่: " + string.Join(", ", worn), small, new Color(1f, 0.9f, 0.7f));
            infoY += 16f;
        }
        if (Count(Item.WaterFlask) > 0)
        {
            ShadowLabel(new Rect(14, infoY, 300, 18), $"กระบอกน้ำ {flaskWater}/{FlaskSize}  [Q] ดื่ม", small, new Color(0.6f, 0.85f, 1f));
            infoY += 16f;
        }
        infoY = DrawPowerHud(infoY);
        infoY = DrawTurtleHud(infoY);
        infoY = DrawSkyHud(infoY);
        infoY = DrawBubbleHud(infoY);
        infoY = DrawHourglassHud(infoY);
        if (poisonT > 0f)
        {
            ShadowLabel(new Rect(14, infoY, 320, 18), Count(Item.Antidote) > 0 ? "ติดพิษ! คลิกยาแก้พิษในกระเป๋า" : "ติดพิษ! พลังชีวิตลดลงเรื่อยๆ", small,
                new Color(0.6f, 1f, 0.4f, 0.6f + 0.4f * Mathf.Sin(Time.time * 6f)));
            infoY += 16f;
        }
        if (skillPoints > 0)
            ShadowLabel(new Rect(14, infoY, 300, 18), $"มีแต้มสกิล {skillPoints} แต้ม! กด K", small,
                new Color(1f, 0.85f, 0.3f, 0.6f + 0.4f * Mathf.Sin(Time.time * 5f)));

        // health bars over hurt enemies
        foreach (var e in enemies)
            if (e != null && e.hp < e.maxHp) HpBar(e.transform.position + Vector3.up * 0.55f, e.hp, e.maxHp, s);
        if (voyage != null)
            foreach (var c in voyage.creatures)
                if (c != null && c.hp < c.maxHp) HpBar(c.transform.position + Vector3.up * 0.6f, c.hp, c.maxHp, s);

        if (!craftOpen && !skillsOpen && !dead && !Busy) DrawPrompt(s);

        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var p = popups[i];
            float t = Time.time - p.t0;
            if (t > 1.2f) { popups.RemoveAt(i); continue; }
            var gp = ToGui(p.pos, s);
            var col = p.col; col.a = 1f - t / 1.2f;
            ShadowLabel(new Rect(gp.x - 100, gp.y - 20 - t * 25f, 200, 20), p.text, labelCenter, col);
        }

        DrawHotbar(W, H);
        if (bagOpen) DrawBag(W, H);
        if (craftOpen) DrawCrafting(W, H);
        if (skillsOpen) DrawSkills(W, H);

        // "!" over the weaver while she has something to teach
        foreach (var v in villagers)
            if (v != null && HasNews(v) && dialog == null)
            {
                var gp = ToGui(v.transform.position + Vector3.up * 0.95f, s);
                float bounce = Mathf.Sin(Time.time * 4f) * 2f;
                ShadowLabel(new Rect(gp.x - 10, gp.y - 22 + bounce, 20, 22), "!", titleCenter, new Color(1f, 0.85f, 0.3f));
            }
        if (dialog != null) DrawDialog(W, H);

        if (Time.time < hintUntil) ShadowLabel(new Rect(0, H - 112, W, 22), hint, labelCenter, new Color(1f, 0.92f, 0.5f));
        if (!touchUsed) ShadowLabel(new Rect(14, H - 22, W - 28, 20),
            "WASD เดิน | E ใช้/โจมตี | R พลัง | T ท่าพิเศษ | B กระเป๋า | 1-8 ช่องลัด | F กิน | Q ดื่มน้ำ | Tab ประดิษฐ์ | K สกิล | M เสียง | ล้อเมาส์ ซูม | F5 เซฟ F9 โหลด F8 เริ่มใหม่", small);
        float sinceSave = Time.time - savedAt;
        if (sinceSave < 1.5f)
            ShadowLabel(new Rect(W - 110, 64, 96, 20), "เซฟแล้ว", new GUIStyle(small) { alignment = TextAnchor.UpperRight }, new Color(1f, 1f, 1f, 1f - sinceSave / 1.5f));

        DrawInk(W, H);
        if (sleepT > 0f)
        {
            float a = sleepT < 1.2f ? sleepT / 1.2f : Mathf.Clamp01((2.6f - sleepT) / 1.4f);
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0.02f, a));
            ShadowLabel(new Rect(0, H / 2 - 20, W, 40), "คร่อก...", titleCenter, new Color(1f, 1f, 1f, a));
        }

        if (sailT > 0f)
        {
            // fade to sea blue, swap islands behind it, fade back in
            float a = sailT < 1.8f ? sailT / 1.8f : Mathf.Clamp01((4.2f - sailT) / 1.6f);
            Fill(new Rect(0, 0, W, H), new Color(0.08f, 0.25f, 0.45f, a));
            string text = arriving
                ? (sailApplied ? $"เกาะที่ {island}: {BiomeName}" : "เห็นฝั่งแล้ว!")
                : (sailApplied ? $"ทะเลเปิด - พายไปเกาะที่ {destIsland}" : "กำลังออกเรือ...");
            ShadowLabel(new Rect(0, H / 2 - 20, W, 40), text, titleCenter, new Color(1f, 1f, 1f, a));
        }

        if (dead)
        {
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.6f));
            ShadowLabel(new Rect(0, H / 2 - 44, W, 40), voyage != null ? "หายไปกลางทะเล..." : "คุณไม่รอด...", titleCenter);
            ShadowLabel(new Rect(0, H / 2 + 4, W, 24), $"ไปถึงเกาะที่ {island} เลเวล {level} วันที่ {Day}", labelCenter);
            // a real button (mouse or finger); the R key still works too
            var again = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            again.normal.textColor = new Color(1f, 0.85f, 0.3f);
            again.hover.textColor = Color.white;
            if (GUI.Button(new Rect(W / 2f - 130f, H / 2 + 40, 260f, 46f), "เริ่มการผจญภัยใหม่ (R)", again)) StartGame(null);
        }
        DrawTouchControls();
    }

    /// Sound on/off button in the top-right corner (also the M key in game).
    void DrawSoundButton(float W)
    {
        bool muted = Sfx.Muted;
        var r = new Rect(W - 122, 6, 112, 20);
        var st = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold };
        st.normal.textColor = muted ? new Color(1f, 0.6f, 0.55f) : new Color(0.7f, 1f, 0.7f);
        st.hover.textColor = st.normal.textColor;
        if (GUI.Button(r, muted ? "เสียง: ปิด (M)" : "เสียง: เปิด (M)", st)) Sfx.ToggleMute();
    }

    // ------------------------------------------------------------------ online UI
    /// Title screen = character creator: name, looks, then play online or offline.
    void DrawTitle(float W, float H)
    {
        var big = new GUIStyle(titleCenter) { fontSize = 36 };
        ShadowLabel(new Rect(0, 28, W, 46), "Island Survival", big, new Color(1f, 0.9f, 0.55f));
        ShadowLabel(new Rect(0, 72, W, 22), "สร้างตัวละครของคุณ แล้วล่องเรือจากเกาะสู่เกาะไปกับเพื่อนๆ", labelCenter, new Color(1f, 1f, 1f, 0.8f));

        var box = new Rect(W / 2f - 280, 104, 560, 360);
        Fill(box, new Color(0.08f, 0.06f, 0.04f, 0.88f));
        Fill(new Rect(box.x, box.y, box.width, 2), new Color(1f, 0.85f, 0.3f, 0.8f));
        GUI.enabled = !connecting;

        // --- preview: the character walking in place, turnable
        var stage = new Rect(box.x + 20, box.y + 20, 170, 250);
        Fill(stage, new Color(0.25f, 0.45f, 0.25f, 1f));
        Fill(new Rect(stage.x, stage.yMax - 60, stage.width, 60), new Color(0.2f, 0.38f, 0.2f, 1f));
        int[] walk = { 0, 1, 0, 2 };
        int frame = walk[(int)(Time.time * 4f) % 4];
        var sprite = PixelArt.Player(previewDir, frame, null, null, look.Code);
        Icon(new Rect(stage.x + 15, stage.y + 15, 140, 230), sprite);
        if (GUI.Button(new Rect(stage.x, stage.yMax + 8, stage.width, 26), previewDir == 0 ? "หันหลัง" : "หันหน้า"))
            previewDir = 1 - previewDir;

        // --- choices
        float x = box.x + 215, y = box.y + 20, w = 325;
        ShadowLabel(new Rect(x, y, w, 20), "ชื่อ", label);
        playerName = GUI.TextField(new Rect(x, y + 22, w, 26), playerName ?? "", 16, new GUIStyle(GUI.skin.textField) { fontSize = 15 });
        y += 60;
        int g = Chooser(x, y, w, "เพศ", look.gender, CharacterLook.Genders.Length, null, CharacterLook.Genders[look.gender]); y += 30;
        if (g != look.gender) { look.gender = g; look.style = g == 1 ? 1 : 0; } // switching gender also picks a fitting hair style
        look.skin = Chooser(x, y, w, "สีผิว", look.skin, CharacterLook.Skins.Length, CharacterLook.Skins[look.skin], null); y += 30;
        look.style = Chooser(x, y, w, "ทรงผม", look.style, CharacterLook.Styles.Length, null, CharacterLook.Styles[look.style]); y += 30;
        look.hair = Chooser(x, y, w, "สีผม", look.hair, CharacterLook.Hairs.Length, CharacterLook.Hairs[look.hair], null); y += 30;
        look.shirt = Chooser(x, y, w, "เสื้อ", look.shirt, CharacterLook.Shirts.Length, CharacterLook.Shirts[look.shirt], null); y += 30;
        look.pants = Chooser(x, y, w, look.gender == 1 ? "กระโปรง" : "กางเกง", look.pants, CharacterLook.Pants.Length, CharacterLook.Pants[look.pants], null); y += 32;
        if (GUI.Button(new Rect(x, y, w, 26), "สุ่มหน้าตา")) look = CharacterLook.Random();

        // --- play
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
        if (GUI.Button(new Rect(box.x + 20, box.yMax - 54, 330, 36), "เล่นออนไลน์ (เจอผู้เล่นอื่น)", btn)) PlayOnline();
        if (GUI.Button(new Rect(box.x + 360, box.yMax - 54, 180, 36), "เล่นออฟไลน์", new GUIStyle(GUI.skin.button) { fontSize = 13 })) PlayOffline();
        GUI.enabled = true;

        var status = new GUIStyle(smallCenter) { wordWrap = true };
        ShadowLabel(new Rect(box.x, box.yMax + 6, box.width, 34), netStatus, status, new Color(1f, 0.9f, 0.6f));
        if (connecting && GUI.Button(new Rect(W / 2f - 60, box.yMax + 40, 120, 24), "ยกเลิก")) PlayOffline();

        ShadowLabel(new Rect(0, H - 22, W, 18), "ความคืบหน้าเซฟไว้ในเบราว์เซอร์นี้ ถ้าเล่นออนไลน์ ทุกคนบนเกาะเดียวกันจะเห็นโลกเดียวกัน", smallCenter, new Color(1f, 1f, 1f, 0.6f));
    }

    /// One "< value >" row of the character creator. Shows a colour swatch or a text value.
    int Chooser(float x, float y, float w, string name, int value, int count, string swatchHex, string text)
    {
        ShadowLabel(new Rect(x, y + 4, 100, 20), name, small);
        float cx = x + 110;
        if (GUI.Button(new Rect(cx, y, 32, 26), "<")) value = (value + count - 1) % count;
        var mid = new Rect(cx + 38, y, w - 110 - 76, 26);
        if (swatchHex != null)
        {
            ColorUtility.TryParseHtmlString(swatchHex, out var col);
            Fill(mid, new Color(0f, 0f, 0f, 0.5f));
            Fill(new Rect(mid.x + 3, mid.y + 3, mid.width - 6, mid.height - 6), col);
        }
        else ShadowLabel(mid, text, labelCenter);
        if (GUI.Button(new Rect(mid.xMax + 6, y, 32, 26), ">")) value = (value + 1) % count;
        return value;
    }

    /// Names (and chat bubbles) over other players and yourself.
    void DrawPlayerTags(float s)
    {
        foreach (var r in remotes.Values)
        {
            if (r == null) continue;
            var gp = ToGui(r.transform.position + Vector3.up * 0.85f, s);
            ShadowLabel(new Rect(gp.x - 70, gp.y - 18, 140, 18), $"{r.playerName}  <size=10>Lv {r.level}</size>", smallCenter, new Color(0.7f, 0.9f, 1f));
            if (Time.time < r.chatUntil) ChatBubble(gp, r.chat);
        }
        if (Time.time < myChatUntil && player != null) ChatBubble(ToGui(player.transform.position + Vector3.up * 0.85f, s), myChat);
    }

    void ChatBubble(Vector2 gp, string text)
    {
        var st = new GUIStyle(small) { wordWrap = true, alignment = TextAnchor.MiddleCenter, fontSize = 11 };
        float w = Mathf.Min(200f, 14f + st.CalcSize(new GUIContent(text)).x);
        float h = st.CalcHeight(new GUIContent(text), w - 8f) + 6f;
        var r = new Rect(gp.x - w / 2f, gp.y - 22 - h, w, h);
        Fill(r, new Color(1f, 1f, 1f, 0.92f));
        st.normal.textColor = new Color(0.1f, 0.08f, 0.06f);
        GUI.Label(new Rect(r.x + 4, r.y + 3, r.width - 8, r.height - 6), text, st);
    }

    void DrawOnlineInfo(float W, float H)
    {
        string state = !Online ? "ออฟไลน์"
                     : voyage != null ? "ออนไลน์ - กลางทะเล"
                     : !inRoom ? "ออนไลน์ - กำลังเข้าเกาะ..."
                     : $"ออนไลน์ - บนเกาะนี้ {remotes.Count + 1} คน{(IsHost && remotes.Count > 0 ? " (โฮสต์)" : "")}";
        ShadowLabel(new Rect(W - 330, 30, 316, 18), state, new GUIStyle(small) { alignment = TextAnchor.UpperRight },
            Online ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 1f, 1f, 0.5f));

        // recent chat
        float y = H - 140;
        for (int i = chatLog.Count - 1; i >= 0 && y > H - 260; i--)
        {
            var c = chatLog[i];
            float age = Time.time - c.t;
            if (age > 20f && !chatOpen) continue;
            float a = chatOpen ? 1f : Mathf.Clamp01((20f - age) / 3f);
            string line = string.IsNullOrEmpty(c.who) ? $"<i>{c.text}</i>" : $"<b>{c.who}:</b> {c.text}";
            ShadowLabel(new Rect(14, y, 420, 18), line, small, new Color(1f, 1f, 1f, a));
            y -= 16;
        }

        if (chatOpen)
        {
            var e = Event.current;
            bool justOpened = Time.time - chatOpenedAt < 0.2f;
            if (e.type == EventType.KeyDown && !justOpened)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { SubmitChat(); e.Use(); return; }
                if (e.keyCode == KeyCode.Escape) { chatOpen = false; e.Use(); return; }
            }
            Fill(new Rect(10, H - 120, 430, 26), new Color(0f, 0f, 0f, 0.6f));
            GUI.SetNextControlName("chat");
            chatText = GUI.TextField(new Rect(14, H - 117, 422, 20), chatText ?? "", 80);
            GUI.FocusControl("chat");
            ShadowLabel(new Rect(14, H - 96, 420, 16), "Enter ส่ง, Esc ยกเลิก", small, new Color(1f, 1f, 1f, 0.6f));
        }
        else if (Shared && remotes.Count > 0)
            ShadowLabel(new Rect(W - 330, 46, 316, 18), "Enter: แชต", new GUIStyle(small) { alignment = TextAnchor.UpperRight }, new Color(1f, 1f, 1f, 0.5f));
    }

    static string ClothingBonus(Item it) => it switch
    {
        Item.StrawHat => "หิวน้ำ -15%",
        Item.TribalHeadband => "ดาเมจ +1",
        Item.LeafTunic => "พลังชีวิตสูงสุด +15",
        Item.HideVest => "รับดาเมจ -20%",
        _ => "",
    };

    /// Conversation box at the bottom of the screen.
    void DrawDialog(float W, float H)
    {
        var d = dialog;
        float pw = Mathf.Min(620f, W - 20f);
        const float optH = 26f;
        var textStyle = new GUIStyle(small) { wordWrap = true, fontSize = 12, richText = true };
        // the box grows with the text (quest objectives can take a few lines)
        float textH = Mathf.Max(40f, textStyle.CalcHeight(new GUIContent(d.text), pw - 28f) + 6f);
        float ph = 38f + textH + d.options.Count * (optH + 3f) + 6f;
        var box = new Rect(W / 2f - pw / 2f, Mathf.Max(8f, H - ph - 92f), pw, ph);
        Fill(box, new Color(0.08f, 0.06f, 0.04f, 0.88f));
        Fill(new Rect(box.x, box.y, box.width, 2), new Color(1f, 0.85f, 0.3f, 0.8f));
        ShadowLabel(new Rect(box.x + 14, box.y + 8, pw - 28, 20), d.speaker, label, new Color(1f, 0.85f, 0.3f));
        GUI.Label(new Rect(box.x + 14, box.y + 30, pw - 28, textH), d.text, textStyle);
        float optY = box.y + 32f + textH;
        for (int i = 0; i < d.options.Count; i++)
        {
            var o = d.options[i];
            GUI.enabled = o.enabled;
            var r = new Rect(box.x + 14, optY + i * (optH + 3f), pw - 28, optH);
            if (GUI.Button(r, $"{i + 1}.  {o.label}", new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 12 })) o.act();
            GUI.enabled = true;
        }
    }

    void HpBar(Vector3 worldPos, int hp, int maxHp, float s)
    {
        var gp = ToGui(worldPos, s);
        Fill(new Rect(gp.x - 13, gp.y - 1, 26, 5), new Color(0f, 0f, 0f, 0.6f));
        Fill(new Rect(gp.x - 12, gp.y, 24f * hp / maxHp, 3), new Color(0.9f, 0.25f, 0.25f));
    }

    /// Progress along the sea route: raft icon moving from island to island.
    void DrawVoyageBar(float W)
    {
        var bar = new Rect(W / 2f - 160f, 16f, 320f, 8f);
        Fill(new Rect(bar.x - 2, bar.y - 2, bar.width + 4, bar.height + 4), new Color(0f, 0f, 0f, 0.5f));
        Fill(new Rect(bar.x, bar.y, bar.width * VoyageProgress, bar.height), new Color(0.4f, 0.75f, 1f));
        Icon(new Rect(bar.x + bar.width * VoyageProgress - 10f, bar.y - 18f, 20f, 20f), PixelArt.Raft(raftLevel));
        ShadowLabel(new Rect(bar.x - 70, bar.y - 6, 66, 20), $"เกาะ {island}", new GUIStyle(small) { alignment = TextAnchor.MiddleRight });
        ShadowLabel(new Rect(bar.xMax + 6, bar.y - 6, 80, 20), $"เกาะ {destIsland}", small);
        float left = Mathf.Max(0f, voyage.length - voyage.raft.x);
        ShadowLabel(new Rect(bar.x, bar.y + 10, bar.width, 18), $"เหลืออีก {left * 10f:0} ม.", smallCenter);
    }

    void DrawSkills(float W, float H)
    {
        const float pw = 420f, rowH = 40f;
        int n = SkillInfo.Length;
        var panel = new Rect(W / 2f - pw / 2f, 60f, pw, 64f + n * (rowH + 4f));
        GUI.Box(panel, GUIContent.none);
        GUI.Box(panel, GUIContent.none);
        ShadowLabel(new Rect(panel.x + 12, panel.y + 6, pw, 22), $"สกิล - เลเวล {level}   <color=#ffd84d>{skillPoints} แต้ม</color>", label);
        ShadowLabel(new Rect(panel.x + 12, panel.y + 26, pw, 18), $"XP {xp}/{XpToNext(level)} - ล้มสัตว์ร้ายเพื่อเลเวลอัป  (K ปิด, กดตัวเลขเพื่ออัป)", small, new Color(0.85f, 0.85f, 0.85f));

        for (int i = 0; i < n; i++)
        {
            var info = SkillInfo[i];
            int lv = skills[i];
            var row = new Rect(panel.x + 8, panel.y + 50 + i * (rowH + 4f), pw - 16, rowH);
            GUI.Box(row, GUIContent.none);
            if (i == 0 || i == 4)
                ShadowLabel(new Rect(row.xMax - 150, row.y - 1, 80, 16), info.combat ? "ต่อสู้" : "เอาชีวิตรอด", small,
                    info.combat ? new Color(1f, 0.6f, 0.55f) : new Color(0.6f, 1f, 0.6f));
            Color nameCol = info.combat ? new Color(1f, 0.75f, 0.7f) : new Color(0.75f, 1f, 0.75f);
            ShadowLabel(new Rect(row.x + 8, row.y + 2, 200, 20), $"{i + 1}. {info.name}", label, nameCol);
            GUI.Label(new Rect(row.x + 8, row.y + 20, 290, 18), $"<color=#cccccc>{info.desc}</color>", small);

            var pips = new System.Text.StringBuilder();
            for (int p = 0; p < info.max; p++) pips.Append(p < lv ? "<color=#ffd84d>■</color>" : "<color=#555555>■</color>");
            GUI.Label(new Rect(row.x + 210, row.y + 3, 100, 18), pips.ToString(), label);

            GUI.enabled = skillPoints > 0 && lv < info.max;
            if (GUI.Button(new Rect(row.xMax - 46, row.y + 8, 40, 24), lv >= info.max ? "เต็ม" : "+")) UpgradeSkill((Skill)i);
            GUI.enabled = true;
        }
    }

    void DrawPrompt(float s)
    {
        string text = null;
        Vector3 at = default;
        if (voyage != null)
        {
            if (voyage.target != null) { text = $"โจมตี{voyage.target.DisplayName} (ดาเมจ {WeaponDamage})"; at = voyage.target.transform.position + Vector3.up * 0.7f; }
            else if (HasSpear) { text = "แทงปลา"; at = PlayerPos + Vector3.up * 0.8f; }
        }
        else if (enemyTarget != null) { text = $"โจมตี{enemyTarget.DisplayName} (ดาเมจ {WeaponDamage})"; at = enemyTarget.transform.position + Vector3.up * 0.6f; }
        else if (villagerTarget != null) { text = $"คุยกับ{villagerTarget.Title}"; at = villagerTarget.transform.position + Vector3.up * 0.95f; }
        else if (target != null) { text = Prompt(target); at = target.transform.position + Vector3.up * target.PromptHeight; }
        else if (pond != null) { text = "ดื่มน้ำ"; at = GridToWorld(pond.Value) + Vector3.up * 0.3f; }
        else if (seaSpot != null && HasSpear) { text = "แทงปลา"; at = GridToWorld(seaSpot.Value) + Vector3.up * 0.3f; }
        if (text == null) return;
        var gp = ToGui(at, s);
        ShadowLabel(new Rect(gp.x - 110, gp.y - 22, 220, 20), $"[E] {text}", labelCenter);
    }

    void DrawInventory(float W, float H)
    {
        var list = new List<Item>();
        foreach (Item it in System.Enum.GetValues(typeof(Item))) if (Count(it) > 0) list.Add(it);
        if (list.Count == 0) return;
        float sw = Mathf.Min(40f, (W - 40f) / list.Count - 4f), gap = 4f;
        float x0 = W / 2f - (list.Count * (sw + gap) - gap) / 2f, y = H - 76f;
        Item? hover = null;
        for (int i = 0; i < list.Count; i++)
        {
            var it = list[i];
            var r = new Rect(x0 + i * (sw + gap), y, sw, sw);
            GUI.Box(r, GUIContent.none);
            Icon(new Rect(r.x + 4, r.y + 4, sw - 8, sw - 8), PixelArt.Icon(it));
            if (IsWorn(it))
            {
                Fill(new Rect(r.x, r.y, r.width, 2), new Color(1f, 0.85f, 0.3f));
                Fill(new Rect(r.x, r.yMax - 2, r.width, 2), new Color(1f, 0.85f, 0.3f));
            }
            string badge = IsWorn(it) ? "<color=#ffd84d>W</color>" : IsTool(it) || IsClothing(it) ? "" : Count(it).ToString();
            ShadowLabel(new Rect(r.x, r.y + r.height - 16, r.width - 3, 16), badge, smallRight);
            if (r.Contains(Event.current.mousePosition)) hover = it;
            if (FoodValue(it) > 0 && GUI.Button(r, GUIContent.none, GUIStyle.none)) Eat(it);
            else if (it == Item.Antidote && GUI.Button(r, GUIContent.none, GUIStyle.none)) UseAntidote();
            else if (IsPowerFruit(it) && GUI.Button(r, GUIContent.none, GUIStyle.none)) EatPowerFruit(it);
            else if (IsClothing(it) && GUI.Button(r, GUIContent.none, GUIStyle.none)) ToggleWear(it);
        }
        if (hover != null)
        {
            var h = hover.Value;
            string extra = FoodValue(h) > 0 ? $"  (คลิกเพื่อกิน, อิ่ม +{FoodValue(h)})"
                         : IsClothing(h) ? (IsWorn(h) ? "  (สวมอยู่ - คลิกเพื่อถอด)" : "  (คลิกเพื่อสวม)") + $"  {ClothingBonus(h)}"
                         : h == Item.Antidote ? "  (คลิกเพื่อใช้ รักษาพิษ)"
                         : IsPowerFruit(h) ? $"  (คลิกเพื่อกิน ได้พลัง{PowerName(PowerOf(h))})"
                         : IsTool(h) ? "  (เครื่องมือ)" : "";
            ShadowLabel(new Rect(0, y - 22, W, 20), Name(h) + extra, labelCenter);
        }
    }

    void DrawCrafting(float W, float H)
    {
        const float pw = 330f, rowH = 52f, top = 70f;
        float contentH = Recipes.Length * (rowH + 4f);
        var panel = new Rect(W - pw - 14, top, pw, Mathf.Min(H - top - 130f, contentH + 40f));
        GUI.Box(panel, GUIContent.none);
        GUI.Box(panel, GUIContent.none);
        ShadowLabel(new Rect(panel.x + 10, panel.y + 6, pw, 22), "ประดิษฐ์   <size=11>(Tab ปิด, กดตัวเลขเพื่อสร้าง)</size>", label);

        var view = new Rect(panel.x + 4, panel.y + 32, pw - 8, panel.height - 36);
        var content = new Rect(0, 0, view.width - 18, contentH);
        craftScroll = GUI.BeginScrollView(view, craftScroll, content, false, true);
        for (int i = 0; i < Recipes.Length; i++)
        {
            var r = Recipes[i];
            var row = new Rect(2, i * (rowH + 4f), content.width - 4, rowH);
            GUI.Box(row, GUIContent.none);
            Sprite icon = r.output != null ? PixelArt.Icon(r.output.Value)
                        : r.places == NodeKind.Shelter ? PixelArt.Shelter()
                        : r.places == NodeKind.Raft ? PixelArt.Raft(1)
                        : r.raftUpgrade ? PixelArt.Raft(raftLevel + 1)
                        : PixelArt.Campfire(0);
            Icon(new Rect(row.x + 5, row.y + 9, 32, 32), icon);
            string hotkey = i < 9 ? $"{i + 1}. " : i == 9 ? "0. " : "";
            ShadowLabel(new Rect(row.x + 44, row.y + 1, 190, 20), $"{hotkey}{r.name}", label);

            var cost = new System.Text.StringBuilder();
            foreach (var c in r.cost)
            {
                if (cost.Length > 0) cost.Append(", ");
                bool enough = Count(c.item) >= c.n;
                cost.Append(enough ? "<color=#b8f0a0>" : "<color=#ff8a8a>")
                    .Append($"{c.n} {Name(c.item)} ({Count(c.item)})</color>");
            }
            GUI.Label(new Rect(row.x + 44, row.y + 19, row.width - 110, 18), cost.ToString(), small);

            bool ok = CanCraft(r, out var why);
            string sub = !ok && why != MissingMaterials ? $"<color=#ffcc66>{why}</color>" : $"<color=#cccccc>{r.desc}</color>";
            GUI.Label(new Rect(row.x + 44, row.y + 34, row.width - 110, 18), sub, small);

            GUI.enabled = ok;
            if (GUI.Button(new Rect(row.xMax - 60, row.y + 13, 54, 26), "สร้าง")) Craft(r);
            GUI.enabled = true;
        }
        GUI.EndScrollView();
    }
}
