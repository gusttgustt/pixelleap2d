using UnityEngine;

public enum EnemyKind { Crab, Snake, Boar, Mimic, SporeSpider, ShroomKing, Barnacle, HermitCrab, StormCloud, ChimeBird, LanternFish, StoneSquid, WindupDoll, TimeBeetle, Snowman, Snowling, FrostBat }

/// Hostile creature. Chases the player when close, wanders otherwise, is scared of campfires.
///   Crab  - night only, comes up on beaches, walks back into the sea at sunrise.
///   Snake - Jungle, fast but fragile.
///   Boar  - Volcano, slow to kill and hits hard.
///   Mimic       - Mushroom island: sits still looking like a glowing mushroom, jumps you up close.
///   SporeSpider - Mushroom island: poisonous bite, leaves clouds of poison spores behind.
///   ShroomKing  - Mushroom island boss: big, slow, puffs spore clouds all around. Never wanders off.
///   Barnacle    - Turtle island: slow little crawler; its bite sticks to you and slows you down.
///   HermitCrab  - Turtle island: hides in a huge shell; break the shell first, then it's soft (and quick).
///   StormCloud  - Sky island: drifts closer, crackles for a moment, then charges straight at you.
///   ChimeBird   - Sky island: harmless peck, but it steals from your bag and flies off. Catch it to get it back.
///   LanternFish - Bubble island: its glowing lure tugs you towards it (IslandGame.Bubble.cs), then it bites.
///   StoneSquid  - Bubble island: looks like a rock until you're close; its ink blinds you for a moment.
///   WindupDoll  - Hourglass island: frozen while you face it, quick as anything while you don't.
///   TimeBeetle  - Hourglass island: blinks a couple of tiles towards you every few seconds.
///   Snowman     - Ice island: slow and sturdy; beaten, it bursts into two quick little snowlings.
///   FrostBat    - Ice island: swoops in fast; its bite chills you (warmth drops).
public class Enemy : MonoBehaviour
{
    public EnemyKind kind;
    public Vector2 grid;
    public int hp, maxHp, xp;

    float speed, damage, aggro;
    const float FearRadius = 3.2f; // won't come this close to a campfire

    SpriteRenderer sr;
    float attackCd = 1f, animT, hurtT, height, wanderT;
    Vector2 wanderDir;
    int frame;

    // Online: the island's host runs every creature; other players see "puppets" that just
    // follow the positions the host sends, and pass their hits on to the host.
    public int netId;
    public bool puppet;
    Vector2 netTarget;

    public string DisplayName => (alpha ? "จ่าฝูง" : "") + BaseName + (variant switch
    {
        IslandGame.Variant.Giant => "ยักษ์", IslandGame.Variant.Fiery => "เพลิง", IslandGame.Variant.Armored => "เกราะ", _ => "",
    });
    /// This island's tougher breed (see IslandGame.Traits.cs), and the pack leader.
    public IslandGame.Variant variant;
    public bool alpha;
    Color baseColor = Color.white;

    public void SetVariant(IslandGame.Variant v)
    {
        variant = v;
        switch (v)
        {
            case IslandGame.Variant.Giant: maxHp *= 2; damage *= 1.3f; xp *= 2; transform.localScale = Vector3.one * 1.35f; break;
            case IslandGame.Variant.Fiery: damage *= 1.5f; xp = Mathf.RoundToInt(xp * 1.5f); baseColor = new Color(1f, 0.6f, 0.45f); break;
            case IslandGame.Variant.Armored: maxHp = Mathf.RoundToInt(maxHp * 1.6f); xp *= 2; baseColor = new Color(0.72f, 0.78f, 0.9f); break;
        }
        hp = maxHp;
    }

    public void MakeAlpha()
    {
        alpha = true;
        maxHp *= 7; damage *= 1.6f; xp *= 5;
        hp = maxHp;
        transform.localScale = Vector3.one * 1.8f;
        baseColor = new Color(1f, 0.92f, 0.7f);
    }

    string BaseName => kind switch
    {
        EnemyKind.Crab => "ปู", EnemyKind.Snake => "งู", EnemyKind.Boar => "หมูป่า",
        EnemyKind.Mimic => "เห็ดหลอก", EnemyKind.SporeSpider => "แมงมุมสปอร์", EnemyKind.ShroomKing => "ราชาเห็ด",
        EnemyKind.Barnacle => "เพรียงคลาน", EnemyKind.HermitCrab => "ปูเสฉวนหอยยักษ์",
        EnemyKind.StormCloud => "ก้อนเมฆสายฟ้า", EnemyKind.ChimeBird => "นกกระดิ่งลม",
        EnemyKind.LanternFish => "ปลาตะเกียง", EnemyKind.StoneSquid => "หมึกหิน",
        EnemyKind.WindupDoll => "ตุ๊กตาไขลาน", EnemyKind.TimeBeetle => "ด้วงทรายเวลา",
        EnemyKind.Snowman => "มนุษย์หิมะละลาย", EnemyKind.Snowling => "ตัวหิมะจิ๋ว", _ => "ค้างคาวน้ำแข็ง",
    };
    /// A mimic that hasn't sprung yet: it can't be targeted and doesn't move.
    public bool Disguised { get; private set; }
    float sporeT = 3f;
    float stunT;

