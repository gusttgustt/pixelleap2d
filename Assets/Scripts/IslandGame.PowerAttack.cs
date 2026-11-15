using UnityEngine;

/// Every special fruit also gives an attack (T key / touch "ท่าพิเศษ"), with its own look.
/// The higher the power's level, the wider it reaches and the harder it hits.
///   Dash   -> สายฟ้าฟาด    chain lightning jumping from creature to creature
///   Shadow -> จันทร์ทมิฬ    a ring of shadow that bursts out around you
///   Vine   -> หนามพันธนาการ thorns burst from the ground and root creatures in place
///   Magnet -> คลื่นแม่เหล็ก  a pulse that flings creatures away
///   Bubble -> ฟองระเบิด    bubbles pop all around you, healing you a little
public partial class IslandGame
{
    float powerAtkCd;

    public static string PowerAttackName(Power p) => p switch
    {
        Power.Dash => "สายฟ้าฟาด", Power.Shadow => "จันทร์ทมิฬ", Power.Vine => "หนามพันธนาการ",
        Power.Magnet => "คลื่นแม่เหล็ก", Power.Bubble => "ฟองระเบิด", _ => "-",
    };

    static Color PowerColor(Power p) => p switch
    {
        Power.Dash => new Color(1f, 0.92f, 0.35f), Power.Shadow => new Color(0.6f, 0.45f, 1f), Power.Vine => new Color(0.45f, 0.85f, 0.3f),
        Power.Magnet => new Color(1f, 0.45f, 0.45f), Power.Bubble => new Color(0.6f, 0.9f, 1f), _ => Color.white,
    };

    float PowerAttackCooldown => Mathf.Max(2.5f, 8f - 0.8f * powerLv);
    /// Reach of the attack in tiles: grows with the power's level.
    float PowerAttackRadius => power switch
    {
        Power.Dash => 3f + 0.8f * powerLv,
        Power.Vine => 2f + 0.5f * powerLv,
        Power.Magnet => 2f + 0.55f * powerLv,
        _ => 1.6f + 0.45f * powerLv,
    };
    int PowerAttackDamage => 2 + powerLv * 2 + SkillLv(Skill.Strength);
    public float PowerAttackReady => power == Power.None ? 0f : 1f - Mathf.Clamp01(powerAtkCd / PowerAttackCooldown);

    void UsePowerAttack()
    {
        if (power == Power.None) { Hint("ยังไม่มีพลัง กินผลไม้พิเศษจากต้นไม้เรืองแสงก่อน", 3f); return; }
        if (powerAtkCd > 0f || player == null || dead || Busy) return;
        float radius = PowerAttackRadius;
        int dmg = PowerAttackDamage;
        Color col = PowerColor(power);
        Vector2 me = player.grid;
        int hits = 0;

        switch (power)
        {
            case Power.Dash: // jump from the nearest creature to the next, up to Lv + 1 times
            {
                Vector3 from = PlayerPos + Vector3.up * 0.6f;
                Vector2 at = me;
                var done = new System.Collections.Generic.HashSet<Enemy>();
                for (int j = 0; j <= powerLv; j++)
                {
                    Enemy next = null;
                    float bd = j == 0 ? radius : 2.5f + 0.3f * powerLv;
                    foreach (var e in enemies)
                    {
                        if (e == null || e.Disguised || done.Contains(e)) continue;
                        float d = (e.grid - at).magnitude;
                        if (d < bd) { bd = d; next = e; }
                    }
                    if (next == null) break;
                    done.Add(next);
                    Vector3 to = next.transform.position + Vector3.up * 0.3f;
                    for (int i = 0; i <= 10; i++) // zig-zag bolt
                        Fx.Burst(Vector3.Lerp(from, to, i / 10f) + (Vector3)(Random.insideUnitCircle * 0.12f), col, 2, 0.15f, 0.5f);
                    from = to; at = next.grid;
                    HitWithPower(next, dmg);
                    hits++;
                }
                if (hits == 0) Fx.Burst(PlayerPos + Vector3.up * 1.2f, col, 14, 0.6f, 1.6f);
                Sfx.Play("hit", 0.7f);
                break;
            }
            case Power.Vine:
                foreach (var e in enemies.ToArray())
                {
                    if (e == null || e.Disguised || (e.grid - me).magnitude > radius) continue;
                    e.Stun(1.4f + 0.35f * powerLv);
                    Fx.Burst(e.transform.position, col, 8, 0.4f, 1.6f, 0.8f, 1f); // thorns shooting up
                    HitWithPower(e, dmg);
                    hits++;
                }
                Ring(radius, col, 20);
                Sfx.Play("chop", 0.6f);
                break;
            case Power.Magnet:
                foreach (var e in enemies.ToArray())
                {
                    if (e == null || e.Disguised || (e.grid - me).magnitude > radius) continue;
                    e.Push((e.grid - me).normalized * (1.5f + 0.3f * powerLv));
                    HitWithPower(e, dmg);
                    hits++;
                }
                Ring(radius * 0.5f, col, 12); Ring(radius, col, 22);
                Sfx.Play("hit", 0.6f);
                break;
            case Power.Bubble:
                foreach (var e in enemies.ToArray())
                {
                    if (e == null || e.Disguised || (e.grid - me).magnitude > radius) continue;
                    HitWithPower(e, dmg);
                    hits++;
                }
                for (int i = 0; i < 6 + powerLv * 3; i++) // bubbles popping all around
                {
                    Vector2 off = Random.insideUnitCircle * radius;
                    Fx.Burst(GridToWorld(me + off) + Vector3.up * 0.3f, col, 4, 0.3f, 0.8f, 0.7f, -1f);
                }
                health = Mathf.Min(MaxHealth, health + 2f * powerLv);
                Sfx.Play("splash", 0.6f);
                break;
            default: // Shadow: a dark nova
                foreach (var e in enemies.ToArray())
                {
                    if (e == null || e.Disguised || (e.grid - me).magnitude > radius) continue;
                    HitWithPower(e, dmg + 1);
                    hits++;
                }
                Ring(radius, col, 24); Ring(radius * 0.6f, new Color(0.2f, 0.1f, 0.35f), 16);
                Sfx.Play("hurt", 0.4f);
                break;
        }
        powerAtkCd = PowerAttackCooldown;
        AddPowerXp(1);
        Popup(PowerAttackName(power) + (hits > 0 ? $"! x{hits}" : "!"), PlayerPos + Vector3.up * 1.2f, col);
    }

    /// Particles around a circle on the ground (radius in tiles).
    void Ring(float radius, Color col, int points)
    {
        Vector2 me = player.grid;
        for (int i = 0; i < points; i++)
        {
            float a = i * Mathf.PI * 2f / points;
            Fx.Burst(GridToWorld(me + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius) + Vector3.up * HeightAt(me), col, 2, 0.2f, 0.8f, 0.6f, 0.5f);
        }
    }
}
