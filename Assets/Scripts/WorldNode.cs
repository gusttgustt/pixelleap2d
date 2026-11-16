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
    public Item dropItem;         // NodeKind.Drop: what's lying here, and how many
    public int dropCount;

    // Regrow timers run on game time (IslandGame.Now) so sleeping through the night makes plants grow.
    float regrowAt, shakeT, animT;
    int fireFrame;
    SpriteRenderer sr;
    Light2D fireLight;
    Vector3 basePos;

    /// Seconds until this node regrows (0 = not regrowing). Used by the save system.
    public float RegrowRemaining => regrowAt > 0f ? Mathf.Max(0.01f, regrowAt - IslandGame.I.Now) : 0f;

    public bool Blocking => kind is NodeKind.Tree or NodeKind.FruitTree or NodeKind.Bush or NodeKind.Stump
                                 or NodeKind.Campfire or NodeKind.Shelter or NodeKind.Raft or NodeKind.Hut or NodeKind.Totem or NodeKind.GlowTree
                                 or NodeKind.House or NodeKind.Lamp or NodeKind.Well or NodeKind.Stall or NodeKind.Fountain or NodeKind.Statue
                                 or NodeKind.ClockRuin or NodeKind.ClockTower
                                 or NodeKind.Wreck or NodeKind.Chest or NodeKind.Pillar or NodeKind.GiantTree or NodeKind.Shrine
                                 or NodeKind.SunkenTemple or NodeKind.GiantClam or NodeKind.PirateShip or NodeKind.Tent or NodeKind.Cannon;

    public float BlockRadius => kind switch
    {
        NodeKind.Bush => 0.42f,
        NodeKind.Campfire => 0.4f,
        NodeKind.Stump => 0.3f,
        NodeKind.Shelter => 0.6f,
        NodeKind.Raft => 0.55f,
        NodeKind.Hut => 0.85f,
        NodeKind.Totem => 0.25f,
        NodeKind.House => 0.95f,
        NodeKind.Fountain => 0.85f,
        NodeKind.Stall => 0.6f,
        NodeKind.Well => 0.45f,
        NodeKind.Lamp => 0.18f,
        NodeKind.Statue => 0.35f,
        NodeKind.ClockTower => 1.0f,
        NodeKind.Wreck => 1.2f,
        NodeKind.GiantTree => 0.9f,
        NodeKind.Pillar => 0.3f,
        NodeKind.Shrine => 0.5f,
        NodeKind.Chest => 0.35f,
        NodeKind.SunkenTemple => 1.4f,
        NodeKind.PirateShip => 1.5f,
        NodeKind.Tent => 0.7f,
        NodeKind.Cannon => 0.4f,
        NodeKind.GiantClam => 0.5f,
        NodeKind.ClockRuin => 0.45f,
        _ => 0.32f,
    };

    public bool CanTarget => kind switch
    {
        NodeKind.Stump or NodeKind.Hut or NodeKind.Totem or NodeKind.House or NodeKind.Lamp or NodeKind.Stall or NodeKind.Statue or NodeKind.ClockRuin or NodeKind.ClockTower
            or NodeKind.Wreck or NodeKind.Pillar or NodeKind.GiantTree or NodeKind.SunkenTemple
            or NodeKind.PirateShip or NodeKind.Tent or NodeKind.Cannon => false,
        NodeKind.GiantClam => true,
        NodeKind.Shrine => true,
        NodeKind.Well or NodeKind.Fountain or NodeKind.AirVent or NodeKind.HotSpring => true,
        NodeKind.Tree or NodeKind.Campfire or NodeKind.Shelter or NodeKind.Raft => true,
        _ => available,
    };

    public float PromptHeight => kind switch
    {
        NodeKind.Tree or NodeKind.FruitTree or NodeKind.GlowTree => 1.45f,
        NodeKind.Shelter or NodeKind.Raft => 1.05f,
        NodeKind.Bush => 0.65f,
        NodeKind.Bamboo => 0.95f,
        NodeKind.Campfire => 0.85f,
        NodeKind.Well or NodeKind.Fountain => 0.9f,
        NodeKind.Farm => 0.5f,
        NodeKind.Kelp => 1.2f,
        NodeKind.Shrine => 1.0f,
        NodeKind.AirVent => 0.6f,
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
        if (k == NodeKind.Lamp) // street lamps light up at night
        {
            fireLight = IslandGame.CreateLight("LampLight", Light2D.LightType.Point, transform);
            fireLight.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            fireLight.color = new Color(1f, 0.8f, 0.5f);
            fireLight.pointLightInnerRadius = 0.3f;
            fireLight.pointLightOuterRadius = 2.8f;
        }
        else if (k == NodeKind.GlowTree) // the special-fruit tree glows gold
        {
            fireLight = IslandGame.CreateLight("TreeGlow", Light2D.LightType.Point, transform);
            fireLight.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            fireLight.color = new Color(1f, 0.85f, 0.4f);
            fireLight.intensity = 1.2f;
            fireLight.pointLightInnerRadius = 0.3f;
            fireLight.pointLightOuterRadius = 3f;
        }
        else if (k == NodeKind.GlowShroom && ((int)(g.x * 7f) + (int)(g.y * 13f)) % 3 == 0) // some of them light the ground
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
            NodeKind.Shell => PixelArt.Shell(),
            NodeKind.Drop => PixelArt.Icon(dropItem),
            NodeKind.House => PixelArt.House(),
            NodeKind.Lamp => PixelArt.Lamp(),
            NodeKind.Farm => PixelArt.Farm(available),
            NodeKind.Well => PixelArt.Well(),
            NodeKind.Stall => PixelArt.Stall(),
            NodeKind.Fountain => PixelArt.Fountain(),
            NodeKind.Statue => PixelArt.Statue(),
            NodeKind.Kelp => PixelArt.Kelp(available),
            NodeKind.CoralNode => PixelArt.CoralNode(available),
            NodeKind.Oyster => PixelArt.Oyster(available),
            NodeKind.AirVent => PixelArt.AirVent(),
            NodeKind.SandCrystal => PixelArt.SandCrystal(available),
            NodeKind.ClockRuin => PixelArt.ClockRuin(),
            NodeKind.ClockTower => PixelArt.ClockTower(),
            NodeKind.IceCrystal => PixelArt.IceCrystal(available),
            NodeKind.Wreck => PixelArt.Wreck(),
            NodeKind.Chest => PixelArt.Chest(available),
            NodeKind.Pillar => PixelArt.Pillar(),
            NodeKind.GiantTree => PixelArt.GiantTree(),
            NodeKind.Shrine => PixelArt.Shrine(),
            NodeKind.LostBundle => PixelArt.LostBundle(),
            NodeKind.SunkenTemple => PixelArt.SunkenTemple(),
            NodeKind.Relic => PixelArt.Relic(available),
            NodeKind.GiantClam => PixelArt.GiantClam(),
            NodeKind.DigSpot => PixelArt.DigSpot(available),
            NodeKind.PirateShip => PixelArt.PirateShip(),
            NodeKind.Tent => PixelArt.Tent(),
            NodeKind.Cannon => PixelArt.Cannon(),
            NodeKind.HotSpring => PixelArt.HotSpring(),
            NodeKind.CloudPlant => PixelArt.CloudPlant(available),
            NodeKind.GlowTree => PixelArt.GlowTree(available, IslandGame.FruitFor(biome)),
            _ => PixelArt.Campfire(fireFrame),
        };
        if (kind is NodeKind.GlowShroom or NodeKind.GlowTree && fireLight != null) fireLight.enabled = available;
        if (!available && kind is NodeKind.Grass or NodeKind.Stick or NodeKind.Rock or NodeKind.Bamboo or NodeKind.Obsidian or NodeKind.Shell)
            sr.enabled = false;
    }

    public void Deplete(float seconds)
    {
        available = false;
        regrowAt = IslandGame.I.Now + seconds / IslandGame.I.GrowScale;
        Refresh();
    }

    public void BecomeStump(float seconds)
    {
        kind = NodeKind.Stump;
        hits = 0;
        regrowAt = IslandGame.I.Now + seconds / IslandGame.I.GrowScale;
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

        if (kind == NodeKind.Lamp && fireLight != null) fireLight.intensity = (1f - game.Daylight) * 1.2f;
        if (kind == NodeKind.HotSpring && Random.value < Time.deltaTime * 6f) // steam
            Fx.Burst(basePos + Vector3.up * 0.2f + (Vector3)(Random.insideUnitCircle * 0.3f), new Color(1f, 1f, 1f, 0.7f), 1, 0.2f, 0.4f, 1.5f, -0.8f);
        if (kind is NodeKind.AirVent or NodeKind.GiantClam && Random.value < Time.deltaTime * 5f) // bubbles rising
            Fx.Burst(basePos + Vector3.up * 0.2f, new Color(0.8f, 0.95f, 1f, 0.9f), 1, 0.15f, 0.6f, 1.2f, -1.2f);

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
