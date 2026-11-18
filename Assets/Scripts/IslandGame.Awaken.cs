using UnityEngine;

/// Level 60: the fruit power awakens (ปลุกพลัง). A new skill (Y key, the touch button, or the
/// last step of a right click) that for a while:
///   - drops the cooldown of the power (R) and the special move (T) to half a second each,
///   - makes every power hit harder, reach further and spread wider,
///   - wraps you in a bright aura in the fruit's colour (other players see it too),
///   - and gives a buff of the fruit's own line:
///       Dash   เทพสายฟ้า   - run much faster; lightning strikes the nearest creature every second
///       Shadow ราชันเงา    - invisible the whole time
///       Vine   ป่าคืนชีพ    - heal every second; creatures close by are rooted
///       Magnet ราชาแม่เหล็ก - pickups around you fly into the bag; creatures close by are thrown off
///       Bubble ป้อมฟองสมุทร - take far less damage, no poison, a bubble shield and slow healing
public partial class IslandGame
{
    public const int AwakenLevel = 60;
    /// Level 99: the second awakening. It lasts longer, comes back sooner, hits harder and further,
    /// its buff is stronger, and while it's on, Y once more lets loose the fruit's finishing move.
    public const int Awaken2Level = 99;
    float AwakenDuration => Awaken2 ? 35f : 20f;   // seconds the awakening lasts
    float AwakenRecharge => Awaken2 ? 45f : 60f;   // seconds after it ends before it can be used again
    const float AwakenSkillCd = 0.5f;   // R and T cooldown while awakened

    float awakenT, awakenCd, awakenTickT, awakenAuraT, awakenRingT;
    bool ultimateUsed; // the finishing move: once per awakening

    public bool Awakened => awakenT > 0f && power != Power.None;
    public bool CanAwaken => level >= AwakenLevel && power != Power.None;
    public bool Awaken2 => level >= Awaken2Level;
    public float AwakenReady => !CanAwaken ? 0f : Awakened ? 1f : 1f - Mathf.Clamp01(awakenCd / AwakenRecharge);

    float AwakenReach => Awakened ? (Awaken2 ? 1.9f : 1.5f) : 1f;
    int AwakenDmg(int d) => Awakened ? Mathf.CeilToInt(d * (Awaken2 ? 2.4f : 1.75f)) : d;
    float AwakenSpeed => !Awakened ? 1f : power == Power.Dash ? (Awaken2 ? 1.75f : 1.5f) : Awaken2 ? 1.2f : 1.1f;
    float AwakenArmor => Awakened && power == Power.Bubble ? (Awaken2 ? 0.25f : 0.4f) : Awakened && power == Power.Iron ? (Awaken2 ? 0.35f : 0.5f) : Awakened && Awaken2 ? 0.85f : 1f;

    public static string UltimateName(Power p) => p switch
    {
        Power.Dash => "พันสายฟ้าฟาด", Power.Shadow => "ราตรีนิรันดร์", Power.Vine => "ป่าพิโรธ",
        Power.Magnet => "หลุมแม่เหล็กโลก", Power.Bubble => "สึนามิฟองสมุทร", _ => NewUltimateName(p),
    };

    /// Level 99, awakened, Y again: a blast of the fruit's power all around (once per awakening).
    void UseUltimate()
    {
        if (!Awakened || !Awaken2 || ultimateUsed || player == null) return;
        ultimateUsed = true;
        var c = PowerColor(power);
        Vector2 me = MeGrid;
        int dmg = AwakenDmg(20 + 8 * powerLv);
        int hits = 0;
        foreach (var e in enemies.ToArray())
        {
            if (e == null || e.Disguised || (e.grid - me).magnitude > 5f) continue;
            HitWithPower(e, dmg);
            if (power == Power.Vine) e.Stun(4f);
            if (power == Power.Magnet) e.Push((me - e.grid).normalized * 1.5f); // dragged into the middle
            hits++;
        }
        if (power == Power.Shadow) invisibleT = Mathf.Max(invisibleT, 8f);
        if (power == Power.Bubble) { bubbleT = Mathf.Max(bubbleT, 10f); bubbleHits = Mathf.Max(bubbleHits, 5); }
        if (power == Power.Vine) health = Mathf.Min(MaxHealth, health + MaxHealth * 0.3f);
        Shockwave(me, 5f, c, 0.9f);
        Shockwave(me, 3.5f, Color.white, 0.8f, 0.1f, 0.6f);
        Shockwave(me, 6f, c, 1f, 0.25f, 0.5f);
        Fx.Radial(PlayerPos + Vector3.up * 0.4f, c, 60, 4.5f, 0.6f, 2.5f);
        if (power == Power.Dash) for (int i = 0; i < 6; i++) LightningFlash();
        ScreenFlash(new Color(c.r, c.g, c.b, 0.5f), 0.7f);
        Shake(0.18f, 0.6f);
        Sfx.Play("levelup", 1f);
        Popup($"ท่าไม้ตาย! {UltimateName(power)}" + (hits > 0 ? $" x{hits}" : ""), PlayerPos + Vector3.up * 1.6f, c);
    }

