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
        public float spawnT = 3f, foamT, animT, hurtT;
        public int dir;
    }

    /// Non-null while paddling between islands.
    [System.NonSerialized] public Voyage voyage;

    const float RaftSpeed = 1.6f;
    public Vector2 RaftPos => voyage != null ? voyage.raft : Vector2.zero;
    Vector3 PlayerPos => voyage != null ? (Vector3)voyage.raft + Vector3.up * 0.25f
                       : player != null ? player.transform.position : Vector3.zero;
    Biome NextBiome => BiomeFor(island + 1);
    public float VoyageProgress => voyage == null ? 0f : Mathf.Clamp01(voyage.raft.x / voyage.length);

    void StartVoyage()
    {
        if (world != null) Destroy(world.gameObject);
        world = new GameObject("Voyage").transform;
        world.SetParent(transform);
        nodes.Clear(); enemies.Clear(); water.Clear();
        player = null; cursor = null; target = null; enemyTarget = null; pond = null; seaSpot = null;

        // Later islands are further away.
        voyage = new Voyage { length = 36f + 4f * Mathf.Min(island, 6) };
        BuildIslet(new Vector2(-7f, 0f), biome, 3.5f);                 // the island you're leaving
        BuildIslet(new Vector2(voyage.length + 7f, 0f), NextBiome, 5f); // where you're heading

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
        voyage.raftSr.sprite = PixelArt.Raft(raftLevel);
        voyage.playerSr = new GameObject("Player").AddComponent<SpriteRenderer>();
        voyage.playerSr.transform.SetParent(world);

        // Jellyfish drift along the route from the start.
        int jellies = Mathf.RoundToInt(voyage.length / 6f);
        for (int i = 0; i < jellies; i++)
            SpawnSea(SeaKind.Jellyfish, new Vector2(Random.Range(6f, voyage.length - 3f), Random.Range(-5f, 5f)));

        camPos = voyage.raft;
        Hint($"Paddle east (D / right) to reach island {island + 1}. Watch out for sharks!", 6f);
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
        if (kb != null) HandleCommonKeys(kb);

        // Paddling: the raft has some momentum, so it drifts a little.
        Vector2 input = PlayerController.ReadInput();
        bool moving = input.sqrMagnitude > 0.01f;
        Vector2 targetVel = Vector2.ClampMagnitude(input, 1f) * RaftSpeed * SpeedMultiplier * (1f + RaftSpeedBonus);
        targetVel.y *= 0.6f;
        v.vel = Vector2.Lerp(v.vel, targetVel, 1f - Mathf.Exp(-2.5f * dt));
        v.raft += v.vel * dt;
        v.raft.y = Mathf.Clamp(v.raft.y, -6.5f, 6.5f);
        v.raft.x = Mathf.Max(v.raft.x, -3f);
        if (moving)
        {
            v.animT += dt * 6f;
            if (input.y > 0.2f) v.dir = 1; else if (input.y < -0.2f) v.dir = 0;
            if (Mathf.Abs(input.x) > 0.2f) v.playerSr.flipX = input.x < 0;
        }
        v.hurtT -= dt;

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
        v.playerSr.transform.position = (Vector3)v.raft + Vector3.up * (0.2f + bob);
        v.playerSr.sortingOrder = v.raftSr.sortingOrder + 1;
        v.playerSr.sprite = PixelArt.Player(v.dir, moving ? 1 + ((int)v.animT & 1) : 0, hat, outfit);
        v.playerSr.color = v.hurtT > 0f ? new Color(1f, 0.45f, 0.45f) : Color.white;

        UpdateWaves();
        SpawnSeaCreatures(dt);

        // Target the closest creature in reach
        v.target = null;
        float best = 1.4f;
        foreach (var c in v.creatures)
        {
            float d = (c.pos - v.raft).magnitude;
            if (d < best) { best = d; v.target = c; }
        }

        if (kb != null)
        {
            bool pressed = kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            bool held = kb.eKey.isPressed || kb.spaceKey.isPressed;
            float since = Time.time - lastInteract;
            if (v.target != null)
            {
                if ((pressed && since > 0.15f) || (held && since > 0.4f)) { lastInteract = Time.time; AttackSea(v.target); }
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
        foreach (var c in v.creatures) { if (c.kind == SeaKind.Shark) sharks++; else jellies++; }
        int sharkCap = Mathf.Min(4, 1 + tier + (IsNight ? 1 : 0) + (island - 1) / 2);
        bool openSea = v.raft.x > 4f && v.raft.x < v.length - 6f; // no sharks right at the shores
        if (openSea && sharks < sharkCap)
            SpawnSea(SeaKind.Shark, v.raft + new Vector2(Random.Range(9f, 12f), Random.Range(-5f, 5f)));
        else if (jellies < 8 && v.raft.x < v.length - 8f)
            SpawnSea(SeaKind.Jellyfish, new Vector2(v.raft.x + Random.Range(10f, 14f), Random.Range(-6f, 6f)));
    }

    void AttackSea(SeaCreature c)
    {
        int dmg = WeaponDamage;
        Sfx.Play("hit", 0.6f);
        Vector3 at = c.transform.position;
        if (c.Hit(dmg, voyage.raft))
        {
            voyage.creatures.Remove(c);
            Reward(at, c.kind == SeaKind.Shark ? Item.RawMeat : (Item?)null, 2, c.xp);
        }
        else Popup($"-{dmg}", at + Vector3.up * 0.5f, Color.white);
    }
}
