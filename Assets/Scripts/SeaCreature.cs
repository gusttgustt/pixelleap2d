using UnityEngine;

public enum SeaKind { Shark, Jellyfish }

/// Open-sea creature met while paddling between islands. Positions are in world space.
///   Shark     - circles the raft, then charges in for a bite and backs off.
///   Jellyfish - drifts slowly; stings if you paddle into it.
public class SeaCreature : MonoBehaviour
{
    public SeaKind kind;
    public Vector2 pos;
    public int hp, maxHp, xp;

    enum State { Circle, Charge, Retreat }
    State state = State.Circle;
    float damage, speed, stateT, angle, animT, hurtT, stingCd, bobPhase;
    Vector2 drift;
    SpriteRenderer sr;
    int frame;

    public string DisplayName => kind == SeaKind.Shark ? "ฉลาม" : "แมงกะพรุน";

    public void Init(SeaKind k, Vector2 p, int tier)
    {
        kind = k;
        pos = p;
        (maxHp, damage, speed, xp) = k == SeaKind.Shark ? (6, 10f, 2.2f, 20) : (2, 5f, 0.3f, 6);
        maxHp = Mathf.RoundToInt(maxHp * (1f + 0.5f * tier));
        damage *= 1f + 0.3f * tier;
        xp = Mathf.RoundToInt(xp * (1f + 0.5f * tier));
        hp = maxHp;
        stateT = Random.Range(3f, 5f);
        angle = Random.value * Mathf.PI * 2f;
        bobPhase = Random.value * 10f;
        drift = Random.insideUnitCircle.normalized;
        sr = gameObject.AddComponent<SpriteRenderer>();
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
        if (pos.x < raft.x - 18f) { Destroy(gameObject); return; } // left far behind

        if (kind == SeaKind.Shark)
        {
            stateT -= dt;
            switch (state)
            {
                case State.Circle:
                    angle += dt * 0.9f;
                    Vector2 orbit = raft + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f) * 3.2f;
                    pos = Vector2.MoveTowards(pos, orbit, speed * dt);
                    if (stateT <= 0f && dist < 6f) { state = State.Charge; stateT = 2.5f; }
                    break;
                case State.Charge:
                    pos = Vector2.MoveTowards(pos, raft, speed * 1.9f * dt);
                    if (dist < 0.6f)
                    {
                        game.DamagePlayer(damage, "ฉลามกัด!");
                        Retreat();
                    }
                    else if (stateT <= 0f) Retreat();
                    break;
                case State.Retreat:
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
        sr.sprite = PixelArt.SeaCreature(kind, frame);
        if (kind == SeaKind.Shark)
        {
            Vector2 heading = state == State.Retreat ? pos - IslandGame.I.RaftPos : IslandGame.I.RaftPos - pos;
            if (Mathf.Abs(heading.x) > 0.05f) sr.flipX = heading.x < 0;
        }
        sr.color = hurtT > 0f ? new Color(1f, 0.45f, 0.45f) : Color.white;
    }

    /// Returns true if the creature died.
    public bool Hit(int dmg, Vector2 from)
    {
        hp -= dmg;
        hurtT = 0.15f;
        Fx.Burst(transform.position, new Color(0.75f, 0.9f, 1f), 8, 1f, 1.4f);
        pos += (pos - from).normalized * 0.4f;
        if (kind == SeaKind.Shark) Retreat(); // a good hit scares it off for a moment
        if (hp > 0) return false;
        Destroy(gameObject);
        return true;
    }
}
