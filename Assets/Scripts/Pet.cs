using UnityEngine;

/// A pet: trots after its owner (you, or another player online), or, when it isn't out with
/// anyone, strolls around the private island near the house.
///
/// code: 0-3 = a pet bought in town (PetKind), 100 + EnemyKind = a wild creature you caught
/// and tamed (drawn with the creature's own sprite, a little smaller).
public class Pet : MonoBehaviour
{
    public int code;
    public string petName;
    public Vector2 grid;

    System.Func<Vector2?> owner; // where to follow (null result = owner gone: hide), or null = roam
    Vector2 home, moveDir;
    float animT, wanderT, height, heartT;
    SpriteRenderer sr;
    bool flip, shown = true;

    const float Roam = 2.6f;

    public bool Caught => code >= 100;
    public bool Following => owner != null;

    public void Init(int petCode, string name, Vector2 at, System.Func<Vector2?> follow)
    {
        code = petCode;
        petName = name;
        owner = follow;
        home = at;
        grid = at;
        sr = gameObject.AddComponent<SpriteRenderer>();
        if (Caught) transform.localScale = Vector3.one * 0.72f; // a tame one is a bit smaller than the wild ones
        height = IslandGame.I.HeightAt(grid);
        heartT = Random.Range(6f, 14f);
        Apply(false);
    }

    void Update()
    {
        var game = IslandGame.I;
        float dt = Time.deltaTime;
        bool moving = false;

        if (owner != null)
        {
            Vector2? o = owner();
            if (o == null) { Show(false); return; }
            Show(true);
            Vector2 to = o.Value - grid;
            float d = to.magnitude;
            if (d > 7f) { Teleport(o.Value - to.normalized * 0.9f); return; } // left far behind (a warp, a dash)
            if (d > 1.1f)
            {
                moveDir = to / d;
                float speed = d > 3f ? 4.2f : d > 1.8f ? 2.9f : 1.8f;
                moving = Step(game, moveDir * speed * dt);
                if (!moving && d > 3.5f) { Teleport(o.Value - moveDir * 0.9f); return; } // stuck behind something
            }
        }
        else if (!game.Busy)
        {
            wanderT -= dt;
            if (wanderT <= 0f)
            {
                wanderT = Random.Range(1.5f, 3.5f);
                Vector2 toHome = home - grid;
                moveDir = Random.value < 0.4f ? Vector2.zero
                        : toHome.magnitude > Roam ? toHome.normalized
                        : Random.insideUnitCircle.normalized;
            }
            if (moveDir != Vector2.zero)
            {
                moving = Step(game, moveDir * 0.8f * dt);
                if (!moving) wanderT = 0f;
            }
        }

        heartT -= dt;
        if (heartT <= 0f)
        {
            heartT = Random.Range(8f, 16f);
            if (!moving && shown) Fx.Burst(transform.position + Vector3.up * 0.45f, new Color(1f, 0.45f, 0.55f), 2, 0.25f, 0.5f, 0.6f, 1.2f);
        }
        if (moving)
        {
            animT += dt * 8f;
            Vector3 w = IslandGame.GridToWorld(grid + moveDir) - IslandGame.GridToWorld(grid);
            if (Mathf.Abs(w.x) > 0.01f) flip = w.x < 0;
        }
        Apply(moving);
    }

    bool Step(IslandGame game, Vector2 delta)
    {
        foreach (var tryDelta in new[] { delta, new Vector2(delta.x, 0f), new Vector2(0f, delta.y) })
        {
            if (tryDelta.sqrMagnitude < 1e-8f) continue;
            Vector2 next = grid + tryDelta;
            if (game.CanWalk(next, 0.1f) && game.CanClimb(grid, next)) { grid = next; return true; }
        }
        return false;
    }

    public void Teleport(Vector2 g)
    {
        var game = IslandGame.I;
        grid = game.CanWalk(g, 0.1f) ? g : owner?.Invoke() ?? g;
        height = game.HeightAt(grid);
        Apply(false);
    }

    void Show(bool on)
    {
        if (shown == on) return;
        shown = on;
        sr.enabled = on;
    }

    void Apply(bool moving)
    {
        height = Mathf.Lerp(height, IslandGame.I.HeightAt(grid), 1f - Mathf.Exp(-12f * Time.deltaTime));
        Vector3 flat = IslandGame.GridToWorld(grid);
        transform.position = flat + Vector3.up * height;
        sr.sortingOrder = IslandGame.SortOrder(flat.y);
        int frame = moving ? 1 + ((int)animT & 1) : 0;
        sr.sprite = PixelArt.Pet(code, frame);
        sr.flipX = flip;
    }
}
