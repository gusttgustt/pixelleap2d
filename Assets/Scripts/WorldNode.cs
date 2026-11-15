using UnityEngine;
using UnityEngine.Rendering.Universal;

/// A harvestable / placeable thing in the world: tree, bush, grass, stick, rock, campfire, shelter, raft...
public class WorldNode : MonoBehaviour
{
    public NodeKind kind;
    public Vector2 grid;          // position in grid space (tile centers are integers)
    public bool available = true; // false = already harvested, waiting to regrow
    public int hits;
    public bool flip;
    public int id;                // same on every player's machine (online sync)

    // Regrow timers run on game time (IslandGame.Now) so sleeping through the night makes plants grow.
    float regrowAt, shakeT, animT;
    int fireFrame;
    SpriteRenderer sr;
    Light2D fireLight;
    Vector3 basePos;

    /// Seconds until this node regrows (0 = not regrowing). Used by the save system.
    public float RegrowRemaining => regrowAt > 0f ? Mathf.Max(0.01f, regrowAt - IslandGame.I.Now) : 0f;

    public bool Blocking => kind is NodeKind.Tree or NodeKind.FruitTree or NodeKind.Bush or NodeKind.Stump
                                 or NodeKind.Campfire or NodeKind.Shelter or NodeKind.Raft or NodeKind.Hut or NodeKind.Totem;

    public float BlockRadius => kind switch
    {
        NodeKind.Bush => 0.42f,
        NodeKind.Campfire => 0.4f,
        NodeKind.Stump => 0.3f,
        NodeKind.Shelter => 0.6f,
        NodeKind.Raft => 0.55f,
        NodeKind.Hut => 0.85f,
        NodeKind.Totem => 0.25f,
        _ => 0.32f,
    };

    public bool CanTarget => kind switch
    {
        NodeKind.Stump or NodeKind.Hut or NodeKind.Totem => false,
        NodeKind.Tree or NodeKind.Campfire or NodeKind.Shelter or NodeKind.Raft => true,
        _ => available,
    };

    public float PromptHeight => kind switch
    {
        NodeKind.Tree or NodeKind.FruitTree => 1.45f,
        NodeKind.Shelter or NodeKind.Raft => 1.05f,
        NodeKind.Bush => 0.65f,
        NodeKind.Bamboo => 0.95f,
        NodeKind.Campfire => 0.85f,
        _ => 0.45f,
    };

    public void Init(NodeKind k, Vector2 g, bool flip)
    {
        kind = k;
        grid = g;
        Vector3 flat = IslandGame.GridToWorld(g);
        basePos = flat + Vector3.up * IslandGame.I.HeightAt(g);
        transform.position = basePos;
        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sortingOrder = IslandGame.SortOrder(flat.y); // Y-sort on the ground position, ignoring height
        this.flip = flip;
        sr.flipX = flip && k is not (NodeKind.Campfire or NodeKind.Shelter or NodeKind.Raft or NodeKind.Totem);

        if (k == NodeKind.Campfire)
        {
            fireLight = IslandGame.CreateLight("FireLight", Light2D.LightType.Point, transform);
            fireLight.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            fireLight.color = new Color(1f, 0.6f, 0.25f);
            fireLight.intensity = 1.4f;
            fireLight.pointLightInnerRadius = 0.3f;
            fireLight.pointLightOuterRadius = 3.5f;
        }
        if (k == NodeKind.GlowShroom && ((int)(g.x * 7f) + (int)(g.y * 13f)) % 3 == 0) // some of them light the ground
        {
            fireLight = IslandGame.CreateLight("ShroomLight", Light2D.LightType.Point, transform);
            fireLight.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            fireLight.color = new Color(0.4f, 1f, 0.9f);
            fireLight.intensity = 0.9f;
            fireLight.pointLightInnerRadius = 0.2f;
            fireLight.pointLightOuterRadius = 2.2f;
        }
        Refresh();
    }

    public void Refresh()
    {
        sr.enabled = true;
        var biome = IslandGame.I.biome;
        sr.color = IslandGame.I.Tint(kind);
        sr.sprite = kind switch
        {
            NodeKind.Tree => PixelArt.Tree(biome),
            NodeKind.FruitTree => PixelArt.FruitTree(available, biome),
            NodeKind.Bamboo => PixelArt.Bamboo(),
            NodeKind.Obsidian => PixelArt.Obsidian(),
            NodeKind.Bush => PixelArt.Bush(available),
            NodeKind.Grass => PixelArt.Grass(),
            NodeKind.Stick => PixelArt.Stick(),
            NodeKind.Rock => PixelArt.Rock(),
            NodeKind.Stump => PixelArt.Stump(),
            NodeKind.Shelter => PixelArt.Shelter(),
            NodeKind.Raft => PixelArt.Raft(IslandGame.I.raftLevel),
            NodeKind.Hut => PixelArt.Hut(),
            NodeKind.Totem => PixelArt.Totem(),
            NodeKind.GlowShroom => PixelArt.GlowShroom(available),
            _ => PixelArt.Campfire(fireFrame),
        };
        if (kind == NodeKind.GlowShroom && fireLight != null) fireLight.enabled = available;
        if (!available && kind is NodeKind.Grass or NodeKind.Stick or NodeKind.Rock or NodeKind.Bamboo or NodeKind.Obsidian)
            sr.enabled = false;
    }

    public void Deplete(float seconds)
    {
        available = false;
        regrowAt = IslandGame.I.Now + seconds;
        Refresh();
    }

    public void BecomeStump(float seconds)
    {
        kind = NodeKind.Stump;
        hits = 0;
        regrowAt = IslandGame.I.Now + seconds;
        Refresh();
    }

    public void Shake() => shakeT = 0.25f;

    /// Re-applies saved state after loading.
    public void Restore(bool isAvailable, int savedHits, float regrowSeconds)
    {
        available = isAvailable;
        hits = savedHits;
        regrowAt = regrowSeconds > 0f ? IslandGame.I.Now + regrowSeconds : 0f;
        Refresh();
    }

    void Update()
    {
        var game = IslandGame.I;
        if (regrowAt > 0f && game.Now >= regrowAt)
        {
            var player = game.player;
            if (kind == NodeKind.Stump && player != null && (player.grid - grid).magnitude < 0.7f)
                regrowAt = game.Now + 5f; // don't grow a tree on top of the player
            else
            {
                regrowAt = 0f;
                available = true;
                if (kind == NodeKind.Stump) kind = NodeKind.Tree;
                Refresh();
            }
        }

        Vector3 p = basePos;
        if (shakeT > 0f)
        {
            shakeT -= Time.deltaTime;
            p.x += Mathf.Sin(Time.time * 70f) * 0.04f * (shakeT / 0.25f);
        }
        transform.position = p;

        if (kind == NodeKind.Campfire)
        {
            animT += Time.deltaTime;
            if (animT > 0.14f) { animT = 0f; fireFrame ^= 1; sr.sprite = PixelArt.Campfire(fireFrame); }
            fireLight.intensity = 1.3f + Mathf.PerlinNoise(Time.time * 4f, 0f) * 0.4f;
            if (Random.value < Time.deltaTime * 3f) // rising sparks
                Fx.Burst(basePos + Vector3.up * 0.35f, new Color(1f, 0.75f, 0.3f), 1, 0.3f, 1.2f, 0.9f, -0.5f);
        }
    }
}
