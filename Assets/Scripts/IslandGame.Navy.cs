using System.Collections.Generic;
using UnityEngine;

/// Government warships (เรือรัฐบาล): the navy hunts wanted sailors on the open sea. Any crossing
/// may meet one - likelier further out, across the ocean to another continent, and the higher
/// your bounty (IslandGame.Bounty.cs). A warship sails alongside and fires cannon volleys: each
/// ball shows a red ring on the water where it will come down, so keep paddling. Paddle up to it
/// and hit it (weapons and powers work); sinking one raises your bounty and its hold is yours.
/// The ship itself is a SeaCreature (SeaKind.Navy).
public partial class IslandGame
{
    public class NavyShot
    {
        public Vector2 from, to;
        public float t, dur, dmg;
        public SpriteRenderer ball, mark;
        public bool bolt; // a Dash officer's lightning: no ball, a yellow ring, then a strike
    }

    /// A sunk warship going down by the stern.
    public class Wreck { public SpriteRenderer sr; public float t; public Vector3 at; public bool flip; }

    /// Pick when (if ever) the navy turns up on this crossing. Called from StartVoyage.
    void PlanNavy(Voyage v)
    {
        v.navyAt = -1f;
        if (voyageVisit != null) return; // a friend's home: the navy leaves you be
        bool ocean = destContinent != continent;
        float heat = Mathf.Clamp01(bounty / 1500000f); // a bigger bounty draws more of them
        float chance = ocean ? 0.45f + 0.35f * heat
                     : Mathf.Max(island, destIsland) < 3 && bounty < 50000 ? 0f // the first islands stay quiet for newcomers
                     : 0.12f + 0.02f * Mathf.Min(Mathf.Max(island, destIsland), 10) + 0.3f * heat;
        if (v.monsterAt > 0f) chance *= 0.5f; // the sea monster keeps them away too
        if (Random.value >= chance) return;
        float a = v.monsterAt > 0f ? (v.monsterAt < v.length * 0.45f ? 0.7f : 0.22f) : Random.Range(0.25f, 0.6f);
        v.navyAt = v.length * a;
        v.navyShips = 1 + ((ocean && bounty >= 200000) || (bounty >= 600000 && Random.value < 0.4f) ? 1 : 0);
        // Now and then the whole navy turns out: a fleet of 3-5 warships led by a flagship whose
        // admiral has a fruit power. Only out past the first islands, likelier across the ocean.
        int far = Mathf.Max(island, destIsland);
        float fleet = ocean ? 0.08f + 0.25f * heat : far >= 5 ? 0.03f + 0.2f * heat : 0f;
        v.navyFleet = Random.value < fleet;
        if (v.navyFleet) v.navyShips = 3 + (heat > 0.5f ? 2 : Random.Range(0, 2));
    }

    /// A random officer's level: most are new to their power, a few are strong, and very rarely
    /// one has awakened it (level 60+). Further out and a bigger bounty bring stronger ones.
    int RollOfficerLv(bool admiral)
    {
        float heat = Mathf.Clamp01(bounty / 1500000f);
        float roll = Random.value - 0.15f * heat - 0.01f * Mathf.Min(Mathf.Max(island, destIsland), 15) - (admiral ? 0.25f : 0f);
        if (roll < 0.04f) return Random.Range(AwakenLevel, 81);   // awakened
        if (roll < 0.16f) return Random.Range(36, AwakenLevel);   // strong
        if (roll < 0.45f) return Random.Range(16, 36);            // seasoned
        return Random.Range(1, 16);                               // new
    }

