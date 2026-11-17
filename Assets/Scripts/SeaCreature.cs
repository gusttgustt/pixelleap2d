using UnityEngine;

public enum SeaKind { Shark, Jellyfish, Serpent, Navy }

/// Open-sea creature met while paddling between islands. Positions are in world space.
///   Shark     - circles the raft, then charges in for a bite and backs off.
///   Jellyfish - drifts slowly; stings if you paddle into it.
///   Serpent   - งูทะเลยักษ์, a sea monster that turns up on some crossings: it follows the raft
///               under the water (can't be hit), rises, lunges for a big bite, then lingers on the
///               surface for a moment - that's your chance to hit it - before diving again.
///   Navy      - เรือรัฐบาล, a government warship hunting wanted sailors (IslandGame.Navy.cs): it
///               keeps a few lengths off the raft and fires cannon volleys (a red ring marks where
///               each ball comes down), and rams you if you paddle in close. Paddle up and hit it.
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
    float fireT, ramCd, laneY, laneT; // the warship's next volley / ram, the lane it holds and when it changes lanes

    /// A shark from a hunting pack (ฝูงฉลามคลั่ง, IslandGame.Voyage.cs): weaker alone, but many.
    public bool frenzy;
    /// A government officer with a fruit power aboard this warship (IslandGame.Navy.cs).
    public Power officer;
    public int officerLv;
    public bool flagship;           // leads a navy fleet (กองทัพเรือรัฐบาล)
    public int shieldHits;          // Bubble officer: a bubble that soaks up the next hits
    public int stolen;              // Magnet officer: gold pulled out of your bag (back when it sinks)
    float powerT, cloakT;

    public string DisplayName => kind switch
    {
        SeaKind.Shark => frenzy ? "ฉลามคลั่ง" : "ฉลาม",
        SeaKind.Serpent => "งูทะเลยักษ์",
        SeaKind.Navy => (flagship ? "เรือธงกองทัพเรือ" : "เรือรัฐบาล") + (officer != Power.None ? $" · {OfficerTitle}" : ""),
        _ => "แมงกะพรุน",
    };
    public bool Awakened => officerLv >= IslandGame.AwakenLevel;
    public string OfficerTitle => officer == Power.None ? "" : $"{(flagship ? "พลเรือเอก" : "นายทหาร")}พลัง{IslandGame.PowerName(officer)} Lv {officerLv}{(Awakened ? " (ตื่นแล้ว)" : "")}";
    public bool IsMonster => kind == SeaKind.Serpent;
    /// Too big to pull or shove about (the sea monster and the warships).
    public bool IsBig => kind is SeaKind.Serpent or SeaKind.Navy;
    /// How high above its waterline to show numbers and its health bar (world units).
    public float Tall => kind switch { SeaKind.Serpent => 1.4f, SeaKind.Navy => 2.1f, _ => 0.5f };
    /// Under the water the serpent can't be hit (or targeted).
    public bool Hittable => cloakT <= 0f && (kind != SeaKind.Serpent || state is State.Rise or State.Lunge or State.Linger);
    /// Extra reach for big creatures (targeting and power ranges, world units).
    public float Reach => kind == SeaKind.Serpent ? 0.8f : kind == SeaKind.Navy ? 1.1f : 0f;
    bool Enraged => (kind is SeaKind.Serpent or SeaKind.Navy) && hp * 2 <= maxHp;

    public void Init(SeaKind k, Vector2 p, int tier)
    {
        kind = k;
        pos = p;
        (maxHp, damage, speed, xp) = k switch
        {
            SeaKind.Shark => (6, 10f, 2.2f, 20),
            SeaKind.Serpent => (45, 16f, 4.2f, 150),
            SeaKind.Navy => (60, 9f, 1.5f, 180),
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
        if (k == SeaKind.Navy) { fireT = 2.5f; laneY = p.y; laneT = 4f; }
        Apply();
    }

    /// A tougher warship (the higher your bounty, the better the navy sends).
    public void Toughen(float hpMul, float dmgMul)
    {
        maxHp = Mathf.RoundToInt(maxHp * hpMul);
        hp = maxHp;
        damage *= dmgMul;
        xp = Mathf.RoundToInt(xp * hpMul);
    }

    public float Damage => damage;

    /// One of a hunting pack: a little weaker and less bold alone, but quicker.
    public void MakeFrenzy()
    {
        frenzy = true;
        maxHp = hp = Mathf.Max(3, maxHp * 2 / 3);
        damage *= 0.5f;
        speed *= 1.25f;
        stateT = Random.Range(2f, 7f); // they don't all charge at once
    }

    /// A fruit-power officer aboard: the higher the level, the tougher the ship.
    public void SetOfficer(Power p, int lv, bool flag)
    {
        officer = p; officerLv = lv; flagship = flag;
        powerT = Random.Range(3.5f, 5.5f);
        Toughen(1f + lv / 50f + (flag ? 0.6f : 0f), 1f + lv / 120f);
    }

    /// Shadow officer: the ship fades from sight (can't be hit) for a moment.
    public void Cloak(float t) => cloakT = Mathf.Max(cloakT, t);

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
        if (!IsBig && !frenzy && pos.x < raft.x - 18f) { Destroy(gameObject); return; } // left far behind (a pack keeps hunting you)
        if (frenzy && pos.x < raft.x - 14f) pos.x = raft.x - 10f;

        if (stunT > 0f) // tangled in vines / knocked silly: just bob there
        {
            stunT -= dt;
            animT += dt;
            Apply();
            return;
        }

        if (kind == SeaKind.Serpent) UpdateSerpent(game, dt, raft, toRaft, dist);
        else if (kind == SeaKind.Navy) UpdateNavy(game, dt, raft, toRaft, dist);
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
                    if (stateT <= 0f) { state = State.Circle; stateT = frenzy ? Random.Range(4f, 7.5f) : Random.Range(3f, 5.5f); angle = Mathf.Atan2(-toRaft.y, -toRaft.x); }
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

    /// The warship sails alongside, a few lengths off, and lets fly with its guns.
    void UpdateNavy(IslandGame game, float dt, Vector2 raft, Vector2 toRaft, float dist)
    {
        float rage = Enraged ? 1.4f : 1f;
        animT += dt * 2f;
        ramCd -= dt;
        cloakT -= dt;
        if (officer != Power.None)
        {
            powerT -= dt;
            if (powerT <= 0f)
            {
                powerT = Random.Range(6f, 8.5f) * (Awakened ? 0.7f : 1f) / rage;
                if (dist < 12f && !game.Invisible) game.OfficerPower(this);
            }
        }
        // It keeps pace with the raft east-west but holds a lane north-south, so you can paddle up to
        // it; every few seconds (or when you've slipped far off) it picks a new lane beside you.
        angle += dt * 0.5f * rage;
        laneT -= dt;
        if (laneT <= 0f || Mathf.Abs(laneY - raft.y) > 5f)
        {
            laneT = Random.Range(5f, 7f);
            float side = raft.y > 3f ? -1f : raft.y < -3f ? 1f : Random.value < 0.6f ? -1f : 1f; // below you more often: the whole ship shows
            laneY = Mathf.Clamp(raft.y + side * Random.Range(2.2f, 3f), -6.2f, 6.2f);
        }
        Vector2 spot = new Vector2(raft.x + 1.4f + Mathf.Sin(angle) * 1.4f, laneY);
        float sp = dist > 8f ? speed * 2.6f : speed; // never shaken off for long
        if (pos.x < raft.x - 10f) pos.x = raft.x - 10f;
        Vector2 step = spot - pos;
        pos += new Vector2(Mathf.Clamp(step.x, -sp * 1.6f * dt, sp * 1.6f * dt), Mathf.Clamp(step.y, -sp * 0.6f * dt, sp * 0.6f * dt)); // slow to turn across
        if (dist < 1.6f && ramCd <= 0f && !game.Invisible) // you came too close: it rams you
        {
            ramCd = 3f;
            game.DamagePlayer(damage * 0.7f, "เรือรัฐบาลพุ่งชน!");
            game.Shake(0.12f, 0.3f);
            game.ShoveRaft(toRaft.normalized * 3f); // knocked aside
            Fx.Burst(transform.position, new Color(0.85f, 0.95f, 1f), 16, 1.4f, 2f, 0.6f, 4f);
        }
        fireT -= dt;
        if (fireT <= 0f)
        {
            fireT = Random.Range(3.4f, 4.6f) / rage;
            if (dist < 10f && !game.Invisible) game.NavyVolley(this, Enraged ? 3 : 2, damage);
        }
        RippleTrail(dt, 0.15f);
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
        if (kind == SeaKind.Navy)
        {
            Vector2 v = game.voyage != null ? game.voyage.vel : Vector2.zero; // sails the way you go
            float hx = Mathf.Abs(v.x) > 0.3f ? v.x : game.RaftPos.x - pos.x;
            if (Mathf.Abs(hx) > 0.1f) sr.flipX = hx < 0;
        }
        else if (kind == SeaKind.Shark)
        {
            Vector2 heading = state == State.Retreat ? pos - game.RaftPos : game.RaftPos - pos;
            if (Mathf.Abs(heading.x) > 0.05f) sr.flipX = heading.x < 0;
        }
        var tint = hurtT > 0f ? new Color(1f, 0.45f, 0.45f) : stunT > 0f ? new Color(0.8f, 1f, 0.7f)
                 : frenzy ? new Color(1f, 0.72f, 0.72f) : shieldHits > 0 ? new Color(0.75f, 0.95f, 1f) : Color.white;
        if (cloakT > 0f) tint.a = 0.22f + 0.08f * Mathf.Sin(Time.time * 10f); // a shimmer on the water
        sr.color = tint;
    }

    /// Returns true if the creature died.
    public bool Hit(int dmg, Vector2 from)
    {
        hp -= dmg;
        hurtT = 0.15f;
        Fx.Burst(transform.position, new Color(0.75f, 0.9f, 1f), 8, 1f, 1.4f);
        if (!IsBig) pos += (pos - from).normalized * 0.4f; // the monster and the warships are too big to knock about
        if (kind == SeaKind.Shark) Retreat(); // a good hit scares it off for a moment
        if (hp > 0) return false;
        if (kind == SeaKind.Navy) { enabled = false; return true; } // it sinks (IslandGame.Navy.cs)
        Destroy(gameObject);
        return true;
    }

    /// Vines hold it in place for a moment (the monster shrugs it off faster).
    public void Stun(float t) => stunT = Mathf.Max(stunT, IsBig ? t * 0.4f : t);

    /// Shoved through the water (world units); the monster barely moves.
    public void Push(Vector2 by)
    {
        pos += IsBig ? by * 0.25f : by;
        if (kind == SeaKind.Shark) Retreat();
    }
}
