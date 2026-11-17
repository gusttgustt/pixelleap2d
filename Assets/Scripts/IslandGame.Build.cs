using UnityEngine;
using UnityEngine.InputSystem;

/// Build mode (G): put things down on the isometric tile grid of your private island, one tile
/// each (the cottage takes a 3 x 3 patch), and take them down again with the hammer for half of
/// their materials back. The hammer also works on adventure islands, on campfires and shelters
/// you built there.
public partial class IslandGame
{
    class BuildDef
    {
        public string name, desc;
        public NodeKind kind;
        public (Item item, int n)[] cost = new (Item, int)[0];
        public bool hammer;   // the take-down tool, not something to place
        public bool move;     // the move tool: pick something up and put it down somewhere else
        public bool turns;    // R rotates it a quarter turn
        public Item? item;    // furniture: placed from the bag, picked up back into it
    }

    /// Inside the house: a hand to pick furniture up (to move it), then the furniture you own.
    static readonly BuildDef[] RoomBuilds =
    {
        new BuildDef { name = "มือ: เก็บ / ย้ายของ", hammer = true, desc = "เก็บเฟอร์นิเจอร์กลับเข้ากระเป๋า แล้ววางใหม่ที่อื่นได้" },
        new BuildDef { name = "เตียงนอน", kind = NodeKind.Bed, item = Item.FurnBed, turns = true, desc = "นอนตอนกลางคืน กด R หมุน" },
        new BuildDef { name = "โต๊ะช่าง", kind = NodeKind.Workbench, item = Item.FurnWorkbench, turns = true, desc = "คราฟต์ของฟาร์มและของตกแต่ง" },
        new BuildDef { name = "โต๊ะไม้", kind = NodeKind.Table, item = Item.FurnTable, turns = true, desc = "ของตกแต่ง กด R หมุน" },
        new BuildDef { name = "เก้าอี้ไม้", kind = NodeKind.Chair, item = Item.FurnChair, turns = true, desc = "ของตกแต่ง กด R หมุน" },
        new BuildDef { name = "พรมทอมือ", kind = NodeKind.Rug, item = Item.FurnRug, desc = "ปูพื้น วางของทับได้" },
        new BuildDef { name = "กระถางต้นไม้", kind = NodeKind.PlantPot, item = Item.FurnPlant, desc = "ของตกแต่ง" },
        new BuildDef { name = "ชั้นวางของ", kind = NodeKind.Shelf, item = Item.FurnShelf, turns = true, desc = "ของตกแต่ง กด R หมุน" },
    };

    BuildDef[] Palette => inHouse ? RoomBuilds : atHome ? Builds : new[] { Builds[0] };
    static bool Flat(NodeKind k) => k is NodeKind.StonePath or NodeKind.Rug;

    static readonly BuildDef[] Builds =
    {
        new BuildDef { name = "ค้อน: รื้อถอน", hammer = true, desc = "รื้อสิ่งที่สร้างไว้ ได้วัตถุดิบคืนครึ่งหนึ่ง" },
        new BuildDef { name = "มือ: ย้ายของ", move = true, desc = "ย้ายบ้าน สัตว์ในฟาร์ม รั้ว แปลงผัก และของที่สร้างไว้ไปวางที่ใหม่ ฟรี" },
        new BuildDef { name = "แปลงผัก", kind = NodeKind.Plot, desc = "ขุดด้วยจอบบนพื้นหญ้า แล้วปลูก รดน้ำ เก็บเกี่ยว" },
        new BuildDef { name = "รั้วไม้", kind = NodeKind.Fence, turns = true, cost = new[] { (Item.Stick, 2), (Item.Wood, 1) }, desc = "ล้อมคอกสัตว์ กด R หมุน" },
        new BuildDef { name = "ทางเดินหิน", kind = NodeKind.StonePath, cost = new[] { (Item.Stone, 2) }, desc = "ปูพื้นหิน เดินทับได้" },
        new BuildDef { name = "แปลงดอกไม้", kind = NodeKind.Flowers, cost = new[] { (Item.Fiber, 3), (Item.Berry, 1) }, desc = "ของประดับ" },
        new BuildDef { name = "ม้านั่ง", kind = NodeKind.Bench, turns = true, cost = new[] { (Item.Plank, 2), (Item.Stick, 2) }, desc = "ของประดับ กด R หมุน" },
        new BuildDef { name = "หุ่นไล่กา", kind = NodeKind.Scarecrow, cost = new[] { (Item.Stick, 3), (Item.Fiber, 5) }, desc = "ผักในรัศมี 3 ช่องโตเร็วขึ้น 25%" },
        new BuildDef { name = "ตะเกียง", kind = NodeKind.Lamp, cost = new[] { (Item.Wood, 2), (Item.Stone, 2), (Item.Rope, 1) }, desc = "ส่องสว่างตอนกลางคืน" },
        new BuildDef { name = "กองไฟ", kind = NodeKind.Campfire, cost = new[] { (Item.Wood, 3), (Item.Stone, 4), (Item.Stick, 2) }, desc = "ทำอาหาร ทอดไข่ ย่างปลา" },
        new BuildDef { name = "บ่อน้ำ", kind = NodeKind.Well, cost = new[] { (Item.Stone, 10), (Item.Wood, 4), (Item.Rope, 2) }, desc = "ดื่มน้ำ เติมกระบอกน้ำ" },
        new BuildDef { name = "เพิงพัก", kind = NodeKind.Shelter, cost = new[] { (Item.Wood, 6), (Item.Rope, 3), (Item.Fiber, 6) }, desc = "นอนตอนกลางคืน" },
        new BuildDef { name = "บ้านไม้", kind = NodeKind.Cottage, cost = new[] { (Item.Plank, 12), (Item.Wood, 6), (Item.Stone, 8), (Item.Rope, 4) }, desc = "บ้านในฝัน ใช้พื้นที่ 3x3 ช่อง นอนได้ตอนกลางคืน" },
    };

