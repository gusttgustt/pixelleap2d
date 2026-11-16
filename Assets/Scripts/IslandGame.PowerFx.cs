using UnityEngine;

/// The look of the five fruit powers. Every level adds more: Lv 1-2 a clean effect, Lv 3 a
/// second layer (shockwaves, branches, after-images), Lv 4 screen flashes, Lv 5 the full show
/// with the screen shaking - and a mastered power keeps a faint glow of its colour around you.
public partial class IslandGame
{
    float shakeT, shakeDur, shakeAmp;
    float screenFlashT, screenFlashDur;
    Color screenFlashCol;
    float auraT;

    // ------------------------------------------------------------------ testing the effects
    static bool? devMode;
    /// Only in the editor, or on the web with "?dev=1" in the address.
    static bool DevMode => devMode ??= Application.isEditor || (Application.absoluteURL ?? "").Contains("dev=1");

    /// Dev keys: F1 next fruit, F2 power level up (wraps 5 -> 1), F3 creatures around you,
    /// F4 slow motion on / off (to look at the effects).
    void DevKeys(UnityEngine.InputSystem.Keyboard kb)
    {
        if (!DevMode || kb == null || player == null || chatOpen) return;
        if (kb.f1Key.wasPressedThisFrame)
        {
            power = power == Power.Bubble || power == Power.None ? Power.Dash : power + 1;
            powerCd = 0f; powerAtkCd = 0f;
            Hint($"[dev] พลัง{PowerName(power)} Lv {powerLv}", 2f);
        }
        if (kb.f2Key.wasPressedThisFrame)
        {
            if (power == Power.None) power = Power.Dash;
            powerLv = powerLv >= 5 ? 1 : powerLv + 1;
            powerXp = powerLv >= 2 ? PowerXpNeed[powerLv - 1] : 0;
            powerCd = 0f; powerAtkCd = 0f;
            Hint($"[dev] พลัง{PowerName(power)} Lv {powerLv}", 2f);
        }
        if (kb.f3Key.wasPressedThisFrame)
            for (int i = 0, n = kb.shiftKey.isPressed ? 1 : 8; i < n; i++) // shift: just one (catching pets)
            {
                float a = i * Mathf.PI * 2f / 8f;
                var g = WalkableNear(player.grid + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (1.8f + (i % 3) * 1.2f));
                var e = new GameObject("Boar").AddComponent<Enemy>();
                e.transform.SetParent(world);
                e.Init(EnemyKind.Boar, g, tier, ++enemyCounter);
                enemies.Add(e);
            }
        if (kb.f4Key.wasPressedThisFrame) Time.timeScale = Time.timeScale < 1f ? 1f : 0.12f;
        if (DevMode) { powerCd = Mathf.Min(powerCd, 0.3f); powerAtkCd = Mathf.Min(powerAtkCd, 0.3f); } // no waiting while testing
    }

    // ------------------------------------------------------------------ screen shake & flash
    public void Shake(float amp, float dur)
    {
        if (amp < shakeAmp * (shakeT / Mathf.Max(0.01f, shakeDur))) return; // a bigger shake is already going
        shakeAmp = amp; shakeDur = dur; shakeT = dur;
    }

    Vector3 ShakeOffset(float dt)
    {
        if (shakeT <= 0f) return Vector3.zero;
        shakeT -= dt;
        float k = Mathf.Clamp01(shakeT / shakeDur);
        return (Vector3)(Random.insideUnitCircle * shakeAmp * k);
    }

    public void ScreenFlash(Color c, float dur)
    {
        screenFlashCol = c; screenFlashDur = dur; screenFlashT = dur;
    }

    void DrawScreenFlash(float W, float H)
    {
        if (screenFlashT <= 0f) return;
        screenFlashT -= Time.deltaTime;
        var c = screenFlashCol;
        c.a *= Mathf.Clamp01(screenFlashT / screenFlashDur);
        Fill(new Rect(0, 0, W, H), c);
    }

    // ------------------------------------------------------------------ building blocks
    Vector3 GroundAt(Vector2 g) => GridToWorld(g) + Vector3.up * HeightAt(g);
    int GroundOrder(Vector2 g) => SortOrder(GridToWorld(g).y) - 2;

