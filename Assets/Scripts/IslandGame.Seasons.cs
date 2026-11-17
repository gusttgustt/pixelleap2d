using UnityEngine;

/// Seasons and changing weather. The year turns through spring, summer, autumn and winter, a few
/// days each; every half day the sky picks new weather from what fits the continent you're on and
/// the season: monsoon storms on the tropical continent in autumn, blizzards on the ice continent
/// in winter, ash falling from the volcano in summer... Each kind of weather has its own particles,
/// colour wash and light, so you can tell them apart at a glance.
public partial class IslandGame
{
    public enum Season { Spring, Summer, Autumn, Winter }
    const int SeasonDays = 3;
    public Season CurSeason => (Season)(((Day - 1) / SeasonDays) % 4);
    int SeasonDay => (Day - 1) % SeasonDays + 1;
    static readonly string[] SeasonNames = { "ฤดูใบไม้ผลิ", "ฤดูร้อน", "ฤดูใบไม้ร่วง", "ฤดูหนาว" };
    static readonly string[] SeasonColors = { "#ffb6d9", "#ffd25a", "#ff9e4a", "#bfe4ff" };
    /// The light of each season: pinkish spring, warm summer, amber autumn, pale cold winter.
    static readonly Color[] SeasonTint =
    {
        new Color(1f, 0.96f, 0.98f), new Color(1f, 0.97f, 0.88f), new Color(1f, 0.88f, 0.74f), new Color(0.86f, 0.93f, 1f),
    };

    Weather nowWeather = Weather.Clear;
    float weatherK = 1f;      // 0 -> 1 while new weather fades in
    float flashT;             // lightning flash on the screen
    float thunderTimer = 4f;
    Season lastSeason = (Season)(-1);

    /// The weather that can come, by continent then season, with weights.
    static readonly (Weather w, int weight)[][][] WeatherTable =
    {
        new[] // tropical: blossoms, scorching summer, monsoon autumn, mild dry winter
        {
            new[] { (Weather.Clear, 3), (Weather.Petals, 3), (Weather.Rain, 1), (Weather.Fireflies, 1) },
            new[] { (Weather.Clear, 2), (Weather.Heat, 4), (Weather.Thunder, 1) },
            new[] { (Weather.Rain, 3), (Weather.Thunder, 3), (Weather.Leaves, 1), (Weather.Clear, 1) },
            new[] { (Weather.Clear, 3), (Weather.Leaves, 2), (Weather.Fog, 1), (Weather.Fireflies, 1) },
        },
        new[] // ice: it snows most of the year, blizzards in winter
        {
            new[] { (Weather.Clear, 2), (Weather.Fog, 2), (Weather.Snow, 2) },
            new[] { (Weather.Clear, 4), (Weather.Fog, 1), (Weather.Snow, 1) },
            new[] { (Weather.Snow, 3), (Weather.Fog, 2), (Weather.Clear, 1) },
            new[] { (Weather.Blizzard, 3), (Weather.Snow, 3), (Weather.Fog, 1) },
        },
        new[] // volcano: ash and heat, thunderstorms over the crater
        {
            new[] { (Weather.Clear, 2), (Weather.Ash, 2), (Weather.Rain, 1) },
            new[] { (Weather.Heat, 3), (Weather.Ash, 3), (Weather.Clear, 1) },
            new[] { (Weather.Ash, 2), (Weather.Thunder, 2), (Weather.Fog, 1) },
            new[] { (Weather.Ash, 2), (Weather.Rain, 2), (Weather.Fog, 1), (Weather.Clear, 1) },
        },
        new[] // sky and deep sea: windy and changeable
        {
            new[] { (Weather.Clear, 3), (Weather.Petals, 1), (Weather.Fog, 1) },
            new[] { (Weather.Clear, 3), (Weather.Heat, 1), (Weather.Thunder, 1) },
            new[] { (Weather.Fog, 2), (Weather.Rain, 2), (Weather.Leaves, 1) },
            new[] { (Weather.Snow, 2), (Weather.Fog, 2), (Weather.Clear, 1) },
        },
    };

    int ClimateContinent => atHome || visiting ? homeContinent : continent;