    public bool buildOpen;
    int buildSel = 2;
    WorldNode carrying;   // the move tool: what you picked up, on its way to a new spot
    WorldNode moveTarget; // the move tool: what you would pick up now
    bool buildTurn;
    SpriteRenderer ghost;
    // what the build cursor is on this frame (also shown in the panel)
    Vector2Int buildTile;
    bool buildOk;
    string buildWhy;
    WorldNode demolishTarget;
    // Point and click / tap to place: the ghost follows the mouse, a click or tap on the ground
    // puts the thing right there (no need to walk up and face the spot).
    bool buildPointer, buildTap;
    Vector2 lastMousePx = new Vector2(-1f, -1f);
    Vector2Int pointerTile;
    Rect buildPanelRect;
    const float PointerReach = 6f;

    BuildDef CurrentBuild => Palette[Mathf.Clamp(buildSel, 0, Palette.Length - 1)];

    void ToggleBuild()
    {
        if (buildOpen) { CloseBuild(); return; }
        if (player == null || voyage != null || dead || Busy) return;
        if (visiting) { Hint("นี่เป็นบ้านของเพื่อน สร้างหรือรื้ออะไรไม่ได้", 2.5f); return; }
        if (!atHome && Count(Item.Hammer) == 0) { Hint("โหมดสร้างใช้ได้บนเกาะส่วนตัว (H) ส่วนบนเกาะผจญภัยใช้ค้อนรื้อกองไฟหรือเพิงพักที่สร้างไว้ได้", 3.5f); return; }
        buildOpen = true;
        buildSel = Mathf.Clamp(buildSel, 0, Palette.Length - 1);
        if (!atHome) buildSel = 0; // only the hammer away from home
        craftOpen = false; skillsOpen = false; bagOpen = false;
        CancelLine();
        Hint(inHouse ? "จัดบ้าน: เลือกของ แล้วแตะ/คลิกที่พื้นตรงไหนก็ได้เพื่อวาง ช่องแรกคือมือไว้เก็บ/ย้ายของ" : atHome ? "โหมดสร้าง: เลือกของ แล้วแตะ/คลิกที่พื้นเพื่อวาง (หรือหันหน้าแล้วกด E) R หมุน G ออก" : "ค้อน: แตะสิ่งที่สร้างไว้ หรือหันไปหาแล้วกด E เพื่อรื้อ", 4f);
    }

    void CloseBuild()
    {
        buildOpen = false;
        if (ghost != null) Destroy(ghost.gameObject);
        ghost = null;
        demolishTarget = null;
        DropCarried();
    }

    void DropCarried()
    {
        if (carrying != null) carrying.SetFaded(false);
        carrying = null;
        moveTarget = null;
    }

    void SelectBuild(int i)
    {
        if (!atHome) { buildSel = 0; return; }
        int n = Palette.Length;
        buildSel = (i % n + n) % n;
        if (!CurrentBuild.move) DropCarried();
    }

