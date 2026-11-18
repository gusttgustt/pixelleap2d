using UnityEngine;

/// The special fruits of the later continents: three each, none like the first continent's five
/// or each other's. A glowing tree on those continents bears one of its own three.
///   ทวีปน้ำแข็ง       Frost  ผลน้ำแข็ง      R: freeze what's around you          T: หอกน้ำแข็งพันเล่ม   Y: จักรพรรดิหิมะ
///                     Mirror ผลกระจก        R: a mirror shield that hits back    T: พายุเศษกระจก       Y: ราชันกระจกเงา
///                     Gale   ผลวายุ         R: a blast of wind flings them away  T: ทอร์นาโด          Y: เทพวายุ
///   ทวีปภูเขาไฟ       Flame  ผลเปลวเพลิง    R: a fireball at the nearest         T: ทะเลเพลิง          Y: อสูรเพลิงกัลป์
///                     Quake  ผลแผ่นดินไหว   R: stamp: the ground stuns them       T: รอยแยกปฐพี         Y: ยักษ์ธรณี
///                     Iron   ผลเหล็กกล้า    R: iron skin (blows barely hurt)     T: หนามเหล็กพันเล่ม     Y: ป้อมเหล็กไหล
///   ทวีปฟ้าและทะเลลึก  Gravity ผลแรงโน้มถ่วง R: pulls everything in to you         T: บดขยี้               Y: ราชันแรงโน้มถ่วง
///                     Star   ผลดวงดาว       R: blink ahead in a burst of stars   T: ฝนดาวตก            Y: เทพดวงดาว
///                     Abyss  ผลกระแสน้ำลึก  R: a water whip on the nearest three T: คลื่นยักษ์           Y: เจ้าแห่งห้วงลึก
/// They level, awaken (60) and awaken again (99, with a finishing move) like every power.
public partial class IslandGame
{
    float ironT, mirrorT; // iron skin, mirror shield

    static readonly Item[][] ContinentFruits =
    {
        new[] { Item.FruitDash, Item.FruitShadow, Item.FruitVine, Item.FruitMagnet, Item.FruitBubble },
        new[] { Item.FruitFrost, Item.FruitMirror, Item.FruitGale },
        new[] { Item.FruitFlame, Item.FruitQuake, Item.FruitIron },
        new[] { Item.FruitGravity, Item.FruitStar, Item.FruitAbyss },
    };

    /// The fruit a glowing tree here bears: the first continent as it always was, the others their own.
    public Item FruitHere(Biome b) => Cont == 0 || atHome ? FruitFor(b) : ContinentFruits[Cont][(int)b % 3];

    static bool IsNewFruit(Item it) => it is Item.FruitFrost or Item.FruitMirror or Item.FruitGale or Item.FruitFlame or Item.FruitQuake or Item.FruitIron or Item.FruitGravity or Item.FruitStar or Item.FruitAbyss;
    static Power NewPowerOf(Item it) => it switch
    {
        Item.FruitFrost => Power.Frost, Item.FruitMirror => Power.Mirror, Item.FruitGale => Power.Gale,
        Item.FruitFlame => Power.Flame, Item.FruitQuake => Power.Quake, Item.FruitIron => Power.Iron,
        Item.FruitGravity => Power.Gravity, Item.FruitStar => Power.Star, _ => Power.Abyss,
    };
    public static Item FruitOfPower(Power p) => p switch
    {
        Power.Shadow => Item.FruitShadow, Power.Vine => Item.FruitVine, Power.Magnet => Item.FruitMagnet, Power.Bubble => Item.FruitBubble,
        Power.Frost => Item.FruitFrost, Power.Mirror => Item.FruitMirror, Power.Gale => Item.FruitGale,
        Power.Flame => Item.FruitFlame, Power.Quake => Item.FruitQuake, Power.Iron => Item.FruitIron,
        Power.Gravity => Item.FruitGravity, Power.Star => Item.FruitStar, Power.Abyss => Item.FruitAbyss, _ => Item.FruitDash,
    };
    /// A continent's powers (fruit users there have one of these).
    public static Power[] ContinentPowers(int c)
    {
        var fr = ContinentFruits[Mathf.Clamp(c, 0, Continents - 1)];
        var p = new Power[fr.Length];
        for (int i = 0; i < fr.Length; i++) p[i] = IsNewFruit(fr[i]) ? NewPowerOf(fr[i]) : PowerOf(fr[i]);
        return p;
    }