    /// A ring racing out across the ground to `radius` tiles.
    void Shockwave(Vector2 g, float radius, Color c, float dur = 0.45f, float delay = 0f, float alpha = 0.9f)
    {
        c.a = alpha;
        Fx.Wave(PixelArt.ShockRing(), GroundAt(g), c, 0.1f, radius * 0.74f, dur, GroundOrder(g), delay);
    }

    /// A ring closing in from `radius` tiles to the centre.
    void Implode(Vector2 g, float radius, Color c, float dur = 0.35f, float alpha = 0.8f)
    {
        c.a = alpha;
        Fx.Wave(PixelArt.ShockRing(), GroundAt(g), c, radius * 0.74f, 0.05f, dur, GroundOrder(g));
    }

    /// A soft glow on the ground that swells and fades.
    void GroundGlow(Vector2 g, float radius, Color c, float dur = 0.5f, float alpha = 0.7f)
    {
        c.a = alpha;
        Fx.Wave(PixelArt.GroundGlow(), GroundAt(g), c, radius * 0.7f, radius * 1.45f, dur, GroundOrder(g) - 1);
    }

    /// The big finish that grows with the level: flash from Lv 4, shaking from Lv 3.
    void PowerClimax(Color col, float weight = 1f)
    {
        if (powerLv >= 3) Shake(0.025f * powerLv * weight, 0.18f + 0.05f * powerLv);
        if (powerLv >= 4) { var f = Color.Lerp(col, Color.white, 0.4f); f.a = (powerLv >= 5 ? 0.35f : 0.2f) * weight; ScreenFlash(f, 0.25f); }
    }

    static Color Rainbow(float t) => Color.HSVToRGB(Mathf.Repeat(t, 1f), 0.45f, 1f);

    // ------------------------------------------------------------------ R powers
    /// Dash: speed lines and a trail of after-images; lightning along the path from Lv 3;
    /// at Lv 5 a thunderbolt strikes where you land.
    void DashFx(Vector2 start, float dist)
    {
        Color col = PowerColor(Power.Dash);
        Vector2 end = start + player.facing.normalized * dist;
        Vector3 a = GroundAt(start), b = GroundAt(end);
        Vector3 back = (a - b).normalized;

        // after-images of the player along the path
        var body = player.Renderer;
        if (body != null && body.sprite != null)
        {
            int ghosts = 2 + powerLv;
            for (int i = 0; i < ghosts; i++)
            {
                float k = i / (float)ghosts;
                var tint = Color.Lerp(col, Color.white, 0.3f); tint.a = 0.25f + 0.35f * k;
                Fx.Wave(body.sprite, Vector3.Lerp(a, b, k), tint, 1f, 1f, 0.2f + 0.25f * k, body.sortingOrder - 1, 0.02f * i, body.flipX);
            }
        }
        // speed lines streaming backwards
        int lines = 10 + powerLv * 6;
        for (int i = 0; i < lines; i++)
        {
            var p = Vector3.Lerp(a, b, Random.value) + Vector3.up * Random.Range(0.1f, 0.9f);
            Fx.Spark(p, back * Random.Range(2f, 4f), Color.Lerp(col, Color.white, Random.value * 0.5f), Random.Range(0.15f, 0.3f), 1f, 0f, 3f);
        }
        Fx.Radial(a + Vector3.up * 0.1f, col, 8 + powerLv * 2, 2f, 0.3f);
        if (powerLv >= 3) // crackling lightning along the way
        {
            Fx.Bolt(a + Vector3.up * 0.4f, b + Vector3.up * 0.4f, col, 0.6f + 0.2f * powerLv, 0.12f, powerLv - 2, 0.25f);
            Fx.Burst(b + Vector3.up * 0.3f, col, 6 + powerLv * 3, 1.6f, 1.6f, 0.4f, 3f, 2f);
        }
        if (powerLv >= 5) // a thunderbolt where you land
        {
            Fx.Bolt(b + Vector3.up * 4f, b + Vector3.up * 0.2f, col, 2f, 0.25f, 2, 0.3f);
            Shockwave(end, 2.2f, col, 0.4f);
            GroundGlow(end, 1.2f, col, 0.4f);
        }
        PowerClimax(col, 0.6f);
    }