    /// Rooted in place (thorns): can't move or bite.
    public void Stun(float seconds) => stunT = Mathf.Max(stunT, seconds);
    /// Knocked away (magnet pulse), stopping at walls and water.
    public void Push(Vector2 by)
    {
        for (int i = 0; i < 8; i++) { var next = grid + by / 8f; if (CanGo(next)) grid = next; else break; }
    }
    /// Hermit crab: its shell soaks up blows until it breaks.
    public int shellHp;
    /// Chime bird: what it ran off with.
    public Item stolenItem;
    public int stolenCount;
    float chargeCd = 2f, windupT, dashT, fleeT;
    Vector2 dashDir;
    public bool Flip => sr != null && sr.flipX;

    public void Init(EnemyKind k, Vector2 g, int tier, int id = 0, bool asPuppet = false)
    {
        kind = k;
        grid = g;
        netId = id;
        puppet = asPuppet;
        netTarget = g;
        (maxHp, damage, speed, aggro, xp) = k switch
        {
            EnemyKind.Snake => (4, 6f, 1.7f, 6f, 15),
            EnemyKind.Boar => (8, 12f, 1.5f, 7f, 25),
            EnemyKind.Mimic => (5, 9f, 1.9f, 6f, 18),
            EnemyKind.SporeSpider => (5, 5f, 1.6f, 7f, 20),
            EnemyKind.ShroomKing => (45, 16f, 0.9f, 8f, 120),
            EnemyKind.Barnacle => (3, 4f, 0.8f, 5f, 8),
            EnemyKind.HermitCrab => (5, 9f, 1.0f, 6f, 24),
            EnemyKind.StormCloud => (6, 12f, 1.1f, 7f, 26),
            EnemyKind.ChimeBird => (3, 2f, 2.1f, 7f, 16),
            EnemyKind.LanternFish => (5, 10f, 1.0f, 6f, 24),
            EnemyKind.StoneSquid => (7, 8f, 1.3f, 6f, 28),
            EnemyKind.WindupDoll => (6, 11f, 2.3f, 9f, 30),
            EnemyKind.TimeBeetle => (5, 8f, 1.0f, 7f, 22),
            EnemyKind.Snowman => (9, 10f, 0.8f, 6f, 30),
            EnemyKind.Snowling => (2, 4f, 2.0f, 8f, 8),
            EnemyKind.FrostBat => (4, 6f, 2.2f, 8f, 20),
            _ => (3, 8f, 1.25f, 9f, 10),
        };
        Disguised = (k == EnemyKind.Mimic || k == EnemyKind.StoneSquid) && !asPuppet;
        shellHp = k == EnemyKind.HermitCrab ? Mathf.RoundToInt(8 * (1f + 0.5f * tier)) : 0;
        // Later islands (second lap and beyond) are tougher and give more XP.
        // further islands (higher island level) have tougher creatures that give more XP
        var isl = IslandGame.I;
        maxHp = Mathf.RoundToInt(maxHp * isl.EnemyHpScale);
        damage *= isl.EnemyDamageScale;
        xp = Mathf.RoundToInt(xp * isl.EnemyHpScale);
        hp = maxHp;

        sr = gameObject.AddComponent<SpriteRenderer>();
        height = IslandGame.I.HeightAt(g);
        Apply();
    }

    /// Puppets: the latest state from the host.
    public void SetNetState(Vector2 g, int newHp, int newMax, bool flip)
    {
        netTarget = g;
        hp = newHp;
        maxHp = Mathf.Max(1, newMax);
        if (sr != null) sr.flipX = flip;
    }