    void HandleBuildInput(Keyboard kb)
    {
        if (player == null) return;
        if (kb != null && !chatOpen)
        {
            if (kb.escapeKey.wasPressedThisFrame && carrying != null) { DropCarried(); Hint("ยกเลิกการย้าย", 1.5f); return; }
            if (kb.escapeKey.wasPressedThisFrame || kb.gKey.wasPressedThisFrame) { CloseBuild(); return; }
            if (kb.hKey.wasPressedThisFrame) { ToggleHome(); return; }
            if (kb.zKey.wasPressedThisFrame) SelectBuild(buildSel - 1);
            if (kb.xKey.wasPressedThisFrame) SelectBuild(buildSel + 1);
            if (kb.rKey.wasPressedThisFrame) buildTurn = !buildTurn;
            if (kb.mKey.wasPressedThisFrame) Sfx.ToggleMute();
            if (kb.fKey.wasPressedThisFrame) EatBest();
            if (kb.qKey.wasPressedThisFrame) DrinkFlask();
            if (kb.bKey.wasPressedThisFrame || kb.iKey.wasPressedThisFrame) { CloseBuild(); ToggleBag(); return; }
        }
        UpdateBuildCursor();

        if (buildTap) // clicked / tapped the ground: act right there
        {
            buildTap = false;
            lastInteract = Time.time;
            var tb = CurrentBuild;
            if (tb.move) { MoveAction(true); return; }
            if (tb.hammer)
            {
                if (Count(Item.Hammer) == 0 && !inHouse) { Hint("ต้องมีค้อน (ทำที่โต๊ะช่างในบ้าน หรือซื้อที่ร้านช่างไม้)", 2.5f); return; }
                if (demolishTarget == null) { Hint(inHouse ? "แตะที่เฟอร์นิเจอร์เพื่อเก็บ" : "แตะที่สิ่งที่สร้างไว้เพื่อรื้อ", 2f); return; }
                Demolish(demolishTarget);
                return;
            }
            if (!buildOk) { Hint(buildWhy, 2f); return; }
            Place(tb, buildTile);
            return;
        }

        bool pressed = touchActionPressed || (kb != null && !chatOpen && (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame));
        bool held = touchActionHeld || (kb != null && !chatOpen && (kb.eKey.isPressed || kb.spaceKey.isPressed));
        float since = Time.time - lastInteract;
        // holding E keeps placing as you walk: handy for fences and paths
        if (!((pressed && since > 0.15f) || (held && since > 0.3f))) return;
        lastInteract = Time.time;
        var b = CurrentBuild;
        if (b.move) { if (pressed) MoveAction(false); return; }
        if (b.hammer)
        {
            if (Count(Item.Hammer) == 0 && !inHouse) { Hint("ต้องมีค้อน (ทำที่โต๊ะช่างในบ้าน หรือซื้อที่ร้านช่างไม้)", 2.5f); return; }
            if (demolishTarget == null) { if (pressed) Hint("หันหน้าไปที่สิ่งที่สร้างไว้เพื่อรื้อ", 2f); return; }
            Demolish(demolishTarget);
            return;
        }
        if (!buildOk) { if (pressed) Hint(buildWhy, 2f); return; }
        Place(b, buildTile);
    }

    Vector2Int FacingTile(float dist) => Tile(player.grid + player.facing.normalized * dist);

    /// The tile under a screen position (pixels, origin bottom-left), if it's ground within reach.
    Vector2Int? ScreenTile(Vector2 px)
    {
        if (cam == null || player == null) return null;
        Vector3 w = cam.ScreenToWorldPoint(new Vector3(px.x, px.y, 0f));
        Vector2 g = WorldDeltaToGrid(w);
        g = WorldDeltaToGrid(new Vector2(w.x, w.y - HeightAt(g))); // stand on hills
        var t = Tile(g);
        if (!InMap(t.x, t.y) || (tiles[t.x, t.y] != 1 && tiles[t.x, t.y] != 2)) return null;
        if (!inHouse && ((Vector2)t - player.grid).magnitude > PointerReach) return null;
        return t;
    }

    /// Mouse hover moves the build cursor; walking (keys / stick) hands it back to facing.
    void UpdateBuildPointer()
    {
        if (PlayerController.ReadInput().sqrMagnitude > 0.01f) buildPointer = false;
        var m = Mouse.current;
        if (m == null || touchUsed) return; // on a phone, only taps aim
        Vector2 px = m.position.ReadValue();
        if ((px - lastMousePx).sqrMagnitude > 9f)
        {
            bool first = lastMousePx.x < 0f;
            lastMousePx = px;
            if (!first) buildPointer = true;
        }
        if (buildPointer)
        {
            var t = ScreenTile(px);
            if (t != null) pointerTile = t.Value; else buildPointer = false;
        }
    }

    /// OnGUI: a click / tap on the ground (not on a button or the panel) while building.
    void BuildPointerEvent(float s)
    {
        var e = Event.current;
        if (!buildOpen || Busy || e.type != EventType.MouseDown || e.button != 0) return;
        if (buildPanelRect.Contains(e.mousePosition)) return;
        var bar = HotbarRect(Screen.width / s, Screen.height / s);
        if (new Rect(bar.x - 8, bar.y - 8, bar.width + 16, bar.height + 16).Contains(e.mousePosition)) return;
        var t = ScreenTile(new Vector2(e.mousePosition.x * s, Screen.height - e.mousePosition.y * s));
        if (t == null) return;
        e.Use();
        pointerTile = t.Value;
        buildPointer = true;
        buildTap = true;
    }