    public static string AwakenName(Power p) => p switch
    {
        Power.Dash => "เทพสายฟ้า", Power.Shadow => "ราชันเงา", Power.Vine => "ป่าคืนชีพ",
        Power.Magnet => "ราชาแม่เหล็ก", Power.Bubble => "ป้อมฟองสมุทร", _ => NewAwakenName(p),
    };

    static string AwakenDesc(Power p) => p switch
    {
        Power.Dash => "วิ่งเร็วขึ้นมาก สายฟ้าผ่าศัตรูใกล้ตัวทุกวินาที",
        Power.Shadow => "ล่องหนตลอดเวลาที่ปลุกพลัง",
        Power.Vine => "ฟื้นเลือดทุกวินาที ศัตรูใกล้ตัวถูกเถาวัลย์รัดไว้",
        Power.Magnet => "ดูดของรอบตัวเข้ากระเป๋าเอง ศัตรูที่เข้าใกล้ถูกผลักกระเด็น",
        Power.Bubble => "รับดาเมจน้อยลงมาก กันพิษ มีโล่ฟองและฟื้นเลือดช้าๆ",
        _ => NewAwakenDesc(p),
    };

    /// Right click: the power (R); if it's recharging, the special move (T); then the awakening (Y).
    void UsePowerChain()
    {
        if (power == Power.None) { UsePower(); return; }
        if (powerCd <= 0f && UsePower()) return;
        if (powerAtkCd <= 0f && UsePowerAttack()) return;
        if (CanAwaken && !Awakened && awakenCd <= 0f) UseAwaken();
    }

    void UseAwaken()
    {
        if (power == Power.None) { Hint("ยังไม่มีพลัง กินผลไม้พิเศษจากต้นไม้เรืองแสงก่อน", 3f); return; }
        if (level < AwakenLevel) { Hint($"ปลุกพลังผลไม้ได้ตอนเลเวล {AwakenLevel} (ตอนนี้เลเวล {level})", 3f); return; }
        if (Awakened && Awaken2 && !ultimateUsed && player != null) { UseUltimate(); return; } // level 99: Y again
        if ((player == null && voyage == null) || dead || Busy || Awakened) return;
        if (awakenCd > 0f) { Hint($"ปลุกพลังได้อีกครั้งใน {Mathf.CeilToInt(awakenCd)} วิ", 2f); return; }

        awakenT = AwakenDuration;
        awakenCd = AwakenDuration + AwakenRecharge;
        ultimateUsed = false;
        awakenTickT = 0.3f;
        powerCd = 0f; powerAtkCd = 0f;
        if (power == Power.Shadow) invisibleT = Mathf.Max(invisibleT, AwakenDuration);
        if (power == Power.Bubble) { bubbleT = AwakenDuration; bubbleHits = Mathf.Max(bubbleHits, 3); poisonT = 0f; }

        var c = PowerColor(power);
        Vector3 at = PlayerPos;
        Sfx.Play("levelup", 0.9f);
        Shockwave(MeGrid, 4f, c, 0.7f);
        Shockwave(MeGrid, 3f, Color.white, 0.6f, 0.12f, 0.7f);
        Shockwave(MeGrid, 5f, c, 0.8f, 0.25f, 0.5f);
        Fx.Rise(at, Color.Lerp(c, Color.white, 0.3f), 60, 0.7f, 3f, 1f, 2f, -0.4f);
        Fx.Radial(at + Vector3.up * 0.4f, c, 40, 3f, 0.5f, 2f);
        ScreenFlash(new Color(c.r, c.g, c.b, 0.4f), 0.6f);
        Shake(0.1f, 0.4f);
        if (power == Power.Dash) LightningFlash();
        Popup($"ปลุกพลัง! {AwakenName(power)}", at + Vector3.up * 1.4f, c);
        Hint($"{AwakenName(power)}: {AwakenDesc(power)} สกิลคูลดาวน์ 0.5 วิ โจมตีแรงขึ้น ไกลขึ้น กว้างขึ้น ({AwakenDuration:0} วิ)"
            + (Awaken2 ? $" · ปลุกพลังขั้น 2: กด Y อีกครั้งเพื่อใช้ท่าไม้ตาย \"{UltimateName(power)}\"" : ""), 6f);
    }