    void SpawnNavy()
    {
        var v = voyage;
        v.navyAt = -1f;
        if (v.raft.x > v.length - 9f) return; // too close to shore now
        float heat = Mathf.Clamp01(bounty / 2000000f);
        for (int i = 0; i < v.navyShips; i++)
        {
            float side = (i % 2 == 0 ? 1f : -1f) * (Random.value < 0.5f ? 1f : -1f);
            var p = v.raft + new Vector2(Random.Range(7f, 9f) + i * 2f, side * Random.Range(2.5f, 4.5f));
            p.y = Mathf.Clamp(p.y, -6f, 6f);
            var c = new GameObject("NavyShip").AddComponent<SeaCreature>();
            c.transform.SetParent(world);
            c.Init(SeaKind.Navy, p, tier);
            c.Toughen(1f + heat * 1.2f + 0.08f * Mathf.Min(navyWins, 10), (1f + heat * 0.6f) * (v.navyFleet && i > 0 ? 0.75f : 1f));
            bool admiral = v.navyFleet && i == 0;
            if (admiral || Random.value < 0.22f + 0.3f * heat) // some ships carry an officer with a fruit power
                c.SetOfficer((Power)Random.Range(1, 6), RollOfficerLv(admiral), admiral);
            v.creatures.Add(c);
        }
        SeaCreature lead = null;
        foreach (var c in v.creatures) if (c != null && c.kind == SeaKind.Navy && c.officer != Power.None && (lead == null || c.officerLv > lead.officerLv)) lead = c;
        string officerNote = lead != null ? $" บนเรือมี{lead.OfficerTitle}!" : "";
        if (v.navyFleet)
        {
            Hint($"กองทัพเรือรัฐบาล! เรือรบ {v.navyShips} ลำล้อมเข้ามา{officerNote} หลบวงแดง/วงเหลืองแล้วจมเรือธงให้ได้", 8f);
            Sfx.Play("hit", 0.9f);
            Shake(0.1f, 0.7f);
            ScreenFlash(new Color(0.1f, 0.2f, 0.45f, 0.35f), 0.7f);
            return;
        }
        string who = v.navyShips > 1 ? "กองเรือรัฐบาล" : "เรือรัฐบาล";
        Hint(bounty > 0 ? $"{who}แล่นเข้ามา! \"หยุดนะ {playerName}! ค่าหัว {Baht(bounty)} ของเจ้าจบที่นี่!\" หลบวงแดงแล้วเข้าไปตีเรือ"
                        : $"{who}แล่นเข้ามาตรวจทะเล! พวกเขายิงปืนใหญ่ใส่ หลบวงแดงแล้วเข้าไปตีเรือ หรือพายหนีให้ถึงฝั่ง", 7f);
        if (lead != null) Hint($"ระวัง!{officerNote} มันใช้พลัง{PowerName(lead.officer)}โจมตีได้", 6f);
        Sfx.Play("hit", 0.7f);
        Shake(0.06f, 0.4f);
    }

    /// Faster paddling away from a ram, etc.
    public void ShoveRaft(Vector2 by)
    {
        if (voyage != null) voyage.vel += by;
    }

    /// A warship fires `n` balls at where the raft is heading.
    public void NavyVolley(SeaCreature ship, int n, float dmg)
    {
        var v = voyage;
        if (v == null || ship == null) return;
        Vector2 muzzle = ship.pos + Vector2.up * 0.35f;
        Fx.Burst((Vector3)muzzle, new Color(0.75f, 0.75f, 0.75f), 14, 0.8f, 1.2f, 1.1f, -1f);
        Fx.Burst((Vector3)muzzle, new Color(1f, 0.7f, 0.3f), 8, 0.6f, 2f, 0.3f);
        Sfx.Play("hit", 0.5f);
        for (int i = 0; i < n; i++)
        {
            Vector2 lead = v.raft + v.vel * (1.1f + i * 0.25f);
            Vector2 to = i == 0 ? lead : lead + Random.insideUnitCircle * 1.4f;
            to.y = Mathf.Clamp(to.y, -6.5f, 6.5f);
            var s = new NavyShot { from = muzzle, to = to, t = 0f, dur = 1.15f + i * 0.3f, dmg = dmg };
            s.mark = new GameObject("ShotMark").AddComponent<SpriteRenderer>();
            s.mark.transform.SetParent(world);
            s.mark.sprite = PixelArt.StrikeMark();
            s.mark.transform.position = (Vector3)to;
            s.mark.transform.localScale = Vector3.one * 1.5f;
            s.mark.sortingOrder = -32400; // on the water, under the boats
            s.ball = new GameObject("Cannonball").AddComponent<SpriteRenderer>();
            s.ball.transform.SetParent(world);
            s.ball.sprite = PixelArt.Cannonball();
            s.ball.sortingOrder = 32000;
            s.ball.transform.position = (Vector3)muzzle;
            v.shots.Add(s);
        }
    }