    void UpdateBuildCursor()
    {
        var b = CurrentBuild;
        if (ghost == null)
        {
            ghost = new GameObject("BuildGhost").AddComponent<SpriteRenderer>();
            ghost.transform.SetParent(world);
            ghost.sortingOrder = 32000;
        }
        demolishTarget = null;
        moveTarget = null;
        UpdateBuildPointer();
        if (b.move) { UpdateMoveCursor(); return; }
        if (b.hammer)
        {
            ghost.enabled = false;
            demolishTarget = FindDemolishTarget();
            cursor.enabled = demolishTarget != null;
            cursor.transform.localScale = Vector3.one;
            if (demolishTarget != null)
            {
                cursor.transform.position = IslandGame.GridToWorld(demolishTarget.grid) + Vector3.up * HeightAt(demolishTarget.grid);
                cursor.color = new Color(1f, 0.35f, 0.3f, 0.6f + 0.4f * Mathf.Sin(Time.time * 8f));
            }
            buildOk = demolishTarget != null;
            buildWhy = demolishTarget != null ? $"กด E เพื่อ{(inHouse ? "เก็บ" : "รื้อ")}{NodeName(demolishTarget.kind)}" : inHouse ? "ไม่มีเฟอร์นิเจอร์ตรงหน้า" : "ไม่มีสิ่งที่สร้างไว้ตรงหน้า";
            return;
        }
        buildTile = buildPointer ? pointerTile : FacingTile(b.kind == NodeKind.Cottage ? 2.2f : 1.25f);
        buildOk = CanPlace(b, buildTile, out buildWhy);
        Vector2 g = buildTile;
        Vector3 pos = GridToWorld(g) + Vector3.up * HeightAt(g);
        ghost.enabled = true;
        ghost.sprite = BuildSprite(b.kind);
        ghost.flipX = b.turns && buildTurn;
        ghost.transform.position = pos;
        ghost.color = buildOk ? new Color(0.7f, 1f, 0.7f, 0.7f) : new Color(1f, 0.45f, 0.45f, 0.55f);
        cursor.enabled = true;
        cursor.transform.localScale = Vector3.one;
        cursor.transform.position = pos;
        cursor.color = buildOk ? new Color(0.6f, 1f, 0.6f, 0.9f) : new Color(1f, 0.4f, 0.4f, 0.9f);
    }

    static Sprite BuildSprite(NodeKind k) => k switch
    {
        NodeKind.Plot => PixelArt.Plot(null, 0, false),
        NodeKind.Fence => PixelArt.Fence(),
        NodeKind.StonePath => PixelArt.StonePath(),
        NodeKind.Flowers => PixelArt.Flowers(0),
        NodeKind.Bench => PixelArt.Bench(),
        NodeKind.Scarecrow => PixelArt.Scarecrow(),
        NodeKind.Lamp => PixelArt.Lamp(),
        NodeKind.Campfire => PixelArt.Campfire(0),
        NodeKind.Well => PixelArt.Well(),
        NodeKind.Shelter => PixelArt.Shelter(),
        NodeKind.Cottage => PixelArt.Cottage(Mathf.Max(1, I.houseLevel)),
        NodeKind.Bed => PixelArt.Bed(),
        NodeKind.Table => PixelArt.Table(),
        NodeKind.Chair => PixelArt.Chair(),
        NodeKind.Rug => PixelArt.Rug(),
        NodeKind.PlantPot => PixelArt.PlantPot(),
        NodeKind.Shelf => PixelArt.Shelf(),
        NodeKind.Workbench => PixelArt.Workbench(),
        _ => PixelArt.Icon(Item.Hammer),
    };

    static string NodeName(NodeKind k)
    {
        foreach (var b in Builds) if (!b.hammer && b.kind == k) return b.name;
        foreach (var b in RoomBuilds) if (!b.hammer && b.kind == k) return b.name;
        return k == NodeKind.Shelter ? "เพิงพัก" : "สิ่งก่อสร้าง";
    }

    static float BuildRadius(NodeKind k) => k switch
    {
        NodeKind.Cottage => 1.0f, NodeKind.Well => 0.45f, NodeKind.Shelter => 0.6f, NodeKind.Campfire => 0.4f,
        NodeKind.Fence => 0.34f, NodeKind.Bench => 0.36f, NodeKind.Lamp => 0.18f, NodeKind.Scarecrow => 0.22f,
        NodeKind.Bed => 0.5f, NodeKind.Table => 0.4f, NodeKind.Chair => 0.25f, NodeKind.PlantPot => 0.22f, NodeKind.Shelf => 0.32f, NodeKind.Workbench => 0.42f,
        _ => 0f, // plots, paths and flower beds can be walked over
    };

