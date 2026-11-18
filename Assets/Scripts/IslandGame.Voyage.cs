using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// The open sea between islands: paddle the raft east to the next island while sharks and
/// jellyfish attack. Positions here are plain world space (no tile grid).
public partial class IslandGame
{
    public class Voyage
    {
        public float length;                // distance (world units) to the next island's shore
        public Vector2 raft, vel;
        public SpriteRenderer raftSr, playerSr;
        public readonly List<SeaCreature> creatures = new List<SeaCreature>();
        public readonly List<(SpriteRenderer sr, float phase)> waves = new List<(SpriteRenderer, float)>();
        public SeaCreature target;
        public float spawnT = 3f, foamT, animT, hurtT, dashT;
        public int dir;
        public float monsterAt = -1f;       // the sea monster surfaces when the raft gets this far (-1: not this trip)
        public SeaCreature monster;
        public float navyAt = -1f;          // government warships turn up when the raft gets this far (IslandGame.Navy.cs)
        public int navyShips = 1;
        public bool navyFleet;              // the whole navy: 3-5 warships and a flagship
        public float swarmAt = -1f;         // a hunting pack of sharks surrounds the raft here (-1: not this trip)
        public int swarmLeft, swarmTotal;   // pack sharks still alive / how many there were
        public readonly List<NavyShot> shots = new List<NavyShot>();
        public readonly List<Wreck> wrecks = new List<Wreck>();
        public Vector2 prisonAt;            // the sunken prison's grate on this crossing (IslandGame.Prison.cs)
        public SpriteRenderer prisonSr;
    }

    /// Non-null while paddling between islands.
    [System.NonSerialized] public Voyage voyage;

    const float RaftSpeed = 1.6f;
    public Vector2 RaftPos => voyage != null ? voyage.raft : Vector2.zero;
    Vector3 PlayerPos => voyage != null ? (Vector3)voyage.raft + Vector3.up * 0.25f
                       : player != null ? player.transform.position : Vector3.zero;
    /// Where the current voyage goes (set when you choose it on the sea chart).
    public int destIsland = 2;
    bool NewIslandTrip => voyageVisit == null && destIsland > ReachOn(destContinent);
    /// Where you're paddling to, for the HUD and hints.
    string VoyageDestName => voyageVisit != null ? $"บ้านของ {visitNameOf(voyageVisit)}" : destContinent != continent ? $"เกาะที่ {destIsland} ({ContinentNames[destContinent]})" : $"เกาะที่ {destIsland}";
    static string visitNameOf(NetMsg m) => string.IsNullOrEmpty(m.name) ? "เพื่อน" : m.name;
    public float VoyageProgress => voyage == null ? 0f : Mathf.Clamp01(voyage.raft.x / voyage.length);