    void Update()
    {
        var game = IslandGame.I;
        if (puppet)
        {
            hurtT -= Time.deltaTime;
            Vector2 before = grid;
            grid = Vector2.Lerp(grid, netTarget, 1f - Mathf.Exp(-10f * Time.deltaTime));
            if ((grid - before).sqrMagnitude > 0.00001f) { animT += Time.deltaTime * 10f; frame = (int)animT & 1; }
            Apply();
            return;
        }
        // Offline, pauses (talking, sleeping...) freeze the creatures. Online the host keeps them going for everyone.
        if (game.player == null || (!game.Online && (game.dead || game.Busy))) { Apply(); return; }
        float dt = Time.deltaTime;
        attackCd -= dt;
        hurtT -= dt;
        if (stunT > 0f) { stunT -= dt; Apply(); return; }

        var (targetGrid, targetWho) = game.NearestPlayer(grid); // chase whichever player is closest
        Vector2 toPlayer = targetGrid - grid;
        float dist = toPlayer.magnitude;

        if (Disguised)
        {
            if (dist > 1.7f) { Apply(); return; } // just a mushroom... nothing to see here
            Reveal();
        }
        if (kind == EnemyKind.WindupDoll && targetWho == 0 && game.player != null && dist < 9f)
        {
            Vector2 look = game.player.facing.normalized, toMe = (grid - targetGrid).normalized;
            if (Vector2.Dot(look, toMe) > 0.45f) { frame = 2; Apply(); return; } // you're looking: freeze mid-step
        }
        if (kind == EnemyKind.TimeBeetle)
        {
            chargeCd -= dt;
            if (chargeCd <= 0f && dist < 7f && dist > 1.2f)
            {
                chargeCd = 3f;
                Vector2 jump = grid + toPlayer / dist * Mathf.Min(2f, dist - 0.9f);
                if (CanGo(jump))
                {
                    Fx.Burst(transform.position + Vector3.up * 0.2f, new Color(1f, 0.85f, 0.4f), 8, 0.6f, 1f);
                    grid = jump;
                    Fx.Burst(IslandGame.GridToWorld(grid) + Vector3.up * 0.2f, new Color(1f, 0.85f, 0.4f), 8, 0.6f, 1f);
                }
            }
        }
        if (kind == EnemyKind.StormCloud) // crackle, then charge
        {
            chargeCd -= dt;
            if (dashT > 0f)
            {
                dashT -= dt;
                Vector2 step = dashDir * 6f * dt;
                if (CanGo(grid + step)) grid += step; else dashT = 0f;
                if (dist < 0.7f && attackCd <= 0f && targetWho == 0) { attackCd = 1.2f; game.DamagePlayer(damage, "สายฟ้า!"); }
                Apply();
                return;
            }
            if (windupT > 0f)
            {
                windupT -= dt;
                if (windupT <= 0f) { dashT = 0.4f; Sfx.Play("hit", 0.4f); }
                frame = 2; // flashing
                Apply();
                return;
            }
            if (chargeCd <= 0f && dist < 4.5f && dist > 1f && game.DistanceToFire(grid) > FearRadius)
            {
                chargeCd = 4f;
                windupT = 0.7f;
                dashDir = toPlayer / dist;
                Apply();
                return;
            }
        }
        if (kind == EnemyKind.ChimeBird && fleeT > 0f) // off with the loot
        {
            fleeT -= dt;
            Vector2 away = dist > 0.01f ? -toPlayer / dist : Random.insideUnitCircle.normalized;
            Vector2 next = grid + away * speed * 1.2f * dt;
            if (CanGo(next)) grid = next;
            animT += dt * 14f; frame = (int)animT & 1;
            if (fleeT <= 0f && dist > 18f && stolenCount > 0) { Destroy(gameObject); return; } // gone for good
            if (fleeT <= 0f && stolenCount > 0) fleeT = 3f; // keeps running while it holds your stuff
            Apply();
            return;
        }
        if (kind is EnemyKind.SporeSpider or EnemyKind.ShroomKing)
        {
            sporeT -= dt;
            if (sporeT <= 0f && dist < 14f)
            {
                if (kind == EnemyKind.SporeSpider) { sporeT = Random.Range(3.5f, 5.5f); game.SpawnSporeCloud(grid); }
                else
                {
                    sporeT = 5.5f; // the king puffs a ring of spores
                    for (int i = 0; i < 4; i++) game.SpawnSporeCloud(grid + new Vector2(Mathf.Cos(i * 1.57f + Time.time), Mathf.Sin(i * 1.57f + Time.time)) * 1.6f);
                    Fx.Burst(transform.position + Vector3.up * 0.6f, new Color(0.6f, 1f, 0.5f), 14, 1.4f, 1.2f, 1f, 2f);
                }
            }
        }
        Vector2 dir = Vector2.zero;
        float pace = 1f;

        if (kind == EnemyKind.Crab && !game.IsNight)
        {
            // head for the sea and disappear once off the land
            dir = (grid - game.Center).normalized;
            Vector2 next = grid + dir * speed * dt;
            if (!game.IsLand(next)) { Fx.Burst(transform.position, PixelArt.WaterColor, 6); Destroy(gameObject); return; }
            grid = next;
        }
        else
        {
            if (dist > 22f && kind != EnemyKind.ShroomKing && !alpha) { Destroy(gameObject); return; } // far away: despawn, new ones spawn closer

            float fireDist = game.DistanceToFire(grid);
            if (fireDist < FearRadius) dir = game.AwayFromFire(grid);       // run from the fire
            else if (dist < aggro) { if (dist > 0.55f) dir = toPlayer / dist; } // chase
            else
            {
                wanderT -= dt;
                if (wanderT <= 0f)
                {
                    wanderT = Random.Range(1.5f, 3f);
                    wanderDir = Random.value < 0.3f ? Vector2.zero : Random.insideUnitCircle.normalized;
                }
                dir = wanderDir;
                pace = 0.4f;
            }

            if (dir != Vector2.zero)
            {
                Vector2 next = grid + dir * speed * pace * dt;
                bool intoFire = fireDist >= FearRadius && game.DistanceToFire(next) < FearRadius;
                if (!intoFire)
                {
                    if (CanGo(next)) grid = next;
                    else if (CanGo(grid + new Vector2(next.x - grid.x, 0f))) grid.x = next.x;
                    else if (CanGo(grid + new Vector2(0f, next.y - grid.y))) grid.y = next.y;
                    else wanderT = 0f;
                }
            }

            if (dist < 0.65f && attackCd <= 0f)
            {
                attackCd = 1.3f;
                if (targetWho == 0)
                {
                    game.DamagePlayer(damage, $"{DisplayName}!");
                    if (kind == EnemyKind.SporeSpider) game.Poison(5f);
                    if (kind == EnemyKind.Barnacle) game.Slow(2.5f);
                    if (kind == EnemyKind.StoneSquid) game.Ink(3f);
                    if (kind == EnemyKind.FrostBat) game.Chill(15f);
                    if (kind == EnemyKind.ChimeBird && stolenCount == 0)
                    {
                        (stolenItem, stolenCount) = game.StealItem();
                        if (stolenCount > 0) fleeT = 6f;
                    }
                }
                else game.NetDamage(targetWho, damage, $"{DisplayName}!"); // bite another player
            }
        }

        if (dir != Vector2.zero)
        {
            animT += dt * 10f * pace;
            frame = (int)animT & 1;
            Vector3 w = IslandGame.GridToWorld(grid + dir) - IslandGame.GridToWorld(grid);
            if (Mathf.Abs(w.x) > 0.01f) sr.flipX = w.x < 0;
        }
        Apply();
    }