    /// Hidden things (picked grass waiting to regrow) don't count as being in the way.
    static bool Hidden(WorldNode n) => !n.available && n.kind is NodeKind.Grass or NodeKind.Stick or NodeKind.Rock or NodeKind.Bamboo or NodeKind.Obsidian or NodeKind.Shell;
    /// Small things lying around that building just clears away (sticks and stones go into your bag).
    static bool Clearable(WorldNode n) => !n.built && n.kind is NodeKind.Grass or NodeKind.Stick or NodeKind.Rock or NodeKind.Drop;

    bool CanPlace(BuildDef b, Vector2Int t, out string why)
    {
        why = null;
        if (!atHome) { why = "สร้างได้เฉพาะบนเกาะส่วนตัว"; return false; }
        if (!InMap(t.x, t.y) || (tiles[t.x, t.y] != 1 && tiles[t.x, t.y] != 2)) { why = "ต้องวางบนพื้นดิน"; return false; }
        if (b.kind == NodeKind.Cottage && nodes.Exists(n => n != null && n.kind == NodeKind.Cottage)) { why = "มีบ้านแล้ว (อัปเกรดได้ที่ตัวบ้าน)"; return false; }
        if (b.item != null && Count(b.item.Value) == 0) { why = $"ไม่มี{Name(b.item.Value)}ในกระเป๋า (คราฟต์ที่โต๊ะช่าง)"; return false; }
        if (b.kind == NodeKind.Plot)
        {
            if (Count(Item.Hoe) == 0) { why = "ต้องมีจอบ (ทำที่โต๊ะช่างในบ้าน หรือซื้อที่ร้านเมล็ดพันธุ์)"; return false; }
            if (tiles[t.x, t.y] != 2) { why = "ขุดแปลงผักได้บนพื้นหญ้าเท่านั้น"; return false; }
        }
        if (!SpotFree(b.kind, t, null, out why)) return false;
        foreach (var c in b.cost) if (Count(c.item) < c.n) { why = MissingMaterials; return false; }
        return true;
    }

    /// Nothing in the way of putting a k on tile t (ignore: the thing being moved there).
    bool SpotFree(NodeKind k, Vector2Int t, WorldNode ignore, out string why)
    {
        why = null;
        Vector2 g = t;
        bool big = k == NodeKind.Cottage;
        if (big) // the whole 3 x 3 patch must be flat land
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (!IsLand(g + new Vector2(dx, dy)) || LevelAt(g + new Vector2(dx, dy)) != LevelAt(g)) { why = "บ้านต้องใช้พื้นเรียบ 3x3 ช่อง"; return false; }
        float own = BuildRadius(k);
        bool animal = k is NodeKind.Chicken or NodeKind.Cow;
        foreach (var n in nodes)
        {
            if (n == null || n == ignore || Hidden(n) || Clearable(n) || (n.IsAnimal && !big)) continue;
            if (Flat(k) != Flat(n.kind) && !big) continue; // furniture on rugs, benches on paths
            float need = big ? 1.45f : animal ? 0.45f : 0.55f;
            if (n.Blocking) need = Mathf.Max(need, n.BlockRadius + (big ? 0.9f : 0.2f));
            if ((n.grid - g).magnitude < need) { why = $"มี{(n.built ? NodeName(n.kind) : "ของ")}อยู่ตรงนี้แล้ว"; return false; }
        }
        foreach (var v in villagers) if (v != null && (own > 0f || big) && (v.grid - g).magnitude < own + 0.3f) { why = "มีคนยืนอยู่ตรงนี้"; return false; }
        return true;
    }

    // ------------------------------------------------------------------ the move tool
    /// Things on your private island you can pick up and put somewhere else: what you built,
    /// your house, farm plots (crops and all) and the farm animals.
    static bool Movable(WorldNode n) => n != null && (n.built || n.IsAnimal || n.kind is NodeKind.Plot or NodeKind.Cottage or NodeKind.Well);

    static string MoveName(NodeKind k) => k switch
    {
        NodeKind.Cottage => "บ้าน", NodeKind.Chicken => "ไก่", NodeKind.Cow => "วัว", NodeKind.Well => "บ่อน้ำ",
        _ => NodeName(k),
    };

    WorldNode FindMoveTarget()
    {
        bool aim = buildPointer;
        Vector2 p = aim ? (Vector2)pointerTile : player.grid + player.facing.normalized * 1.0f;
        WorldNode best = null;
        float bd = float.MaxValue;
        foreach (var n in nodes)
        {
            if (!Movable(n)) continue;
            bool big = n.kind == NodeKind.Cottage;
            float d = (n.grid - p).magnitude;
            float reach = big ? 1.6f : Mathf.Max(0.75f, BuildRadius(n.kind) + 0.35f);
            if (d < reach && d < bd && (aim || (n.grid - player.grid).magnitude < (big ? 3.2f : 2.6f))) { bd = d; best = n; }
        }
        return best;
    }