    const float ShotRadius = 0.8f;

    void UpdateNavy(float dt)
    {
        var v = voyage;
        if (v.navyAt > 0f && v.raft.x >= v.navyAt) SpawnNavy();
        for (int i = v.shots.Count - 1; i >= 0; i--)
        {
            var s = v.shots[i];
            if (s.ball == null || s.mark == null) { v.shots.RemoveAt(i); continue; }
            s.t += dt;
            float k = Mathf.Clamp01(s.t / s.dur);
            Vector2 p = Vector2.Lerp(s.from, s.to, k);
            s.ball.enabled = !s.bolt;
            s.ball.transform.position = new Vector3(p.x, p.y + Mathf.Sin(k * Mathf.PI) * 2.4f, 0f);
            s.mark.color = s.bolt ? new Color(1f, 0.92f, 0.25f, 0.35f + 0.5f * k + 0.15f * Mathf.Sin(Time.time * 24f))
                                  : new Color(1f, 0.25f, 0.2f, 0.35f + 0.45f * k + 0.15f * Mathf.Sin(Time.time * 18f));
            if (k < 1f) continue;
            if (s.bolt)
            {
                Fx.Burst((Vector3)s.to + Vector3.up * 1.2f, new Color(1f, 0.95f, 0.5f), 16, 1.6f, 3f, 0.35f, 0f);
                Fx.Burst((Vector3)s.to, new Color(1f, 1f, 0.8f), 10, 0.8f, 2f, 0.3f);
                Sfx.Play("hit", 0.4f);
                if ((v.raft - s.to).magnitude <= ShotRadius && !Invisible) { DamagePlayer(s.dmg, "สายฟ้าของนายทหารรัฐบาล!"); Shake(0.08f, 0.25f); }
                Destroy(s.ball.gameObject);
                Destroy(s.mark.gameObject);
                v.shots.RemoveAt(i);
                continue;
            }
            Fx.Burst((Vector3)s.to, new Color(0.85f, 0.95f, 1f), 18, 1.2f, 2.6f, 0.7f, 4f); // a tall splash
            Fx.Burst((Vector3)s.to, new Color(1f, 1f, 1f, 0.8f), 8, 0.6f, 1f, 0.5f);
            Sfx.Play("splash", 0.45f);
            if ((v.raft - s.to).magnitude <= ShotRadius && !Invisible)
            {
                DamagePlayer(s.dmg, "ลูกปืนใหญ่เรือรัฐบาล!");
                Shake(0.1f, 0.3f);
                Fx.Burst((Vector3)v.raft, new Color(1f, 0.6f, 0.25f), 14, 1f, 2f, 0.6f, 2f);
            }
            Destroy(s.ball.gameObject);
            Destroy(s.mark.gameObject);
            v.shots.RemoveAt(i);
        }
        for (int i = v.wrecks.Count - 1; i >= 0; i--)
        {
            var w = v.wrecks[i];
            if (w.sr == null) { v.wrecks.RemoveAt(i); continue; }
            w.t += dt;
            float k = w.t / 3f;
            w.sr.transform.position = w.at + Vector3.down * k * 1.4f;
            w.sr.transform.rotation = Quaternion.Euler(0f, 0f, (w.flip ? -1f : 1f) * Mathf.Min(1f, k * 1.5f) * 22f);
            w.sr.color = new Color(0.75f, 0.8f, 0.9f, Mathf.Clamp01(1.2f - k));
            if (Random.value < dt * 8f) Fx.Burst(w.at + new Vector3(Random.Range(-1f, 1f), Random.Range(0f, 1.2f) - k, 0f), new Color(0.4f, 0.4f, 0.42f), 3, 0.4f, 0.6f, 1f, -1f);
            if (k >= 1.2f) { Destroy(w.sr.gameObject); v.wrecks.RemoveAt(i); }
        }
    }

