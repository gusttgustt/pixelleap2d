using UnityEngine;

public enum SeaKind { Shark, Jellyfish, Serpent }

/// Open-sea creature met while paddling between islands. Positions are in world space.
///   Shark     - circles the raft, then charges in for a bite and backs off.
///   Jellyfish - drifts slowly; stings if you paddle into it.
///   Serpent   - งูทะเลยักษ์, a sea monster that turns up on some crossings: it follows the raft
///               under the water (can't be hit), rises, lunges for a big bite, then lingers on the
///               surface for a moment - that's your chance to hit it - before diving again.
/// While you're invisible (Shadow power) nothing charges or lunges at you.
public class SeaCreature : MonoBehaviour
{
    public SeaKind kind;
    public Vector2 pos;
    public int hp, maxHp, xp;

    enum State { Circle, Charge, Retreat, Stalk, Rise, Lunge, Linger, Dive }
    State state = State.Circle;
    float damage, speed, stateT, angle, animT, hurtT, stingCd, bobPhase, stunT, splashT;
    Vector2 drift;
    SpriteRenderer sr;
    int frame;
    bool bit; // this lunge has already landed

    public string DisplayName => kind switch { SeaKind.Shark => "ฉลาม", SeaKind.Serpent => "งูทะเลยักษ์", _ => "แมงกะพรุน" };
    public bool IsMonster => kind == SeaKind.Serpent;
    /// Under the water the serpent can't be hit (or targeted).
    public bool Hittable => kind != SeaKind.Serpent || state is State.Rise or State.Lunge or State.Linger;
    /// Extra reach for big creatures (targeting and power ranges, world units).
    public float Reach => kind == SeaKind.Serpent ? 0.8f : 0f;
    bool Enraged => kind == SeaKind.Serpent && hp * 2 <= maxHp;

    public void Init(SeaKind k, Vector2 p, int tier)
    {
        kind = k;
        pos = p;
        (maxHp, damage, speed, xp) = k switch
        {
            SeaKind.Shark => (6, 10f, 2.2f, 20),
            SeaKind.Serpent => (45, 16f, 4.2f, 150),
            _ => (2, 5f, 0.3f, 6),
        };
        maxHp = Mathf.RoundToInt(maxHp * (1f + 0.5f * tier));
        damage *= 1f + 0.3f * tier;
        xp = Mathf.RoundToInt(xp * (1f + 0.5f * tier));
        hp = maxHp;
        stateT = Random.Range(3f, 5f);
        angle = Random.value * Mathf.PI * 2f;
        bobPhase = Random.value * 10f;
        drift = Random.insideUnitCircle.normalized;
        sr = gameObject.AddComponent<SpriteRenderer>();
        if (k == SeaKind.Serpent) { state = State.Rise; stateT = 1.2f; transform.localScale = Vector3.one * 1.3f; } // it makes an entrance
        Apply();
    }