    void Reveal()
    {
        Disguised = false;
        attackCd = 0.4f;
        Fx.Burst(transform.position + Vector3.up * 0.3f, new Color(0.5f, 1f, 0.9f), 10, 1.2f, 1.4f);
        Sfx.Play("hurt", 0.4f);
    }

    bool CanGo(Vector2 g) => IslandGame.I.CanWalk(g, 0.2f) && IslandGame.I.CanClimb(grid, g);

    void Apply()
    {
        height = Mathf.Lerp(height, IslandGame.I.HeightAt(grid), 1f - Mathf.Exp(-12f * Time.deltaTime));
        Vector3 flat = IslandGame.GridToWorld(grid);
        transform.position = flat + Vector3.up * height;
        sr.sortingOrder = IslandGame.SortOrder(flat.y);
        sr.sprite = PixelArt.Enemy(kind, Disguised ? 2 : kind == EnemyKind.HermitCrab && shellHp <= 0 ? frame + 2 : frame);
        sr.color = hurtT > 0f ? new Color(1f, 0.4f, 0.4f) : baseColor;
    }

    /// Returns true if the enemy died.
    public bool Hit(int dmg, Vector2 from)
    {
        hurtT = 0.15f;
        if (Disguised) Reveal();
        Fx.Burst(transform.position + Vector3.up * 0.15f, kind == EnemyKind.Snake ? new Color(0.4f, 0.7f, 0.3f) : new Color(1f, 0.5f, 0.3f), 6);
        if (puppet) return false; // the host decides; it tells everyone when it dies
        if (shellHp > 0) // the shell takes the hit
        {
            shellHp -= dmg;
            Fx.Burst(transform.position + Vector3.up * 0.3f, new Color(0.9f, 0.85f, 0.75f), 5);
            if (shellHp <= 0)
            {
                speed = 1.9f; // naked and scared: much quicker
                Fx.Burst(transform.position + Vector3.up * 0.3f, new Color(1f, 0.9f, 0.8f), 16, 1.4f, 1.6f);
                Sfx.Play("chop", 0.6f);
            }
            return false;
        }
        if (variant == IslandGame.Variant.Armored) dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * 0.6f)); // armour soaks blows
        hp -= dmg;
        Vector2 knock = (grid - from).normalized * (kind == EnemyKind.ShroomKing ? 0.05f : kind == EnemyKind.Boar ? 0.25f : 0.45f);
        if (CanGo(grid + knock)) grid += knock;
        attackCd = Mathf.Max(attackCd, 0.5f);
        if (hp > 0) return false;
        Destroy(gameObject);
        return true;
    }
}