    void StartVoyage()
    {
        LeaveRoom(); // online: leave the island's room; at sea you share the waters with the other boats (JoinRoom below)
        if (!atHome) RememberIsland(); // so you can sail back to it later (from home: sailing to a friend)
        if (world != null) Destroy(world.gameObject);
        world = new GameObject("Voyage").transform;
        world.SetParent(transform);
        nodes.Clear(); enemies.Clear(); water.Clear();
        player = null; cursor = null; target = null; enemyTarget = null; pond = null; seaSpot = null;

        // Later islands are further away; going back along a route you know is quicker.
        float length = voyageVisit != null ? 30f
                     : NewIslandTrip ? 36f + 4f * Mathf.Min(destIsland, 6) : 22f + 4f * Mathf.Min(Mathf.Abs(destIsland - island), 4);
        if (voyageVisit == null && destContinent != continent) length += 16f; // across the open ocean to another continent
        voyage = new Voyage { length = length };
        BuildIslet(new Vector2(-7f, 0f), biome, 3.5f);                             // the island you're leaving
        BuildIslet(new Vector2(voyage.length + 7f, 0f), voyageVisit != null ? Biome.Tropical : BiomeOf(destIsland, destContinent), 5f); // where you're heading

        var wavesRoot = new GameObject("Waves").transform;
        wavesRoot.SetParent(world);
        for (int i = 0; i < 120; i++)
        {
            var sr = new GameObject("Wave").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(wavesRoot);
            sr.sprite = PixelArt.Wave();
            sr.sortingOrder = -32500; // under everything, over the sea background
            sr.transform.position = new Vector3(Random.Range(-14f, 14f), Random.Range(-8f, 8f), 0f);
            voyage.waves.Add((sr, Random.value * 10f));
        }

        voyage.raftSr = new GameObject("Raft").AddComponent<SpriteRenderer>();
        voyage.raftSr.transform.SetParent(world);
        voyage.raftSr.sprite = BoatSprite;
        voyage.playerSr = new GameObject("Player").AddComponent<SpriteRenderer>();
        voyage.playerSr.transform.SetParent(world);

        // Jellyfish drift along the route from the start.
        int jellies = Mathf.RoundToInt(voyage.length / 6f);
        for (int i = 0; i < jellies; i++)
            SpawnSea(SeaKind.Jellyfish, new Vector2(Random.Range(6f, voyage.length - 3f), Random.Range(-5f, 5f)));

        // Some crossings have a sea monster lurking: likelier further out, at night and across the ocean.
        // The sea monster lives in deep water: almost always met crossing the ocean to another
        // continent, and sometimes on the way between islands from island 7 on. Never nearer home.
        float monsterChance = voyageVisit != null ? 0f
            : destContinent != continent ? 0.9f
            : Mathf.Max(island, destIsland) >= 7 ? 0.3f + (IsNight ? 0.1f : 0f) : 0f;
        if (Random.value < monsterChance) voyage.monsterAt = voyage.length * Random.Range(0.3f, 0.6f);
        // A big pack of hunting sharks (ฝูงฉลามคลั่ง): rare near home, more common far out, at
        // night and across the ocean. Never on the way to a friend's home.
        int farOut = Mathf.Max(island, destIsland);
        float swarmChance = voyageVisit != null || farOut < 2 ? 0f
            : destContinent != continent ? 0.3f
            : 0.05f + 0.012f * Mathf.Min(farOut, 15) + (IsNight ? 0.08f : 0f);
        if (voyage.monsterAt > 0f) swarmChance *= 0.4f;
        if (Random.value < swarmChance) voyage.swarmAt = voyage.length * Random.Range(0.2f, 0.55f);
        PlanNavy(voyage); // government warships hunting wanted sailors (IslandGame.Navy.cs)
        PlanPrisonGate(voyage); // the sunken prison, on the volcano continent's waters (IslandGame.Prison.cs)

        camPos = voyage.raft;
        JoinRoom(); // online: everyone at sea on these waters sees each other's boats (IslandGame.Net.cs)
        Hint($"พายไปทางตะวันออก (D / ขวา) เพื่อไป{VoyageDestName} ระวังฉลามด้วย!" + (power != Power.None ? " (ใช้พลัง R / ท่าพิเศษ T ได้)" : ""), 6f);
    }

    void EndVoyage() => voyage = null; // the world itself is replaced by StartGame