    static string NewPowerName(Power p) => p switch
    {
        Power.Frost => "น้ำแข็ง", Power.Mirror => "กระจก", Power.Gale => "วายุ", Power.Flame => "เปลวเพลิง", Power.Quake => "แผ่นดินไหว",
        Power.Iron => "เหล็กกล้า", Power.Gravity => "แรงโน้มถ่วง", Power.Star => "ดวงดาว", Power.Abyss => "กระแสน้ำลึก", _ => "-",
    };
    static string NewPowerDesc(Power p) => p switch
    {
        Power.Frost => "แช่แข็งศัตรูรอบตัวให้ขยับไม่ได้",
        Power.Mirror => "โล่กระจก กันการโจมตีและสะท้อนดาเมจกลับใส่ศัตรู",
        Power.Gale => "ลมกรรโชกพัดศัตรูรอบตัวกระเด็น",
        Power.Flame => "ยิงลูกไฟใส่ศัตรูที่ใกล้ที่สุด แรงมาก",
        Power.Quake => "กระทืบพื้น ศัตรูรอบตัวมึนงง",
        Power.Iron => "ผิวเหล็ก รับดาเมจน้อยลงมากชั่วคราว",
        Power.Gravity => "ดูดศัตรูรอบตัวเข้ามาหาแล้วกดไว้",
        Power.Star => "วาร์ปไปข้างหน้า ดาวระเบิดตรงที่ลงจอด",
        Power.Abyss => "แส้น้ำฟาดศัตรูใกล้ตัว 3 ตัว",
        _ => "",
    };
    static string NewAttackName(Power p) => p switch
    {
        Power.Frost => "หอกน้ำแข็งพันเล่ม", Power.Mirror => "พายุเศษกระจก", Power.Gale => "ทอร์นาโด", Power.Flame => "ทะเลเพลิง", Power.Quake => "รอยแยกปฐพี",
        Power.Iron => "หนามเหล็กพันเล่ม", Power.Gravity => "บดขยี้", Power.Star => "ฝนดาวตก", Power.Abyss => "คลื่นยักษ์", _ => "-",
    };
    static Color NewPowerColor(Power p) => p switch
    {
        Power.Frost => new Color(0.65f, 0.9f, 1f), Power.Mirror => new Color(0.85f, 0.95f, 1f), Power.Gale => new Color(0.6f, 1f, 0.8f),
        Power.Flame => new Color(1f, 0.5f, 0.15f), Power.Quake => new Color(0.8f, 0.6f, 0.35f), Power.Iron => new Color(0.75f, 0.78f, 0.85f),
        Power.Gravity => new Color(0.55f, 0.35f, 0.9f), Power.Star => new Color(1f, 0.95f, 0.6f), Power.Abyss => new Color(0.2f, 0.5f, 0.95f), _ => Color.white,
    };
    static string NewAwakenName(Power p) => p switch
    {
        Power.Frost => "จักรพรรดิหิมะ", Power.Mirror => "ราชันกระจกเงา", Power.Gale => "เทพวายุ", Power.Flame => "อสูรเพลิงกัลป์", Power.Quake => "ยักษ์ธรณี",
        Power.Iron => "ป้อมเหล็กไหล", Power.Gravity => "ราชันแรงโน้มถ่วง", Power.Star => "เทพดวงดาว", Power.Abyss => "เจ้าแห่งห้วงลึก", _ => "-",
    };
    static string NewAwakenDesc(Power p) => p switch
    {
        Power.Frost => "ศัตรูใกล้ตัวถูกแช่แข็งทุกวินาที", Power.Mirror => "สะท้อนทุกการโจมตีกลับใส่ศัตรู",
        Power.Gale => "ลมหมุนรอบตัวพัดศัตรูกระเด็นทุกวินาที วิ่งเร็วขึ้น", Power.Flame => "เปลวไฟรอบตัวเผาศัตรูใกล้ๆ ทุกวินาที",
        Power.Quake => "แผ่นดินสั่นทุกวินาที ศัตรูรอบตัวมึนงง", Power.Iron => "รับดาเมจน้อยลงครึ่งหนึ่งตลอดเวลา",
        Power.Gravity => "ศัตรูรอบตัวถูกกดจนขยับแทบไม่ได้", Power.Star => "ดาวตกใส่ศัตรูใกล้ๆ และฟื้นเลือดทุกวินาที",
        Power.Abyss => "ฟื้นเลือดทุกวินาที คลื่นผลักศัตรูที่เข้าใกล้", _ => "",
    };
    static string NewUltimateName(Power p) => p switch
    {
        Power.Frost => "ยุคน้ำแข็ง", Power.Mirror => "ห้องกระจกพันบาน", Power.Gale => "พายุทลายฟ้า", Power.Flame => "สุริยะเพลิง", Power.Quake => "ธรณีพิโรธ",
        Power.Iron => "ภูผาเหล็ก", Power.Gravity => "หลุมดำจิ๋ว", Power.Star => "ซุปเปอร์โนวา", Power.Abyss => "ห้วงสมุทรกลืนกิน", _ => "-",
    };
    float NewPowerCooldown => power switch
    {
        Power.Frost => 9f - 0.8f * powerLv, Power.Mirror => 14f - 1.2f * powerLv, Power.Gale => 7f - 0.6f * powerLv,
        Power.Flame => 4f - 0.35f * powerLv, Power.Quake => 8f - 0.7f * powerLv, Power.Iron => 16f - 1.5f * powerLv,
        Power.Gravity => 10f - 0.9f * powerLv, Power.Star => 5f - 0.5f * powerLv, _ => 5f - 0.45f * powerLv, // Abyss
    };

