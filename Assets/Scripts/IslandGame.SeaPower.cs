using System.Collections.Generic;
using UnityEngine;

/// The special powers (R) and special moves (T) while paddling between islands. They work
/// like on land, but on the raft and against sea creatures:
///   Dash   - the raft surges ahead (no hits while surging); from Lv 3 it zaps what it passes
///   Shadow - same as land: sharks lose track of you and the sea monster can't find you to lunge
///   Vine   - pulls the nearest creature to the raft and hits it (the monster is only held a moment)
///   Magnet - no pickups out here: a pulse that shoves the creatures around you away
///   Bubble - same as land: a shield against the next bites
/// Ranges are in tiles, measured on the tile grid like on land (WorldDeltaToGrid).
public partial class IslandGame
{
    /// Distance on the tile grid from the raft to a sea creature, less its size.
    float SeaDist(SeaCreature c) => WorldDeltaToGrid(c.pos - voyage.raft).magnitude - c.Reach * 1.6f;

    /// Creatures the powers can reach right now, nearest first.
    List<SeaCreature> SeaInRange(float range)
    {
        var list = new List<SeaCreature>();
        foreach (var c in voyage.creatures)
            if (c != null && c.Hittable && SeaDist(c) <= range) list.Add(c);
        list.Sort((a, b) => SeaDist(a).CompareTo(SeaDist(b)));
        return list;
    }

    /// Paddle direction: what you're pressing, else the way you face.
    Vector2 SeaFacing()
    {
        Vector2 input = PlayerController.ReadInput();
        if (input.sqrMagnitude > 0.04f) return input.normalized;
        return new Vector2(voyage.playerSr != null && voyage.playerSr.flipX ? -1f : 1f, 0f);
    }

    bool UseSeaPower() => power switch
    {
        Power.Dash => SeaDash(),
        Power.Shadow => DoShadow(),
        Power.Vine => SeaVine(),
        Power.Magnet => SeaMagnet(),
        Power.Bubble => DoBubble(),
        Power.None => false,
        _ => SeaNova(), // the later continents' fruits on the raft: a burst of the power around you
    };

    bool SeaNova()
    {
        int hits = 0;
        foreach (var c in SeaInRange(2f + 0.4f * powerLv)) { HitSea(c, 2 + powerLv, new Color(1f, 0.9f, 0.4f)); hits++; }
        Ring(2f + 0.4f * powerLv, PowerColor(power), 20);
        Popup($"พลัง{PowerName(power)}!" + (hits > 0 ? $" x{hits}" : ""), PlayerPos + Vector3.up * 1.1f, PowerColor(power));
        return true;
    }

    bool SeaDash()
    {
        var v = voyage;
        Vector2 dir = SeaFacing();
        float push = 5f + 0.9f * powerLv;                  // the raft's momentum carries it ~push/2.5 units
        Vector2 start = v.raft;
        Vector2 end = start + dir * (push / 2.5f);
        v.vel = dir * push;
        v.dashT = 0.4f;                                   // nothing lands while you surge (PowerBlocks)
        Sfx.Play("splash", 0.5f);
        DashFx(WorldDeltaToGrid(start), WorldDeltaToGrid(end), v.playerSr);
        for (int i = 0; i < 10; i++) // a burst of spray off the back of the raft
            Fx.Spark((Vector3)(start + Random.insideUnitCircle * 0.3f), (Vector3)(-dir * Random.Range(1.5f, 3f) + Random.insideUnitCircle), new Color(1f, 1f, 1f, 0.85f), 0.4f, 1f, 2f, 1f);
        if (powerLv >= 3) // lightning: everything along the way gets zapped
            foreach (var c in v.creatures.ToArray())
            {
                if (c == null || !c.Hittable) continue;
                if (DistToSegment(c.pos, start, end) > 0.6f + c.Reach) continue;
                HitSea(c, 1 + powerLv, new Color(1f, 0.9f, 0.4f));
            }
        return true;
    }

    bool SeaVine()
    {
        var near = SeaInRange(4f + powerLv);
        if (near.Count == 0) { Hint("ไม่มีสัตว์ทะเลให้ดึงในระยะ", 1.5f); return false; }
        var c = near[0];
        if (c.IsBig) c.Stun(2f);                          // far too big to pull: the vines just hold it
        else c.pos = voyage.raft + (c.pos - voyage.raft).normalized * 0.9f;
        VineFx(c.transform.position);
        HitSea(c, 1 + powerLv, new Color(1f, 0.9f, 0.4f));
        return true;
    }

    bool SeaMagnet()
    {
        float range = 2f + powerLv;
        var near = SeaInRange(range);
        if (near.Count == 0) { Hint("ไม่มีสัตว์ทะเลรอบตัวให้ผลัก", 1.5f); return false; }
        foreach (var c in near)
        {
            c.Push((c.pos - voyage.raft).normalized * (1.5f + 0.3f * powerLv));
            c.Stun(0.6f);
        }
        MagnetFx(range);
        Popup($"ผลักสัตว์ทะเล {near.Count} ตัว!", PlayerPos + Vector3.up * 1.1f, new Color(1f, 0.6f, 0.6f));
        return true;
    }