    /// The weather for this half day: the same for the same island, continent, season and time.
    Weather PickWeather()
    {
        bool underSea = voyage == null && biome is Biome.Bubble or Biome.Sunken;
        int slot = Mathf.FloorToInt(clock / (DayLength * 0.5f));
        if (!atHome && !visiting && biome == Biome.Storm && voyage == null) return slot % 3 == 0 ? Weather.Rain : Weather.Thunder;
        int c = Mathf.Clamp(ClimateContinent, 0, Continents - 1);
        var r = new System.Random(unchecked(seed * 31 + island * 977 + c * 7919 + slot * 104729));
        var table = WeatherTable[c][(int)CurSeason];
        int total = 0;
        foreach (var e in table) total += e.weight;
        int roll = r.Next(total);
        Weather w = table[0].w;
        foreach (var e in table) { if (roll < e.weight) { w = e.w; break; } roll -= e.weight; }
        if (underSea) w = w is Weather.Fog or Weather.Fireflies ? w : Weather.Clear; // no rain at the bottom of the sea
        return w;
    }

    // ------------------------------------------------------------------ what the weather does to you
    bool WeatherHere => voyage == null && !inHouse && player != null;
    /// Snow and blizzards chill you on any island: stay near a campfire (IslandGame.Ice.cs).
    bool ColdWeather => WeatherHere && !atHome && nowWeather is Weather.Snow or Weather.Blizzard;
    float WeatherCold => !WeatherHere ? 1f : nowWeather == Weather.Blizzard ? 0.5f : nowWeather == Weather.Snow ? 0.85f : 1f;
    float WeatherThirst => !WeatherHere ? 1f : nowWeather switch
    {
        Weather.Heat => hat == Item.StrawHat ? 1.3f : 1.7f, // a straw hat keeps the worst of the sun off
        Weather.Ash => 1.3f,
        Weather.Rain or Weather.Thunder => 0.5f,             // you can drink the rain
        _ => CurSeason == Season.Summer ? 1.15f : 1f,
    };
    float WeatherHunger => !WeatherHere ? 1f : nowWeather is Weather.Snow or Weather.Blizzard ? 1.3f : CurSeason == Season.Winter ? 1.15f : 1f;
    float WeatherRegen => !WeatherHere ? 1f : nowWeather switch { Weather.Petals => 3f, Weather.Ash => 0f, _ => 1f };
    /// Wading through a blizzard is slow, unless you have spiked boots.
    public float WeatherSpeed => WeatherHere && nowWeather == Weather.Blizzard && Count(Item.SpikedBoots) == 0 ? 0.8f : 1f;

    static string WeatherEffect(Weather w) => w switch
    {
        Weather.Heat => "หิวน้ำเร็วขึ้น (หมวกฟางช่วยได้)",
        Weather.Rain => "หิวน้ำช้าลง",
        Weather.Thunder => "ระวังฟ้าผ่า! หิวน้ำช้าลง",
        Weather.Snow => "หนาว อยู่ใกล้กองไฟ หิวเร็วขึ้น",
        Weather.Blizzard => "หนาวจัด เดินช้า (รองเท้าหนามช่วยได้)",
        Weather.Ash => "หิวน้ำขึ้น พลังชีวิตไม่ฟื้นเอง",
        Weather.Petals => "พลังชีวิตฟื้นเร็วขึ้น",
        _ => null,
    };

    static string WeatherLine(Weather w) => w switch
    {
        Weather.Rain => "ฝนเริ่มตกแล้ว",
        Weather.Thunder => "ฟ้าร้องครืนๆ พายุฝนฟ้าคะนองกำลังมา!",
        Weather.Snow => "หิมะเริ่มโปรยปราย",
        Weather.Blizzard => "พายุหิมะ! มองแทบไม่เห็นทาง",
        Weather.Heat => "แดดร้อนจัด อากาศระอุ",
        Weather.Petals => "กลีบดอกไม้ปลิวว่อน",
        Weather.Ash => "เถ้าภูเขาไฟร่วงลงมาจากฟ้า",
        Weather.Fog => "หมอกลงหนา",
        Weather.Leaves => "ลมพัดใบไม้ปลิว",
        _ => null,
    };