    void Update()
    {
        var game = IslandGame.I;
        if (game.voyage == null) return;
        if (game.dead || game.Busy) { Apply(); return; }
        float dt = Time.deltaTime;
        hurtT -= dt;
        stingCd -= dt;
        Vector2 raft = game.RaftPos;
        Vector2 toRaft = raft - pos;
        float dist = toRaft.magnitude;
        if (kind != SeaKind.Serpent && pos.x < raft.x - 18f) { Destroy(gameObject); return; } // left far behind

        if (stunT > 0f) // tangled in vines / knocked silly: just bob there
        {
            stunT -= dt;
            animT += dt;
            Apply();
            return;
        }

        if (kind == SeaKind.Serpent) UpdateSerpent(game, dt, raft, toRaft, dist);
        else if (kind == SeaKind.Shark)
        {
            stateT -= dt;
            switch (state)
            {
                case State.Circle:
                    angle += dt * 0.9f;
                    float r = game.Invisible ? 5.5f : 3.2f; // it can't see you: it loses track and swims wide
                    Vector2 orbit = raft + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f) * r;
                    pos = Vector2.MoveTowards(pos, orbit, speed * dt);
                    if (stateT <= 0f && dist < 6f && !game.Invisible) { state = State.Charge; stateT = 2.5f; }
                    break;
                case State.Charge:
                    pos = Vector2.MoveTowards(pos, raft, speed * 1.9f * dt);
                    if (game.Invisible) Retreat();
                    else if (dist < 0.6f)
                    {
                        game.DamagePlayer(damage, "ฉลามกัด!");
                        Retreat();
                    }
                    else if (stateT <= 0f) Retreat();
                    break;
                default:
                    pos -= toRaft.normalized * speed * dt;
                    if (stateT <= 0f) { state = State.Circle; stateT = Random.Range(3f, 5.5f); angle = Mathf.Atan2(-toRaft.y, -toRaft.x); }
                    break;
            }
            animT += dt * 4f;
        }
        else
        {
            pos += drift * speed * dt + new Vector2(0f, Mathf.Sin(Time.time * 1.3f + bobPhase) * 0.15f * dt);
            if (dist < 0.6f && stingCd <= 0f)
            {
                stingCd = 1.5f;
                game.DamagePlayer(damage, "แมงกะพรุนต่อย!");
            }
            animT += dt * 2.5f;
        }
        frame = (int)animT & 1;
        Apply();
    }

    void UpdateSerpent(IslandGame game, float dt, Vector2 raft, Vector2 toRaft, float dist)
    {
        stateT -= dt;
        animT += dt * 3f;
        float rage = Enraged ? 1.35f : 1f;
        switch (state)
        {
            case State.Stalk: // under the water, keeping pace with the raft
                angle += dt * 0.7f * rage;
                Vector2 spot = raft + new Vector2(Mathf.Cos(angle) * 4.5f, Mathf.Sin(angle) * 2.6f);
                if (pos.x < raft.x - 9f) pos.x = raft.x + 6f; // never shaken off for long
                pos = Vector2.MoveTowards(pos, spot, (speed + 1.5f) * dt);
                RippleTrail(dt, 0.12f);
                if (game.Invisible) stateT = Mathf.Max(stateT, 0.6f); // can't find you to attack
                if (stateT <= 0f && dist < 7f)
                {
                    state = State.Rise; stateT = 0.9f / rage;
                    Sfx.Play("splash", 0.7f);
                }
                break;
            case State.Rise: // breaks the surface: a warning ring of foam
                RippleTrail(dt, 0.05f);
                if (stateT <= 0f)
                {
                    if (game.Invisible) { Linger(); break; }
                    state = State.Lunge; stateT = 1.3f; bit = false;
                    game.Shake(0.05f, 0.25f);
                }
                break;
            case State.Lunge:
                pos = Vector2.MoveTowards(pos, raft, speed * 1.6f * rage * dt);
                RippleTrail(dt, 0.04f);
                if (!bit && dist < 0.9f)
                {
                    bit = true;
                    game.DamagePlayer(damage, "งูทะเลยักษ์งับ!");
                    game.Shake(0.12f, 0.3f);
                    Fx.Burst(transform.position, new Color(0.85f, 0.95f, 1f), 18, 1.6f, 2.2f, 0.6f, 4f);
                    Linger();
                }
                else if (stateT <= 0f || game.Invisible) Linger();
                break;
            case State.Linger: // catching its breath on the surface - hit it now!
                pos += (pos - raft).normalized * 0.4f * dt + new Vector2(game.voyage.vel.x * 0.8f, 0f) * dt;
                if (stateT <= 0f) { state = State.Dive; stateT = 0.6f; Sfx.Play("splash", 0.5f); }
                break;
            default: // Dive
                RippleTrail(dt, 0.06f);
                if (stateT <= 0f) { state = State.Stalk; stateT = Random.Range(3.5f, 5.5f) / rage; angle = Mathf.Atan2(-toRaft.y, -toRaft.x); }
                break;
        }
        frame = (int)animT & 1;
    }

    void Linger()
    {
        state = State.Linger;
        stateT = Enraged ? 2.2f : 2.8f;
    }

    void RippleTrail(float dt, float every)
    {
        splashT -= dt;
        if (splashT > 0f) return;
        splashT = every;
        Fx.Burst(new Vector3(pos.x + Random.Range(-0.6f, 0.6f), pos.y + Random.Range(-0.2f, 0.2f), 0f), new Color(1f, 1f, 1f, 0.8f), 2, 0.4f, 0.6f, 0.5f, 1f);
    }

    void Retreat()
    {
        state = State.Retreat;
        stateT = 1.4f;
    }

    void Apply()
    {
        float bob = Mathf.Sin(Time.time * 2f + bobPhase) * 0.03f;
        transform.position = new Vector3(pos.x, pos.y + bob, 0f);
        sr.sortingOrder = IslandGame.SortOrder(pos.y);
        var game = IslandGame.I;
        if (kind == SeaKind.Serpent)
        {
            bool under = state is State.Stalk || (state == State.Dive && stateT < 0.3f);
            sr.sprite = under ? PixelArt.SerpentUnder(frame) : PixelArt.SeaCreature(kind, frame);
            if (under) sr.sortingOrder = -32000; // a dark shape below the waves
            float a = state == State.Rise ? Mathf.Clamp01(1f - stateT / 0.9f) * 0.7f + 0.3f
                    : state == State.Dive ? Mathf.Clamp01(stateT / 0.6f) * 0.7f + 0.3f : 1f;
            var col = hurtT > 0f ? new Color(1f, 0.45f, 0.45f) : Enraged && !under ? new Color(1f, 0.85f, 0.85f) : Color.white;
            col.a = under ? 0.8f : a;
            sr.color = stunT > 0f ? new Color(0.8f, 1f, 0.7f, col.a) : col;
            float hx = game.RaftPos.x - pos.x;
            if (Mathf.Abs(hx) > 0.1f) sr.flipX = hx < 0;
            return;
        }
        sr.sprite = PixelArt.SeaCreature(kind, frame);
        if (kind == SeaKind.Shark)
        {
            Vector2 heading = state == State.Retreat ? pos - game.RaftPos : game.RaftPos - pos;
            if (Mathf.Abs(heading.x) > 0.05f) sr.flipX = heading.x < 0;
        }
        sr.color = hurtT > 0f ? new Color(1f, 0.45f, 0.45f) : stunT > 0f ? new Color(0.8f, 1f, 0.7f) : Color.white;
    }

    /// Returns true if the creature died.
    public bool Hit(int dmg, Vector2 from)
    {
        hp -= dmg;
        hurtT = 0.15f;
        Fx.Burst(transform.position, new Color(0.75f, 0.9f, 1f), 8, 1f, 1.4f);
        if (kind != SeaKind.Serpent) pos += (pos - from).normalized * 0.4f; // the monster is too big to knock about
        if (kind == SeaKind.Shark) Retreat(); // a good hit scares it off for a moment
        if (hp > 0) return false;
        Destroy(gameObject);
        return true;
    }

    /// Vines hold it in place for a moment (the monster shrugs it off faster).
    public void Stun(float t) => stunT = Mathf.Max(stunT, kind == SeaKind.Serpent ? t * 0.4f : t);

    /// Shoved through the water (world units); the monster barely moves.
    public void Push(Vector2 by)
    {
        pos += kind == SeaKind.Serpent ? by * 0.25f : by;
        if (kind == SeaKind.Shark) Retreat();
    }
}