    /// Reached level 99 (from AddXp).
    void Awaken2Unlocked()
    {
        QuestBanner("ปลุกพลังขั้น 2!", power == Power.None
            ? "กินผลไม้พิเศษแล้วจะได้ปลุกพลังขั้น 2: นานขึ้น แรงขึ้น และท่าไม้ตาย"
            : $"ปลุกพลังนานขึ้น ({AwakenDuration:0} วิ) แรงขึ้น บัพแรงขึ้น · ท่าไม้ตายใหม่ \"{UltimateName(power)}\" (ระหว่างปลุกพลังกด Y อีกครั้ง)", new Color(1f, 0.82f, 0.3f));
        if (power != Power.None) { var c = PowerColor(power); Shockwave(MeGrid, 4.5f, c, 0.9f); ScreenFlash(new Color(c.r, c.g, c.b, 0.35f), 0.6f); }
    }

    /// Reached level 60 (from AddXp).
    void AwakenUnlocked()
    {
        if (power == Power.None) { Hint($"เลเวล {AwakenLevel}! กินผลไม้พิเศษแล้วจะปลุกพลังผลไม้ได้", 6f); return; }
        var c = PowerColor(power);
        Shockwave(MeGrid, 3.5f, c, 0.8f);
        Fx.Rise(PlayerPos, c, 40, 0.6f, 2.5f, 1f, 2f, -0.4f);
        ScreenFlash(new Color(c.r, c.g, c.b, 0.3f), 0.5f);
        Hint($"เลเวล {AwakenLevel}! พลัง{PowerName(power)}ตื่นแล้ว ได้สกิลใหม่ \"{AwakenName(power)}\" กด Y (หรือคลิกขวาเมื่อท่าอื่นติดคูลดาวน์)", 8f);
    }

    void TickAwaken(float dt)
    {
        if (awakenCd > 0f) awakenCd -= dt;
        if (awakenT <= 0f) return;
        awakenT -= dt;
        if (power == Power.None || dead) { awakenT = 0f; return; }
        if (awakenT <= 0f)
        {
            Popup("พลังที่ปลุกไว้หมดแล้ว", PlayerPos + Vector3.up * 1.2f, new Color(0.85f, 0.85f, 0.85f));
            Hint($"ปลุกพลังได้อีกครั้งใน {AwakenRecharge:0} วิ", 3f);
            return;
        }
        AwakenAura(dt);

        awakenTickT -= dt;
        if (awakenTickT > 0f) return;
        awakenTickT = 1f;
        Vector2 me = MeGrid;
        Color col = PowerColor(power);
        switch (power)
        {
            case Power.Dash: // a bolt onto the nearest creature
                if (player == null) break;
                Enemy best = null;
                float bd = 4.5f;
                foreach (var e in enemies)
                {
                    if (e == null || e.Disguised) continue;
                    float d = (e.grid - me).magnitude;
                    if (d < bd) { bd = d; best = e; }
                }
                if (best != null) { SkyBoltFx(best, col); HitWithPower(best, (2 + powerLv) * (Awaken2 ? 2 : 1)); Sfx.Play("hit", 0.4f); }
                break;
            case Power.Shadow:
                invisibleT = Mathf.Max(invisibleT, awakenT);
                break;
            case Power.Vine:
                health = Mathf.Min(MaxHealth, health + (Awaken2 ? 6f : 3f));
                if (player == null) break;
                foreach (var e in enemies)
                {
                    if (e == null || e.Disguised || (e.grid - me).magnitude > 2.5f) continue;
                    e.Stun(1.1f);
                    Fx.Rise(e.transform.position, new Color(0.3f, 0.55f, 0.18f), 6, 0.15f, 2f, 0.35f, 2f, 0f);
                }
                break;
            case Power.Magnet:
                if (player == null) break;
                int got = 0;
                foreach (var n in nodes.ToArray())
                {
                    if (n == null || !IsPickup(n) || (n.grid - me).magnitude > 4f) continue;
                    MagnetPullFx(n.transform.position);
                    Interact(n);
                    if (++got >= 3) break;
                }
                foreach (var e in enemies)
                {
                    if (e == null || e.Disguised || (e.grid - me).magnitude > 1.6f) continue;
                    e.Push((e.grid - me).normalized * 2f);
                }
                break;
            case Power.Bubble:
                health = Mathf.Min(MaxHealth, health + (Awaken2 ? 3.5f : 1.5f));
                poisonT = 0f;
                break;
            default: NewAwakenTick(me, col); break; // the later continents' fruits (IslandGame.NewPowers.cs)
        }
    }