    /// How the weather changes the light of the day (on top of the time of day and the season).
    (Color, float) WeatherLight(Color col, float intensity)
    {
        col *= SeasonTint[(int)CurSeason];
        float k = WeatherFx ? weatherK : 0f;
        switch (nowWeather)
        {
            case Weather.Rain: col = Color.Lerp(col, col * new Color(0.75f, 0.8f, 0.95f), k); intensity *= Mathf.Lerp(1f, 0.78f, k); break;
            case Weather.Thunder: col = Color.Lerp(col, col * new Color(0.62f, 0.66f, 0.85f), k); intensity *= Mathf.Lerp(1f, 0.62f, k); break;
            case Weather.Snow: col = Color.Lerp(col, col * new Color(0.9f, 0.95f, 1.05f), k); intensity *= Mathf.Lerp(1f, 0.92f, k); break;
            case Weather.Blizzard: col = Color.Lerp(col, col * new Color(0.85f, 0.9f, 1f), k); intensity *= Mathf.Lerp(1f, 0.8f, k); break;
            case Weather.Heat: col = Color.Lerp(col, col * new Color(1.08f, 1f, 0.8f), k); intensity *= Mathf.Lerp(1f, 1.08f, k); break;
            case Weather.Ash: col = Color.Lerp(col, col * new Color(0.95f, 0.75f, 0.62f), k); intensity *= Mathf.Lerp(1f, 0.8f, k); break;
            case Weather.Fog: intensity *= Mathf.Lerp(1f, 0.9f, k); break;
        }
        if (flashT > 0f && WeatherFx) { col = Color.Lerp(col, Color.white, flashT); intensity = Mathf.Max(intensity, flashT * 1.3f); }
        return (col, intensity);
    }

