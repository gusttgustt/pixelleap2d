using UnityEngine;

public enum VillagerRole { Weaver, Elder, Trader, Villager, Shipwright, Doctor, Child, Keeper, Sage, Diver }

/// A friendly member of the tribe. Strolls around their hut and stops to face you when you're near.
public class Villager : MonoBehaviour
{
    public VillagerRole role;
    public string npcName;
    public Vector2 grid;
    public int look;          // sprite variant
    public string[] chatter;  // lines for ordinary villagers

    Vector2 home, wanderDir;
    float wanderT, animT, height;
    SpriteRenderer sr;
    int frame;

    const float Speed = 0.6f, Roam = 2.2f;

    public void Init(VillagerRole r, string name, int lookId, Vector2 homePos)
    {
        role = r;
        npcName = name;
        look = lookId;
        home = homePos;
        grid = homePos;
        sr = gameObject.AddComponent<SpriteRenderer>();
        height = IslandGame.I.HeightAt(grid);
        Apply();
    }

    public string Title => role switch
    {
        VillagerRole.Weaver => $"{npcName} ช่างทอผ้า",
        VillagerRole.Elder => $"ผู้เฒ่า{npcName}",
        VillagerRole.Trader => $"{npcName} พ่อค้า",
        VillagerRole.Shipwright => $"{npcName} ช่างต่อเรือ",
        VillagerRole.Doctor => npcName,
        VillagerRole.Keeper => $"{npcName} ผู้ดูแลเต่า",
        VillagerRole.Sage => $"{npcName} ผู้เฝ้ากระดิ่งลม",
        VillagerRole.Diver => $"{npcName} นักประดาน้ำ",
        _ => npcName,
    };

    void Update()
    {
        var game = IslandGame.I;
        float dt = Time.deltaTime;
        bool moving = false;
        var player = game.player;
        bool playerNear = player != null && (player.grid - grid).magnitude < 2f;

        bool lost = role == VillagerRole.Child && !game.Flag("child" + game.island); // waits where she got lost
        if (!game.Busy && !playerNear && !lost)
        {
            wanderT -= dt;
            if (wanderT <= 0f)
            {
                wanderT = Random.Range(2f, 4f);
                // mostly stand around; sometimes stroll, pulled back towards home
                Vector2 toHome = home - grid;
                wanderDir = Random.value < 0.45f ? Vector2.zero
                          : toHome.magnitude > Roam ? toHome.normalized
                          : Random.insideUnitCircle.normalized;
            }
            if (wanderDir != Vector2.zero)
            {
                Vector2 next = grid + wanderDir * Speed * dt;
                if (game.CanWalk(next, 0.15f) && game.CanClimb(grid, next)) { grid = next; moving = true; }
                else wanderT = 0f;
            }
        }

        if (moving)
        {
            animT += dt * 6f;
            Vector3 w = IslandGame.GridToWorld(grid + wanderDir) - IslandGame.GridToWorld(grid);
            if (Mathf.Abs(w.x) > 0.01f) sr.flipX = w.x < 0;
        }
        else if (playerNear) sr.flipX = player.transform.position.x < transform.position.x; // look at the player
        frame = moving ? 1 + ((int)animT & 1) : 0;
        Apply();
    }

    /// Jump straight to a new home (the lost child going back to the village).
    public void Teleport(Vector2 g)
    {
        home = g;
        grid = g;
        height = IslandGame.I.HeightAt(g);
        Apply();
    }

    void Apply()
    {
        height = Mathf.Lerp(height, IslandGame.I.HeightAt(grid), 1f - Mathf.Exp(-12f * Time.deltaTime));
        Vector3 flat = IslandGame.GridToWorld(grid);
        transform.position = flat + Vector3.up * height;
        sr.sortingOrder = IslandGame.SortOrder(flat.y);
        sr.sprite = PixelArt.Villager(look, frame);
    }
}