    // ------------------------------------------------------------------ helpers
    /// Hit every creature within r of a spot. stun / push (negative: pull in) optional.
    int HitArea(Vector2 at, float r, int dmg, float stun = 0f, float push = 0f)
    {
        int hits = 0;
        foreach (var e in enemies.ToArray())
        {
            if (e == null || e.Disguised || (e.grid - at).magnitude > r) continue;
            if (stun > 0f) e.Stun(stun);
            if (push != 0f) e.Push((e.grid - at).normalized * push);
            HitWithPower(e, dmg);
            hits++;
        }
        return hits;
    }

    Enemy NearestEnemy(float range, System.Collections.Generic.HashSet<Enemy> skip = null)
    {
        Enemy best = null;
        float bd = range;
        foreach (var e in enemies)
        {
            if (e == null || e.Disguised || (skip != null && skip.Contains(e))) continue;
            float d = (e.grid - player.grid).magnitude;
            if (d < bd) { bd = d; best = e; }
        }
        return best;
    }

    // ------------------------------------------------------------------ R
    bool DoNewPower()
    {
        Vector2 me = player.grid;
        Color col = PowerColor(power);
        float reach = AwakenReach;
        switch (power)
        {
            case Power.Frost:
                HitArea(me, (2f + 0.3f * powerLv) * reach, 1 + powerLv, 1.5f + 0.3f * powerLv);
                Shockwave(me, 2f + 0.3f * powerLv, col, 0.6f);
                Popup("แช่แข็ง!", PlayerPos + Vector3.up * 1f, col);
                return true;
            case Power.Mirror:
                bubbleT = 5f + powerLv; bubbleHits = 1 + powerLv / 2; mirrorT = bubbleT;
                BubbleFx();
                Popup("โล่กระจก!", PlayerPos + Vector3.up * 1f, col);
                return true;
            case Power.Gale:
                HitArea(me, (3f + 0.4f * powerLv) * reach, 1 + powerLv / 2, 0f, 2.2f + 0.3f * powerLv);
                Shockwave(me, 3f + 0.4f * powerLv, col, 0.5f);
                Fx.Radial(PlayerPos + Vector3.up * 0.4f, col, 30, 3f, 0.4f, 1.5f);
                return true;
            case Power.Flame:
            {
                var e = NearestEnemy((6f + powerLv) * reach);
                if (e == null) { Hint("ไม่มีศัตรูในระยะ", 1.5f); return false; }
                ChainBoltFx(PlayerPos + Vector3.up * 0.6f, e.transform.position + Vector3.up * 0.3f, col);
                Fx.Burst(e.transform.position + Vector3.up * 0.3f, col, 18, 1.2f, 1.5f, 0.7f, 2f);
                HitWithPower(e, 3 + 2 * powerLv);
                return true;
            }
            case Power.Quake:
                HitArea(me, (2.2f + 0.3f * powerLv) * reach, 2 + powerLv, 1f);
                Shockwave(me, 2.2f + 0.3f * powerLv, col, 0.6f);
                Shake(0.08f, 0.3f);
                return true;
            case Power.Iron:
                ironT = 3f + 0.5f * powerLv;
                Fx.Burst(PlayerPos + Vector3.up * 0.4f, col, 16, 0.8f, 1.2f);
                Popup("ผิวเหล็ก!", PlayerPos + Vector3.up * 1f, col);
                return true;
            case Power.Gravity:
                foreach (var e in enemies.ToArray())
                {
                    if (e == null || e.Disguised || (e.grid - me).magnitude > (4f + 0.5f * powerLv) * reach) continue;
                    e.Push((me - e.grid) * 0.7f);
                    e.Stun(1f);
                    HitWithPower(e, 1 + powerLv / 2);
                }
                Shockwave(me, 4f + 0.5f * powerLv, col, 0.5f);
                return true;
            case Power.Star:
            {
                float dist = (3f + 0.4f * powerLv) * reach;
                Fx.Burst(PlayerPos + Vector3.up * 0.4f, col, 14, 1f, 1.5f);
                player.Dash(dist);
                HitArea(player.grid + player.facing * dist, 1.6f, 2 + powerLv);
                Fx.Radial(PlayerPos + Vector3.up * 0.4f, col, 24, 2f, 0.4f, 1.5f);
                return true;
            }
            default: // Abyss
            {
                var done = new System.Collections.Generic.HashSet<Enemy>();
                for (int i = 0; i < 3; i++)
                {
                    var e = NearestEnemy((4f + powerLv) * reach, done);
                    if (e == null) break;
                    done.Add(e);
                    ChainBoltFx(PlayerPos + Vector3.up * 0.5f, e.transform.position + Vector3.up * 0.3f, col);
                    e.Stun(0.6f);
                    HitWithPower(e, 2 + powerLv);
                }
                if (done.Count == 0) { Hint("ไม่มีศัตรูในระยะ", 1.5f); return false; }
                return true;
            }
        }
    }

