using UnityEngine;

public enum EnemyKind { Crab, Snake, Boar }

/// Hostile creature. Chases the player when close, wanders otherwise, is scared of campfires.
///   Crab  - night only, comes up on beaches, walks back into the sea at sunrise.
///   Snake - Jungle, fast but fragile.
///   Boar  - Volcano, slow to kill and hits hard.
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

    public string DisplayName => kind.ToString();
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
            _ => (3, 8f, 1.25f, 9f, 10),
        };
        // Later islands (second lap and beyond) are tougher and give more XP.
        maxHp = Mathf.RoundToInt(maxHp * (1f + 0.5f * tier));
        damage *= 1f + 0.3f * tier;
        xp = Mathf.RoundToInt(xp * (1f + 0.5f * tier));
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

        var (targetGrid, targetWho) = game.NearestPlayer(grid); // chase whichever player is closest
        Vector2 toPlayer = targetGrid - grid;
        float dist = toPlayer.magnitude;
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
            if (dist > 22f) { Destroy(gameObject); return; } // far away: despawn, new ones spawn closer

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
                if (targetWho == 0) game.DamagePlayer(damage, $"{DisplayName}!");
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

    bool CanGo(Vector2 g) => IslandGame.I.CanWalk(g, 0.2f) && IslandGame.I.CanClimb(grid, g);

    void Apply()
    {
        height = Mathf.Lerp(height, IslandGame.I.HeightAt(grid), 1f - Mathf.Exp(-12f * Time.deltaTime));
        Vector3 flat = IslandGame.GridToWorld(grid);
        transform.position = flat + Vector3.up * height;
        sr.sortingOrder = IslandGame.SortOrder(flat.y);
        sr.sprite = PixelArt.Enemy(kind, frame);
        sr.color = hurtT > 0f ? new Color(1f, 0.4f, 0.4f) : Color.white;
    }

    /// Returns true if the enemy died.
    public bool Hit(int dmg, Vector2 from)
    {
        hurtT = 0.15f;
        Fx.Burst(transform.position + Vector3.up * 0.15f, kind == EnemyKind.Snake ? new Color(0.4f, 0.7f, 0.3f) : new Color(1f, 0.5f, 0.3f), 6);
        if (puppet) return false; // the host decides; it tells everyone when it dies
        hp -= dmg;
        Vector2 knock = (grid - from).normalized * (kind == EnemyKind.Boar ? 0.25f : 0.45f);
        if (CanGo(grid + knock)) grid += knock;
        attackCd = Mathf.Max(attackCd, 0.5f);
        if (hp > 0) return false;
        Destroy(gameObject);
        return true;
    }
}
