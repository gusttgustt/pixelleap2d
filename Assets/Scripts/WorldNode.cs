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
    public bool built;            // placed by the player: the hammer can take it down (IslandGame.Build.cs)
    public bool IsAnimal => kind is NodeKind.Chicken or NodeKind.Cow;

    // farm animals stroll around where they were let out (IslandGame.Farm.cs)
    Vector2 home, wanderDir;
    Vector2? netGrid; // a friend's animal: walks where its owner's game says it is (IslandGame.HomeSync.cs)
    float wanderT, walkT;

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
                                 or NodeKind.SunkenTemple or NodeKind.GiantClam or NodeKind.PirateShip or NodeKind.Tent or NodeKind.Cannon
                                 or NodeKind.Mirror or NodeKind.CrystalVault or NodeKind.LightningRod
                                 or NodeKind.GiantGrass or NodeKind.GiantFlower or NodeKind.Beehive or NodeKind.Anthill
                                 or NodeKind.Fence or NodeKind.Bench or NodeKind.Scarecrow or NodeKind.Cottage or NodeKind.Signpost
                                 or NodeKind.Bed or NodeKind.Table or NodeKind.Chair or NodeKind.PlantPot or NodeKind.Shelf or NodeKind.Workbench
                                 or NodeKind.CaveEntrance;

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
        NodeKind.Mirror => 0.35f,
        NodeKind.CrystalVault => 1.1f,
        NodeKind.LightningRod => 0.3f,
        NodeKind.GiantGrass => 0.45f,
        NodeKind.GiantFlower => 0.35f,
        NodeKind.Beehive => 0.4f,
        NodeKind.Anthill => 1.3f,
        NodeKind.GiantClam => 0.5f,
        NodeKind.ClockRuin => 0.45f,
        NodeKind.Fence => 0.34f,
        NodeKind.Bench => 0.36f,
        NodeKind.Scarecrow => 0.22f,
        NodeKind.Cottage => 1.0f,
        NodeKind.Signpost => 0.2f,
        NodeKind.Bed => 0.5f, NodeKind.Table => 0.4f, NodeKind.Chair => 0.25f, NodeKind.PlantPot => 0.22f,
        NodeKind.Shelf => 0.32f, NodeKind.Workbench => 0.42f,
        NodeKind.CaveEntrance => 0.8f,
        _ => 0.32f,
    };

    public bool CanTarget => kind switch
    {
        NodeKind.Stump => true, // chop it out with an axe (IslandGame.Play.cs)
        NodeKind.Hut or NodeKind.Totem or NodeKind.House or NodeKind.Lamp or NodeKind.Stall or NodeKind.Statue or NodeKind.ClockRuin or NodeKind.ClockTower
            or NodeKind.Wreck or NodeKind.Pillar or NodeKind.GiantTree or NodeKind.SunkenTemple
            or NodeKind.PirateShip or NodeKind.Tent or NodeKind.Cannon => false,
        NodeKind.GiantClam => true,
        NodeKind.Mirror or NodeKind.CrystalVault or NodeKind.LightningRod or NodeKind.GiantGrass => true,
        NodeKind.Anthill => false,
        NodeKind.Plot or NodeKind.Chicken or NodeKind.Cow or NodeKind.Cottage or NodeKind.Signpost => true,
        NodeKind.Fence or NodeKind.StonePath or NodeKind.Flowers or NodeKind.Bench or NodeKind.Scarecrow => false,
        NodeKind.Bed or NodeKind.Workbench or NodeKind.HouseDoor => true,
        NodeKind.CaveEntrance or NodeKind.CaveExit => true,
        NodeKind.Table or NodeKind.Chair or NodeKind.Rug or NodeKind.PlantPot or NodeKind.Shelf => false,
        NodeKind.Shrine => true,
        NodeKind.Well or NodeKind.Fountain or NodeKind.AirVent or NodeKind.HotSpring => true,
        NodeKind.Tree or NodeKind.Campfire or NodeKind.Shelter or NodeKind.Raft => true,
        NodeKind.Bush or NodeKind.FruitTree => available || IslandGame.I.HasAxe, // picked clean: chop it down
        _ => available,
    };

    public float PromptHeight => kind switch
    {
        NodeKind.Tree or NodeKind.FruitTree or NodeKind.GlowTree => 1.45f,
        NodeKind.Shelter => 1.05f,
        NodeKind.Raft => IslandGame.I.BoatPromptHeight,
        NodeKind.Bush => 0.65f,
        NodeKind.Bamboo => 0.95f,
        NodeKind.Campfire => 0.85f,
        NodeKind.Well or NodeKind.Fountain => 0.9f,
        NodeKind.Farm => 0.5f,
        NodeKind.Kelp => 1.2f,
        NodeKind.Shrine => 1.0f,
        NodeKind.Mirror => 1.0f,
        NodeKind.CrystalVault => 1.8f,
        NodeKind.LightningRod => 2.0f,
        NodeKind.GiantGrass => 1.6f,
        NodeKind.GiantFlower => 1.8f,
        NodeKind.Beehive => 1.6f,
        NodeKind.AirVent => 0.6f,
        NodeKind.Plot => 0.55f,
        NodeKind.Cow => 0.8f,
        NodeKind.Chicken => 0.5f,
        NodeKind.Cottage => 1.9f,
        NodeKind.Signpost => 1.0f,
        NodeKind.Bed => 0.6f, NodeKind.Workbench => 0.8f, NodeKind.HouseDoor => 0.3f,
        NodeKind.CaveEntrance => 1.4f, NodeKind.CaveExit => 0.5f,
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
        if (k is NodeKind.StonePath or NodeKind.Rug or NodeKind.HouseDoor or NodeKind.CaveExit) sr.sortingOrder = k == NodeKind.Rug ? -30490 : -30500;                      // flat on the ground
        else if (k == NodeKind.Plot) sr.sortingOrder = IslandGame.SortOrder(flat.y + IslandGame.HalfH); // under anyone standing on it
        home = g;
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
        if (IslandGame.IsHomeKind(kind)) { sr.color = Color.white; sr.sprite = IslandGame.HomeSprite(this, 0); return; }
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
            NodeKind.Raft => IslandGame.I.BoatSprite,
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
            NodeKind.CrystalCluster => PixelArt.CrystalCluster(available),
            NodeKind.LightningRod => PixelArt.LightningRod(hits > 0),
            NodeKind.CopperOre => PixelArt.CopperOre(available),
            NodeKind.GiantGrass => PixelArt.GiantGrass(flip),
            NodeKind.GiantFlower => PixelArt.GiantFlower(available),
            NodeKind.Beehive => PixelArt.Beehive(available),
            NodeKind.Anthill => PixelArt.Anthill(),
            NodeKind.Mirror => PixelArt.Mirror(hits % 4),
            NodeKind.CrystalVault => PixelArt.CrystalVault(IslandGame.I.Flag("vault" + IslandGame.I.island)),
            NodeKind.HotSpring => PixelArt.HotSpring(),
            NodeKind.CloudPlant => PixelArt.CloudPlant(available),
            NodeKind.GlowTree => PixelArt.GlowTree(available, IslandGame.FruitFor(biome)),
            NodeKind.CaveEntrance => PixelArt.CaveMouth(),
            NodeKind.CaveExit => PixelArt.CaveLight(),
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

    /// Seconds until the current growth stage / animal product is done (0 = not running).
    public float TimerLeft => regrowAt > 0f ? Mathf.Max(0f, regrowAt - IslandGame.I.Now) : 0f;
    public void StartTimer(float seconds) => regrowAt = IslandGame.I.Now + seconds;

    /// Farm animals: mostly stand and peck, sometimes stroll, never far from where they were let out.
    void Wander(IslandGame game)
    {
        float dt = Time.deltaTime;
        var player = game.player;
        bool near = player != null && (player.grid - grid).magnitude < 1.1f; // hold still to be fed / milked
        foreach (var r in game.remotes.Values) if (r != null && (r.grid - grid).magnitude < 1.1f) near = true; // a visiting friend too
        bool moving = false;
        if (netGrid != null) // someone else's animal: follow the owner's copy
        {
            Vector2 to = netGrid.Value - grid;
            if (to.magnitude > 3f) grid = netGrid.Value;
            else if (to.magnitude > 0.02f)
            {
                wanderDir = to.normalized;
                grid = Vector2.MoveTowards(grid, netGrid.Value, (kind == NodeKind.Cow ? 0.5f : 0.8f) * dt);
                moving = true;
            }
        }
        else if (!near && !game.Busy)
        {
            wanderT -= dt;
            if (wanderT <= 0f)
            {
                wanderT = Random.Range(1.5f, 3.5f);
                Vector2 toHome = home - grid;
                wanderDir = Random.value < 0.5f ? Vector2.zero : toHome.magnitude > 2.2f ? toHome.normalized : Random.insideUnitCircle.normalized;
            }
            if (wanderDir != Vector2.zero)
            {
                float speed = kind == NodeKind.Cow ? 0.35f : 0.6f;
                Vector2 next = grid + wanderDir * speed * dt;
                if (game.CanWalk(next, 0.15f) && game.CanClimb(grid, next) && (player == null || (player.grid - next).magnitude > 0.5f)) { grid = next; moving = true; }
                else wanderT = 0f;
            }
        }
        if (moving)
        {
            walkT += dt * (kind == NodeKind.Cow ? 4f : 7f);
            Vector3 w = IslandGame.GridToWorld(grid + wanderDir) - IslandGame.GridToWorld(grid);
            if (Mathf.Abs(w.x) > 0.01f) sr.flipX = w.x < 0;
            Vector3 flat = IslandGame.GridToWorld(grid);
            basePos = flat + Vector3.up * game.HeightAt(grid);
            sr.sortingOrder = IslandGame.SortOrder(flat.y);
        }
        else if (near && player != null) sr.flipX = player.transform.position.x < transform.position.x;
        sr.sprite = IslandGame.HomeSprite(this, moving ? 1 + ((int)walkT & 1) : 0);
    }

    /// A friend's copy of this node changed (private islands online, IslandGame.HomeSync.cs).
    public void ApplyState(NodeSave s, bool follow)
    {
        kind = s.kind;
        available = s.available;
        hits = s.hits;
        built = s.built;
        dropItem = (Item)s.item;
        dropCount = s.count;
        regrowAt = s.regrow > 0f ? IslandGame.I.Now + s.regrow : 0f;
        if (IsAnimal && follow) netGrid = s.grid;
        else if ((s.grid - grid).sqrMagnitude > 0.0001f && !IsAnimal)
        {
            grid = s.grid;
            Vector3 flat = IslandGame.GridToWorld(grid);
            basePos = flat + Vector3.up * IslandGame.I.HeightAt(grid);
            sr.sortingOrder = IslandGame.SortOrder(flat.y);
        }
        Refresh();
    }

    /// Picked up and put down somewhere else on the private island (the move tool, IslandGame.Build.cs).
    public void MoveTo(Vector2 g)
    {
        grid = g;
        home = g;
        netGrid = null;
        Vector3 flat = IslandGame.GridToWorld(g);
        basePos = flat + Vector3.up * IslandGame.I.HeightAt(g);
        transform.position = basePos;
        if (kind is NodeKind.StonePath or NodeKind.Rug or NodeKind.HouseDoor) sr.sortingOrder = kind == NodeKind.Rug ? -30490 : -30500;
        else if (kind == NodeKind.Plot) sr.sortingOrder = IslandGame.SortOrder(flat.y + IslandGame.HalfH);
        else sr.sortingOrder = IslandGame.SortOrder(flat.y);
        SetFaded(false);
        Shake();
    }

    /// Half see-through while it's being carried to a new spot.
    public void SetFaded(bool on)
    {
        var c = sr.color;
        c.a = on ? 0.35f : 1f;
        sr.color = c;
    }

    public Sprite CurrentSprite => sr != null ? sr.sprite : null;
    public bool Flipped => sr != null && sr.flipX;

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
        if (regrowAt > 0f && game.Now >= regrowAt && kind == NodeKind.Plot)
        {
            regrowAt = 0f;
            game.CropGrown(this); // next growth stage (IslandGame.Farm.cs)
        }
        else if (regrowAt > 0f && game.Now >= regrowAt)
        {
            var player = game.player;
            if (kind == NodeKind.Stump && player != null && (player.grid - grid).magnitude < 0.7f)
                regrowAt = game.Now + 5f; // don't grow a tree on top of the player
            else
            {
                regrowAt = 0f;
                available = true;
                if (kind == NodeKind.Stump) { kind = NodeKind.Tree; hits = 0; }
                else if (kind is NodeKind.Bush or NodeKind.FruitTree) hits = 0; // fruit again: a half-chopped one heals
                Refresh();
            }
        }

        if (IsAnimal) Wander(game);
        Vector3 p = basePos;
        if (kind == NodeKind.Raft) // big ships lie out in the water, not up the beach (on whichever shore it's on)
        {
            p += game.BoatShoreOffsetAt(grid);
            if (sr != null) sr.flipX = game.SeaSide(grid).x > 0.3f; // sea to the right: face the other way
        }
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
