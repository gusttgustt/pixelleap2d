using UnityEngine;

/// Another player on the same island, moved by the position updates they send (~10 a second).
public class RemotePlayer : MonoBehaviour
{
    public int netId;
    public string playerName = "";
    public int level = 1;
    public Vector2 grid;
    public string chat;
    public float chatUntil;
    public bool downed;          // knocked out: a friend can revive them (IslandGame.Revive.cs)

    Vector2 target;
    int dir, frame, look;
    public int Look => look; // their character, for showing them on a boat (IslandGame.Crew.cs)
    bool flip, seen;
    Item? hat, outfit;
    float height;
    SpriteRenderer sr;
    int petCode = -1;
    Pet pet;

    // At sea (IslandGame.Voyage.cs): their boat, with them standing on the deck.
    public bool atSea;
    public bool hidden;          // at sea, riding on someone's deck (or on mine): no boat of their own
    SpriteRenderer boatSr;
    int boatLook;

    public void Init(int id, string name, bool sea = false)
    {
        netId = id;
        playerName = string.IsNullOrEmpty(name) ? "ผู้เล่น " + id : name;
        atSea = sea;
        sr = gameObject.AddComponent<SpriteRenderer>();
        if (sea)
        {
            boatSr = new GameObject("Boat").AddComponent<SpriteRenderer>();
            boatSr.transform.SetParent(transform);
            boatSr.enabled = false;
        }
        else GroundShadow.Attach(sr);
        sr.enabled = false; // until the first position arrives
    }

    /// A boat's position at sea. m.x is how far along its own crossing it is (0..1): shown at the
    /// same share of my crossing, so we meet in the same waters whatever the route lengths.
    public void ApplySea(NetMsg m, float myLength, bool hide)
    {
        if (!atSea) return;
        target = new Vector2(m.x * myLength, m.y);
        if (!seen || (target - grid).magnitude > 6f) grid = target;
        seen = true;
        hidden = hide;
        awaken = hide ? Power.None : (Power)m.aw;
        sr.enabled = !hide;
        boatSr.enabled = !hide;
        dir = m.d;
        frame = m.f;
        flip = m.fl == 1;
        hat = m.hat > 0 ? (Item)(m.hat - 1) : (Item?)null;
        outfit = m.outfit > 0 ? (Item)(m.outfit - 1) : (Item?)null;
        level = Mathf.Max(1, m.lv);
        look = Mathf.Max(0, m.look);
        awaken = (Power)m.aw;
        boatLook = Mathf.Max(0, m.kind);
        if (!string.IsNullOrEmpty(m.name)) playerName = m.name;
    }

    public void Apply(NetMsg m)
    {
        target = new Vector2(m.x, m.y);
        if (!seen || (target - grid).magnitude > 4f) { grid = target; height = IslandGame.I.HeightAt(grid); } // first update or teleport
        seen = true;
        sr.enabled = true;
        dir = m.d;
        frame = m.f;
        flip = m.fl == 1;
        hat = m.hat > 0 ? (Item)(m.hat - 1) : (Item?)null;
        outfit = m.outfit > 0 ? (Item)(m.outfit - 1) : (Item?)null;
        level = Mathf.Max(1, m.lv);
        look = Mathf.Max(0, m.look);
        if (!string.IsNullOrEmpty(m.name)) playerName = m.name;
        bool wasDown = downed;
        downed = m.dn == 1;
        if (downed && !wasDown) IslandGame.I.FriendDowned(this);
        int pc = m.pet - 1;
        if (pc != petCode)
        {
            petCode = pc;
            if (pet != null) Destroy(pet.gameObject);
            pet = null;
            if (pc >= 0)
            {
                pet = new GameObject("Remote pet").AddComponent<Pet>();
                pet.transform.SetParent(transform.parent);
                pet.Init(pc, "", grid + new Vector2(-0.7f, 0.5f), () => this != null && seen ? grid : (Vector2?)null);
            }
        }
    }

    void OnDestroy()
    {
        if (pet != null) Destroy(pet.gameObject);
    }

    // Their fruit power, while it's awakened (IslandGame.Awaken.cs): the aura shows on them here too.
    Power awaken;
    float auraT;

    void Update()
    {
        if (!seen) return;
        if (awaken != Power.None && sr.enabled)
        {
            auraT -= Time.deltaTime;
            if (auraT <= 0f) { auraT = 1f / 30f; IslandGame.AwakenAuraParticles(transform.position, awaken); }
        }
        if (atSea)
        {
            grid = Vector2.Lerp(grid, target, 1f - Mathf.Exp(-8f * Time.deltaTime)); // world position on the open sea
            float bob = Mathf.Sin(Time.time * 2f + netId) * 0.04f;
            var at = (Vector3)grid;
            boatSr.sprite = PixelArt.Boat(boatLook);
            boatSr.sortingOrder = IslandGame.SortOrder(grid.y);
            boatSr.flipX = false;
            transform.position = at + Vector3.up * (PixelArt.BoatDeck(boatLook) + bob);
            // keep the boat where it is (it's a child): put it back after moving the player
            boatSr.transform.position = at + Vector3.up * bob;
            sr.sortingOrder = boatSr.sortingOrder + 1;
            sr.sprite = PixelArt.Player(dir, frame, hat, outfit, look);
            sr.flipX = flip;
            return;
        }
        grid = Vector2.Lerp(grid, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
        height = Mathf.Lerp(height, IslandGame.I.HeightAt(grid), 1f - Mathf.Exp(-14f * Time.deltaTime));
        Vector3 flat = IslandGame.GridToWorld(grid);
        transform.position = flat + Vector3.up * height;
        sr.sortingOrder = IslandGame.SortOrder(flat.y);
        sr.sprite = PixelArt.Player(dir, frame, hat, outfit, look);
        sr.flipX = flip;
    }
}