    /// The officer aboard uses their fruit power (called by the ship, SeaCreature.UpdateNavy).
    /// Every power hits harder with the officer's level; awakened ones (Lv 60+) do it bigger.
    public void OfficerPower(SeaCreature ship)
    {
        var v = voyage;
        if (v == null || ship == null) return;
        float mul = 1f + ship.officerLv / 40f;
        bool awake = ship.Awakened;
        Vector3 at = ship.transform.position + Vector3.up * ship.Tall;
        Color pc = PowerColor(ship.officer);
        Popup($"พลัง{PowerName(ship.officer)}!", at + Vector3.up * 0.3f, pc);
        Fx.Burst(at, pc, 18, 1.2f, 2f, 0.6f, 1f);
        Vector2 toShip = ship.pos - v.raft;
        switch (ship.officer)
        {
            case Power.Dash: // lightning rains around you
                int n = 3 + ship.officerLv / 20 + (awake ? 3 : 0);
                for (int i = 0; i < n; i++)
                {
                    Vector2 to = v.raft + v.vel * 0.8f + (i == 0 ? Vector2.zero : Random.insideUnitCircle * (1.5f + i * 0.35f));
                    to.y = Mathf.Clamp(to.y, -6.5f, 6.5f);
                    var s = new NavyShot { from = to, to = to, t = 0f, dur = 0.9f + i * 0.12f, dmg = ship.Damage * 0.6f * mul, bolt = true };
                    s.mark = new GameObject("BoltMark").AddComponent<SpriteRenderer>();
                    s.mark.transform.SetParent(world);
                    s.mark.sprite = PixelArt.StrikeMark();
                    s.mark.transform.position = (Vector3)to;
                    s.mark.transform.localScale = Vector3.one * 1.5f;
                    s.mark.sortingOrder = -32400;
                    s.ball = new GameObject("Bolt").AddComponent<SpriteRenderer>();
                    s.ball.transform.SetParent(world);
                    s.ball.enabled = false;
                    v.shots.Add(s);
                }
                Hint($"{ship.OfficerTitle} เรียกสายฟ้า! หลบวงสีเหลือง", 3f);
                break;
            case Power.Shadow: // fades from sight, then fires from nowhere
                ship.Cloak(awake ? 4f : 2.5f);
                NavyVolley(ship, 3 + (awake ? 2 : 0), ship.Damage * Mathf.Min(1.5f, mul * 0.8f));
                Hint($"{ship.OfficerTitle} ทำให้เรือล่องหน! ตีไม่โดนจนกว่าจะโผล่มา", 3f);
                break;
            case Power.Vine: // vines grab the boat and drag it in
                if (Invisible) break;
                ShoveRaft(toShip.normalized * (2.5f + ship.officerLv / 15f));
                DamagePlayer(ship.Damage * 0.35f * mul, "เถาวัลย์รัดเรือ!");
                slowT = Mathf.Max(slowT, awake ? 3f : 1.8f);
                Popup("โดนเถาวัลย์รัด! พายช้าลง", PlayerPos + Vector3.up * 1f, pc);
                break;
            case Power.Magnet: // pulls the boat in and gold out of your bag
                if (Invisible) break;
                ShoveRaft(toShip.normalized * (2f + ship.officerLv / 20f));
                int steal = Mathf.Min(Count(Item.GoldCoin), 2 + ship.officerLv / 10 + (awake ? 5 : 0));
                if (steal > 0)
                {
                    inv[Item.GoldCoin] -= steal;
                    ship.stolen += steal;
                    Popup($"-{steal} เหรียญทอง (โดนดูด!)", PlayerPos + Vector3.up * 1f, new Color(1f, 0.85f, 0.3f));
                    Hint($"{ship.OfficerTitle} ดูดเหรียญทองไป {steal} เหรียญ จมเรือเพื่อเอาคืน!", 3f);
                }
                break;
            default: // Bubble: a shield of bubbles around the ship
                ship.shieldHits = 2 + ship.officerLv / 20 + (awake ? 2 : 0);
                Hint($"{ship.OfficerTitle} สร้างโล่ฟองสบู่ กันได้ {ship.shieldHits} ครั้ง", 3f);
                break;
        }
    }