    // ------------------------------------------------------------------ T at sea
    void UseSeaPowerAttack()
    {
        var v = voyage;
        float radius = PowerAttackRadius;
        int dmg = PowerAttackDamage;
        Color col = PowerColor(power);
        Vector2 me = MeGrid;
        int hits = 0;

        switch (power)
        {
            case Power.Dash: // chain lightning from creature to creature
            {
                Vector3 from = PlayerPos + Vector3.up * 0.6f;
                var done = new HashSet<SeaCreature>();
                SeaCreature at = null;
                for (int j = 0; j <= powerLv; j++)
                {
                    SeaCreature next = null;
                    float bd = j == 0 ? radius : 2.5f + 0.3f * powerLv;
                    foreach (var c in v.creatures)
                    {
                        if (c == null || !c.Hittable || done.Contains(c)) continue;
                        float d = j == 0 ? SeaDist(c) : WorldDeltaToGrid(c.pos - at.pos).magnitude - c.Reach * 1.6f;
                        if (d < bd) { bd = d; next = c; }
                    }
                    if (next == null) break;
                    done.Add(next);
                    Vector3 to = next.transform.position + Vector3.up * (next.IsBig ? 1f : 0.3f);
                    ChainBoltFx(from, to, col);
                    from = to; at = next;
                    hits++;
                }
                if (powerLv >= 5) // mastered: bolts from the sky onto everything around you as well
                {
                    float ring = 1.6f + 0.45f * powerLv;
                    foreach (var c in SeaInRange(ring))
                    {
                        if (done.Contains(c)) continue;
                        done.Add(c);
                        Vector3 to = c.transform.position + Vector3.up * 0.2f;
                        Fx.Bolt(to + Vector3.up * 3.5f, to, col, 1.6f, 0.22f, 1, 0.3f);
                        Fx.Radial(to, col, 10, 2f, 0.3f, 2f);
                        hits++;
                    }
                    Ring(ring, col, 26);
                    Shockwave(me, ring, col, 0.5f);
                    LightningFlash();
                }
                foreach (var c in done) HitSea(c, dmg, new Color(1f, 0.9f, 0.4f));
                if (hits == 0) Fx.Burst(PlayerPos + Vector3.up * 1.2f, col, 14, 0.6f, 1.6f);
                Sfx.Play("hit", 0.7f);
                break;
            }
            case Power.Vine: // thorny vines burst up out of the sea and hold them
                foreach (var c in SeaInRange(radius))
                {
                    c.Stun(1.4f + 0.35f * powerLv);
                    Fx.Rise(c.transform.position, new Color(0.3f, 0.55f, 0.18f), 6 + powerLv * 2, 0.15f, 2f + 0.3f * powerLv, 0.35f, powerLv >= 3 ? 2f : 1f, 0f);
                    HitSea(c, dmg, new Color(1f, 0.9f, 0.4f));
                    hits++;
                }
                Ring(radius, col, 20);
                Sfx.Play("chop", 0.6f);
                break;
            case Power.Magnet:
                foreach (var c in SeaInRange(radius))
                {
                    c.Push((c.pos - v.raft).normalized * (1.5f + 0.3f * powerLv));
                    HitSea(c, dmg, new Color(1f, 0.9f, 0.4f));
                    hits++;
                }
                Ring(radius * 0.5f, col, 12); Ring(radius, col, 22);
                Sfx.Play("hit", 0.6f);
                break;
            case Power.Bubble:
                foreach (var c in SeaInRange(radius)) { HitSea(c, dmg, new Color(1f, 0.9f, 0.4f)); hits++; }
                for (int i = 0; i < 6 + powerLv * 3; i++)
                    Fx.Burst(GridToWorld(me + Random.insideUnitCircle * radius) + Vector3.up * 0.3f, col, 4, 0.3f, 0.8f, 0.7f, -1f);
                health = Mathf.Min(MaxHealth, health + 2f * powerLv);
                Sfx.Play("splash", 0.6f);
                break;
            default: // Shadow: a dark nova
                foreach (var c in SeaInRange(radius)) { HitSea(c, dmg + 1, new Color(1f, 0.9f, 0.4f)); hits++; }
                Ring(radius, col, 24); Ring(radius * 0.6f, new Color(0.2f, 0.1f, 0.35f), 16);
                Sfx.Play("hurt", 0.4f);
                break;
        }
        AttackBurstFx(power, me, radius);
        powerAtkCd = PowerAttackCooldown;
        AddPowerXp(1);
        Popup(PowerAttackName(power) + (hits > 0 ? $"! x{hits}" : "!"), PlayerPos + Vector3.up * 1.2f, col);
    }
}