    bool CanMoveTo(WorldNode n, Vector2Int t, out string why)
    {
        why = null;
        if (!InMap(t.x, t.y) || (tiles[t.x, t.y] != 1 && tiles[t.x, t.y] != 2)) { why = "ต้องวางบนพื้นดิน"; return false; }
        if (n.kind == NodeKind.Plot && tiles[t.x, t.y] != 2) { why = "แปลงผักต้องอยู่บนพื้นหญ้า"; return false; }
        if (((Vector2)t - n.grid).sqrMagnitude < 0.01f) { why = "เลือกที่ใหม่ที่ไม่ใช่ที่เดิม"; return false; }
        return SpotFree(n.kind, t, n, out why);
    }

    void UpdateMoveCursor()
    {
        cursor.transform.localScale = Vector3.one;
        if (carrying == null)
        {
            ghost.enabled = false;
            moveTarget = FindMoveTarget();
            cursor.enabled = moveTarget != null;
            if (moveTarget != null)
            {
                cursor.transform.position = GridToWorld(moveTarget.grid) + Vector3.up * HeightAt(moveTarget.grid);
                cursor.color = new Color(1f, 0.85f, 0.3f, 0.6f + 0.4f * Mathf.Sin(Time.time * 8f));
            }
            buildOk = moveTarget != null;
            buildWhy = moveTarget != null ? $"กด E หรือแตะเพื่อยก{MoveName(moveTarget.kind)}" : "ไม่มีของที่ย้ายได้ตรงหน้า (บ้าน สัตว์ รั้ว แปลงผัก ของที่สร้าง)";
            return;
        }
        buildTile = buildPointer ? pointerTile : FacingTile(carrying.kind == NodeKind.Cottage ? 2.2f : 1.25f);
        buildOk = CanMoveTo(carrying, buildTile, out buildWhy);
        Vector2 g = buildTile;
        Vector3 pos = GridToWorld(g) + Vector3.up * HeightAt(g);
        ghost.enabled = true;
        ghost.sprite = carrying.CurrentSprite;
        ghost.flipX = carrying.Flipped;
        ghost.transform.position = pos;
        ghost.color = buildOk ? new Color(0.7f, 1f, 0.7f, 0.75f) : new Color(1f, 0.45f, 0.45f, 0.55f);
        cursor.enabled = true;
        cursor.transform.position = pos;
        cursor.color = buildOk ? new Color(0.6f, 1f, 0.6f, 0.9f) : new Color(1f, 0.4f, 0.4f, 0.9f);
        if (buildOk) buildWhy = $"กด E หรือแตะเพื่อวาง{MoveName(carrying.kind)}ตรงนี้ (Esc ยกเลิก)";
    }

    void MoveAction(bool tapped)
    {
        if (carrying == null)
        {
            if (moveTarget == null) { Hint(tapped ? "แตะที่ของที่อยากย้าย: บ้าน สัตว์ในฟาร์ม รั้ว แปลงผัก หรือของที่สร้างไว้" : "หันหน้าไปที่ของที่อยากย้าย", 2.5f); return; }
            carrying = moveTarget;
            carrying.SetFaded(true);
            Sfx.Play("pickup", 0.4f);
            Hint($"ยก{MoveName(carrying.kind)}แล้ว เลือกที่ใหม่ แล้วแตะ/กด E เพื่อวาง (Esc ยกเลิก)", 3f);
            return;
        }
        if (!buildOk) { Hint(buildWhy, 2f); return; }
        var n = carrying;
        Vector2 g = buildTile;
        float clear = n.kind == NodeKind.Cottage ? 1.5f : 0.6f;
        foreach (var c in nodes.FindAll(c => c != null && c != n && (Hidden(c) || Clearable(c)) && (c.grid - g).magnitude < clear))
        {
            if (c.available && c.kind == NodeKind.Stick) inv[Item.Stick] = Count(Item.Stick) + 1;
            if (c.available && c.kind == NodeKind.Rock) inv[Item.Stone] = Count(Item.Stone) + 1;
            if (c.kind == NodeKind.Drop) inv[c.dropItem] = Count(c.dropItem) + c.dropCount;
            RemoveNode(c);
        }
        n.MoveTo(g);
        carrying = null;
        if (!CanWalk(player.grid, 0.18f)) player.grid = FreeStand(player.grid); // put down on top of yourself: step aside
        Fx.Burst(n.transform.position + Vector3.up * 0.2f, new Color(0.85f, 0.75f, 0.55f), n.kind == NodeKind.Cottage ? 20 : 8, 1f, 0.9f);
        Sfx.Play("craft", 0.45f);
        Popup($"ย้าย{MoveName(n.kind)}แล้ว", n.transform.position + Vector3.up * 0.8f, new Color(1f, 0.95f, 0.6f));
        homeSendT = Mathf.Min(homeSendT, 3f); // visitors who come later see it in the new spot
    }