    static Item FruitOf(Power p) => p > Power.Bubble ? FruitOfPower(p) : p switch
    {
        Power.Shadow => Item.FruitShadow, Power.Vine => Item.FruitVine, Power.Magnet => Item.FruitMagnet, Power.Bubble => Item.FruitBubble, _ => Item.FruitDash,
    };

    int NavyAlive => voyage == null ? 0 : voyage.creatures.FindAll(c => c != null && c.kind == SeaKind.Navy).Count;

    /// A warship went down: it sinks by the stern, its hold is yours and your bounty goes up.
    void NavySunk(SeaCreature c, Vector3 at)
    {
        var sr = c.GetComponent<SpriteRenderer>();
        if (sr != null) voyage.wrecks.Add(new Wreck { sr = sr, at = c.transform.position, flip = sr.flipX });
        Destroy(c); // the picture stays (sinking); the ship itself is gone
        navyWins++;
        Sfx.Play("levelup", 0.8f);
        Shake(0.15f, 0.6f);
        ScreenFlash(new Color(1f, 0.85f, 0.6f, 0.35f), 0.5f);
        Fx.Burst(at + Vector3.up * 0.8f, new Color(1f, 0.6f, 0.25f), 36, 2.5f, 3f, 1f, 2f, 2f);
        Fx.Burst(at, new Color(0.85f, 0.95f, 1f), 30, 2.5f, 2.5f, 0.8f, 4f);
        Reward(at, Item.CookedMeat, 2, c.xp);
        Loot(Item.GoldCoin, 10 + 5 * tier + Mathf.Min(navyWins, 10), at + Vector3.up * 0.9f);
        Loot(Item.Rope, 3, at + Vector3.up * 1.3f);
        if (Random.value < 0.35f) Loot(Item.Pearl, 1 + tier, at + Vector3.up * 1.7f);
        if (Random.value < 0.12f) Loot(RandomPowerFruit(new System.Random()), 1, at + Vector3.up * 2.1f);
        if (c.stolen > 0) Loot(Item.GoldCoin, c.stolen, at + Vector3.up * 2.4f); // your gold back
        if (c.officer != Power.None)
        {
            float fruitChance = c.Awakened ? 0.6f : 0.25f + c.officerLv / 200f;
            bool fruit = Random.value < fruitChance;
            if (fruit) Loot(FruitOf(c.officer), 1, at + Vector3.up * 2.7f);
            Loot(Item.GoldCoin, 5 + c.officerLv / 2, at + Vector3.up * 3f);
            AddBounty(c.officerLv * 2000 + (c.flagship ? 100000 : 0), null, null);
            Hint($"ชนะ{c.OfficerTitle}!" + (fruit ? $" ได้{Name(FruitOf(c.officer))}ของเขามาด้วย!" : "") + (NavyAlive > 0 ? $" ยังเหลือเรืออีก {NavyAlive} ลำ" : ""), 6f);
        }
        int gain = NavyBounty;
        AddBounty(gain, at + Vector3.up * 2.4f, null);
        if (c.officer == Power.None) Hint(NavyAlive > 0 ? $"จมเรือรัฐบาลได้หนึ่งลำ! ค่าหัว +{Baht(gain)} ยังเหลืออีกลำ!"
                           : $"จมเรือรัฐบาลได้แล้ว! ได้ของในห้องเก็บของเรือ ค่าหัวพุ่งขึ้นเป็น {Baht(bounty)}", 6f);
    }
}