    // ------------------------------------------------------------------ T
    int NewPowerAttack(Vector2 me, float radius, int dmg, Color col)
    {
        int hits;
        Vector2 face = player.facing.sqrMagnitude > 0.01f ? player.facing.normalized : Vector2.right;
        switch (power)
        {
            case Power.Frost: hits = HitArea(me, radius, dmg + 1, 1.2f); Ring(radius, col, 24); break;
            case Power.Mirror: hits = HitArea(me, radius, dmg) + HitArea(me, radius * 0.7f, dmg); Ring(radius, col, 24); Ring(radius * 0.7f, Color.white, 16); break;
            case Power.Gale: hits = HitArea(me, radius, dmg, 0f, -1.4f); Ring(radius, col, 24); Ring(radius * 0.5f, col, 12); break; // pulled into the twister
            case Power.Flame: hits = HitArea(me, radius * 1.1f, dmg + 2); Ring(radius, col, 26); Fx.Radial(PlayerPos + Vector3.up * 0.3f, col, 40, radius, 0.5f, 2f); break;
            case Power.Quake:
                hits = 0;
                foreach (var e in enemies.ToArray())
                {
                    if (e == null || e.Disguised || DistToSegment(e.grid, me, me + face * radius * 2f) > 0.9f) continue;
                    e.Stun(1.2f); HitWithPower(e, dmg + 3); hits++;
                }
                for (int i = 1; i <= 8; i++) Fx.Burst(GridToWorld(me + face * (radius * 2f * i / 8f)) + Vector3.up * 0.1f, col, 4, 0.4f, 1f, 0.6f, 1f);
                Shake(0.1f, 0.3f);
                break;
            case Power.Iron: hits = HitArea(me, radius, Mathf.RoundToInt(dmg * 1.5f)); Ring(radius, col, 22); break;
            case Power.Gravity: hits = HitArea(me, radius * 0.85f, dmg * 2, 0.8f); Ring(radius * 0.85f, col, 22); Shockwave(me, radius, col, 0.5f); break;
            case Power.Star:
                hits = 0;
                for (int i = 0; i < 3 + powerLv; i++)
                {
                    var list = enemies.FindAll(e => e != null && !e.Disguised && (e.grid - me).magnitude < radius * 1.3f);
                    if (list.Count == 0) break;
                    var e = list[Random.Range(0, list.Count)];
                    Fx.Bolt(e.transform.position + Vector3.up * 3.5f, e.transform.position, col, 1.4f, 0.2f, 1, 0.3f);
                    HitWithPower(e, dmg);
                    hits++;
                }
                break;
            default: // Abyss: a wave rolling out in front of you
                hits = 0;
                foreach (var e in enemies.ToArray())
                {
                    if (e == null || e.Disguised) continue;
                    Vector2 to = e.grid - me;
                    if (to.magnitude > radius * 1.4f || Vector2.Dot(to.normalized, face) < 0.35f) continue;
                    e.Push(face * 2f); HitWithPower(e, dmg + 1); hits++;
                }
                for (int i = 1; i <= 6; i++) Fx.Burst(GridToWorld(me + face * (radius * 1.4f * i / 6f)) + Vector3.up * 0.2f, col, 6, 0.6f, 1.2f, 0.7f, -0.5f);
                break;
        }
        Sfx.Play("hit", 0.6f);
        return hits;
    }