    void UpdateWeather(float dt)
    {
        if (player == null || cam == null) return;
        var w = PickWeather();
        if (w != nowWeather)
        {
            nowWeather = w;
            weatherK = 0f;
            string line = WeatherLine(w), fx = WeatherEffect(w);
            if (line != null && !inHouse && Time.time >= hintUntil) Hint(fx != null ? $"{line} · {fx}" : line, 4f);
        }
        weatherK = Mathf.MoveTowards(weatherK, 1f, dt / 4f);
        flashT = Mathf.MoveTowards(flashT, 0f, dt * 3f);
        var season = CurSeason;
        if (season != lastSeason)
        {
            bool first = (int)lastSeason < 0;
            lastSeason = season;
            if (!first && !inHouse)
            {
                Hint($"เข้าสู่{SeasonNames[(int)season]}แล้ว", 4f);
                Popup(SeasonNames[(int)season], PlayerPos + Vector3.up * 1.4f, PixelArt.Hex(SeasonColors[(int)season]));
            }
        }
        if (voyage != null || inHouse) return;

        // spawn just above the top of the view, across its whole width
        float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
        Vector3 c = cam.transform.position;
        Vector3 Top(float above = 0.3f) => new Vector3(c.x + Random.Range(-halfW - 1f, halfW + 1f), c.y + halfH + above, 0f);
        Vector3 Anywhere() => new Vector3(c.x + Random.Range(-halfW, halfW), c.y + Random.Range(-halfH, halfH), 0f);
        float rate = WeatherFx ? dt * weatherK : 0f; // effects off: no rain, snow, ash... (the weather still counts)
        float fall = halfH * 2f + 1f; // distance to cross the screen
        switch (nowWeather)
        {
            case Weather.Rain:
            case Weather.Thunder:
            {
                float n = (nowWeather == Weather.Thunder ? 90f : 50f) * rate;
                for (int i = 0; i < Mathf.FloorToInt(n) + (Random.value < n % 1f ? 1 : 0); i++)
                {
                    float sp = Random.Range(9f, 12f);
                    var at = Top();
                    for (int k = 0; k < 3; k++) // a streak, three pixels long
                        Fx.Spark(at + new Vector3(0.15f * k / PixelArt.PPU, k / (float)PixelArt.PPU, 0f), new Vector3(-1.5f, -sp, 0f), new Color(0.7f, 0.82f, 1f, 0.85f - k * 0.2f), fall / sp, 1f);
                }
                if (nowWeather == Weather.Thunder)
                {
                    if (strikeT > 0f)
                    {
                        strikeT -= dt;
                        if (strikeT <= 0f) LightningStrike();
                    }
                    thunderTimer -= dt;
                    if (thunderTimer <= 0f && !atHome && !visiting && strikeT <= 0f && Random.value < 0.45f)
                    {
                        // lightning picks a spot near you: a ring closes in on it, then it hits
                        thunderTimer = Random.Range(9f, 16f);
                        strikeAt = PlayerPos + (Vector3)(Random.insideUnitCircle * 1.6f);
                        strikeT = 1.1f;
                        Fx.Radial(strikeAt, new Color(1f, 0.95f, 0.5f), 14, -1.2f, 1.1f, 1f, 0.5f);
                    }
                    else if (thunderTimer <= 0f && !WeatherFx) thunderTimer = Random.Range(5f, 11f); // just thunder you can't see
                    else if (thunderTimer <= 0f)
                    {
                        thunderTimer = Random.Range(5f, 11f);
                        flashT = 0.8f;
                        var a = new Vector3(c.x + Random.Range(-halfW, halfW), c.y + halfH + 0.5f, 0f);
                        Fx.Bolt(a, a + new Vector3(Random.Range(-1.5f, 1.5f), -Random.Range(halfH * 0.8f, halfH * 1.6f), 0f), new Color(0.85f, 0.9f, 1f), 1.5f, 0.3f, 2, 0.35f);
                        Shake(0.12f, 0.3f);
                    }
                }
                break;
            }
            case Weather.Snow:
            case Weather.Blizzard:
            {
                bool bliz = nowWeather == Weather.Blizzard;
                float n = (bliz ? 70f : 16f) * rate;
                for (int i = 0; i < Mathf.FloorToInt(n) + (Random.value < n % 1f ? 1 : 0); i++)
                {
                    float sp = bliz ? Random.Range(2.5f, 4f) : Random.Range(0.7f, 1.3f);
                    float wind = bliz ? -Random.Range(3f, 5f) : Random.Range(-0.4f, 0.2f);
                    var at = bliz ? new Vector3(c.x + halfW + Random.Range(0f, 2f), c.y + Random.Range(-halfH, halfH + 2f), 0f) : Top();
                    Fx.Spark(at, new Vector3(wind, -sp, 0f), new Color(1f, 1f, 1f, 0.9f), bliz ? (halfW * 2f + 3f) / -wind : fall / sp, Random.value < 0.25f ? 2f : 1f);
                }
                break;
            }
            case Weather.Ash:
            {
                float n = 14f * rate;
                for (int i = 0; i < Mathf.FloorToInt(n) + (Random.value < n % 1f ? 1 : 0); i++)
                {
                    float sp = Random.Range(0.8f, 1.5f);
                    bool ember = Random.value < 0.12f;
                    Fx.Spark(Top(), new Vector3(Random.Range(-0.6f, 0.3f), -sp, 0f), ember ? new Color(1f, 0.55f, 0.2f) : new Color(0.32f, 0.3f, 0.3f, 0.85f), fall / sp, Random.value < 0.3f ? 2f : 1f);
                }
                break;
            }
            case Weather.Petals:
            case Weather.Leaves:
            {
                bool petals = nowWeather == Weather.Petals;
                float n = 7f * rate;
                for (int i = 0; i < Mathf.FloorToInt(n) + (Random.value < n % 1f ? 1 : 0); i++)
                {
                    Color col = petals ? (Random.value < 0.6f ? new Color(1f, 0.72f, 0.85f) : new Color(1f, 0.92f, 0.96f))
                                       : Random.value < 0.33f ? new Color(0.95f, 0.55f, 0.15f) : Random.value < 0.5f ? new Color(0.85f, 0.3f, 0.15f) : new Color(0.95f, 0.8f, 0.25f);
                    float sp = Random.Range(0.6f, 1.1f);
                    Fx.Spark(Top(), new Vector3(Random.Range(0.4f, 1.2f), -sp, 0f), col, fall / sp, 2f);
                }
                break;
            }
            case Weather.Heat:
                if (Random.value < rate * 6f) Fx.Rise(Anywhere(), new Color(1f, 0.95f, 0.7f, 0.25f), 1, 0.5f, 0.6f, 1.5f, 2f, -0.2f); // shimmer
                break;
            case Weather.Fireflies:
                if (WeatherFx && IsNight && Random.value < dt * 6f) Fx.Burst(PlayerPos + new Vector3(Random.Range(-4f, 4f), Random.Range(-2.5f, 2.5f), 0f), new Color(0.85f, 1f, 0.4f), 1, 0.2f, 0.2f, 2f, -0.2f);
                break;
        }
    }

    float strikeT;
    Vector3 strikeAt;

    void LightningStrike()
    {
        flashT = 1f;
        Fx.Bolt(strikeAt + new Vector3(Random.Range(-0.6f, 0.6f), 6f, 0f), strikeAt, new Color(0.9f, 0.95f, 1f), 2f, 0.3f, 2, 0.4f);
        Fx.Radial(strikeAt, new Color(1f, 0.9f, 0.4f), 16, 2.5f, 0.4f);
        Shake(0.25f, 0.35f);
        Sfx.Play("hurt", 0.4f);
        if (player != null && Vector3.Distance(PlayerPos, strikeAt) < 0.7f) DamagePlayer(15f, "ฟ้าผ่า!");
    }