    /// The aura: thick and bright enough to see from across the screen.
    void AwakenAura(float dt)
    {
        awakenAuraT -= dt;
        if (awakenAuraT <= 0f)
        {
            awakenAuraT = 1f / 30f;
            AwakenAuraParticles(PlayerPos, power);
        }
        awakenRingT -= dt;
        if (awakenRingT <= 0f)
        {
            awakenRingT = 0.7f;
            var c = PowerColor(power);
            GroundGlow(MeGrid, 0.9f, c, 0.7f, 0.5f);
            Shockwave(MeGrid, 1.2f, c, 0.6f, 0f, 0.5f);
        }
    }

    /// One puff of the awakened aura at someone's feet (also used for other players, RemotePlayer.cs).
    public static void AwakenAuraParticles(Vector3 feet, Power p)
    {
        var c = PowerColor(p);
        var light = Color.Lerp(c, Color.white, 0.45f);
        // a column of flame-like light rising around the body
        Fx.Rise(feet + Vector3.up * 0.05f, c, 2, 0.32f, 1.6f, 0.55f, 2f, -0.6f);
        Fx.Rise(feet + Vector3.up * 0.2f, light, 1, 0.22f, 1.2f, 0.45f, 1f, -0.4f);
        // sparks circling the waist
        float a = Time.time * 5f + Random.value * 6.3f;
        Fx.Spark(feet + new Vector3(Mathf.Cos(a) * 0.45f, 0.35f + Mathf.Sin(a) * 0.18f, 0f), new Vector3(0f, 0.5f, 0f), light, 0.5f, 2f);
        switch (p)
        {
            case Power.Dash:
                if (Random.value < 0.15f)
                {
                    Vector3 s = feet + new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(0.2f, 0.9f), 0f);
                    Fx.Bolt(s, s + new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(-0.35f, 0.35f), 0f), c, 0.6f, 0.1f, 0, 0.12f);
                }
                break;
            case Power.Shadow:
                Fx.Rise(feet, new Color(0.15f, 0.05f, 0.3f, 0.85f), 1, 0.4f, 0.9f, 0.7f, 2f, -0.2f);
                break;
            case Power.Vine:
                if (Random.value < 0.3f) Fx.Burst(feet + Vector3.up * 0.4f, new Color(0.6f, 0.95f, 0.35f), 1, 0.5f, 0.5f, 0.6f, 1f);
                break;
            case Power.Magnet:
                if (Random.value < 0.3f) Fx.Spark(feet + new Vector3(Mathf.Cos(-a) * 0.5f, 0.4f + Mathf.Sin(-a) * 0.2f, 0f), Vector3.zero, new Color(0.45f, 0.65f, 1f), 0.4f, 2f);
                break;
            case Power.Bubble:
                if (Random.value < 0.3f) Fx.Rise(feet + Vector3.up * 0.2f, new Color(0.85f, 0.97f, 1f), 1, 0.4f, 0.8f, 0.8f, 2f, -0.8f);
                break;
        }
    }

    float DrawAwakenHud(float y)
    {
        if (!CanAwaken) return y;
        var c = PowerColor(power);
        float ready = AwakenReady;
        string text = Awakened ? $"ปลุกพลัง{(Awaken2 ? "ขั้น 2" : "")}: {AwakenName(power)} เหลือ {awakenT:0.0} วิ" + (Awaken2 && !ultimateUsed ? $" · ท่าไม้ตาย [Y]" : "")
                    : ready >= 1f ? $"ปลุกพลัง: {AwakenName(power)} [Y]"
                    : $"ปลุกพลัง: {AwakenName(power)} ({Mathf.CeilToInt(awakenCd)} วิ)";
        float fill = Awakened ? awakenT / AwakenDuration : ready;
        Bar(new Rect(14, y + 2, 180, 10), fill, Awakened || ready >= 1f ? Color.Lerp(c, Color.white, Awakened ? 0.35f + 0.25f * Mathf.Sin(Time.time * 8f) : 0f) : new Color(0.45f, 0.42f, 0.38f), text);
        return y + 19f;
    }
}
