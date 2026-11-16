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
    bool flip, seen;
    Item? hat, outfit;
    float height;
    SpriteRenderer sr;
    int petCode = -1;
    Pet pet;

    public void Init(int id, string name)
    {
        netId = id;
        playerName = string.IsNullOrEmpty(name) ? "ผู้เล่น " + id : name;
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.enabled = false; // until the first position arrives
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

    void Update()
    {
        if (!seen) return;
        grid = Vector2.Lerp(grid, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
        height = Mathf.Lerp(height, IslandGame.I.HeightAt(grid), 1f - Mathf.Exp(-14f * Time.deltaTime));
        Vector3 flat = IslandGame.GridToWorld(grid);
        transform.position = flat + Vector3.up * height;
        sr.sortingOrder = IslandGame.SortOrder(flat.y);
        sr.sprite = PixelArt.Player(dir, frame, hat, outfit, look);
        sr.flipX = flip;
    }
}