    void Place(BuildDef b, Vector2Int t)
    {
        Vector2 g = t;
        foreach (var n in nodes.FindAll(n => n != null && (Hidden(n) || Clearable(n)) && (n.grid - g).magnitude < (b.kind == NodeKind.Cottage ? 1.5f : 0.6f)))
        {
            if (n.available && n.kind == NodeKind.Stick) inv[Item.Stick] = Count(Item.Stick) + 1;
            if (n.available && n.kind == NodeKind.Rock) inv[Item.Stone] = Count(Item.Stone) + 1;
            if (n.kind == NodeKind.Drop) inv[n.dropItem] = Count(n.dropItem) + n.dropCount;
            RemoveNode(n); // picked grass under it won't grow back through the floor
        }
        foreach (var c in b.cost) inv[c.item] -= c.n;
        if (b.item != null) inv[b.item.Value]--;
        if (b.kind == NodeKind.Cottage) houseLevel = Mathf.Max(1, houseLevel);
        var node = Spawn(b.kind, g, b.turns && buildTurn);
        node.built = true;
        if (!CanWalk(player.grid, 0.18f)) player.grid = FreeStand(player.grid); // built on top of yourself: step aside
        node.Shake();
        Fx.Burst(node.transform.position + Vector3.up * 0.2f, new Color(0.85f, 0.75f, 0.55f), b.kind == NodeKind.Cottage ? 24 : 8, 1f, 0.9f);
        Sfx.Play(b.kind == NodeKind.Plot ? "chop" : "craft", 0.45f);
        if (b.kind == NodeKind.Cottage) Hint("สร้างบ้านเสร็จแล้ว! กด E ที่บ้านเพื่อเข้าไปข้างใน หรืออัปเกรดบ้าน", 4f);
    }

    WorldNode FindDemolishTarget()
    {
        bool aim = buildPointer;
        Vector2 p = aim ? (Vector2)pointerTile : player.grid + player.facing.normalized * 1.0f;
        WorldNode best = null;
        float bd = float.MaxValue;
        foreach (var n in nodes)
        {
            if (n == null || !(n.built || n.kind == NodeKind.Plot)) continue;
            if (n.kind == NodeKind.Cottage) continue; // the house stays (upgrade it instead)
            float d = (n.grid - p).magnitude;
            float reach = Mathf.Max(0.75f, BuildRadius(n.kind) + 0.35f);
            if (d < reach && d < bd && (aim || (n.grid - player.grid).magnitude < 2.6f)) { bd = d; best = n; }
        }
        return best;
    }

    void Demolish(WorldNode n)
    {
        BuildDef def = null;
        foreach (var b in Palette) if (!b.hammer && b.kind == n.kind) { def = b; break; }
        if (def == null) foreach (var b in Builds) if (!b.hammer && b.kind == n.kind) { def = b; break; }
        var got = new System.Collections.Generic.List<string>();
        if (def?.item != null) // furniture goes back into the bag whole
        {
            inv[def.item.Value] = Count(def.item.Value) + 1;
            got.Add($"{Name(def.item.Value)} 1");
        }
        else if (def != null)
            for (int i = 0; i < def.cost.Length; i++)
            {
                var (it, cnt) = def.cost[i];
                int back = Mathf.Max(i == 0 ? 1 : 0, cnt / 2); // half back, at least one of the main material
                if (back <= 0) continue;
                inv[it] = Count(it) + back;
                got.Add($"{Name(it)} {back}");
            }
        if (n.kind == NodeKind.Plot && n.dropCount is 1 or 2) { inv[n.dropItem] = Count(n.dropItem) + 1; got.Add($"{Name(n.dropItem)} 1"); }
        if (n.kind == NodeKind.Plot && n.dropCount >= 3) { var c = CropOf(n.dropItem); inv[c] = Count(c) + 1; got.Add($"{Name(c)} 1"); }
        Vector3 at = n.transform.position;
        Fx.Burst(at + Vector3.up * 0.3f, new Color(0.7f, 0.55f, 0.38f), 14, 1.2f, 1.2f);
        Sfx.Play("chop", 0.6f);
        player.PlayAction(PixelArt.AttackAnim, 16f);
        Popup(got.Count > 0 ? "+" + string.Join(", ", got) : $"รื้อ{NodeName(n.kind)}แล้ว", at + Vector3.up * 0.8f, new Color(1f, 0.95f, 0.6f));
        RemoveShared(n);
    }

