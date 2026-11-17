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
            c.Toughen(1f + heat * 1.2f + 0.08f * Mathf.Min(navyWins, 10), 1f + heat * 0.6f);
            v.creatures.Add(c);
        }
        string who = v.navyShips > 1 ? "กองเรือรัฐบาล" : "เรือรัฐบาล";
        Hint(bounty > 0 ? $"{who}แล่นเข้ามา! \"หยุดนะ {playerName}! ค่าหัว {Baht(bounty)} ของเจ้าจบที่นี่!\" หลบวงแดงแล้วเข้าไปตีเรือ"
                        : $"{who}แล่นเข้ามาตรวจทะเล! พวกเขายิงปืนใหญ่ใส่ หลบวงแดงแล้วเข้าไปตีเรือ หรือพายหนีให้ถึงฝั่ง", 7f);
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
            s.ball.transform.position = new Vector3(p.x, p.y + Mathf.Sin(k * Mathf.PI) * 2.4f, 0f);
            s.mark.color = new Color(1f, 0.25f, 0.2f, 0.35f + 0.45f * k + 0.15f * Mathf.Sin(Time.time * 18f));
            if (k < 1f) continue;
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
        int gain = NavyBounty;
        AddBounty(gain, at + Vector3.up * 2.4f, null);
        Hint(NavyAlive > 0 ? $"จมเรือรัฐบาลได้หนึ่งลำ! ค่าหัว +{Baht(gain)} ยังเหลืออีกลำ!"
                           : $"จมเรือรัฐบาลได้แล้ว! ได้ของในห้องเก็บของเรือ ค่าหัวพุ่งขึ้นเป็น {Baht(bounty)}", 6f);
    }
}