    // ------------------------------------------------------------------ Y: awakened, every second
    void NewAwakenTick(Vector2 me, Color col)
    {
        if (player == null) return;
        int d = (2 + powerLv) * (Awaken2 ? 2 : 1);
        switch (power)
        {
            case Power.Frost: HitArea(me, 2.5f, d, 1f); break;
            case Power.Mirror: mirrorT = Mathf.Max(mirrorT, 1.2f); bubbleT = Mathf.Max(bubbleT, 1.2f); bubbleHits = Mathf.Max(bubbleHits, 2); break;
            case Power.Gale: HitArea(me, 1.8f, d / 2 + 1, 0f, 1.6f); break;
            case Power.Flame: if (HitArea(me, 2f, d) > 0) Fx.Burst(PlayerPos + Vector3.up * 0.3f, col, 10, 1.2f, 1f); break;
            case Power.Quake: HitArea(me, 2.5f, d, 0.6f); break;
            case Power.Iron: break; // AwakenArmor: half damage the whole time
            case Power.Gravity: foreach (var e in enemies) if (e != null && (e.grid - me).magnitude < 3f) e.Stun(0.5f); break;
            case Power.Star:
                health = Mathf.Min(MaxHealth, health + (Awaken2 ? 5f : 2.5f));
                var t = NearestEnemy(4.5f);
                if (t != null) { Fx.Bolt(t.transform.position + Vector3.up * 3.5f, t.transform.position, col, 1.2f, 0.18f, 1, 0.3f); HitWithPower(t, d); }
                break;
            default: // Abyss
                health = Mathf.Min(MaxHealth, health + (Awaken2 ? 4f : 2f));
                foreach (var e in enemies) if (e != null && (e.grid - me).magnitude < 1.6f) e.Push((e.grid - me).normalized * 1.5f);
                break;
        }
    }

    /// Iron skin and the mirror shield count down here (TickPower).
    void TickNewPowers(float dt)
    {
        if (ironT > 0f) ironT -= dt;
        if (mirrorT > 0f) mirrorT -= dt;
    }

    /// A blow the mirror shield caught: it comes back at the nearest creature.
    void MirrorReflect()
    {
        if (mirrorT <= 0f || player == null) return;
        var e = NearestEnemy(3.5f);
        if (e != null) { ChainBoltFx(PlayerPos + Vector3.up * 0.5f, e.transform.position + Vector3.up * 0.3f, PowerColor(Power.Mirror)); HitWithPower(e, 2 + 2 * powerLv); }
    }
}