    // ------------------------------------------------------------------ HUD
    void DrawBuildPanel(float W, float H)
    {
        var bar = HotbarRect(W, H);
        var pal = Palette;
        int count = pal.Length;
        const float cell = 50f, gap = 4f;
        float w = count * (cell + gap) - gap;
        float x0 = W / 2f - w / 2f, y0 = bar.y - cell - 64f;
        var panel = new Rect(x0 - 10, y0 - 26, w + 20, cell + 84f);
        buildPanelRect = panel;
        Fill(panel, new Color(0.08f, 0.06f, 0.04f, 0.85f));
        Fill(new Rect(panel.x, panel.y, panel.width, 2), new Color(0.6f, 1f, 0.6f, 0.8f));
        ShadowLabel(new Rect(panel.x + 10, panel.y + 4, 300, 20), inHouse ? "จัดบ้าน" : atHome ? "โหมดสร้าง" : "ค้อน", label, new Color(0.7f, 1f, 0.7f));
        var bst = new GUIStyle(GUI.skin.button) { fontSize = 11 };
        if (CrispGUI.Button(new Rect(panel.xMax - 60, panel.y + 4, 52, 20), "ปิด (G)", bst)) { CloseBuild(); return; }
        if (atHome && CrispGUI.Button(new Rect(panel.xMax - 132, panel.y + 4, 68, 20), buildTurn ? "หมุน (R) ↻" : "หมุน (R)", bst)) buildTurn = !buildTurn;

        for (int i = 0; i < count; i++)
        {
            var b = pal[i];
            var r = new Rect(x0 + i * (cell + gap), y0, cell, cell);
            GUI.Box(r, GUIContent.none);
            if (i == buildSel) { Fill(new Rect(r.x, r.y, r.width, 2), new Color(0.6f, 1f, 0.6f)); Fill(new Rect(r.x, r.yMax - 2, r.width, 2), new Color(0.6f, 1f, 0.6f)); }
            bool afford = true;
            foreach (var c in b.cost) if (Count(c.item) < c.n) afford = false;
            if (b.hammer) afford = Count(Item.Hammer) > 0 || inHouse;
            if (b.move) afford = true;
            if (b.item != null) afford = Count(b.item.Value) > 0;
            if (b.kind == NodeKind.Plot && !b.hammer) afford = Count(Item.Hoe) > 0;
            GUI.color = afford ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            Icon(new Rect(r.x + 4, r.y + 4, cell - 8, cell - 8), b.move ? (carrying != null && carrying.CurrentSprite != null ? carrying.CurrentSprite : PixelArt.Icon(Item.FurnChair)) : b.hammer ? PixelArt.Icon(inHouse ? Item.FurnChair : Item.Hammer) : BuildSprite(b.kind));
            if (b.item != null) ShadowLabel(new Rect(r.x, r.yMax - 16, r.width - 3, 16), Count(b.item.Value).ToString(), smallRight);
            GUI.color = Color.white;
            if (CrispGUI.Button(r, GUIContent.none, GUIStyle.none)) SelectBuild(i);
        }
        var sel = CurrentBuild;
        var cost = new System.Text.StringBuilder();
        foreach (var c in sel.cost)
        {
            if (cost.Length > 0) cost.Append(", ");
            cost.Append(Count(c.item) >= c.n ? "<color=#b8f0a0>" : "<color=#ff8a8a>").Append($"{c.n} {Name(c.item)} ({Count(c.item)})</color>");
        }
        if (sel.item != null) cost.Append($"<color=#cccccc>มีในกระเป๋า {Count(sel.item.Value)} ชิ้น</color>");
        else if (sel.move) cost.Append(carrying != null ? $"<color=#ffd84d>กำลังยก{MoveName(carrying.kind)}</color>" : "<color=#cccccc>ฟรี ย้ายกี่ครั้งก็ได้</color>");
        else if (sel.cost.Length == 0) cost.Append(sel.hammer ? (inHouse ? "<color=#cccccc>เก็บได้คืนครบ</color>" : "<color=#cccccc>ใช้ค้อน</color>") : "<color=#cccccc>ใช้จอบ ไม่เสียวัตถุดิบ</color>");
        ShadowLabel(new Rect(x0, y0 + cell + 4, w, 18), $"<b>{sel.name}</b>   {cost}", small);
        string sub = sel.move ? (buildOk ? $"<color=#cccccc>{buildWhy}</color>" : $"<color=#ffcc66>{buildWhy}</color>")
                   : buildOk ? $"<color=#cccccc>{sel.desc}  ·  แตะ/คลิกที่พื้นเพื่อวาง</color>" : $"<color=#ffcc66>{buildWhy}</color>";
        ShadowLabel(new Rect(x0, y0 + cell + 22, w, 18), sub, small);
    }
}