    void DrawWeatherOverlay(float W, float H)
    {
        if (voyage != null || inHouse || !WeatherFx) return;
        float k = weatherK;
        switch (nowWeather)
        {
            case Weather.Fog: FillOver(new Rect(0, 0, W, H), new Color(0.85f, 0.88f, 0.92f, 0.3f * k)); break;
            case Weather.Rain: FillOver(new Rect(0, 0, W, H), new Color(0.2f, 0.25f, 0.35f, 0.15f * k)); break;
            case Weather.Thunder: FillOver(new Rect(0, 0, W, H), new Color(0.1f, 0.12f, 0.22f, 0.25f * k)); break;
            case Weather.Snow: FillOver(new Rect(0, 0, W, H), new Color(0.9f, 0.95f, 1f, 0.08f * k)); break;
            case Weather.Blizzard: FillOver(new Rect(0, 0, W, H), new Color(0.92f, 0.95f, 1f, 0.32f * k)); break;
            case Weather.Heat: FillOver(new Rect(0, 0, W, H), new Color(1f, 0.85f, 0.4f, (0.09f + 0.03f * Mathf.Sin(Time.time * 2f)) * k)); break;
            case Weather.Ash: FillOver(new Rect(0, 0, W, H), new Color(0.35f, 0.18f, 0.1f, 0.18f * k)); break;
            case Weather.Petals: FillOver(new Rect(0, 0, W, H), new Color(1f, 0.75f, 0.85f, 0.05f * k)); break;
        }
        if (flashT > 0f) FillOver(new Rect(0, 0, W, H), new Color(1f, 1f, 1f, flashT * 0.55f));
    }

    // ------------------------------------------------------------------ effects on / off
    static int weatherFx = -1;
    /// Rain, snow, ash and the rest drawn on the screen (a setting kept in this browser / device).
    /// Off, the weather still happens and still counts (thirst, cold, lightning): only the show goes.
    public static bool WeatherFx
    {
        get
        {
            if (weatherFx < 0) { try { weatherFx = PlayerPrefs.GetInt("weatherFx", 1); } catch { weatherFx = 1; } }
            return weatherFx != 0;
        }
        set
        {
            weatherFx = value ? 1 : 0;
            try { PlayerPrefs.SetInt("weatherFx", weatherFx); PlayerPrefs.Save(); } catch { }
        }
    }

    void DrawWeatherFxButton(Rect r)
    {
        if (title) return;
        bool on = WeatherFx;
        var st = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold };
        st.normal.textColor = on ? new Color(0.7f, 1f, 0.7f) : new Color(1f, 0.6f, 0.55f);
        st.hover.textColor = st.normal.textColor;
        if (CrispGUI.Button(r, on ? "เอฟเฟกต์อากาศ: เปิด" : "เอฟเฟกต์อากาศ: ปิด", st))
        {
            WeatherFx = !on;
            Hint(on ? "ปิดเอฟเฟกต์สภาพอากาศแล้ว (อากาศยังมีผลกับตัวละครเหมือนเดิม)" : "เปิดเอฟเฟกต์สภาพอากาศแล้ว", 2.5f);
        }
    }

    /// Season and weather under the little sky window at the top of the screen.
    void DrawSeasonInfo(Rect r)
    {
        Fill(r, new Color(0f, 0f, 0f, 0.6f));
        var st = new GUIStyle(smallCenter) { fontSize = 12, richText = true, wordWrap = false };
        int s = (int)CurSeason;
        ShadowLabel(new Rect(r.x, r.y, r.width, 16), $"<color={SeasonColors[s]}><b>{SeasonNames[s]}</b></color> {SeasonDay}/{SeasonDays}", st);
        ShadowLabel(new Rect(r.x, r.y + 15, r.width, 16), WeatherName(nowWeather), st, new Color(0.9f, 0.95f, 1f));
        string fx = WeatherEffect(nowWeather);
        if (fx != null && WeatherHere)
        {
            var tip = new GUIStyle(smallCenter) { fontSize = 10, richText = true, wordWrap = false };
            float tw = tip.CalcSize(new GUIContent(fx)).x + 10f;
            var tr = new Rect(r.xMax + 4f, r.y + 16f, tw, 15f); // beside the weather's name
            Fill(tr, new Color(0f, 0f, 0f, 0.5f));
            ShadowLabel(tr, fx, tip, nowWeather is Weather.Petals or Weather.Rain ? new Color(0.7f, 1f, 0.7f) : new Color(1f, 0.8f, 0.5f));
        }
    }
}
