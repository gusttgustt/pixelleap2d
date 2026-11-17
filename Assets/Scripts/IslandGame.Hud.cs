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
        if (m != null && !craftOpen && !skillsOpen && !bagOpen && !wantedOpen && dialog?.shop == null && dialog?.chart != true) // the wheel scrolls the panels while they're open
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
        else if (player != null) goal = player.transform.position + Vector3.up * 0.3f + (Vector3)CameraLead(Time.deltaTime); // looks a little ahead of you (IslandGame.Feel.cs)
        else return;
        camPos = Vector3.Lerp(camPos, goal, 1f - Mathf.Exp(-5f * Time.deltaTime));
        float u = 1f / (PixelArt.PPU * zoom); // snap to screen pixels
        Vector3 shake = ShakeOffset(Time.deltaTime);
        cam.transform.position = new Vector3(Mathf.Round((camPos.x + shake.x) / u) * u, Mathf.Round((camPos.y + shake.y) / u) * u, -10f);
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
        bool dim = voyage == null && biome is Biome.Mushroom or Biome.Bubble or Biome.Sunken;
        if (biome == Biome.Mushroom && voyage == null) daylight = Mathf.Min(daylight, 0.42f); // the mushroom canopy keeps it dim
        if (biome == Biome.Bubble && voyage == null) daylight = Mathf.Clamp(daylight, 0.38f, 0.6f); // deep under the sea
        if (biome == Biome.Sunken && voyage == null) daylight = Mathf.Clamp(daylight, 0.36f, 0.5f); // the sea floor
        Color col;
        float intensity;
        if (dim) // under a canopy or the sea: only a hint of the hour gets through
        {
            col = Color.Lerp(new Color(0.45f, 0.55f, 1f), Color.white, daylight);
            if (biome == Biome.Mushroom) col = Color.Lerp(col, new Color(0.7f, 0.55f, 1f), 0.35f);
            if (biome == Biome.Bubble) col = Color.Lerp(col, new Color(0.5f, 0.85f, 1f), 0.45f);
            if (biome == Biome.Sunken) col = Color.Lerp(col, new Color(0.4f, 0.75f, 1f), 0.6f);
            intensity = Mathf.Lerp(0.22f, 1f, daylight);
        }
        else (col, intensity) = SkyLight(Phase); // morning gold, white noon, red evening, blue night
        (col, intensity) = WeatherLight(col, intensity); // the season and the weather (IslandGame.Seasons.cs)
        AnnouncePartOfDay();
        if (inHouse) { intensity = Mathf.Max(intensity, 0.85f); col = Color.Lerp(col, new Color(1f, 0.92f, 0.8f), 0.7f); } // lamps on indoors
        // The 2D light doesn't reach the sprites in the web build, so the time of day is painted
        // over the screen instead (DrawTimeOfDayWash): the light itself stays plain white.
        lightCol = col; lightPower = intensity;
        if (sun != null) { sun.color = Color.white; sun.intensity = 1f; }
        Color bg = biome == Biome.Sky && voyage == null ? new Color(0.62f, 0.8f, 0.98f)
                  : biome == Biome.Bubble && voyage == null ? new Color(0.06f, 0.2f, 0.38f)
                  : biome == Biome.Sunken && voyage == null ? new Color(0.03f, 0.1f, 0.22f) : PixelArt.WaterColor;
        if (inHouse) bg = new Color(0.08f, 0.055f, 0.045f);
        bg.a = 1f;
        cam.backgroundColor = bg;
    }

    // ------------------------------------------------------------------ morning, noon, evening, night
    public enum DayPart { Morning, Noon, Evening, Night }
    static readonly string[] DayPartNames = { "เช้า", "กลางวัน", "เย็น", "กลางคืน" };
    static readonly string[] DayPartColors = { "#ffd48a", "#fff6c0", "#ff9a5c", "#9db4ff" };
    DayPart lastPart = (DayPart)(-1);

    /// Night is when daylight is low (IsNight, Phase < ~0.24 or > ~0.76); the day splits into a
    /// golden morning, a bright noon and a red evening.
    public DayPart PartOfDay => IsNight ? DayPart.Night : Phase < 0.40f ? DayPart.Morning : Phase < 0.63f ? DayPart.Noon : DayPart.Evening;

    /// The colour and strength of the light at a time of day (0 = midnight, 0.5 = noon).
    static readonly (float at, Color col, float power)[] SkyKeys =
    {
        (0.00f, new Color(0.30f, 0.40f, 0.95f), 0.20f), // midnight: deep blue
        (0.19f, new Color(0.38f, 0.44f, 0.95f), 0.25f),
        (0.245f, new Color(1.00f, 0.58f, 0.62f), 0.52f), // sunrise: pink
        (0.30f, new Color(1.00f, 0.82f, 0.62f), 0.80f), // morning: gold
        (0.38f, new Color(1.00f, 0.93f, 0.80f), 0.90f),
        (0.44f, new Color(1.00f, 1.00f, 0.98f), 1.00f), // noon: white and bright
        (0.58f, new Color(1.00f, 1.00f, 0.96f), 1.00f),
        (0.65f, new Color(1.00f, 0.74f, 0.48f), 0.82f), // evening: orange
        (0.72f, new Color(1.00f, 0.50f, 0.32f), 0.62f), // sunset: red
        (0.765f, new Color(0.72f, 0.40f, 0.62f), 0.40f), // dusk: purple
        (0.82f, new Color(0.36f, 0.42f, 0.95f), 0.24f),
        (1.00f, new Color(0.30f, 0.40f, 0.95f), 0.20f),
    };

    static (Color, float) SkyLight(float phase)
    {
        for (int i = 1; i < SkyKeys.Length; i++)
            if (phase <= SkyKeys[i].at)
            {
                var a = SkyKeys[i - 1]; var b = SkyKeys[i];
                float k = Mathf.SmoothStep(0f, 1f, (phase - a.at) / (b.at - a.at));
                return (Color.Lerp(a.col, b.col, k), Mathf.Lerp(a.power, b.power, k));
            }
        return (SkyKeys[0].col, SkyKeys[0].power);
    }

    /// A short line when the morning, noon or evening begins (night has its own warning in Play).
    void AnnouncePartOfDay()
    {
        var part = PartOfDay;
        if (part == lastPart) return;
        bool first = (int)lastPart < 0;
        lastPart = part;
        if (first || dead || inHouse || Time.time < hintUntil) return; // don't talk over another hint
        string line = part switch
        {
            DayPart.Morning => "รุ่งเช้าแล้ว แสงแดดสีทองส่องมา สัตว์ร้ายกลับไปนอน",
            DayPart.Noon => "เที่ยงวัน แดดจ้าที่สุด",
            DayPart.Evening => "เย็นแล้ว พระอาทิตย์กำลังตก เตรียมกองไฟก่อนมืด",
            _ => null,
        };
        if (line != null) Hint(line, 3.5f);
    }

    /// Top of the screen next to the status bars: a little window on the sky with the sun or moon
    /// riding across it, and the name of the time of day under it.
    void DrawSkyDial(Rect r)
    {
        float ph = Phase;
        var (light, _) = SkyLight(ph);
        bool night = PartOfDay == DayPart.Night;
        Color top = night ? new Color(0.04f, 0.06f, 0.2f) : Color.Lerp(new Color(0.3f, 0.55f, 0.95f), light, 0.35f);
        Color low = night ? new Color(0.12f, 0.15f, 0.38f) : Color.Lerp(new Color(0.65f, 0.85f, 1f), light, 0.6f);
        Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0f, 0f, 0f, 0.45f));
        float skyH = r.height - 16f;
        for (int i = 0; i < 6; i++) Fill(new Rect(r.x, r.y + i * skyH / 6f, r.width, skyH / 6f + 1f), Color.Lerp(top, low, i / 5f));
        if (night)
            for (int i = 0; i < 9; i++)
            {
                float a = 0.5f + 0.5f * Mathf.Sin(Time.time * 2f + i * 1.9f);
                Fill(new Rect(r.x + 4 + (i * 23) % (r.width - 8), r.y + 3 + (i * 11) % (skyH - 8), 1.5f, 1.5f), new Color(1f, 1f, 1f, 0.4f + 0.5f * a));
            }
        // the sun crosses from 0.25 (sunrise) to 0.75 (sunset); the moon takes the other half
        float t = night ? Mathf.Repeat(ph + 0.25f, 1f) * 2f : (ph - 0.25f) * 2f;
        t = Mathf.Clamp01(t);
        float bx = r.x + 6f + t * (r.width - 12f), by = r.y + skyH - 4f - Mathf.Sin(t * Mathf.PI) * (skyH - 10f);
        if (night)
        {
            Fill(new Rect(bx - 4, by - 4, 8, 8), new Color(0.95f, 0.95f, 0.85f));
            Fill(new Rect(bx - 1, by - 5, 6, 6), Color.Lerp(top, low, 0.4f)); // a crescent
        }
        else
        {
            Color sunCol = PartOfDay == DayPart.Noon ? new Color(1f, 0.95f, 0.55f) : new Color(1f, 0.6f, 0.3f);
            Fill(new Rect(bx - 6, by - 6, 12, 12), new Color(sunCol.r, sunCol.g, sunCol.b, 0.3f));
            Fill(new Rect(bx - 4, by - 4, 8, 8), sunCol);
        }
        Fill(new Rect(r.x, r.y + skyH - 2, r.width, 2), night ? new Color(0.05f, 0.12f, 0.2f) : new Color(0.2f, 0.5f, 0.3f)); // the ground
        Fill(new Rect(r.x, r.y + skyH, r.width, 16f), new Color(0f, 0f, 0f, 0.55f));
        var st = new GUIStyle(smallCenter) { fontSize = 12, richText = true, fontStyle = FontStyle.Bold };
        ShadowLabel(new Rect(r.x, r.y + skyH - 1, r.width, 17), $"<color={DayPartColors[(int)PartOfDay]}>{DayPartNames[(int)PartOfDay]}</color>", st);
    }

    /// IMGUI hands its colours to the screen as they are, but the project renders in linear colour
    /// space: convert, so a dark wash darkens instead of turning into a pale haze.
    static Color Over(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(c.linear.r, c.linear.g, c.linear.b, c.a) : c;
    static void FillOver(Rect r, Color c) => Fill(r, Over(c));

    static Texture2D vignetteTex;
    Color lightCol = Color.white;
    float lightPower = 1f;

    /// The light of the hour over the whole world (under the HUD): darkness by night, a colour
    /// wash for the gold morning and the red evening, and darker edges at night.
    void DrawTimeOfDayWash(float W, float H)
    {
        if (inHouse) return;
        var full = new Rect(0, 0, W, H);
        float dark = Mathf.Clamp01(1f - lightPower);
        if (dark > 0.01f) FillOver(full, new Color(lightCol.r * 0.12f, lightCol.g * 0.14f, lightCol.b * 0.3f, dark * 0.85f));
        float hue = 1f - Mathf.Min(lightCol.r, Mathf.Min(lightCol.g, lightCol.b));
        if (hue > 0.02f) FillOver(full, new Color(lightCol.r, lightCol.g, lightCol.b, hue * 0.22f));
        if (voyage == null && biome is Biome.Mushroom or Biome.Bubble or Biome.Sunken) return;
        if (vignetteTex == null)
        {
            const int n = 64;
            vignetteTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, (d - 0.35f) / 0.65f));
                }
            vignetteTex.SetPixels(px);
            vignetteTex.Apply();
        }
        float ph = Phase;
        float nightK = 1f - Mathf.Clamp01(daylight / 0.35f);
        float eveK = Mathf.Clamp01(1f - Mathf.Abs(ph - 0.71f) / 0.07f);
        float mornK = Mathf.Clamp01(1f - Mathf.Abs(ph - 0.27f) / 0.06f);
        Color c = new Color(0.01f, 0.02f, 0.08f, 0.6f * nightK);
        c = Color.Lerp(c, new Color(0.5f, 0.12f, 0.05f, 0.3f), eveK);
        c = Color.Lerp(c, new Color(1f, 0.6f, 0.55f, 0.18f), mornK);
        if (c.a < 0.01f) return;
        GUI.color = Over(c);
        GUI.DrawTexture(new Rect(0, 0, W, H), vignetteTex, ScaleMode.StretchToFill);
        GUI.color = Color.white;
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
        NodeKind.Stump => HasAxe ? $"ตัดตอไม้ออก ({n.hits}/{StumpChops})" : "ตอไม้ (ต้องมีขวาน)",
        NodeKind.Bamboo => "ตัดไม้ไผ่",
        NodeKind.Obsidian => "เก็บออบซิเดียน",
        NodeKind.FruitTree => "เก็บผลไม้",
        NodeKind.Bush => "เก็บเบอร์รี่",
        NodeKind.Grass => "ตัดหญ้า",
        NodeKind.Stick => "เก็บกิ่งไม้",
        NodeKind.Rock => "เก็บหิน",
        NodeKind.Campfire => "ใช้กองไฟ  (X ดับไฟ)",
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
        NodeKind.Chest => "เปิดหีบสมบัติ",
        NodeKind.DigSpot => "ขุดสมบัติ",
        NodeKind.CrystalCluster => "เก็บเศษผลึก",
        NodeKind.Mirror => "หมุนกระจก",
        NodeKind.LightningRod => n.hits > 0 ? "สายล่อฟ้า (ใช้งานได้)" : "ซ่อมสายล่อฟ้า (ทองแดง 2)",
        NodeKind.CopperOre => "ขุดแร่ทองแดง",
        NodeKind.GiantGrass => "ฟันหญ้ายักษ์",
        NodeKind.GiantFlower => "เก็บน้ำหวาน",
        NodeKind.Beehive => "เก็บน้ำผึ้ง (ระวังผึ้ง!)",
        NodeKind.CrystalVault => "เปิดห้องนิรภัยผลึก",
        NodeKind.Relic => "อ่านแผ่นจารึก",
        NodeKind.GiantClam => "หายใจจากหอยยักษ์",
        NodeKind.Shrine => "ไหว้ศาลเจ้าโบราณ",
        NodeKind.LostBundle => "เก็บห่อของที่หาย",
        NodeKind.IceCrystal => "เก็บเกล็ดน้ำแข็ง",
        NodeKind.HotSpring => "แช่บ่อน้ำร้อน",
        NodeKind.Shelter => IsNight ? "นอน" : "นอน (กลางคืนเท่านั้น)",
        NodeKind.Raft => $"เปิดแผนที่ทะเล ({BoatName(raftLevel)} Lv {raftLevel})",
        NodeKind.Plot => PlotPrompt(n),
        NodeKind.Chicken or NodeKind.Cow => AnimalPrompt(n),
        NodeKind.Cottage => $"{HouseName(houseLevel)} Lv {Mathf.Max(1, houseLevel)} (เข้าบ้าน / อัปเกรด)",
        NodeKind.Bed => IsNight ? "นอน" : "เตียง (นอนได้ตอนกลางคืน)",
        NodeKind.Workbench => "ใช้โต๊ะช่าง (ของฟาร์ม / ของตกแต่ง)",
        NodeKind.HouseDoor => "ออกจากบ้าน",
        NodeKind.Signpost => "อ่านป้าย",
        _ => "",
    };

    // ------------------------------------------------------------------ HUD
    void InitStyles()
    {
        if (label != null) return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, richText = true, clipping = TextClipping.Overflow, wordWrap = false };
        label.normal.textColor = Color.white;
        labelCenter = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        small = new GUIStyle(label) { fontSize = 12, fontStyle = FontStyle.Normal };
        smallRight = new GUIStyle(small) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
        smallCenter = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
        titleCenter = new GUIStyle(labelCenter) { fontSize = 28 };
    }

    static void ShadowLabel(Rect r, string text, GUIStyle style, Color? col = null)
    {
        Color c = col ?? Color.white;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.75f * c.a);
        CrispGUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
        style.normal.textColor = c;
        CrispGUI.Label(r, text, style);
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

    // ------------------------------------------------------------------ controls
    bool helpOpen; // F1: every key, in a readable panel

    static readonly (string key, string what)[] AllKeys =
    {
        ("WASD", "เดิน"), ("E / Space", "ใช้ เก็บ คุย โจมตี"), ("Tab / C", "ประดิษฐ์"), ("B", "กระเป๋า"),
        ("1-8", "ช่องลัด"), ("F", "กินอาหาร"), ("Q", "ดื่มจากกระบอกน้ำ"), ("X", "ดับกองไฟ"),
        ("R", "ใช้พลังผลไม้"), ("T", "ท่าพิเศษ"), ("K", "สกิล"), ("P", "สัตว์เลี้ยง / จับสัตว์"),
        ("H", "เกาะส่วนตัว / กลับไปผจญภัย"), ("G", "สร้าง / รื้อ"), ("V", "เยี่ยมบ้านเพื่อน"), ("Enter", "แชท"),
        ("J", "ป้ายประกาศค่าหัว"), ("M", "เปิด/ปิดเสียง"), ("F5", "เซฟ"), ("ล้อเมาส์", "ซูม"), ("F1", "เปิด/ปิดหน้านี้"),
    };

    /// A short line of the keys you need right now, with F1 for the rest.
    void DrawKeyBar(float W, float H)
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.f1Key.wasPressedThisFrame && Event.current.type == EventType.Repaint) helpOpen = !helpOpen;
        string[] keys = buildOpen ? new[] { "WASD เดิน", "E วาง/รื้อ", "Z X เลือกของ", "R หมุน", "G ออก" }
                      : new[] { "WASD เดิน", "E ใช้/เก็บ", "Tab ประดิษฐ์", "B กระเป๋า", "F กิน", "Q ดื่ม", "F1 ปุ่มทั้งหมด" };
        var st = new GUIStyle(small) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        float x = 10f, y = H - 24f;
        foreach (var k in keys)
        {
            float w = st.CalcSize(new GUIContent(k)).x + 14f;
            Fill(new Rect(x, y, w, 20f), new Color(0f, 0f, 0f, 0.45f));
            int sp = k.IndexOf(' ');
            ShadowLabel(new Rect(x, y, w, 20f), $"<color=#ffd84d><b>{k.Substring(0, sp)}</b></color>{k.Substring(sp)}", st);
            x += w + 5f;
        }
        if (!helpOpen) return;
        float pw = Mathf.Min(560f, W - 20f), rows = Mathf.Ceil(AllKeys.Length / 2f), ph = 52f + rows * 24f;
        var box = new Rect(W / 2f - pw / 2f, Mathf.Max(10f, H / 2f - ph / 2f - 20f), pw, ph);
        Fill(box, new Color(0.06f, 0.07f, 0.1f, 0.94f));
        Fill(new Rect(box.x, box.y, box.width, 3f), new Color(1f, 0.85f, 0.3f));
        ShadowLabel(new Rect(box.x + 14, box.y + 8, pw, 24), "ปุ่มทั้งหมด  <size=12><color=#aaaaaa>(F1 ปิด)</color></size>", label, new Color(1f, 0.85f, 0.3f));
        var ks = new GUIStyle(label) { fontSize = 14 };
        for (int i = 0; i < AllKeys.Length; i++)
        {
            float cx = box.x + 16 + (i % 2) * (pw / 2f), cy = box.y + 40 + (i / 2) * 24f;
            ShadowLabel(new Rect(cx, cy, 110, 22), AllKeys[i].key, ks, new Color(1f, 0.85f, 0.3f));
            ShadowLabel(new Rect(cx + 104, cy, pw / 2f - 120, 22), AllKeys[i].what, new GUIStyle(ks) { fontStyle = FontStyle.Normal });
        }
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
        CrispGUI.Reset();
        if (!title) { DrawTimeOfDayWash(W, H); DrawWeatherOverlay(W, H); } // under every button and panel
        DrawSoundButton(W);
        if (!title) DrawHomeButton(W); // private island <-> adventure (IslandGame.Home.cs)
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
        DrawCrewPanel(W, H); // crews (IslandGame.Crew.cs)

        if (hurtT > 0f) Fill(new Rect(0, 0, W, H), new Color(0.8f, 0f, 0f, hurtT * 0.6f));
        DrawWarnings(W, H);

        // status block: a dark backing so it reads over grass and sand, with room between the bars
        Fill(new Rect(6, 6, 208, 98), new Color(0f, 0f, 0f, 0.32f));
        Bar(new Rect(14, 12, 192, 17), health / MaxHealth, new Color(0.85f, 0.25f, 0.25f), $"พลังชีวิต {Mathf.CeilToInt(health)}/{MaxHealth:0}");
        Bar(new Rect(14, 35, 192, 17), hunger / 100f, new Color(0.95f, 0.6f, 0.2f), $"ความอิ่ม {Mathf.CeilToInt(hunger)}");
        Bar(new Rect(14, 58, 192, 17), thirst / 100f, new Color(0.3f, 0.65f, 0.95f), $"น้ำ {Mathf.CeilToInt(thirst)}");
        Bar(new Rect(14, 81, 192, 15), xp / (float)XpToNext(level), new Color(0.6f, 0.45f, 0.95f), $"Lv {level}   XP {xp}/{XpToNext(level)}");
        string where = visiting ? (inHouse ? $"ในบ้านของ {visitName}  <size=11>{HouseName(houseLevel)} Lv {Mathf.Max(1, houseLevel)}</size>" : $"เกาะส่วนตัวของ {visitName}  <size=11>มาเยี่ยมบ้าน</size>")
            : inHouse ? $"ในบ้าน{HouseName(houseLevel)} Lv {houseLevel}  <size=11>ห้อง {RoomSize}x{RoomSize} ช่อง</size>"
            : atHome ? "เกาะส่วนตัว  <size=11>บ้าน ฟาร์ม และตลาดเมืองท่า</size>"
            : voyage != null ? $"กลางทะเล กำลังไป{VoyageDestName}"
            : $"เกาะที่ {island}: {T.name}  <size=11>{ContinentName} · {BiomeName} · Lv {IslandLevel} · {ProsperityName(Prosperity)}</size>";
        ShadowLabel(new Rect(14, 110, 400, 22), $"{where}   วันที่ {Day} - <color={DayPartColors[(int)PartOfDay]}>{DayPartNames[(int)PartOfDay]}</color>", label);
        DrawSkyDial(new Rect(222, 8, 76, 52));
        DrawSeasonInfo(new Rect(220, 64, 80, 32));
        string goal = visiting ? "เดินชมบ้านเพื่อน ทักทายด้วย Enter ร้านค้าในตลาดใช้ได้ตามปกติ  (H กลับบ้านตัวเอง)"
                    : atHome ? $"ปลูกผัก เลี้ยงสัตว์ ตกปลา ขายของที่ตลาด แล้วสร้างบ้านในฝัน  (H กลับไปผจญภัยที่เกาะ {island})"
                    : voyage != null ? "เป้าหมาย: พายไปทางตะวันออก (D) จนถึงฝั่ง"
                    : raftLevel == 0 ? "เป้าหมาย: สร้างแพแล้วล่องไปเกาะถัดไป"
                    : raftLevel < farthest ? $"เป้าหมาย: คุยกับช่างต่อเรือ เพื่ออัปเกรดเรือเป็น Lv {farthest} แล้วไปเกาะใหม่"
                    : $"เป้าหมาย: {BoatName(raftLevel)} Lv {raftLevel} พร้อมแล้ว - ไปเกาะใหม่ที่ {farthest + 1}";
        float infoY = 136f;
        if (TutorialActive) infoY = DrawTutorial(W, H, infoY, s); // first steps, one at a time (IslandGame.Feel.cs)
        else { ShadowLabel(new Rect(14, infoY, 400, 20), goal, small, new Color(1f, 1f, 1f, 0.85f)); infoY += 24f; }
        if (voyage != null) DrawVoyageBar(W);
        if (hat != null || outfit != null)
        {
            var worn = new List<string>();
            if (hat != null) worn.Add(Name(hat.Value));
            if (outfit != null) worn.Add(Name(outfit.Value));
            ShadowLabel(new Rect(14, infoY, 320, 18), "สวมอยู่: " + string.Join(", ", worn), small, new Color(1f, 0.9f, 0.7f));
            infoY += 19f;
        }
        if (Count(Item.WaterFlask) > 0)
        {
            ShadowLabel(new Rect(14, infoY, 300, 18), $"กระบอกน้ำ {flaskWater}/{FlaskSize}  [Q] ดื่ม", small, new Color(0.6f, 0.85f, 1f));
            infoY += 19f;
        }
        infoY = DrawBountyHud(infoY);  // IslandGame.Bounty.cs
        infoY = DrawHomeHud(infoY, s); // coins, fishing line, animals (IslandGame.Home.cs)
        infoY = DrawPowerHud(infoY);
        infoY = DrawTurtleHud(infoY);
        infoY = DrawSkyHud(infoY);
        infoY = DrawBubbleHud(infoY);
        infoY = DrawHourglassHud(infoY);
        infoY = DrawIceHud(infoY);
        infoY = DrawSunkenHud(infoY);
        infoY = DrawPirateHud(infoY);
        infoY = DrawCrystalHud(infoY);
        infoY = DrawStormHud(infoY);
        infoY = DrawCompassHud(infoY);
        if (poisonT > 0f)
        {
            ShadowLabel(new Rect(14, infoY, 320, 18), Count(Item.Antidote) > 0 ? "ติดพิษ! คลิกยาแก้พิษในกระเป๋า" : "ติดพิษ! พลังชีวิตลดลงเรื่อยๆ", small,
                new Color(0.6f, 1f, 0.4f, 0.6f + 0.4f * Mathf.Sin(Time.time * 6f)));
            infoY += 19f;
        }
        if (skillPoints > 0)
            ShadowLabel(new Rect(14, infoY, 300, 18), $"มีแต้มสกิล {skillPoints} แต้ม! กด K", small,
                new Color(1f, 0.85f, 0.3f, 0.6f + 0.4f * Mathf.Sin(Time.time * 5f)));

        // health bars over hurt enemies
        foreach (var e in enemies)
            if (e != null && e.hp < e.maxHp) HpBar(e.transform.position + Vector3.up * 0.55f, e.hp, e.maxHp, s);
        if (voyage != null)
            foreach (var c in voyage.creatures)
                if (c != null && c.enabled && c.hp < c.maxHp) HpBar(c.transform.position + Vector3.up * (c.Tall + 0.1f), c.hp, c.maxHp, s);

        if (!craftOpen && !skillsOpen && !dead && !Busy && !buildOpen) DrawPrompt(s);

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
        if (buildOpen && !Busy) DrawBuildPanel(W, H); // IslandGame.Build.cs
        DrawBountyFlash(W);
        BuildPointerEvent(s); // a click / tap on the ground places right there

        // "!" over the weaver while she has something to teach
        foreach (var v in villagers)
            if (v != null && HasNews(v) && dialog == null)
            {
                var gp = ToGui(v.transform.position + Vector3.up * 0.95f, s);
                float bounce = Mathf.Sin(Time.time * 4f) * 2f;
                ShadowLabel(new Rect(gp.x - 10, gp.y - 22 + bounce, 20, 22), "!", titleCenter, new Color(1f, 0.85f, 0.3f));
            }
        if (wantedOpen) DrawWanted(W, H); // IslandGame.Bounty.cs (over the "!" marks)
        if (dialog != null) DrawDialog(W, H);

        if (Time.time < hintUntil) ShadowLabel(new Rect(0, H - 112, W, 22), hint, labelCenter, new Color(1f, 0.92f, 0.5f));
        if (!touchUsed) DrawKeyBar(W, H);
        float sinceSave = Time.time - savedAt;
        if (sinceSave < 1.5f)
            ShadowLabel(new Rect(W - 110, 64, 96, 20), "เซฟแล้ว", new GUIStyle(small) { alignment = TextAnchor.UpperRight }, new Color(1f, 1f, 1f, 1f - sinceSave / 1.5f));

        DrawStormOverlay(W, H);
        DrawScreenFlash(W, H);
        DrawInk(W, H);
        if (sleepT > 0f)
        {
            float a = sleepT < 1.2f ? sleepT / 1.2f : Mathf.Clamp01((2.6f - sleepT) / 1.4f);
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0.02f, a));
            ShadowLabel(new Rect(0, H / 2 - 20, W, 40), "คร่อก...", titleCenter, new Color(1f, 1f, 1f, a));
        }

        DrawHomeWarp(W, H);
        if (sailT > 0f)
        {
            // fade to sea blue, swap islands behind it, fade back in
            float a = sailT < 1.8f ? sailT / 1.8f : Mathf.Clamp01((4.2f - sailT) / 1.6f);
            Fill(new Rect(0, 0, W, H), new Color(0.08f, 0.25f, 0.45f, a));
            string text = arriving
                ? (sailApplied ? (visiting ? $"บ้านของ {visitName}" : $"เกาะที่ {island}: {BiomeName}") : "เห็นฝั่งแล้ว!")
                : (sailApplied ? $"ทะเลเปิด - พายไป{VoyageDestName}" : "กำลังออกเรือ...");
            ShadowLabel(new Rect(0, H / 2 - 20, W, 40), text, titleCenter, new Color(1f, 1f, 1f, a));
        }

        if (dead)
        {
            // The character lies in the middle of the screen: keep it clear. Text goes in a band at
            // the top, the button in a band at the bottom, and the middle is only lightly dimmed.
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, downed ? 0.15f : 0.3f));
            Fill(new Rect(0, 0, W, 120), new Color(0f, 0f, 0f, 0.45f));
            Fill(new Rect(0, H - 110, W, 110), new Color(0f, 0f, 0f, 0.45f));
            if (downed)
            {
                ShadowLabel(new Rect(0, 28, W, 40), "หมดสติ!", titleCenter, new Color(1f, 0.6f, 0.5f));
                ShadowLabel(new Rect(0, 72, W, 24), $"รอเพื่อนมาชุบชีวิต... เหลืออีก {Mathf.CeilToInt(Mathf.Max(0f, downedT))} วินาที", labelCenter,
                    new Color(1f, 0.92f, 0.5f, 0.7f + 0.3f * Mathf.Sin(Time.time * 4f)));
            }
            else
            {
                ShadowLabel(new Rect(0, 28, W, 40), voyage != null ? "หายไปกลางทะเล..." : "คุณไม่รอด...", titleCenter);
                ShadowLabel(new Rect(0, 72, W, 24), $"ไปถึงเกาะที่ {island} เลเวล {level} วันที่ {Day}", labelCenter);
            }
            // a real button (mouse or finger); the R key still works too
            var again = new GUIStyle(GUI.skin.button) { fontSize = downed ? 15 : 18, fontStyle = FontStyle.Bold };
            again.normal.textColor = new Color(1f, 0.85f, 0.3f);
            again.hover.textColor = Color.white;
            string btnText = downed ? "ยอมแพ้ เริ่มการผจญภัยใหม่ (R)" : "เริ่มการผจญภัยใหม่ (R)";
            if (CrispGUI.Button(new Rect(W / 2f - 140f, H - 80, 280f, 46f), btnText, again)) Respawn();
        }
        DrawStickerPanel(W, H);      // chat stickers (IslandGame.Sticker.cs)
        DrawMousePowerButtons(W, H); // big R / T buttons for the mouse (IslandGame.Touch.cs)
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
        if (CrispGUI.Button(r, muted ? "เสียง: ปิด (M)" : "เสียง: เปิด (M)", st)) Sfx.ToggleMute();
        DrawWeatherFxButton(new Rect(r.x - 112, r.y, 106, r.height)); // weather effects on / off (IslandGame.Seasons.cs)
    }

    // ------------------------------------------------------------------ online UI
    /// Title screen = character creator: name, looks, then play online or offline.
    void DrawTitle(float W, float H)
    {
        var big = new GUIStyle(titleCenter) { fontSize = 36 };
        ShadowLabel(new Rect(0, 28, W, 46), "Island Survival", big, new Color(1f, 0.9f, 0.55f));
        ShadowLabel(new Rect(0, 72, W, 22), "ตั้งชื่อตัวละครของคุณ แล้วล่องเรือจากเกาะสู่เกาะไปกับเพื่อนๆ", labelCenter, new Color(1f, 1f, 1f, 0.8f));

        var box = new Rect(W / 2f - 280, 104, 560, 390);
        Fill(box, new Color(0.08f, 0.06f, 0.04f, 0.88f));
        Fill(new Rect(box.x, box.y, box.width, 2), new Color(1f, 0.85f, 0.3f, 0.8f));
        GUI.enabled = !connecting;

        // --- preview: the character walking in place, turnable
        var stage = new Rect(box.x + 20, box.y + 20, 170, 250);
        Fill(stage, new Color(0.25f, 0.45f, 0.25f, 1f));
        Fill(new Rect(stage.x, stage.yMax - 60, stage.width, 60), new Color(0.2f, 0.38f, 0.2f, 1f));
        int[] walk = { 0, 1, 0, 2 };
        int frame = PixelArt.WalkFrames(look.Code) == 4 ? 1 + (int)(Time.time * 8f) % 4 : walk[(int)(Time.time * 4f) % 4];
        var sprite = PixelArt.Player(previewDir, frame, null, null, look.Code);
        Icon(new Rect(stage.x + 15, stage.y + 15, 140, 230), sprite);
        if (CrispGUI.Button(new Rect(stage.x, stage.yMax + 8, stage.width, 26), previewDir == 0 ? "หันหลัง" : "หันหน้า"))
            previewDir = 1 - previewDir;

        // --- choices
        float x = box.x + 215, y = box.y + 20, w = 325;
        ShadowLabel(new Rect(x, y, w, 20), "ชื่อ", label);
        playerName = GUI.TextField(new Rect(x, y + 22, w, 26), playerName ?? "", 16, new GUIStyle(GUI.skin.textField) { fontSize = 15 });
        y += 60;
        if (!CharacterLook.CanChoose)
        {
            // ready-made characters only: all of them side by side, tap one to pick it
            ShadowLabel(new Rect(x, y, w, 20), "ตัวละคร  <size=11>(แตะเพื่อเลือก)</size>", label);
            DrawCharacterCards(x, y + 20, w, 96);
            ShadowLabel(new Rect(x, y + 118, w, 18), "หมวกและชุดที่สวมจะไม่แสดงบนตัว แต่ยังได้ค่าพลังเหมือนเดิม", small, new Color(1f, 1f, 1f, 0.6f));
            DrawAccount(x, box.y + 216, w); // log in to play the same character anywhere (IslandGame.Account.cs)
        }
        else
        {
        look.preset = Chooser(x, y, w, "ตัวละคร", look.preset, CharacterLook.Presets.Length, null, CharacterLook.Presets[look.preset]); y += 34;
        if (look.preset > 0)
        {
            var note = new GUIStyle(small) { wordWrap = true };
            ShadowLabel(new Rect(x, y, w, 60), "ตัวละครสำเร็จรูป: ปรับสีผม/เสื้อไม่ได้ และจะไม่เห็นหมวกหรือชุดที่สวมบนตัว (แต่ยังได้ค่าพลังเหมือนเดิม) — กด < เพื่อกลับไปปรับแต่งเอง", note, new Color(1f, 0.85f, 0.9f));
        }
        else
        {
        int g = Chooser(x, y, w, "เพศ", look.gender, CharacterLook.Genders.Length, null, CharacterLook.Genders[look.gender]); y += 30;
        if (g != look.gender) { look.gender = g; look.style = g == 1 ? 1 : 0; } // switching gender also picks a fitting hair style
        look.skin = Chooser(x, y, w, "สีผิว", look.skin, CharacterLook.Skins.Length, CharacterLook.Skins[look.skin], null); y += 30;
        look.style = Chooser(x, y, w, "ทรงผม", look.style, CharacterLook.Styles.Length, null, CharacterLook.Styles[look.style]); y += 30;
        look.hair = Chooser(x, y, w, "สีผม", look.hair, CharacterLook.Hairs.Length, CharacterLook.Hairs[look.hair], null); y += 30;
        look.shirt = Chooser(x, y, w, "เสื้อ", look.shirt, CharacterLook.Shirts.Length, CharacterLook.Shirts[look.shirt], null); y += 30;
        look.pants = Chooser(x, y, w, look.gender == 1 ? "กระโปรง" : "กางเกง", look.pants, CharacterLook.Pants.Length, CharacterLook.Pants[look.pants], null); y += 32;
        if (CrispGUI.Button(new Rect(x, y, w, 26), "สุ่มหน้าตา")) look = CharacterLook.Random();
        }
        }

        // --- play
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
        string acct = acctName ?? (SavedToken.Length > 0 ? SavedUser : "");
        if (CrispGUI.Button(new Rect(box.x + 20, box.yMax - 54, 330, 36), acct.Length > 0 ? $"เล่นออนไลน์ (บัญชี {acct})" : "เล่นออนไลน์ (เจอผู้เล่นอื่น)", btn)) PlayOnline();
        if (CrispGUI.Button(new Rect(box.x + 360, box.yMax - 54, 180, 36), "เล่นออฟไลน์", new GUIStyle(GUI.skin.button) { fontSize = 13 })) PlayOffline();
        GUI.enabled = true;

        var status = new GUIStyle(smallCenter) { wordWrap = true };
        ShadowLabel(new Rect(box.x, box.yMax + 6, box.width, 34), netStatus, status, new Color(1f, 0.9f, 0.6f));
        if (connecting && CrispGUI.Button(new Rect(W / 2f - 60, box.yMax + 40, 120, 24), "ยกเลิก")) PlayOffline();

        ShadowLabel(new Rect(0, H - 22, W, 18), "ไม่ล็อกอิน: เซฟอยู่ในเบราว์เซอร์นี้เท่านั้น  ·  ล็อกอิน: เล่นตัวเดิมได้จากทุกเครื่อง", smallCenter, new Color(1f, 1f, 1f, 0.6f));
        string built = BuildTimeText();
        if (built != null) ShadowLabel(new Rect(14, 8, 320, 18), built, small, new Color(1f, 1f, 1f, 0.7f)); // top left, clear of the sound button
    }

    static string buildTimeText;
    static readonly string[] ThaiMonths = { "ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.", "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค." };

    /// "อัปเดตล่าสุด ..." from the build stamp (Resources/BuildVersion.txt, UTC yyyyMMddHHmmss),
    /// shown in Thai time (UTC+7) - the web player often has no time zone of its own.
    static string BuildTimeText()
    {
        if (buildTimeText != null) return buildTimeText.Length > 0 ? buildTimeText : null;
        buildTimeText = "";
        var asset = Resources.Load<TextAsset>("BuildVersion");
        string v = asset != null ? asset.text.Trim() : "";
        if (System.DateTime.TryParseExact(v, "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var utc))
        {
            var th = utc.AddHours(7);
            buildTimeText = $"อัปเดตล่าสุด {th.Day} {ThaiMonths[th.Month - 1]} {th.Year + 543} เวลา {th:HH:mm} น.";
        }
        return buildTimeText.Length > 0 ? buildTimeText : null;
    }

    /// The ready-made characters as cards in a row (Presets[1..]): each shows the character and
    /// its name, the picked one walks and has a gold frame. Tap / click a card to pick it.
    void DrawCharacterCards(float x, float y, float w, float h)
    {
        int n = CharacterLook.Presets.Length - 1;
        const float gap = 6f;
        float cw = (w - gap * (n - 1)) / n;
        var name = new GUIStyle(smallCenter) { wordWrap = true, fontSize = 11 };
        Vector2 mouse = Event.current.mousePosition;
        for (int i = 1; i <= n; i++)
        {
            var r = new Rect(x + (i - 1) * (cw + gap), y, cw, h);
            bool picked = look.preset == i, hover = GUI.enabled && r.Contains(mouse);
            Fill(r, picked ? new Color(1f, 0.85f, 0.3f, 0.95f) : hover ? new Color(1f, 1f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.15f));
            var inner = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
            Fill(inner, picked ? new Color(0.25f, 0.45f, 0.25f, 1f) : new Color(0.16f, 0.26f, 0.16f, 1f));
            int code = new CharacterLook { preset = i, gender = 1, style = 1 }.Code;
            int frame = picked ? 1 + (int)(Time.time * 8f) % Mathf.Max(1, PixelArt.WalkFrames(code)) : 0;
            var sprite = PixelArt.Player(0, frame, null, null, code);
            if (sprite != null) Icon(new Rect(inner.x + 4, inner.y + 3, inner.width - 8, inner.height - 34), sprite);
            ShadowLabel(new Rect(inner.x, inner.yMax - 30, inner.width, 28), CharacterLook.Presets[i], name,
                picked ? new Color(1f, 0.9f, 0.55f) : new Color(1f, 1f, 1f, 0.8f));
            if (CrispGUI.Button(r, GUIContent.none, GUIStyle.none) && !picked)
            {
                look = new CharacterLook { preset = i, gender = 1, style = 1 };
                Sfx.Play("pickup", 0.3f);
            }
        }
    }

    /// One "< value >" row of the character creator. Shows a colour swatch or a text value.
    int Chooser(float x, float y, float w, string name, int value, int count, string swatchHex, string text)
    {
        ShadowLabel(new Rect(x, y + 4, 100, 20), name, small);
        float cx = x + 110;
        if (CrispGUI.Button(new Rect(cx, y, 32, 26), "<")) value = (value + count - 1) % count;
        var mid = new Rect(cx + 38, y, w - 110 - 76, 26);
        if (swatchHex != null)
        {
            ColorUtility.TryParseHtmlString(swatchHex, out var col);
            Fill(mid, new Color(0f, 0f, 0f, 0.5f));
            Fill(new Rect(mid.x + 3, mid.y + 3, mid.width - 6, mid.height - 6), col);
        }
        else ShadowLabel(mid, text, labelCenter);
        if (CrispGUI.Button(new Rect(mid.xMax + 6, y, 32, 26), ">")) value = (value + 1) % count;
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
            if (r.downed) DrawReviveTag(r, gp); // knocked out: a friend can bring them back (IslandGame.Revive.cs)
            else if (Time.time < r.chatUntil) ChatBubble(gp, r.chat);
        }
        if (Time.time < myChatUntil && player != null) ChatBubble(ToGui(player.transform.position + Vector3.up * 0.85f, s), myChat);
    }

    static Texture2D bubbleTex, bubbleTailTex;
    static GUIStyle bubbleBox, bubbleShadow;
    static readonly Color BubbleFill = new Color32(255, 250, 238, 255), BubbleEdge = new Color32(70, 48, 30, 255);

    /// Speech bubble over a player's head: rounded cream box with a dark outline, a soft
    /// shadow and a little tail pointing down at the speaker. gp = top of the name tag.
    void ChatBubble(Vector2 gp, string text)
    {
        if (bubbleTex == null) MakeBubbleTextures();
        var sticker = StickerOf(text); // a sticker: the picture in the bubble instead of words (IslandGame.Sticker.cs)
        if (sticker != null)
        {
            const float s = 74f;
            var sr = new Rect(Mathf.Round(gp.x - s / 2f), Mathf.Round(gp.y - 27f - s), s, s);
            GUI.Box(new Rect(sr.x + 2f, sr.y + 3f, sr.width, sr.height), GUIContent.none, bubbleShadow);
            GUI.Box(sr, GUIContent.none, bubbleBox);
            GUI.DrawTexture(new Rect(Mathf.Round(gp.x) - 6f, sr.yMax - 2f, 12f, 9f), bubbleTailTex);
            GUI.DrawTexture(new Rect(sr.x + 4f, sr.y + 4f, s - 8f, s - 8f), sticker, ScaleMode.ScaleToFit);
            return;
        }
        var content = new GUIContent(text);
        var st = new GUIStyle(small) { wordWrap = true, alignment = TextAnchor.MiddleCenter, fontSize = 12, richText = false };
        st.normal.textColor = new Color32(45, 30, 20, 255);
        const float padX = 9f, padY = 5f, maxW = 190f;
        float textW = Mathf.Min(maxW - padX * 2f, st.CalcSize(content).x + 2f);
        float w = Mathf.Max(34f, textW + padX * 2f);
        float h = st.CalcHeight(content, textW) + padY * 2f;
        var r = new Rect(Mathf.Round(gp.x - w / 2f), Mathf.Round(gp.y - 27f - h), Mathf.Round(w), Mathf.Round(h));

        GUI.Box(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), GUIContent.none, bubbleShadow);       // shadow
        GUI.Box(r, GUIContent.none, bubbleBox);                                                       // body
        GUI.DrawTexture(new Rect(Mathf.Round(gp.x) - 6f, r.yMax - 2f, 12f, 9f), bubbleTailTex);       // tail
        CrispGUI.Label(new Rect(r.x + padX, r.y + padY, textW, r.height - padY * 2f), content, st);
    }

    /// The bubble's 9-sliced rounded box (and its shadow) and the tail, drawn once.
    static void MakeBubbleTextures()
    {
        const int n = 24; const float rad = 7.5f, edge = 1.6f;
        bubbleTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var shadowTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // signed distance to a rounded square filling the texture (negative inside)
                float qx = Mathf.Abs(x + 0.5f - n / 2f) - (n / 2f - rad), qy = Mathf.Abs(y + 0.5f - n / 2f) - (n / 2f - rad);
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - rad;
                float a = Mathf.Clamp01(0.5f - d);                        // inside the shape
                float inner = Mathf.Clamp01(0.5f - (d + edge));           // inside the outline
                var c = Color.Lerp(BubbleEdge, BubbleFill, inner); c.a = a;
                bubbleTex.SetPixel(x, y, c);
                shadowTex.SetPixel(x, y, new Color(0f, 0f, 0f, a * 0.3f));
            }
        bubbleTex.Apply(); shadowTex.Apply();
        var border = new RectOffset(10, 10, 10, 10);
        bubbleBox = new GUIStyle { border = border }; bubbleBox.normal.background = bubbleTex;
        bubbleShadow = new GUIStyle { border = border }; bubbleShadow.normal.background = shadowTex;

        // tail: a downward triangle, outlined on its two slanted sides, open at the top
        const int tw = 24, th = 18;
        bubbleTailTex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                float fromTop = th - 1 - y + 0.5f;                       // texture y grows upwards
                float half = (tw / 2f) * (1f - fromTop / th);            // half width at this row
                float dx = Mathf.Abs(x + 0.5f - tw / 2f);
                float d = (dx - half) * 0.85f;                           // ~distance to the slanted sides
                float a = Mathf.Clamp01(0.5f - d);
                float inner = Mathf.Clamp01(0.5f - (d + edge * 2f));
                var c = Color.Lerp(BubbleEdge, BubbleFill, inner); c.a = a;
                bubbleTailTex.SetPixel(x, y, c);
            }
        bubbleTailTex.Apply();
    }

    void DrawOnlineInfo(float W, float H)
    {
        string state = !Online ? "ออฟไลน์"
                     : voyage != null ? "ออนไลน์ - กลางทะเล"
                     : !inRoom ? "ออนไลน์ - กำลังเข้าเกาะ..."
                     : $"ออนไลน์ - บนเกาะนี้ {remotes.Count + 1} คน{(IsHost && remotes.Count > 0 ? " (โฮสต์)" : "")}";
        ShadowLabel(new Rect(W - 330, 30, 316, 18), state, new GUIStyle(small) { alignment = TextAnchor.UpperRight },
            Online ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 1f, 1f, 0.5f));

        DrawChat(W, H); // chat log + typing box, laid out for mouse or touch (IslandGame.ChatMobile.cs)
    }

    static string ClothingBonus(Item it) => it switch
    {
        Item.StrawHat => "หิวน้ำ -15%",
        Item.TribalHeadband => "ดาเมจ +1",
        Item.LeafTunic => "พลังชีวิตสูงสุด +15",
        Item.HideVest => "รับดาเมจ -20%",
        Item.CrystalArmor => "รับดาเมจ -35%, พลังชีวิต +20",
        Item.StormHood => "โจมตีเร็ว +20%, ดาเมจ +1",
        _ => "",
    };

    /// Conversation box at the bottom of the screen.
    void DrawDialog(float W, float H)
    {
        var d = dialog;
        if (d.shop != null) { DrawShop(W, H, d); return; } // market stalls (IslandGame.Shop.cs)
        if (d.chart) { DrawChart(W, H); return; } // the sea chart (IslandGame.Chart.cs)
        float pw = Mathf.Min(620f, W - 20f);
        float optH = d.icons != null ? 40f : 26f;
        var textStyle = new GUIStyle(small) { wordWrap = true, fontSize = 12, richText = true };
        // the box grows with the text (quest objectives can take a few lines)
        float textH = Mathf.Max(40f, textStyle.CalcHeight(new GUIContent(d.text), pw - 28f) + 6f);
        float ph = 38f + textH + d.options.Count * (optH + 3f) + 6f;
        var box = new Rect(W / 2f - pw / 2f, Mathf.Max(8f, H - ph - 92f), pw, ph);
        Fill(box, new Color(0.08f, 0.06f, 0.04f, 0.88f));
        Fill(new Rect(box.x, box.y, box.width, 2), new Color(1f, 0.85f, 0.3f, 0.8f));
        ShadowLabel(new Rect(box.x + 14, box.y + 8, pw - 28, 20), d.speaker, label, new Color(1f, 0.85f, 0.3f));
        CrispGUI.Label(new Rect(box.x + 14, box.y + 30, pw - 28, textH), d.text, textStyle);
        if (renamePet >= 0) { DrawRenameField(new Rect(box.x + 16, box.y + 30 + textH - 30, Mathf.Min(320f, pw - 32f), 22f)); if (dialog != d) return; } // naming a pet (IslandGame.Pet.cs)
        float optY = box.y + 32f + textH;
        for (int i = 0; i < d.options.Count; i++)
        {
            var o = d.options[i];
            GUI.enabled = o.enabled;
            var r = new Rect(box.x + 14, optY + i * (optH + 3f), pw - 28, optH);
            if (CrispGUI.Button(r, $"{i + 1}.  {o.label}", new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 12 })) o.act();
            GUI.enabled = true;
            if (d.icons != null && i < d.icons.Length && d.icons[i] != null) Icon(new Rect(r.xMax - 70f, r.y + 2f, 64f, optH - 4f), d.icons[i]);
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
        Icon(new Rect(bar.x + bar.width * VoyageProgress - 10f, bar.y - 18f, 20f, 20f), BoatSprite);
        ShadowLabel(new Rect(bar.x - 70, bar.y - 6, 66, 20), atHome ? "บ้าน" : $"เกาะ {island}", new GUIStyle(small) { alignment = TextAnchor.MiddleRight });
        ShadowLabel(new Rect(bar.xMax + 6, bar.y - 6, 120, 20), voyageVisit != null ? $"บ้าน{visitNameOf(voyageVisit)}" : $"เกาะ {destIsland}", small);
        float left = Mathf.Max(0f, voyage.length - voyage.raft.x);
        ShadowLabel(new Rect(bar.x, bar.y + 10, bar.width, 18), $"เหลืออีก {left * 10f:0} ม.", smallCenter);

        DrawNavyBars(W, voyage.monster != null ? 100f : 66f); // government warships (IslandGame.Bounty.cs)
        var m = voyage.monster; // the sea monster's health, boss style
        if (m == null) return;
        var hb = new Rect(W / 2f - 140f, 66f, 280f, 10f);
        ShadowLabel(new Rect(hb.x, hb.y - 18, hb.width, 18), m.Hittable ? m.DisplayName : $"{m.DisplayName} (ดำน้ำอยู่)", smallCenter, new Color(1f, 0.75f, 0.7f));
        Fill(new Rect(hb.x - 2, hb.y - 2, hb.width + 4, hb.height + 4), new Color(0f, 0f, 0f, 0.6f));
        Fill(new Rect(hb.x, hb.y, hb.width * Mathf.Clamp01((float)m.hp / m.maxHp), hb.height), m.Hittable ? new Color(0.85f, 0.25f, 0.3f) : new Color(0.45f, 0.3f, 0.35f));
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
            CrispGUI.Label(new Rect(row.x + 8, row.y + 20, 290, 18), $"<color=#cccccc>{info.desc}</color>", small);

            var pips = new System.Text.StringBuilder();
            for (int p = 0; p < info.max; p++) pips.Append(p < lv ? "<color=#ffd84d>■</color>" : "<color=#555555>■</color>");
            CrispGUI.Label(new Rect(row.x + 210, row.y + 3, 100, 18), pips.ToString(), label);

            GUI.enabled = skillPoints > 0 && lv < info.max;
            if (CrispGUI.Button(new Rect(row.xMax - 46, row.y + 8, 40, 24), lv >= info.max ? "เต็ม" : "+")) UpgradeSkill((Skill)i);
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
        else if (fishState != 0) { text = fishState == 2 ? "ปลากินเบ็ด! กดเลย!" : "รอปลากินเบ็ด... (E เก็บสาย)"; at = PlayerPos + Vector3.up * 1.1f; }
        else if (enemyTarget != null)
        {
            text = $"โจมตี{enemyTarget.DisplayName} (ดาเมจ {WeaponDamage})";
            if (CatchTarget(enemyTarget) && Count(Item.PetNet) > 0 && enemyTarget.hp * 2 <= enemyTarget.maxHp) text += "  [P] จับมาเลี้ยง!"; // IslandGame.Pet.cs
            at = enemyTarget.transform.position + Vector3.up * 0.6f;
        }
        else if (villagerTarget != null) { text = $"คุยกับ{villagerTarget.Title}"; at = villagerTarget.transform.position + Vector3.up * 0.95f; }
        else if (target != null) { text = visiting && !VisitAllowed(target.kind) ? $"ของ{visitName} (ดูได้อย่างเดียว)" : Prompt(target); at = target.transform.position + Vector3.up * target.PromptHeight; }
        else if (pond != null) { text = "ดื่มน้ำ"; at = GridToWorld(pond.Value) + Vector3.up * 0.3f; }
        else if (seaSpot != null && HasRod) { text = "ตกปลา (เบ็ด)"; at = GridToWorld(seaSpot.Value) + Vector3.up * 0.3f; }
        else if (seaSpot != null && HasSpear) { text = "แทงปลา"; at = GridToWorld(seaSpot.Value) + Vector3.up * 0.3f; }
        if (string.IsNullOrEmpty(text)) { DrawNearMiss(s); return; } // something usable just out of reach (IslandGame.Feel.cs)
        var gp = ToGui(at, s);
        string note = enemyTarget == null && villagerTarget == null && target != null && voyage == null && fishState == 0 ? TargetNote(target) : null;
        DrawKeyTag(gp, text, note, 1f, !touchUsed);
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
            if (FoodValue(it) > 0 && CrispGUI.Button(r, GUIContent.none, GUIStyle.none)) Eat(it);
            else if (it == Item.Antidote && CrispGUI.Button(r, GUIContent.none, GUIStyle.none)) UseAntidote();
            else if (IsPowerFruit(it) && CrispGUI.Button(r, GUIContent.none, GUIStyle.none)) EatPowerFruit(it);
            else if (IsClothing(it) && CrispGUI.Button(r, GUIContent.none, GUIStyle.none)) ToggleWear(it);
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
        if (benchOpen && !inHouse) { craftOpen = false; benchOpen = false; return; } // the home list only works inside your house
        const float pw = 360f, rowH = 52f, headH = 22f, top = 64f;
        var view = CraftView(); // the workbench has its own list (IslandGame.House.cs)
        var tabs = CraftTabs;
        int tab = Mathf.Clamp(CurrentTab, 0, tabs.Length - 1);
        // rows plus a header wherever the category changes (only while showing every tab)
        float contentH = 0f;
        for (int i = 0; i < view.Count; i++) contentH += (tab == 0 && (i == 0 || RecipeCat(view[i]) != RecipeCat(view[i - 1])) ? headH : 0f) + rowH + 4f;
        if (view.Count == 0) contentH = 40f;
        const float headerH = 86f;
        var panel = new Rect(W - pw - 14, top, pw, Mathf.Min(H - top - 130f, contentH + headerH + 6f));
        Color accent = benchOpen ? new Color(1f, 0.78f, 0.45f) : new Color(0.6f, 0.95f, 1f);
        GUI.Box(panel, GUIContent.none);
        GUI.Box(panel, GUIContent.none);
        Fill(new Rect(panel.x, panel.y, panel.width, 3), accent);
        ShadowLabel(new Rect(panel.x + 10, panel.y + 6, pw, 22), benchOpen ? "โต๊ะช่าง · ของใช้ในบ้าน" : "ประดิษฐ์ · ของผจญภัย", label, accent);
        CrispGUI.Label(new Rect(panel.x + 10, panel.y + 26, pw - 20, 16),
            benchOpen ? "<color=#cccccc>ทำได้ในบ้านเท่านั้น · [ ] เปลี่ยนหมวด · ตัวเลขสร้าง</color>"
                      : "<color=#cccccc>ของใช้ในบ้านทำที่โต๊ะช่างในบ้าน · [ ] เปลี่ยนหมวด</color>", small);
        var fire = benchOpen ? null : FireInReach();
        if (fire != null && CrispGUI.Button(new Rect(panel.xMax - 92, panel.y + 5, 84, 22), "ดับกองไฟ (X)", new GUIStyle(GUI.skin.button) { fontSize = 11 })) { PutOutFire(fire); return; }

        // category tabs
        float tx = panel.x + 6, ty = panel.y + 46, tw = (pw - 12f) / tabs.Length;
        var tabStyle = new GUIStyle(GUI.skin.button) { fontSize = 11, richText = true, padding = new RectOffset(1, 1, 2, 2) };
        for (int t = 0; t < tabs.Length; t++)
        {
            bool on = t == tab;
            if (on) Fill(new Rect(tx + t * tw, ty, tw - 2, 28), new Color(accent.r, accent.g, accent.b, 0.35f));
            if (CrispGUI.Button(new Rect(tx + t * tw, ty, tw - 2, 28), on ? $"<b>{tabs[t]}</b>" : tabs[t], tabStyle)) { CurrentTab = t; craftScroll = Vector2.zero; return; }
        }

        var viewRect = new Rect(panel.x + 4, panel.y + headerH, pw - 8, panel.height - headerH - 4);
        var content = new Rect(0, 0, viewRect.width - 18, contentH);
        craftScroll = CrispGUI.BeginScrollView(viewRect, craftScroll, content, false, true);
        if (view.Count == 0) CrispGUI.Label(new Rect(8, 10, content.width - 16, 20), "<color=#cccccc>ยังไม่มีสูตรในหมวดนี้</color>", small);
        float y = 0f;
        for (int i = 0; i < view.Count; i++)
        {
            var r = view[i];
            if (tab == 0 && (i == 0 || RecipeCat(r) != RecipeCat(view[i - 1])))
            {
                ShadowLabel(new Rect(6, y + 2, content.width - 8, 18), $"— {RecipeCat(r)} —", small, accent);
                y += headH;
            }
            var row = new Rect(2, y, content.width - 4, rowH);
            y += rowH + 4f;
            GUI.Box(row, GUIContent.none);
            if (r.output != null && TutorialRecipe == r.output) // the first-steps guide asks for this one (IslandGame.Feel.cs)
            {
                var hl = new Color(1f, 0.85f, 0.3f, 0.55f + 0.45f * Mathf.Sin(Time.time * 6f));
                Fill(new Rect(row.x, row.y, row.width, 2), hl); Fill(new Rect(row.x, row.yMax - 2, row.width, 2), hl);
                Fill(new Rect(row.x, row.y, 2, row.height), hl); Fill(new Rect(row.xMax - 2, row.y, 2, row.height), hl);
            }
            Sprite icon = r.output != null ? PixelArt.Icon(r.output.Value)
                        : r.places == NodeKind.Shelter ? PixelArt.Shelter()
                        : r.places == NodeKind.Raft ? PixelArt.Raft(1)
                        : r.raftUpgrade ? PixelArt.Raft(raftLevel + 1)
                        : PixelArt.Campfire(0);
            Icon(new Rect(row.x + 5, row.y + 9, 32, 32), icon);
            string hotkey = i < 9 ? $"{i + 1}. " : i == 9 ? "0. " : "";
            string extra = r.count > 1 ? $" x{r.count}" : "";
            ShadowLabel(new Rect(row.x + 44, row.y + 1, 220, 20), $"{hotkey}{r.name}{extra}", label);

            var cost = new System.Text.StringBuilder();
            foreach (var c in r.cost)
            {
                if (cost.Length > 0) cost.Append(", ");
                bool enough = Count(c.item) >= c.n;
                cost.Append(enough ? "<color=#b8f0a0>" : "<color=#ff8a8a>")
                    .Append($"{c.n} {Name(c.item)} ({Count(c.item)})</color>");
            }
            CrispGUI.Label(new Rect(row.x + 44, row.y + 19, row.width - 110, 18), cost.ToString(), small);

            bool ok = CanCraft(r, out var why);
            string sub = !ok && why != MissingMaterials ? $"<color=#ffcc66>{why}</color>" : $"<color=#cccccc>{r.desc}</color>";
            CrispGUI.Label(new Rect(row.x + 44, row.y + 34, row.width - 110, 18), sub, small);

            GUI.enabled = ok;
            if (CrispGUI.Button(new Rect(row.xMax - 60, row.y + 13, 54, 26), "สร้าง")) Craft(r);
            GUI.enabled = true;
        }
        CrispGUI.EndScrollView();
    }
}
