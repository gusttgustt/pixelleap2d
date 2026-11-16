using UnityEngine;

/// Clear warnings when the character is in trouble: very low health, very hungry or very
/// thirsty. A pulsing red edge around the screen (low health), a banner at the top that says
/// what to do, a warning beep the moment you cross into danger, and a heartbeat when health
/// is nearly gone.
public partial class IslandGame
{
    const float LowHealthFrac = 0.3f, CriticalHealthFrac = 0.15f, LowNeed = 20f;
    bool warnedHp, warnedHunger, warnedThirst;
    float heartT;

    bool LowHealth => health < MaxHealth * LowHealthFrac;
    bool CriticalHealth => health < MaxHealth * CriticalHealthFrac;
    bool WarningsShown => !title && !dead && player != null && sleepT <= 0f && sailT <= 0f;

    void UpdateWarnings(float dt)
    {
        if (!WarningsShown) { heartT = 0f; return; }
        // a beep once when you cross into danger (and again only after you've recovered)
        bool beep = false;
        if (LowHealth && !warnedHp) { warnedHp = beep = true; }
        else if (health > MaxHealth * (LowHealthFrac + 0.1f)) warnedHp = false;
        if (hunger < LowNeed && !warnedHunger) { warnedHunger = beep = true; }
        else if (hunger > LowNeed + 10f) warnedHunger = false;
        if (thirst < LowNeed && !warnedThirst) { warnedThirst = beep = true; }
        else if (thirst > LowNeed + 10f) warnedThirst = false;
        if (beep) Sfx.Play("warn", 0.55f);

        // heartbeat: faster the closer you are to dying
        if (!CriticalHealth) { heartT = 0f; return; }
        heartT -= dt;
        if (heartT > 0f) return;
        heartT = Mathf.Lerp(0.55f, 1.1f, health / (MaxHealth * CriticalHealthFrac));
        Sfx.Play("heartbeat", 0.7f);
    }

    void DrawWarnings(float W, float H)
    {
        if (!WarningsShown) return;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (CriticalHealth ? 9f : 5f));

        // red glow around the edge of the screen
        if (LowHealth)
        {
            float strength = Mathf.InverseLerp(MaxHealth * LowHealthFrac, 0f, health); // 0 at the threshold, 1 at zero
            float a = (0.18f + 0.32f * strength) * (0.55f + 0.45f * pulse);
            Vignette(W, H, new Color(0.85f, 0f, 0f), a, 26f + 40f * strength);
        }
        else if (hunger <= 0f || thirst <= 0f) // starving / parched: health is draining
            Vignette(W, H, new Color(0.9f, 0.45f, 0f), 0.22f * (0.55f + 0.45f * pulse), 30f);

        // banners at the top middle: what's wrong and what to do about it
        float y = 14f;
        if (LowHealth)
            y = WarnBanner(W, y, CriticalHealth ? "!! เลือดใกล้หมด !!  หนีจากศัตรู แล้วกินอาหารปิ้งหรือน้ำผึ้ง (F)"
                                                : "! เลือดน้อย !  ถอยออกมา กินอาหารปิ้งหรือน้ำผึ้ง (F) หรือนอนพัก",
                new Color(1f, 0.35f, 0.35f), pulse);
        if (hunger < LowNeed)
            y = WarnBanner(W, y, hunger <= 0f ? "! อดอาหาร - เลือดกำลังลด !  กด F กินอาหาร"
                                              : "! หิวมาก !  กด F กินอาหาร หรือไปเก็บเบอร์รี่ ผลไม้",
                new Color(1f, 0.65f, 0.25f), pulse);
        if (thirst < LowNeed)
            WarnBanner(W, y, thirst <= 0f ? "! ขาดน้ำ - เลือดกำลังลด !  ไปดื่มน้ำ" + (Count(Item.WaterFlask) > 0 && flaskWater > 0 ? " (Q)" : "")
                                          : "! กระหายน้ำมาก !  ไปดื่มน้ำ" + (Count(Item.WaterFlask) > 0 && flaskWater > 0 ? " หรือกด Q" : ""),
                new Color(0.45f, 0.75f, 1f), pulse);
    }

    float WarnBanner(float W, float y, string text, Color col, float pulse)
    {
        var r = new Rect(W / 2f - 230f, y, 460f, 26f);
        Fill(r, new Color(0f, 0f, 0f, 0.6f));
        Fill(new Rect(r.x, r.y, 4f, r.height), col);
        Fill(new Rect(r.xMax - 4f, r.y, 4f, r.height), col);
        ShadowLabel(r, text, labelCenter, Color.Lerp(col, Color.white, 0.35f * pulse));
        return y + 30f;
    }

    /// Soft coloured edges: several thin bands fading in towards the middle.
    static void Vignette(float W, float H, Color c, float alpha, float depth)
    {
        const int bands = 8;
        float step = depth / bands;
        for (int i = 0; i < bands; i++)
        {
            var col = new Color(c.r, c.g, c.b, alpha * (1f - i / (float)bands));
            float o = i * step;
            Fill(new Rect(o, o, W - 2f * o, step), col);                     // top
            Fill(new Rect(o, H - o - step, W - 2f * o, step), col);          // bottom
            Fill(new Rect(o, o + step, step, H - 2f * o - 2f * step), col);  // left
            Fill(new Rect(W - o - step, o + step, step, H - 2f * o - 2f * step), col); // right
        }
    }
}