    /// A small decorative island (the one you left / the one you're heading to).
    void BuildIslet(Vector2 centerWorld, Biome b, float radius)
    {
        Vector2 cg = WorldDeltaToGrid(centerWorld); // GridToWorld is linear, so this maps points too
        int r = Mathf.CeilToInt(radius);
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > radius) continue;
                var sr = new GameObject("Tile").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(world);
                sr.sprite = PixelArt.Land(d < radius - 1.5f, Random.Range(0, 4), 0, b);
                sr.sortingOrder = -31000 + dx + dy;
                sr.transform.position = GridToWorld(cg + new Vector2(dx, dy));
            }
        int trees = radius > 4f ? 4 : 2;
        for (int i = 0; i < trees; i++)
        {
            Vector2 off = Random.insideUnitCircle * (radius - 2f);
            var sr = new GameObject("Tree").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(world);
            sr.sprite = i == 0 ? PixelArt.FruitTree(true, b) : PixelArt.Tree(b);
            Vector3 p = GridToWorld(cg + off);
            sr.transform.position = p;
            sr.sortingOrder = SortOrder(p.y);
        }
    }

    void SpawnSea(SeaKind kind, Vector2 p)
    {
        var c = new GameObject(kind.ToString()).AddComponent<SeaCreature>();
        c.transform.SetParent(world);
        c.Init(kind, p, tier);
        voyage.creatures.Add(c);
    }

    void UpdateVoyage(float dt, Keyboard kb)
    {
        var v = voyage;
        v.creatures.RemoveAll(c => c == null);
        if (kb != null && !chatOpen)
        {
            HandleCommonKeys(kb);
            if (kb.rKey.wasPressedThisFrame) UsePower();        // powers work on the raft too (IslandGame.SeaPower.cs)
            if (kb.tKey.wasPressedThisFrame) UsePowerAttack();
            if (kb.yKey.wasPressedThisFrame) UseAwaken();
        }

        // Paddling: the raft has some momentum, so it drifts a little.
        Vector2 input = PlayerController.ReadInput();
        bool moving = input.sqrMagnitude > 0.01f;
        Vector2 targetVel = Vector2.ClampMagnitude(input, 1f) * RaftSpeed * SpeedMultiplier * (1f + RaftSpeedBonus + (Count(Item.CanvasSail) > 0 ? 0.25f : 0f)); // canvas sail: +25%
        targetVel.y *= 0.6f;
        v.vel = Vector2.Lerp(v.vel, targetVel, 1f - Mathf.Exp(-2.5f * dt));
        v.raft += v.vel * dt;
        v.raft.y = Mathf.Clamp(v.raft.y, -6.5f, 6.5f);
        v.raft.x = Mathf.Max(v.raft.x, -3f);
        CrewVoyageTick(dt); // a passenger rides the captain's boat; the captain shows the crew on deck (IslandGame.Crew.cs)
        if (moving)
        {
            v.animT += dt * 6f;
            if (input.y > 0.2f) v.dir = 1; else if (input.y < -0.2f) v.dir = 0;
            if (Mathf.Abs(input.x) > 0.2f) v.playerSr.flipX = input.x < 0;
        }
        v.hurtT -= dt;
        v.dashT -= dt;

        // Foam trail
        if (v.vel.magnitude > 0.4f)
        {
            v.foamT -= dt;
            if (v.foamT <= 0f)
            {
                v.foamT = 0.07f;
                Fx.Burst((Vector3)(v.raft - v.vel.normalized * 0.5f), new Color(1f, 1f, 1f, 0.8f), 1, 0.2f, 0.3f, 0.6f, 0f);
            }
        }

        // Raft + player visuals, bobbing on the waves
        float bob = Mathf.Sin(Time.time * 2f) * 0.04f;
        v.raftSr.transform.position = (Vector3)v.raft + Vector3.up * bob;
        v.raftSr.sortingOrder = SortOrder(v.raft.y);
        v.playerSr.transform.position = (Vector3)v.raft + Vector3.up * (PixelArt.BoatDeck(BoatLookNow) + bob); // standing on the deck
        v.playerSr.sortingOrder = v.raftSr.sortingOrder + 1;
        v.playerSr.sprite = PixelArt.Player(v.dir, moving ? 1 + ((int)v.animT & 1) : 0, hat, outfit, lookCode);
        v.playerSr.color = v.hurtT > 0f ? new Color(1f, 0.45f, 0.45f) : Invisible ? new Color(0.7f, 0.65f, 1f, 0.35f) : Color.white; // see-through while invisible

        UpdateWaves();
        SpawnSeaCreatures(dt);
        if (v.monsterAt > 0f && v.raft.x >= v.monsterAt) SpawnMonster();
        if (v.swarmAt > 0f && v.raft.x >= v.swarmAt) SpawnSwarm();
        UpdateNavy(dt);

        // Target the closest creature in reach (big ones reach further; the monster only while it's up)
        v.target = null;
        float best = 1.4f;
        foreach (var c in v.creatures)
        {
            if (!c.Hittable) continue;
            float d = (c.pos - v.raft).magnitude - c.Reach;
            if (d < best) { best = d; v.target = c; }
        }

        if (!chatOpen)
        {
            bool pressed = touchActionPressed || (kb != null && (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame));
            bool held = touchActionHeld || (kb != null && (kb.eKey.isPressed || kb.spaceKey.isPressed));
            float since = Time.time - lastInteract;
            if (PrisonGateTick(v, pressed)) return; // diving into the sunken prison (IslandGame.Prison.cs)
            if (v.target != null)
            {
                if ((pressed || held) && since >= AttackInterval) { lastInteract = Time.time; AttackSea(v.target); }
            }
            else if (HasSpear && ((pressed && since > 0.15f) || (held && since > 0.8f)))
            {
                lastInteract = Time.time;
                FishAt((Vector3)(v.raft + new Vector2(v.playerSr.flipX ? -0.9f : 0.9f, -0.2f)));
            }
        }

        if (v.raft.x >= v.length) BeginSailFade(true); // reached the shore
    }

    /// Keep the wave glints around the camera so the sea looks endless.
    void UpdateWaves()
    {
        float hh = cam.orthographicSize + 1f, hw = hh * cam.aspect + 1f;
        Vector3 c = cam.transform.position;
        foreach (var (sr, phase) in voyage.waves)
        {
            Vector3 p = sr.transform.position;
            if (p.x < c.x - hw) p.x += 2f * hw; else if (p.x > c.x + hw) p.x -= 2f * hw;
            if (p.y < c.y - hh) p.y += 2f * hh; else if (p.y > c.y + hh) p.y -= 2f * hh;
            sr.transform.position = p;
            var col = Color.white;
            col.a = 0.25f + 0.35f * Mathf.Sin(Time.time * 1.5f + phase);
            sr.color = col;
        }
    }

    void SpawnSeaCreatures(float dt)
    {
        var v = voyage;
        v.spawnT -= dt;
        if (v.spawnT > 0f) return;
        v.spawnT = 4f;
        int sharks = 0, jellies = 0;
        foreach (var c in v.creatures) { if (c.kind == SeaKind.Shark) sharks++; else if (c.kind == SeaKind.Jellyfish) jellies++; }
        int sharkCap = Mathf.Min(4, 1 + tier + (IsNight ? 1 : 0) + (island - 1) / 2);
        if (v.monster != null) sharkCap = Mathf.Max(0, sharkCap - 2); // the sharks keep clear of it
        if (NavyAlive > 0) sharkCap = Mathf.Max(0, sharkCap - 1);   // and of the guns
        bool openSea = v.raft.x > 4f && v.raft.x < v.length - 6f; // no sharks right at the shores
        if (openSea && sharks < sharkCap)
            SpawnSea(SeaKind.Shark, v.raft + new Vector2(Random.Range(9f, 12f), Random.Range(-5f, 5f)));
        else if (jellies < 8 && v.raft.x < v.length - 8f)
            SpawnSea(SeaKind.Jellyfish, new Vector2(v.raft.x + Random.Range(10f, 14f), Random.Range(-6f, 6f)));
    }

    void AttackSea(SeaCreature c)
    {
        Sfx.Play("hit", 0.6f);
        HitSea(c, RollDamage(c.transform.position), Color.white);
    }

    /// Hit a sea creature (weapon or power). Returns true if it died.
    bool HitSea(SeaCreature c, int dmg, Color popupCol)
    {
        dmg = AwakenDmg(dmg);
        if (c == null || !c.Hittable) return false;
        Vector3 at = c.transform.position;
        bool navy = c.kind == SeaKind.Navy;
        if (navy) AddBounty(NavyHitBounty, null, null); // attacking a government ship: they write it down
        if (c.shieldHits > 0) // a Bubble officer's shield takes the hit (IslandGame.Navy.cs)
        {
            c.shieldHits--;
            Popup(c.shieldHits > 0 ? $"โล่ฟองกันไว้! (เหลือ {c.shieldHits})" : "โล่ฟองแตกแล้ว!", at + Vector3.up * c.Tall, new Color(0.6f, 0.9f, 1f));
            Fx.Burst(at + Vector3.up * 0.8f, new Color(0.75f, 0.95f, 1f, 0.8f), 12, 1.2f, 1.6f, 0.6f, 1f);
            Sfx.Play("splash", 0.35f);
            return false;
        }
        if (!c.Hit(dmg, voyage.raft)) { Popup($"-{dmg}", at + Vector3.up * c.Tall, popupCol); return false; }
        voyage.creatures.Remove(c);
        if (c.IsMonster) MonsterSlain(c, at);
        else if (navy) NavySunk(c, at);
        else Reward(at, c.kind == SeaKind.Shark ? Item.RawMeat : (Item?)null, 2, c.xp);
        if (c.frenzy) PackSharkDown(at);
        return true;
    }

    // ------------------------------------------------------------------ the sea monster
    void SpawnMonster()
    {
        var v = voyage;
        v.monsterAt = -1f;
        if (v.raft.x > v.length - 8f) return; // too close to shore now
        var p = v.raft + new Vector2(Random.Range(4.5f, 6f), Random.Range(-1f, 1f) * 2.5f);
        p.y = Mathf.Clamp(p.y, -6f, 6f);
        var c = new GameObject("SeaMonster").AddComponent<SeaCreature>();
        c.transform.SetParent(world);
        c.Init(SeaKind.Serpent, p, tier);
        v.creatures.Add(c);
        v.monster = c;
        Hint("น้ำทะเลปั่นป่วน... งูทะเลยักษ์โผล่ขึ้นมาแล้ว! ตีมันตอนโผล่พ้นน้ำ หรือพายหนีไปให้ถึงฝั่ง", 7f);
        Sfx.Play("splash", 1f);
        Shake(0.1f, 0.6f);
        ScreenFlash(new Color(0.05f, 0.15f, 0.35f, 0.45f), 0.8f);
        Fx.Burst((Vector3)p, new Color(0.85f, 0.95f, 1f), 30, 2.2f, 3f, 0.9f, 4f);
    }

    // ------------------------------------------------------------------ a pack of sharks
    void SpawnSwarm()
    {
        var v = voyage;
        v.swarmAt = -1f;
        if (v.raft.x > v.length - 10f) return; // too close to shore now
        bool ocean = destContinent != continent;
        int n = Mathf.Min(14, 6 + 2 * tier + Mathf.Min(Mathf.Max(island, destIsland), 10) / 2 + (ocean ? 3 : 0) + (IsNight ? 1 : 0));
        for (int i = 0; i < n; i++) // a ring closing in around the raft, thicker ahead
        {
            float a = i / (float)n * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            var p = v.raft + new Vector2(Mathf.Cos(a) * Random.Range(7f, 10f) + 3f, Mathf.Sin(a) * Random.Range(4f, 6f));
            p.y = Mathf.Clamp(p.y, -6.3f, 6.3f);
            var c = new GameObject("PackShark").AddComponent<SeaCreature>();
            c.transform.SetParent(world);
            c.Init(SeaKind.Shark, p, tier);
            c.MakeFrenzy();
            v.creatures.Add(c);
        }
        for (int i = 0; i < 6; i++) // a bloom of jellyfish drifts in with them
            SpawnSea(SeaKind.Jellyfish, new Vector2(v.raft.x + Random.Range(5f, 14f), Random.Range(-6f, 6f)));
        v.swarmLeft = v.swarmTotal = n;
        Hint($"ครีบนับสิบโผล่ขึ้นรอบเรือ... ฝูงฉลามคลั่ง {n} ตัวล้อมเข้ามาแล้ว! ปราบให้หมดเพื่อรับรางวัล หรือพายหนีให้ถึงฝั่ง", 7f);
        Sfx.Play("splash", 1f);
        Shake(0.08f, 0.5f);
        ScreenFlash(new Color(0.35f, 0.05f, 0.05f, 0.35f), 0.7f);
    }

    void PackSharkDown(Vector3 at)
    {
        var v = voyage;
        if (v == null || v.swarmLeft <= 0) return;
        v.swarmLeft--;
        if (v.swarmLeft > 0)
        {
            if (v.swarmLeft == v.swarmTotal / 2) Hint($"ฝูงฉลามเหลืออีกครึ่งเดียว ({v.swarmLeft} ตัว)!", 3f);
            return;
        }
        Sfx.Play("levelup", 0.8f);
        ScreenFlash(new Color(1f, 0.95f, 0.7f, 0.3f), 0.5f);
        Reward(at, Item.RawMeat, 4 + v.swarmTotal / 2, 15 * v.swarmTotal);
        Loot(Item.GoldCoin, 4 + v.swarmTotal + 3 * tier, at + Vector3.up * 0.9f);
        if (Random.value < 0.5f) Loot(Item.Pearl, 1 + tier, at + Vector3.up * 1.3f);
        if (Random.value < 0.08f) Loot(RandomPowerFruit(new System.Random()), 1, at + Vector3.up * 1.7f);
        Hint($"ปราบฝูงฉลามคลั่งได้ทั้ง {v.swarmTotal} ตัว! ได้รางวัลจากฝูง", 5f);
    }

    void MonsterSlain(SeaCreature c, Vector3 at)
    {
        voyage.monster = null;
        Sfx.Play("levelup", 0.8f);
        Shake(0.15f, 0.5f);
        ScreenFlash(new Color(1f, 0.95f, 0.7f, 0.35f), 0.5f);
        Fx.Burst(at + Vector3.up * 0.8f, new Color(0.4f, 0.85f, 0.7f), 40, 2.5f, 3f, 1.1f, 3f, 2f);
        Fx.Burst(at, new Color(0.85f, 0.95f, 1f), 30, 2.5f, 2.5f, 0.8f, 4f);
        Reward(at, Item.Pearl, 2 + tier, c.xp);
        Loot(Item.GoldCoin, 5 + 3 * tier, at + Vector3.up * 0.9f);
        Loot(Item.RawFish, 3, at + Vector3.up * 1.3f);
        if (Random.value < 0.25f) Loot(RandomPowerFruit(new System.Random()), 1, at + Vector3.up * 1.7f);
        Hint("ปราบงูทะเลยักษ์ได้แล้ว! ได้ไข่มุกกับเหรียญทองมาเพียบ", 5f);
    }

    void Loot(Item it, int n, Vector3 at)
    {
        inv[it] = Count(it) + n;
        Popup($"+{n} {Name(it)}", at, new Color(1f, 0.95f, 0.6f));
    }
}