    /// Shadow: purple smoke sucked into you, then you fade; a dark pulse from Lv 3, the world
    /// dims for a moment at Lv 5.
    void ShadowFx()
    {
        Color col = PowerColor(Power.Shadow), dark = new Color(0.18f, 0.08f, 0.3f);
        Vector3 at = PlayerPos;
        Fx.Radial(at + Vector3.up * 0.4f, col, 14 + powerLv * 6, -2f - 0.4f * powerLv, 0.4f, powerLv >= 3 ? 2f : 1f);
        Fx.Rise(at + Vector3.up * 0.1f, dark, 10 + powerLv * 4, 0.5f, 0.8f, 0.9f, 2f, -0.3f);
        Fx.Burst(at + Vector3.up * 0.4f, Color.Lerp(col, Color.white, 0.3f), 8 + powerLv * 3, 1f, 1.2f, 0.7f, 0.5f);
        if (powerLv >= 3) { Implode(player.grid, 2f + 0.4f * powerLv, col, 0.35f); GroundGlow(player.grid, 1f, dark, 0.8f, 0.8f); }
        if (powerLv >= 5) ScreenFlash(new Color(0.12f, 0.03f, 0.22f, 0.45f), 0.6f);
        PowerClimax(col, 0.4f);
    }

    /// Vine: a wriggling green vine shoots out; leaves burst where it grabs; Lv 5 thorns.
    void VineFx(Vector3 to)
    {
        Color col = PowerColor(Power.Vine), leaf = new Color(0.65f, 0.95f, 0.35f);
        Vector3 from = PlayerPos + Vector3.up * 0.3f;
        to += Vector3.up * 0.3f;
        Vector3 side = Vector3.Cross(to - from, Vector3.forward).normalized;
        int steps = Mathf.Max(10, Mathf.RoundToInt(Vector3.Distance(from, to) * 18f));
        float wiggle = 0.12f + 0.03f * powerLv;
        for (int i = 0; i <= steps; i++)
        {
            float k = i / (float)steps;
            var p = Vector3.Lerp(from, to, k) + side * Mathf.Sin(k * Mathf.PI * 3f) * wiggle * Mathf.Sin(k * Mathf.PI);
            Fx.Spark(p, Vector3.zero, col, 0.35f + 0.25f * k, 1f + 0.3f * powerLv);
            if (i % 4 == 0) Fx.Spark(p, new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.2f, 0.8f), 0f), leaf, 0.5f, 1f, 1.5f, 1f);
        }
        Fx.Burst(to, leaf, 6 + powerLv * 3, 1.2f, 1.4f, 0.7f, 2f, powerLv >= 3 ? 2f : 1f);
        if (powerLv >= 3) Fx.Radial(to - Vector3.up * 0.2f, col, 10 + powerLv * 2, 1.6f, 0.35f);
        if (powerLv >= 5) Fx.Rise(to - Vector3.up * 0.3f, new Color(0.35f, 0.6f, 0.2f), 14, 0.25f, 2.2f, 0.35f, 2f, 0f); // thorns
        PowerClimax(col, 0.5f);
    }

    /// Magnet: red and blue sparks spiral in from the edge of its reach.
    void MagnetFx(float range)
    {
        Color red = PowerColor(Power.Magnet), blue = new Color(0.45f, 0.65f, 1f);
        Vector3 at = PlayerPos + Vector3.up * 0.3f;
        int n = 12 + powerLv * 6;
        Fx.Radial(at, red, n, -range * 0.7f / 0.45f, 0.45f, powerLv >= 3 ? 2f : 1f);
        Fx.Radial(at, blue, n, -range * 0.55f / 0.45f, 0.45f, 1f);
        Implode(player.grid, range, red, 0.45f, 0.7f);
        if (powerLv >= 3) Implode(player.grid, range * 0.75f, blue, 0.4f, 0.6f);
        if (powerLv >= 5) GroundGlow(player.grid, 1.2f, Color.Lerp(red, blue, 0.5f), 0.5f);
        PowerClimax(red, 0.5f);
    }

    /// One pickup flying into you.
    void MagnetPullFx(Vector3 from)
    {
        Color c = Random.value < 0.5f ? PowerColor(Power.Magnet) : new Color(0.45f, 0.65f, 1f);
        Vector3 to = PlayerPos + Vector3.up * 0.3f;
        for (int i = 0; i < 2 + powerLv; i++)
            Fx.Spark(from + Vector3.up * 0.2f + (Vector3)(Random.insideUnitCircle * 0.1f), (to - from) / 0.3f, c, 0.3f, 1f + 0.2f * powerLv);
    }

    /// Bubble: the shield blows up around you with a splash of bubbles; rainbow shimmer at Lv 5.
    void BubbleFx()
    {
        Color col = PowerColor(Power.Bubble);
        Vector3 at = PlayerPos;
        Fx.Rise(at + Vector3.up * 0.2f, Color.Lerp(col, Color.white, 0.4f), 10 + powerLv * 5, 0.6f, 1f, 1f, 2f, -0.6f);
        Fx.Radial(at + Vector3.up * 0.4f, col, 10 + powerLv * 3, 1.5f, 0.4f);
        Shockwave(player.grid, 1.4f + 0.2f * powerLv, col, 0.4f, 0f, 0.7f);
        if (powerLv >= 3) Shockwave(player.grid, 1f + 0.2f * powerLv, Color.white, 0.4f, 0.1f, 0.5f);
        if (powerLv >= 5)
            for (int i = 0; i < 14; i++)
                Fx.Spark(at + Vector3.up * 0.4f + (Vector3)(Random.insideUnitCircle * 0.5f), new Vector3(0f, 0.6f, 0f), Rainbow(i / 14f), 0.8f, 2f);
        PowerClimax(col, 0.3f);
    }

    /// While a power is running (and always, faintly, once it's mastered): its colour around you.
    void PowerAura(float dt)
    {
        if (player == null || dead || power == Power.None) return;
        float rate = Invisible ? 6f + 3f * powerLv : Bubbled ? 5f + 3f * powerLv : powerLv >= 5 ? 4f : 0f;
        if (rate <= 0f) return;
        auraT -= dt;
        if (auraT > 0f) return;
        auraT = 1f / rate;
        Vector3 at = PlayerPos;
        if (Invisible)
            Fx.Rise(at + Vector3.up * 0.2f, new Color(0.35f, 0.2f, 0.6f, 0.7f), 1, 0.35f, 0.6f, 0.8f, 1f + (powerLv >= 3 ? 1f : 0f), -0.2f);
        else if (Bubbled)
        {
            float a = Time.time * 3f + Random.value * 6.3f;
            var p = at + new Vector3(Mathf.Cos(a) * 0.4f, 0.4f + Mathf.Sin(a) * 0.35f, 0f);
            Fx.Spark(p, new Vector3(0f, 0.25f, 0f), powerLv >= 5 ? Rainbow(Time.time * 0.5f + Random.value * 0.3f) : Color.Lerp(PowerColor(Power.Bubble), Color.white, 0.5f), 0.6f, powerLv >= 3 ? 2f : 1f);
        }
        else // mastered
            Fx.Rise(at + Vector3.up * 0.1f, PowerColor(power), 1, 0.4f, 0.7f, 0.7f, 1f, -0.3f);
    }

    // ------------------------------------------------------------------ T special moves
    /// One link of the chain lightning.
    void ChainBoltFx(Vector3 from, Vector3 to, Color col)
    {
        Fx.Bolt(from, to, col, 0.7f + 0.3f * powerLv, 0.14f + 0.02f * powerLv, powerLv >= 3 ? powerLv - 2 : 0, 0.28f);
        Fx.Radial(to, col, 6 + powerLv * 3, 1.8f, 0.3f, powerLv >= 3 ? 2f : 1f);
        Fx.Burst(to, Color.white, 3 + powerLv, 1f, 1.2f, 0.25f, 2f);
    }

    /// A bolt straight down from the sky (Lv 5 chain lightning, around you).
    void SkyBoltFx(Enemy e, Color col)
    {
        Vector3 to = e.transform.position + Vector3.up * 0.2f;
        Fx.Bolt(to + Vector3.up * 3.5f, to, col, 1.6f, 0.22f, 1, 0.3f);
        Fx.Radial(to, col, 10, 2f, 0.3f, 2f);
        Shockwave(e.grid, 0.9f, col, 0.3f, 0f, 0.8f);
    }

    /// The burst every special move ends with, sized to its reach.
    void AttackBurstFx(Power p, Vector2 me, float radius)
    {
        Color col = PowerColor(p);
        Vector3 at = PlayerPos + Vector3.up * 0.2f;
        switch (p)
        {
            case Power.Dash:
                GroundGlow(me, 0.8f + 0.15f * powerLv, col, 0.35f, 0.6f);
                if (powerLv >= 3) Fx.Burst(at + Vector3.up * 0.8f, Color.white, 6 + powerLv * 2, 1f, 2f, 0.3f, 2f);
                break;
            case Power.Vine:
                Shockwave(me, radius, col, 0.5f);
                Fx.Radial(at, new Color(0.65f, 0.95f, 0.35f), 12 + powerLv * 5, radius * 1.4f, 0.45f, powerLv >= 3 ? 2f : 1f);
                if (powerLv >= 3) // a ring of thorns bursting up around the edge
                {
                    int spikes = 6 + powerLv * 2;
                    for (int i = 0; i < spikes; i++)
                    {
                        float a = i * Mathf.PI * 2f / spikes;
                        var g = me + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                        Fx.Rise(GroundAt(g), new Color(0.3f, 0.55f, 0.18f), 4 + powerLv, 0.1f, 2f + 0.3f * powerLv, 0.35f, 2f, 0f);
                    }
                }
                if (powerLv >= 5) Shockwave(me, radius * 0.6f, new Color(0.65f, 0.95f, 0.35f), 0.45f, 0.12f);
                break;
            case Power.Magnet:
                var blue = new Color(0.45f, 0.65f, 1f);
                Shockwave(me, radius, col, 0.45f);
                Shockwave(me, radius * 0.85f, blue, 0.45f, 0.08f);
                Fx.Radial(at, col, 12 + powerLv * 4, radius * 1.5f, 0.4f, powerLv >= 3 ? 2f : 1f);
                Fx.Radial(at, blue, 12 + powerLv * 4, radius * 1.2f, 0.4f);
                if (powerLv >= 5) Shockwave(me, radius * 1.15f, Color.white, 0.5f, 0.16f, 0.6f);
                break;
            case Power.Bubble:
                Shockwave(me, radius, col, 0.5f);
                Fx.Rise(at, Color.Lerp(col, Color.white, 0.4f), 10 + powerLv * 5, radius * 0.6f, 1.2f, 1f, 2f, -0.8f);
                Fx.Rise(at, new Color(1f, 0.5f, 0.6f), 6 + powerLv * 2, 0.3f, 1f, 0.8f, 1f, -0.5f); // healing
                if (powerLv >= 3) Shockwave(me, radius * 0.7f, Color.white, 0.45f, 0.1f, 0.6f);
                if (powerLv >= 5)
                    for (int i = 0; i < 3; i++) Shockwave(me, radius * (0.8f + 0.2f * i), Rainbow(i / 3f), 0.55f, 0.12f * i, 0.7f);
                break;
            default: // Shadow: darkness rushes in, then bursts out
                var dark = new Color(0.18f, 0.08f, 0.3f);
                Fx.Radial(at, col, 14 + powerLv * 5, -radius * 1.2f / 0.25f * 0.4f, 0.25f, 2f);
                Shockwave(me, radius, col, 0.45f, 0.1f);
                Shockwave(me, radius * 0.75f, dark, 0.45f, 0.18f);
                GroundGlow(me, radius * 0.5f, dark, 0.6f, 0.8f);
                Fx.Rise(at, dark, 10 + powerLv * 4, radius * 0.5f, 1f, 0.9f, 2f, -0.3f);
                if (powerLv >= 5) ScreenFlash(new Color(0.12f, 0.03f, 0.22f, 0.5f), 0.5f);
                break;
        }
        PowerClimax(col, p == Power.Dash ? 0.8f : 1f);
    }
}
