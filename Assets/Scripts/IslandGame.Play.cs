using UnityEngine;
using UnityEngine.InputSystem;

/// Main loop: survival stats, gathering, crafting, combat, XP & skills, sleeping and sailing.
public partial class IslandGame
{
    Vector2? pond;         // pond tile the player can drink from right now
    Vector2? seaSpot;      // sea tile in reach (fishing)
    Enemy enemyTarget;     // enemy in attack range
    float lastInteract, enemyTimer, sleepT, sailT, hurtT, savedAt = -10f;
    bool sleepApplied, sailApplied;
    int nightWarnedDay;

    /// True while sleeping or sailing: player input is ignored.
    public bool Busy => sleepT > 0f || sailT > 0f || homeT > 0f || dialog != null;

    void Update()
    {
        float dt = Time.deltaTime;
        var kb = Keyboard.current;
        UpdateCamera();
        UpdateWater(dt);
        UpdateDayNight(dt);
        Fx.Tick(dt);
        hurtT -= dt;
        UpdateWarnings(dt);
        UpdateTouch();
        UpdateLantern();
        TickPower(dt);
        TickHourglass(dt);
        if (!title && !dead) { UpdateTurtle(dt); UpdateSky(dt); UpdateBubble(dt); UpdateIce(dt); UpdateWeather(dt); UpdateSunken(dt); UpdatePirate(dt); UpdateCrystal(dt); UpdateStorm(dt); }

        if (title) { UpdateTitle(); return; }
        NetPoll();
        TickVisit(dt); // online: keep the server's copy of your private island fresh (IslandGame.Visit.cs)
        DevKeys(kb);
        DevHomeKeys(kb); // ?dev=1: F6 skip a minute, F7 coins and materials (IslandGame.Home.cs)

        if (dead)
        {
            if (kb != null && kb.rKey.wasPressedThisFrame) StartGame(null);
            return;
        }
        if (sailT > 0f) { UpdateSail(dt); return; }
        if (homeT > 0f) { UpdateHomeWarp(dt); return; } // going to / from the private island (IslandGame.Home.cs)
        if (sleepT > 0f) { NetTick(dt); UpdateSleep(dt); return; }
        var inputKb = chatOpen ? null : kb; // typing a chat message: keys aren't game controls

        Now += dt;

        if (voyage != null)
        {
            if (kb != null && kb.f9Key.wasPressedThisFrame)
            {
                var data = SaveSystem.Read();
                if (data != null) { EndVoyage(); StartGame(data); }
                return;
            }
            if (TickSurvival(dt)) return;
            UpdateVoyage(dt, kb);
            return;
        }

        NetTick(dt);

        if (kb != null)
        {
            if (kb.f5Key.wasPressedThisFrame) { SaveGame(); Hint("บันทึกเกมแล้ว", 1.5f); }
            if (kb.f9Key.wasPressedThisFrame)
            {
                var data = SaveSystem.Read();
                if (data != null) StartGame(data); else Hint("ไม่พบไฟล์เซฟ", 2f);
                return;
            }
            if (kb.f8Key.wasPressedThisFrame) { SaveSystem.Delete(); StartGame(null); return; }
        }

        autosaveTimer += dt;
        if (autosaveTimer >= AutosaveInterval) SaveGame();
        respawnTimer += dt;
        if (respawnTimer >= RespawnInterval) { respawnTimer = 0f; if (IsHost) RespawnMaterials(); } // online the host decides

        if (IsNight && nightWarnedDay != Day && !atHome)
        {
            nightWarnedDay = Day;
            Hint("ตกกลางคืนแล้ว... สัตว์ร้ายเริ่มดุขึ้น อยู่ใกล้กองไฟไว้!", 6f);
        }

        if (TickSurvival(dt)) return;
        if (dialog != null) { HandleDialog(inputKb); return; } // talking pauses the action (just for you)

        if (!atHome) UpdateEnemies(dt); // the private island is safe
        else enemies.RemoveAll(e => e == null);
        if (buildOpen) // placing / taking down things on the tile grid (IslandGame.Build.cs)
        {
            target = null; enemyTarget = null; villagerTarget = null; pond = null; seaSpot = null;
            HandleChatKeys(kb);
            HandleBuildInput(inputKb);
            return;
        }
        FindTarget();
        TickFishing(dt); // the fishing line (IslandGame.Farm.cs)
        TickPets(dt);    // the pet walking with you finds things (IslandGame.Pet.cs)
        HandleChatKeys(kb);
        HandleInput(inputKb);
    }

    /// Hunger / thirst / health over time. Returns true if the player died.
    bool TickSurvival(float dt)
    {
        float drain = (1f - 0.12f * SkillLv(Skill.IronStomach)) * (atHome ? 0.5f : 1f); // Iron Stomach slows the drain; home is restful
        float sun = hat == Item.StrawHat ? 0.85f : 1f;          // a straw hat keeps the sun off
        hunger = Mathf.Max(0f, hunger - dt * drain * 100f / StarveSeconds);
        thirst = Mathf.Max(0f, thirst - dt * drain * sun * 100f / ThirstSeconds);
        if (hunger <= 0f) health -= dt * 2f;
        if (thirst <= 0f) health -= dt * 2.5f;
        if (poisonT > 0f) { poisonT -= dt; health -= dt * 1.5f; } // spider bites and spore clouds
        if (hunger > 70f && thirst > 50f) health = Mathf.Min(MaxHealth, health + dt * 0.4f);
        return CheckDeath();
    }

    /// Keys that work both on land and at sea.
    void HandleCommonKeys(Keyboard kb)
    {
        if (kb.tabKey.wasPressedThisFrame || kb.cKey.wasPressedThisFrame) { craftOpen = !craftOpen; benchOpen = false; skillsOpen = false; bagOpen = false; }
        if (kb.kKey.wasPressedThisFrame) { skillsOpen = !skillsOpen; craftOpen = false; bagOpen = false; }
        if (kb.escapeKey.wasPressedThisFrame) { craftOpen = false; skillsOpen = false; bagOpen = false; }
        HandleHotbarKeys(kb); // B bag, 1-8 shortcuts
        if (kb.fKey.wasPressedThisFrame) EatBest();
        if (kb.qKey.wasPressedThisFrame) DrinkFlask();
        if (kb.hKey.wasPressedThisFrame) ToggleHome();  // private island <-> adventure (IslandGame.Home.cs)
        if (kb.gKey.wasPressedThisFrame) ToggleBuild(); // build / take down (IslandGame.Build.cs)
        if (kb.pKey.wasPressedThisFrame) PetKey();      // catch a weakened creature / your pets (IslandGame.Pet.cs)
        if (kb.vKey.wasPressedThisFrame) AskHomes();    // visit a friend's private island (IslandGame.Visit.cs)
        if (kb.mKey.wasPressedThisFrame) { Sfx.ToggleMute(); Hint(Sfx.Muted ? "ปิดเสียง (M)" : "เปิดเสียง (M)", 1.5f); }
        if (craftOpen)
            for (int i = 0; i < CraftList.Length && i < 10; i++)
                if (kb[i < 9 ? (Key)((int)Key.Digit1 + i) : Key.Digit0].wasPressedThisFrame) Craft(CraftList[i]);
        if (skillsOpen)
            for (int i = 0; i < skills.Length && i < 9; i++)
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) UpgradeSkill((Skill)i);
    }

    /// Kill reward shared by land and sea creatures.
    void Reward(Vector3 at, Item? drop, int count, int xpGain)
    {
        if (drop != null && count > 0)
        {
            inv[drop.Value] = Count(drop.Value) + count;
            Popup($"+{count} {Name(drop.Value)}", at + Vector3.up * 0.5f, new Color(1f, 0.95f, 0.6f));
        }
        AddXp(xpGain, at);
    }

    bool CheckDeath()
    {
        if (health > 0f) return false;
        if (atHome) // nobody dies at home: you faint and wake up hungry
        {
            health = 10f;
            Hint("คุณเป็นลมเพราะหิว! รีบกินอะไรสักหน่อย (บนเกาะส่วนตัวไม่มีวันตาย)", 4f);
            return false;
        }
        health = 0f; dead = true; craftOpen = false; skillsOpen = false;
        if (cursor != null) cursor.enabled = false;
        if (Shared) { AddChat("", "คุณตายแล้ว"); LeaveRoom(); } // someone else looks after the creatures now
        Sfx.Play("hurt", 0.8f);
        KeepHomeOnDeath();   // ...but your private island waits for the next run (IslandGame.Home.cs)
        SaveSystem.Delete(); // death is permanent: next start is a new run
        return true;
    }

    void HandleInput(Keyboard kb)
    {
        if (chatOpen) return;
        if (kb != null) HandleCommonKeys(kb);
        if (kb != null && kb.rKey.wasPressedThisFrame) UsePower();
        if (kb != null && kb.tKey.wasPressedThisFrame) UsePowerAttack();

        bool pressed = touchActionPressed || (kb != null && (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame));
        bool held = touchActionHeld || (kb != null && (kb.eKey.isPressed || kb.spaceKey.isPressed));
        float since = Time.time - lastInteract;
        if (fishState != 0) // a line is out: any press reels it in (IslandGame.Farm.cs)
        {
            if (pressed && since > 0.15f) { lastInteract = Time.time; ReelIn(); }
            return;
        }
        bool Trigger(float repeatDelay)
        {
            if ((pressed && since > 0.15f) || (held && since > repeatDelay)) { lastInteract = Time.time; return true; }
            return false;
        }

        // attacks: one per AttackInterval whether you tap or hold (max 3 a second)
        if (enemyTarget != null) { if ((pressed || held) && since >= AttackInterval) { lastInteract = Time.time; player.PlayAction(PixelArt.AttackAnim, 16f); Attack(enemyTarget); } }
        else if (villagerTarget != null) { if (pressed && since > 0.15f) OpenDialog(villagerTarget); }
        else if (target != null)
        {
            bool once = target.kind is NodeKind.Campfire or NodeKind.Shelter or NodeKind.Raft or NodeKind.Plot or NodeKind.Chicken or NodeKind.Cow or NodeKind.Cottage or NodeKind.Signpost
                or NodeKind.Bed or NodeKind.Workbench or NodeKind.HouseDoor;
            if (once ? (pressed && since > 0.15f) : Trigger(0.3f))
            {
                lastInteract = Time.time;
                if (!once) player.PlayAction(PixelArt.AttackAnim, 16f); // chop / mine / pick: same swing
                Interact(target);
            }
        }
        else if (pond != null) { if (Trigger(0.5f)) Drink(); }
        else if (seaSpot != null)
        {
            if (HasRod) { if (pressed && since > 0.15f) { lastInteract = Time.time; CastLine(seaSpot.Value); } }
            else if (HasSpear) { if (Trigger(0.8f)) FishAt(GridToWorld(seaSpot.Value)); }
            else if (pressed) Hint("น้ำทะเลเค็มเกินไป ดื่มไม่ได้ ทำหอกเพื่อแทงปลาตรงนี้", 3f);
        }
    }

    // ------------------------------------------------------------------ targeting
    void FindTarget()
    {
        target = null; enemyTarget = null; villagerTarget = null; pond = null; seaSpot = null;
        Vector2 pg = player.grid;

        float bestEnemy = InteractRange;
        foreach (var e in enemies)
        {
            if (e == null || e.Disguised) continue;
            float d = (e.grid - pg).magnitude;
            if (d < bestEnemy) { bestEnemy = d; enemyTarget = e; }
        }

        if (enemyTarget == null)
        {
            float bestTalk = InteractRange;
            foreach (var v in villagers)
            {
                if (v == null) continue;
                float d = (v.grid - pg).magnitude;
                if (d < bestTalk) { bestTalk = d; villagerTarget = v; }
            }
        }

        if (enemyTarget == null && villagerTarget == null)
        {
            float best = float.MaxValue;
            foreach (var n in nodes)
            {
                if (!n.CanTarget) continue;
                Vector2 d = n.grid - pg;
                float dist = d.magnitude;
                if (dist > InteractRange + (n.Blocking ? Mathf.Max(0f, n.BlockRadius - 0.25f) : 0f)) continue; // big things: reach their edge
                float score = dist - (dist > 0.01f ? Vector2.Dot(d / dist, player.facing) * 0.35f : 0f); // prefer what we face
                if (score < best) { best = score; target = n; }
            }

            // Nearest pond tile within reach; whichever of pond / object is closer wins.
            float pondDist = 1.1f;
            var t = Tile(pg);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!IsFresh(t.x + dx, t.y + dy)) continue;
                    var tile = new Vector2(t.x + dx, t.y + dy);
                    float d = (tile - pg).magnitude;
                    if (d < pondDist) { pondDist = d; pond = tile; }
                }
            if (target != null && pond != null)
            {
                if ((target.grid - pg).magnitude <= pondDist) pond = null; else target = null;
            }
            if (target == null && pond == null && biome != Biome.Sky && !inHouse) seaSpot = NearestSea(pg, 1.2f); // no fishing in the clouds
        }

        Vector3? at = null;
        if (enemyTarget != null) at = GridToWorld(enemyTarget.grid) + Vector3.up * HeightAt(enemyTarget.grid);
        else if (villagerTarget != null) at = GridToWorld(villagerTarget.grid) + Vector3.up * HeightAt(villagerTarget.grid);
        else if (target != null) at = GridToWorld(target.grid) + Vector3.up * HeightAt(target.grid);
        else if (pond != null) at = GridToWorld(pond.Value) + Vector3.down * 0.125f;
        else if (seaSpot != null && (HasSpear || HasRod)) at = GridToWorld(seaSpot.Value) + Vector3.down * 0.125f;
        cursor.enabled = at != null;
        if (at != null)
        {
            cursor.transform.position = at.Value;
            cursor.color = enemyTarget != null ? new Color(1f, 0.4f, 0.4f, 0.9f) : new Color(1f, 1f, 1f, 0.55f + 0.45f * Mathf.Sin(Time.time * 6f));
        }
    }

    // ------------------------------------------------------------------ gathering
    bool HasSpear => Count(Item.Spear) > 0 || Count(Item.ObsidianSpear) > 0 || Count(Item.Trident) > 0;
    bool HasAxe => Count(Item.StoneAxe) > 0 || Count(Item.ObsidianAxe) > 0;
    int ChopsNeeded => Mathf.Max(1, 4 - (Count(Item.ObsidianAxe) > 0 ? 2 : 0) - SkillLv(Skill.Lumberjack));
    int WeaponDamage => (weapon != null && Count(weapon.Value) > 0 ? 0 : 1) + GearDamage + SkillLv(Skill.Strength); // bare hands hit for 1

    /// Forager skill: chance of one extra item.
    int Bonus() => rnd.NextDouble() < 0.3 * SkillLv(Skill.Forager) ? 1 : 0;

    void Interact(WorldNode n)
    {
        if (visiting && !VisitAllowed(n.kind)) { Hint($"นี่เป็นของ{visitName} ดูได้อย่างเดียวนะ", 2f); return; } // a friend's island (IslandGame.Visit.cs)
        Vector3 at = n.transform.position;
        Color leaf = new Color(0.4f, 0.75f, 0.3f), wood = new Color(0.6f, 0.4f, 0.2f);
        bool knife = Count(Item.StoneKnife) > 0;
        switch (n.kind)
        {
            case NodeKind.Grass:
                Give(Item.Fiber, rnd.Next(1, 3) + (knife ? 1 : 0) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 0.1f, leaf, 6, 0.8f, 1.3f);
                DepleteShared(n, 40f);
                break;
            case NodeKind.Stick:
                Give(Item.Stick, 1 + Bonus(), n);
                Fx.Burst(at, wood, 4, 0.6f, 1f);
                RemoveShared(n); // new ones appear elsewhere
                break;
            case NodeKind.Rock:
                Give(Item.Stone, 1 + Bonus(), n);
                Fx.Burst(at, new Color(0.6f, 0.62f, 0.65f), 4, 0.6f, 1f);
                RemoveShared(n);
                break;
            case NodeKind.Obsidian:
                Give(Item.Obsidian, 1 + Bonus(), n);
                Fx.Burst(at, new Color(0.35f, 0.3f, 0.5f), 5, 0.6f, 1f);
                RemoveShared(n);
                break;
            case NodeKind.Bamboo:
                Give(Item.Bamboo, rnd.Next(1, 3) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 0.5f, leaf, 6, 0.8f, 1f, 0.8f, 3f);
                DepleteShared(n, 90f);
                break;
            case NodeKind.Drop: PickUpDrop(n); break;
            case NodeKind.Chest: if (n.available) OpenChest(n); break;
            case NodeKind.Shrine: PrayAtShrine(n); break;
            case NodeKind.DigSpot: if (n.available) DigTreasure(n); break;
            case NodeKind.Mirror: TurnMirror(n); break;
            case NodeKind.LightningRod: RepairRod(n); break;
            case NodeKind.GiantGrass: CutGiantGrass(n); break;
            case NodeKind.Beehive: TakeHoney(n); break;
            case NodeKind.GiantFlower:
                Give(Item.Nectar, rnd.Next(1, 3) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 1.2f, new Color(1f, 0.6f, 0.8f), 8, 0.8f, 1f, 1f, 2f);
                DepleteShared(n, 120f);
                break;
            case NodeKind.CopperOre:
                Give(Item.Copper, rnd.Next(1, 3) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 0.3f, new Color(0.9f, 0.55f, 0.3f), 8, 0.7f, 1.2f);
                Sfx.Play("chop", 0.5f);
                DepleteShared(n, 120f);
                break;
            case NodeKind.CrystalVault: OpenVault(n); break;
            case NodeKind.CrystalCluster:
                Give(Item.CrystalShard, rnd.Next(1, 3) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 0.4f, new Color(0.85f, 0.9f, 1f), 10, 0.8f, 1.4f);
                Sfx.Play("chop", 0.5f);
                DepleteShared(n, 110f);
                break;
            case NodeKind.Relic: if (n.available) ReadRelic(n); break;
            case NodeKind.GiantClam:
                air = 100f;
                Popup("เติมอากาศเต็ม", at + Vector3.up * 0.6f, new Color(0.7f, 0.95f, 1f));
                Sfx.Play("drink", 0.4f);
                break;
            case NodeKind.LostBundle: PickLostBundle(n); break;
            case NodeKind.Kelp: // tall kelp: fibre (and sometimes a fish hiding in it)
                Give(Item.Fiber, rnd.Next(2, 4) + Bonus(), n);
                if (rnd.NextDouble() < 0.25) Give(Item.RawFish, 1, n);
                Fx.Burst(at + Vector3.up * 0.8f, new Color(0.3f, 0.7f, 0.5f), 6, 0.8f, 0.8f);
                DepleteShared(n, 70f);
                break;
            case NodeKind.CoralNode:
                Give(Item.Coral, rnd.Next(1, 3) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 0.3f, new Color(1f, 0.5f, 0.55f), 6, 0.6f, 1f);
                DepleteShared(n, 90f);
                break;
            case NodeKind.Oyster:
                if (rnd.NextDouble() < 0.6) Give(Item.Pearl, 1, n); else Popup("หอยว่างเปล่า", at + Vector3.up * 0.4f, Color.white);
                Fx.Burst(at + Vector3.up * 0.2f, new Color(0.95f, 0.95f, 1f), 6, 0.5f, 1f);
                DepleteShared(n, 120f);
                break;
            case NodeKind.IceCrystal:
                Give(Item.IceShard, rnd.Next(1, 3) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 0.3f, new Color(0.8f, 0.95f, 1f), 8, 0.6f, 1.2f);
                DepleteShared(n, 100f);
                break;
            case NodeKind.HotSpring:
                warmth = 100f;
                health = Mathf.Min(MaxHealth, health + 5f);
                Popup("อุ่นสบาย~", at + Vector3.up * 0.6f, new Color(1f, 0.75f, 0.5f));
                Sfx.Play("drink", 0.4f);
                break;
            case NodeKind.SandCrystal:
                Give(Item.TimeSand, rnd.Next(1, 3) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 0.3f, new Color(1f, 0.85f, 0.4f), 8, 0.6f, 1.2f);
                DepleteShared(n, 100f);
                break;
            case NodeKind.AirVent:
                air = 100f;
                Popup("เติมอากาศเต็ม", at + Vector3.up * 0.6f, new Color(0.7f, 0.95f, 1f));
                Sfx.Play("drink", 0.4f);
                break;
            case NodeKind.Well:
            case NodeKind.Fountain: DrinkFromWell(n); break;
            case NodeKind.Farm:
                Give(rnd.NextDouble() < 0.5 ? Item.Fruit : Item.Berry, rnd.Next(2, 4) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 0.3f, leaf, 6, 0.8f, 1f);
                DepleteShared(n, 120f);
                break;
            case NodeKind.CloudPlant:
                Give(Item.CloudCotton, rnd.Next(1, 3) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 0.4f, Color.white, 8, 1f, 1f, 1f, 1f);
                DepleteShared(n, 90f);
                break;
            case NodeKind.Shell:
                Give(Item.ShellShard, rnd.Next(1, 3) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 0.1f, new Color(1f, 0.9f, 0.85f), 5, 0.6f, 1f);
                DepleteShared(n, 80f);
                break;
            case NodeKind.GlowTree:
                PickGlowingFruit(n);
                break;
            case NodeKind.GlowShroom:
                Give(Item.GlowSpore, rnd.Next(1, 3) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 0.3f, new Color(0.5f, 1f, 0.9f), 8, 1f, 1.2f, 0.8f, 2f);
                DepleteShared(n, 100f);
                break;
            case NodeKind.Bush:
                Give(Item.Berry, rnd.Next(2, 4) + (knife ? 1 : 0) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 0.3f, leaf, 6, 1f, 1.2f, 0.7f, 3f);
                Fx.Burst(at + Vector3.up * 0.3f, new Color(0.75f, 0.15f, 0.25f), 3);
                n.Shake();
                DepleteShared(n, 45f);
                break;
            case NodeKind.FruitTree:
                Give(Item.Fruit, rnd.Next(1, 3) + Bonus(), n);
                Fx.Burst(at + Vector3.up * 1f, leaf, 8, 1.2f, 0.8f, 1f, 2f);
                n.Shake();
                DepleteShared(n, 60f);
                break;
            case NodeKind.Tree:
                n.Shake();
                Sfx.Play("chop", HasAxe ? 0.7f : 0.35f);
                if (biome != Biome.Volcanic) Fx.Burst(at + Vector3.up * 1f, leaf, 5, 1.2f, 0.8f, 1f, 2f);
                if (!HasAxe)
                {
                    if (rnd.NextDouble() < 0.35) Give(Item.Stick, 1, n);
                    else Hint("ทำขวานเพื่อตัดต้นไม้เอาไม้", 3f);
                }
                else
                {
                    Fx.Burst(at + Vector3.up * 0.3f, wood, 6, 1.2f, 1.4f);
                    if (++n.hits < ChopsNeeded) SendNode(n, "hits"); // friends can chop the same tree
                    if (n.hits >= ChopsNeeded)
                    {
                        Give(Item.Wood, 3 + Bonus(), n);
                        if (rnd.NextDouble() < 0.5) Give(Item.Stick, 1, n);
                        if (biome != Biome.Volcanic) Fx.Burst(at + Vector3.up * 1f, leaf, 14, 1.6f, 1f, 1.2f, 2f);
                        StumpShared(n, 180f);
                    }
                    else Popup("ฉับ!", at + Vector3.up * 1.1f, Color.white);
                }
                break;
            case NodeKind.Campfire: craftOpen = true; skillsOpen = false; break;
            case NodeKind.Shelter: TrySleep(); break;
            case NodeKind.Cottage: CottageDialog(n); break; // go in, sleep, upgrade (IslandGame.House.cs)
            case NodeKind.Bed: TrySleep(); break;
            case NodeKind.Workbench: OpenBench(); break;
            case NodeKind.HouseDoor: BeginWarp(Warp.ExitHouse); break;
            case NodeKind.Plot: UsePlot(n); break;          // dig / plant / water / harvest (IslandGame.Farm.cs)
            case NodeKind.Chicken:
            case NodeKind.Cow: UseAnimal(n); break;
            case NodeKind.Signpost: ReadSign(n); break;
            case NodeKind.Raft: TrySail(); break;
        }
    }

    void Give(Item it, int amount, WorldNode from)
    {
        inv[it] = Count(it) + amount;
        AutoHotbar(it);
        Vector3 pos = from != null ? from.transform.position + Vector3.up * from.PromptHeight * 0.8f
                                   : PlayerPos + Vector3.up * 0.9f;
        Popup($"+{amount} {Name(it)}", pos, new Color(1f, 0.95f, 0.6f));
        Sfx.Play("pickup", 0.35f);
    }

    void Drink()
    {
        bool refill = Count(Item.WaterFlask) > 0 && flaskWater < FlaskSize;
        if (thirst >= 99f && !refill) { Hint("ยังไม่หิวน้ำ", 1.5f); return; }
        Vector3 at = GridToWorld(pond.Value);
        if (thirst < 99f)
        {
            thirst = Mathf.Min(100f, thirst + 20f);
            Popup("+20 น้ำ", at + Vector3.up * 0.4f, new Color(0.55f, 0.85f, 1f));
        }
        if (refill)
        {
            flaskWater = FlaskSize;
            Popup("เติมกระบอกน้ำแล้ว", at + Vector3.up * 0.65f, new Color(0.55f, 0.85f, 1f));
        }
        Fx.Burst(at, new Color(0.7f, 0.9f, 1f), 6, 0.6f, 1.2f);
        Sfx.Play("drink", 0.6f);
    }

    void DrinkFlask()
    {
        if (Count(Item.WaterFlask) == 0) return;
        if (flaskWater <= 0) { Hint("กระบอกน้ำหมดแล้ว ไปเติมที่บ่อน้ำ", 2f); return; }
        if (thirst >= 99f) { Hint("ยังไม่หิวน้ำ", 1.5f); return; }
        flaskWater--;
        thirst = Mathf.Min(100f, thirst + 25f);
        Popup("+25 น้ำ", PlayerPos + Vector3.up * 0.9f, new Color(0.55f, 0.85f, 1f));
        Sfx.Play("drink", 0.6f);
    }

    void FishAt(Vector3 at)
    {
        Fx.Burst(at, new Color(0.75f, 0.9f, 1f), 8, 0.8f, 1.5f);
        Sfx.Play("splash", 0.6f);
        if (rnd.NextDouble() < 0.35 + 0.15 * SkillLv(Skill.Fisher))
        {
            int fish = Count(Item.DivingBell) > 0 ? 2 : 1; // the diving bell spots the shoals
            inv[Item.RawFish] = Count(Item.RawFish) + fish;
            Popup($"+{fish} ปลาดิบ", at + Vector3.up * 0.5f, new Color(1f, 0.95f, 0.6f));
            Sfx.Play("pickup", 0.4f);
        }
        else Popup("พลาด!", at + Vector3.up * 0.5f, new Color(0.8f, 0.9f, 1f));
    }

    // ------------------------------------------------------------------ combat & XP
    void Attack(Enemy e)
    {
        int dmg = WeaponDamage;
        Sfx.Play("hit", 0.6f);
        Vector3 at = e.transform.position;
        if (e.puppet)
        {
            // Online, not the host: show the hit, let the host's game decide (it replies "kill").
            e.Hit(dmg, player.grid);
            Send(new NetMsg { t = "hit", to = hostId, id = e.netId, dmg = dmg });
            Popup($"-{dmg}", at + Vector3.up * 0.5f, Color.white);
            return;
        }
        if (e.Hit(dmg, player.grid))
        {
            enemies.Remove(e);
            if (Shared) Send(new NetMsg { t = "kill", id = e.netId, by = myId, kind = (int)e.kind, x = at.x, y = at.y });
            KillExtras(e); // stolen goods back, alpha loot
            GrantKill(e.kind, e.xp, at);
        }
        else Popup($"-{dmg}", at + Vector3.up * 0.5f, Color.white);
    }

    /// Loot and XP for finishing off a creature.
    void GrantKill(EnemyKind kind, int xpGain, Vector3 at)
    {
        var (drop, n) = kind switch
        {
            EnemyKind.Boar => (Item.RawMeat, 2),
            EnemyKind.Snake => (Item.RawMeat, 1),
            EnemyKind.Mimic => (Item.GlowSpore, 2),
            EnemyKind.SporeSpider => (Item.GlowSpore, 1),
            EnemyKind.ShroomKing => (Item.GlowSpore, 6),
            EnemyKind.Barnacle => (Item.ShellShard, 1),
            EnemyKind.HermitCrab => (Item.ShellShard, 3),
            EnemyKind.ChimeBird => (Item.Feather, 2),
            EnemyKind.StormCloud => (Item.CloudCotton, 2),
            EnemyKind.LanternFish => (Item.RawFish, 2),
            EnemyKind.StoneSquid => (Item.Pearl, 1),
            EnemyKind.WindupDoll => (Item.Gear, 2),
            EnemyKind.TimeBeetle => (Item.Gear, 1),
            EnemyKind.Snowman => (Item.IceShard, 2),
            EnemyKind.Snowling => (Item.IceShard, 1),
            EnemyKind.FrostBat => (Item.Fur, 1),
            EnemyKind.Eel => (Item.RawFish, 2),
            EnemyKind.DeepCrab => (Item.CrabMeat, 2),
            EnemyKind.Kraken => (Item.Pearl, 6),
            EnemyKind.SkeletonPirate => (Item.GoldCoin, 2),
            EnemyKind.PirateMonkey => (Item.Fruit, 2),
            EnemyKind.Captain => (Item.GoldCoin, 30),
            EnemyKind.CrystalGolem => (Item.CrystalShard, 3),
            EnemyKind.MirrorWisp => (Item.CrystalShard, 1),
            EnemyKind.ThunderToad => (Item.Copper, 1),
            EnemyKind.StormHawk => (Item.Feather, 2),
            EnemyKind.Thunderbird => (Item.Feather, 6),
            EnemyKind.WorkerAnt => (Item.Chitin, 1),
            EnemyKind.SoldierAnt => (Item.Chitin, 2),
            EnemyKind.GiantBee => (Item.Honey, 1),
            EnemyKind.AntQueen => (Item.Chitin, 10),
            _ => (Item.CrabMeat, 1),
        };
        QuestKill(kind);
        BossDown(kind);
        if (kind == EnemyKind.Snowman) SplitSnowman(WorldDeltaToGrid(at)); // bursts into snowlings
        if (kind == EnemyKind.ShroomKing) Hint("ราชาเห็ดล้มแล้ว! ทางน้ำโล่งแล้ว กลับไปบอกช่างต่อเรือ", 6f);
        inv[drop] = Count(drop) + n;
        Popup($"+{n} {Name(drop)}", at + Vector3.up * 0.5f, new Color(1f, 0.95f, 0.6f));
        // hides for the weaver's clothes: boars always, snakes sometimes
        if (kind == EnemyKind.Boar || (kind == EnemyKind.Snake && rnd.NextDouble() < 0.5))
        {
            inv[Item.Hide] = Count(Item.Hide) + 1;
            Popup("+1 หนังสัตว์", at + Vector3.up * 0.25f, new Color(1f, 0.95f, 0.6f));
        }
        AddXp(xpGain, at);
    }

    void AddXp(int amount, Vector3 at)
    {
        xp += amount;
        Popup($"+{amount} XP", at + Vector3.up * 0.75f, new Color(0.75f, 0.6f, 1f));
        while (xp >= XpToNext(level))
        {
            xp -= XpToNext(level);
            level++;
            skillPoints++;
            health = Mathf.Min(MaxHealth, health + 25f);
            Popup($"เลเวล {level}!", PlayerPos + Vector3.up * 1.2f, new Color(1f, 0.85f, 0.3f));
            Fx.Burst(PlayerPos + Vector3.up * 0.4f, new Color(1f, 0.85f, 0.3f), 20, 1.4f, 2f, 1f, 3f);
            Sfx.Play("levelup", 0.7f);
            Hint($"เลเวล {level}! มีแต้มสกิล {skillPoints} แต้ม กด K เพื่ออัปสกิล", 5f);
        }
    }

    void UpgradeSkill(Skill s)
    {
        int i = (int)s;
        if (skillPoints <= 0) { Hint("ไม่มีแต้มสกิล ล้มสัตว์ร้ายเพื่อเลเวลอัป", 2f); return; }
        if (skills[i] >= SkillInfo[i].max) { Hint($"{SkillInfo[i].name} เต็มแล้ว", 2f); return; }
        skills[i]++;
        skillPoints--;
        if (s == Skill.Toughness) health += 15f;
        Sfx.Play("craft", 0.5f);
        Popup($"{SkillInfo[i].name} {skills[i]}", PlayerPos + Vector3.up * 1f, new Color(1f, 0.85f, 0.3f));
    }

    public void DamagePlayer(float amount, string why)
    {
        if (Busy || dead) return;
        if (PowerBlocks()) return; // bubble shield / mid-dash
        if (rnd.NextDouble() < 0.1 * SkillLv(Skill.Evasion))
        {
            Popup("หลบได้!", PlayerPos + Vector3.up * 0.9f, new Color(0.7f, 1f, 0.7f));
            return;
        }
        if (voyage != null) amount *= 1f - RaftArmor; // a sturdier raft keeps you out of the water
        amount *= 1f - GearArmor; // hide vest and other armour
        if (voyage != null && Count(Item.Balloon) > 0) amount *= 0.3f; // flying above the waves
        health -= amount;
        hurtT = 0.3f;
        if (player != null) player.Flash();
        if (voyage != null) voyage.hurtT = 0.2f;
        Sfx.Play("hurt", 0.7f);
        Popup($"-{amount:0} {why}", PlayerPos + Vector3.up * 0.9f, new Color(1f, 0.45f, 0.45f));
        CheckDeath();
    }

    // ------------------------------------------------------------------ respawning materials
    /// Every few seconds, top respawning materials back up at random spots away from the player.
    void RespawnMaterials()
    {
        int sticks = 0, rocks = 0, obsidian = 0;
        foreach (var n in nodes)
        {
            if (n.kind == NodeKind.Stick) sticks++;
            else if (n.kind == NodeKind.Rock) rocks++;
            else if (n.kind == NodeKind.Obsidian) obsidian++;
        }
        if (sticks < stickCap) TrySpawnMaterial(NodeKind.Stick);
        if (rocks < rockCap) TrySpawnMaterial(NodeKind.Rock);
        if (obsidian < obsidianCap) TrySpawnMaterial(NodeKind.Obsidian);
    }

    void TrySpawnMaterial(NodeKind kind)
    {
        var trees = kind == NodeKind.Stick ? nodes.FindAll(n => n.kind is NodeKind.Tree or NodeKind.FruitTree) : null;
        for (int attempt = 0; attempt < 25; attempt++)
        {
            Vector2 g;
            if (trees != null && trees.Count > 0 && rnd.NextDouble() < 0.6)
            {
                // sticks mostly fall under trees
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = 0.7f + (float)rnd.NextDouble() * 0.4f;
                g = trees[rnd.Next(trees.Count)].grid + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            else g = new Vector2(rnd.Next(Size), rnd.Next(Size)) + new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 0.4f;

            if (!IsLand(g) || (g - player.grid).magnitude < 4f || !FreeSpot(g, 0.2f)) continue;
            SendSpawn(Spawn(kind, g, false)); // online: tell everyone where it appeared
            return;
        }
    }

    // ------------------------------------------------------------------ enemies
    int EnemyCap(EnemyKind k) => k switch
    {
        EnemyKind.Crab => IsNight ? Mathf.Min(6, 2 + tier + Day / 3) : 0,
        EnemyKind.Snake => (IsNight ? 4 : 2) + tier,
        EnemyKind.Boar => (IsNight ? 3 : 2) + tier,
        EnemyKind.Mimic => 3 + tier,
        EnemyKind.SporeSpider => (IsNight ? 4 : 2) + tier,
        EnemyKind.Barnacle => 4 + tier,
        EnemyKind.HermitCrab => (IsNight ? 3 : 2) + tier,
        EnemyKind.StormCloud => (IsNight ? 3 : 2) + tier,
        EnemyKind.ChimeBird => 3 + tier,
        EnemyKind.LanternFish => (IsNight ? 4 : 2) + tier,
        EnemyKind.StoneSquid => 3 + tier,
        EnemyKind.WindupDoll => (IsNight ? 5 : 3) + tier,
        EnemyKind.TimeBeetle => 3 + tier,
        EnemyKind.Snowman => (IsNight ? 4 : 2) + tier,
        EnemyKind.FrostBat => (IsNight ? 4 : 1) + tier,
        EnemyKind.Eel => 3 + tier,
        EnemyKind.DeepCrab => (IsNight ? 3 : 2) + tier,
        EnemyKind.SkeletonPirate => (IsNight ? 5 : 3) + tier,
        EnemyKind.PirateMonkey => 2 + tier,
        EnemyKind.CrystalGolem => (IsNight ? 3 : 2) + tier,
        EnemyKind.MirrorWisp => (IsNight ? 4 : 2) + tier,
        EnemyKind.ThunderToad => 3 + tier,
        EnemyKind.StormHawk => (IsNight ? 3 : 2) + tier,
        EnemyKind.WorkerAnt => 5 + tier,
        EnemyKind.SoldierAnt => (IsNight ? 3 : 1) + tier,
        _ => 0,
    };

    EnemyKind[] BiomeEnemies() => (biome, tier > 0) switch
    {
        (Biome.Jungle, false) => new[] { EnemyKind.Crab, EnemyKind.Snake },
        (Biome.Jungle, true) => new[] { EnemyKind.Crab, EnemyKind.Snake, EnemyKind.Boar },
        (Biome.Volcanic, _) => new[] { EnemyKind.Crab, EnemyKind.Boar },
        (Biome.Mushroom, _) => new[] { EnemyKind.Crab, EnemyKind.Mimic, EnemyKind.SporeSpider },
        (Biome.Turtle, _) => new[] { EnemyKind.Crab, EnemyKind.Barnacle, EnemyKind.HermitCrab },
        (Biome.Sky, _) => new[] { EnemyKind.StormCloud, EnemyKind.ChimeBird },
        (Biome.Bubble, _) => new[] { EnemyKind.LanternFish, EnemyKind.StoneSquid },
        (Biome.Hourglass, _) => new[] { EnemyKind.WindupDoll, EnemyKind.TimeBeetle },
        (Biome.Ice, _) => new[] { EnemyKind.Snowman, EnemyKind.FrostBat },
        (Biome.Sunken, _) => new[] { EnemyKind.Eel, EnemyKind.DeepCrab },
        (Biome.Pirate, _) => new[] { EnemyKind.Crab, EnemyKind.SkeletonPirate, EnemyKind.PirateMonkey },
        (Biome.Crystal, _) => new[] { EnemyKind.CrystalGolem, EnemyKind.MirrorWisp },
        (Biome.Storm, _) => new[] { EnemyKind.ThunderToad, EnemyKind.StormHawk },
        (Biome.Insect, _) => new[] { EnemyKind.WorkerAnt, EnemyKind.SoldierAnt },
        (_, true) => new[] { EnemyKind.Crab, EnemyKind.Snake },
        _ => new[] { EnemyKind.Crab },
    };

    void UpdateEnemies(float dt)
    {
        enemies.RemoveAll(e => e == null);
        if (!IsHost) return; // online, the island's host spawns creatures for everyone
        enemyTimer += dt;
        if (enemyTimer < 5f) return;
        enemyTimer = 0f;

        foreach (var kind in BiomeEnemies())
        {
            int count = 0;
            foreach (var e in enemies) if (e.kind == kind) count++;
            if (count < EnemyCap(kind) && TrySpawnEnemy(kind)) return; // at most one new enemy per tick
        }
    }

    bool TrySpawnEnemy(EnemyKind kind)
    {
        int wantTile = kind is EnemyKind.Crab or EnemyKind.Barnacle or EnemyKind.HermitCrab ? 1 : 2; // shore creatures on the beach, others inland
        for (int attempt = 0; attempt < 40; attempt++)
        {
            int x = rnd.Next(Size), y = rnd.Next(Size);
            if (tiles[x, y] != wantTile) continue;
            var g = new Vector2(x, y);
            float d = (g - player.grid).magnitude;
            if (d < 7f || d > 15f || DistanceToFire(g) < 4.5f || !CanWalk(g, 0.2f)) continue;
            var e = new GameObject(kind.ToString()).AddComponent<Enemy>();
            e.transform.SetParent(world);
            e.Init(kind, g, tier, ++enemyCounter);
            if (T.variant != Variant.None && rnd.NextDouble() < 0.25) e.SetVariant(T.variant); // this island's tougher breed
            enemies.Add(e);
            Fx.Burst(e.transform.position, kind == EnemyKind.Crab ? new Color(0.7f, 0.85f, 1f) : new Color(0.5f, 0.6f, 0.3f), 6);
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ crafting & food
    public bool CanCraft(Recipe r, out string why)
    {
        why = null;
        if (r.learn && r.output != null && !learned.Contains(r.output.Value)) { why = r.output == Item.Antidote || r.output == Item.MushroomLantern ? "ต้องเรียนจากหมอโมรา (เกาะเห็ด)"
                : r.output == Item.CanvasSail ? "ต้องเรียนจากยายชบา (เกาะเต่ายักษ์)"
                : r.output == Item.Balloon ? "ต้องเรียนจากปู่ลมพัด (เกาะลอยฟ้า)"
                : r.output == Item.DivingBell ? "ต้องเรียนจากแม่มุก (เกาะใต้ฟองอากาศ)"
                : r.output == Item.Sundial ? "ต้องเรียนจากลุงฟันเฟือง (เกาะนาฬิกาทราย)"
                : r.output == Item.SpikedBoots ? "ต้องเรียนจากพรานธารา (เกาะน้ำแข็ง)"
                : r.output == Item.Trident ? "ต้องเรียนจาก ดร.สมุทร (นครบาดาล)"
                : r.output == Item.Compass ? "ต้องเรียนจากลุงตาเดียว (อ่าวโจรสลัด)"
                : r.output == Item.CrystalArmor ? "ต้องเรียนจากช่างแก้วเพชรา (เกาะผลึก)"
                : r.output == Item.StormHood ? "ต้องเรียนจากนักประดิษฐ์อัสนี (เกาะพายุ)"
                : r.output == Item.ButterflyWings ? "ต้องเรียนจากป้าผีเสื้อ (เกาะแมลงยักษ์)" : "ต้องเรียนจากช่างทอของชนเผ่า"; return false; }
        if (r.output != null && (IsTool(r.output.Value) || IsClothing(r.output.Value)) && Count(r.output.Value) > 0) { why = "มีอยู่แล้ว"; return false; }
        if (voyage != null && (r.needsFire || r.places != null || r.raftUpgrade)) { why = "ทำกลางทะเลไม่ได้"; return false; }
        if (r.places == NodeKind.Raft && raftLevel > 0) { why = "มีแพอยู่แล้ว"; return false; }
        if (r.raftUpgrade)
        {
            RefreshRaftUpgrade(r);
            if (raftLevel == 0) { why = "ต้องสร้างแพก่อน"; return false; }
            if (raftLevel >= farthest) { why = $"เรือ Lv {raftLevel} พร้อมไปเกาะใหม่แล้ว"; return false; }
            var raft = RaftNode;
            if (raft == null || (raft.grid - player.grid).magnitude > 2.2f) { why = "ต้องยืนข้างแพ"; return false; }
        }
        if (r.needsFire && DistanceToFire(player.grid) > 2.2f) { why = "ต้องอยู่ใกล้กองไฟ"; return false; }
        if (r.atShore && NearestSea(player.grid, 1.8f) == null) { why = "ต้องยืนริมทะเล"; return false; }
        foreach (var c in r.cost) if (Count(c.item) < c.n) { why = MissingMaterials; return false; }
        return true;
    }

    void Craft(Recipe r)
    {
        if (!CanCraft(r, out var why)) { Hint(why, 2f); return; }
        if (r.places != null && !TryPlace(r.places.Value, r.atShore)) { Hint($"ไม่มีที่ว่างพอจะสร้าง{r.name}ตรงนี้", 2f); return; }
        foreach (var c in r.cost) inv[c.item] -= c.n;
        Sfx.Play("craft", 0.5f);
        if (r.raftUpgrade) { UpgradeRaft(); return; }
        if (r.output != null) Give(r.output.Value, r.count, null);
        if (r.output != null && IsWeapon(r.output.Value)) AutoEquipWeapon();
        else Popup($"สร้าง{r.name}แล้ว!", PlayerPos + Vector3.up * 0.9f, Color.white);
        if (r.output != null && IsClothing(r.output.Value)) // put it straight on if that slot is free
        {
            var it = r.output.Value;
            if ((IsHat(it) && hat == null) || (IsOutfit(it) && outfit == null)) ToggleWear(it);
        }
        if (r.output == Item.WaterFlask) flaskWater = 0;
        if (r.places == NodeKind.Raft)
        {
            raftLevel = 1;
            RaftNode?.Refresh();
            Hint("สร้างแพเสร็จแล้ว! แพจะพาคุณข้ามจากเกาะสู่เกาะ เตรียมอาหารกับน้ำให้พร้อม แล้วออกเรือได้เลย", 6f);
        }
    }

    bool TryPlace(NodeKind kind, bool atShore)
    {
        float own = kind switch { NodeKind.Shelter => 0.6f, NodeKind.Raft => 0.55f, _ => 0.4f };
        var dirs = new System.Collections.Generic.List<Vector2> { player.facing };
        for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4f; dirs.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a))); }
        foreach (float dist in new[] { 1.1f, 1.6f })
            foreach (var d in dirs)
            {
                Vector2 g = player.grid + d * dist;
                if (!IsLand(g) || (atShore && !TouchesSea(g))) continue;
                if ((g - player.grid).magnitude < own + 0.35f || !FreeSpot(g, own + 0.1f)) continue;
                var n = Spawn(kind, g, false);
                n.built = kind != NodeKind.Raft; // the hammer can take campfires and shelters down again
                Fx.Burst(n.transform.position, new Color(0.8f, 0.7f, 0.5f), 10, 1.2f, 0.8f);
                if (kind != NodeKind.Raft) SendSpawn(n); // campfires and shelters are shared; your raft is yours
                return true;
            }
        return false;
    }

    void EatBest()
    {
        // full but hurt: eat whatever heals the most (honey included)
        if (hunger >= 99f && health < MaxHealth - 0.5f)
        {
            foreach (var it in new[] { Item.Honey, Item.Salad, Item.CookedMeat, Item.CookedCrab, Item.CookedEgg, Item.CookedFruit, Item.CookedFish })
                if (Count(it) > 0) { Eat(it); return; }
            Hint("ยังไม่หิว (อาหารปิ้งหรือน้ำผึ้งช่วยฟื้น HP ได้แม้อิ่ม)", 2f);
            return;
        }
        foreach (var it in new[] { Item.Salad, Item.CookedMeat, Item.CookedCrab, Item.CookedFruit, Item.CookedFish, Item.CookedEgg, Item.Fruit, Item.Carrot, Item.Tomato, Item.Milk, Item.Berry, Item.RawFish, Item.Tuna, Item.CrabMeat, Item.RawMeat })
            if (Count(it) > 0) { Eat(it); return; }
        Hint("ไม่มีอาหาร! ไปเก็บเบอร์รี่หรือผลไม้", 2f);
    }

    void Eat(Item it)
    {
        int food = FoodValue(it);
        if (food <= 0 || Count(it) == 0) return;
        int heal = HealValue(it);
        bool hurt = health < MaxHealth - 0.5f;
        if (hunger >= 99f && !(heal > 0 && hurt)) // healing food can still be eaten when full, if you're hurt
        {
            Hint(hurt ? "ยังไม่หิว (อาหารปิ้งหรือน้ำผึ้งช่วยฟื้น HP ได้แม้อิ่ม)" : "ยังไม่หิว", 1.5f);
            return;
        }
        inv[it]--;
        hunger = Mathf.Min(100f, hunger + food);
        thirst = Mathf.Min(100f, thirst + ThirstValue(it));
        health = Mathf.Min(MaxHealth, health + heal);
        Sfx.Play("eat", 0.6f);
        Popup($"+{food} อิ่ม", PlayerPos + Vector3.up * 0.9f, new Color(1f, 0.7f, 0.3f));
        if (heal > 0 && hurt) Popup($"+{heal} HP", PlayerPos + Vector3.up * 1.2f, new Color(1f, 0.4f, 0.45f));
    }

    // ------------------------------------------------------------------ sleep
    void TrySleep()
    {
        if (!IsNight) { Hint("นอนได้เฉพาะตอนกลางคืน", 2f); return; }
        foreach (var e in enemies)
            if (e != null && (e.grid - player.grid).magnitude < 5f) { Hint("นอนไม่ได้ มีศัตรูอยู่ใกล้ๆ!", 2f); return; }
        sleepT = 0.0001f;
        sleepApplied = false;
        craftOpen = false; skillsOpen = false;
    }

    void UpdateSleep(float dt)
    {
        sleepT += dt;
        if (!sleepApplied && sleepT >= 1.2f)
        {
            sleepApplied = true;
            bool alone = !(Shared && remotes.Count > 0);
            hunger = Mathf.Max(1f, hunger - 15f);
            thirst = Mathf.Max(1f, thirst - 20f);
            health = Mathf.Min(MaxHealth, health + 35f);
            if (alone)
            {
                float skip = Mathf.Repeat(0.27f - Phase, 1f) * DayLength; // wake up just after sunrise
                clock += skip;
                Now += skip; // plants keep growing while you sleep
                foreach (var e in enemies) if (e != null) Destroy(e.gameObject);
                enemies.Clear();
                Hint($"นอนจนถึงเช้า วันที่ {Day}", 3f);
            }
            else Hint("งีบไปพักหนึ่ง (คืนยังไม่จบเพราะคนอื่นยังไม่นอน)", 3f); // time is shared online
            SaveGame();
        }
        if (sleepT >= 2.6f) sleepT = 0f;
    }

    // ------------------------------------------------------------------ sailing to the next island
    bool arriving;          // sail fade direction: false = leaving an island, true = landing
    float sailWarnUntil;

    // ------------------------------------------------------------------ raft upgrades
    /// Each island's own material makes the raft strong enough for the next, rougher crossing:
    /// Tropical = timber, Jungle = bamboo, Tribe = woven cloth sail, Volcano = obsidian. Costs grow with every level.
    public static string RaftMaterial(Biome b) => b switch
    {
        Biome.Jungle => "ไม้ไผ่", Biome.Volcanic => "ออบซิเดียน", Biome.Tribal => "ผ้า", _ => "ไม้",
    };

    void RefreshRaftUpgrade(Recipe r)
    {
        int L = Mathf.Max(1, raftLevel);
        r.name = $"อัปเกรดเรือเป็น Lv {L + 1}";
        (r.cost, r.desc) = biome switch
        {
            Biome.Jungle => (new[] { (Item.Bamboo, 8 + 2 * L), (Item.Rope, 4 + L), (Item.Wood, 4) }, "ตัวแพไม้ไผ่: เบาและเร็วขึ้น"),
            Biome.Volcanic => (new[] { (Item.Obsidian, 4 + L), (Item.Wood, 6 + L), (Item.Rope, 4) }, "หัวแพออบซิเดียน: ทนการกัด"),
            Biome.Tribal => (new[] { (Item.Cloth, 3 + L), (Item.Wood, 6 + L), (Item.Rope, 4) }, "ใบเรือผ้าทอ: รับลมได้มากขึ้น"),
            _ => (new[] { (Item.Wood, 8 + 2 * L), (Item.Rope, 4 + L), (Item.Stone, 4) }, "เสริมพื้นแพให้ฝ่าคลื่นแรงได้"),
        };
    }

    void UpgradeRaft()
    {
        raftLevel++;
        var raft = RaftNode;
        if (raft != null)
        {
            raft.Refresh();
            raft.Shake();
            Fx.Burst(raft.transform.position + Vector3.up * 0.4f, new Color(1f, 0.85f, 0.3f), 16, 1.4f, 1.6f, 1f, 3f);
        }
        Popup($"{BoatName(raftLevel)} Lv {raftLevel}!", PlayerPos + Vector3.up * 1f, new Color(1f, 0.85f, 0.3f));
        Hint($"อัปเกรดเป็น{BoatName(raftLevel)} Lv {raftLevel} แล้ว: เร็วขึ้น {RaftSpeedBonus * 100f:0}% รับดาเมจในทะเลลดลง {RaftArmor * 100f:0}%", 6f);
    }

    /// What the boat has become at each level (its sprite changes too, see PixelArt.Raft).
    public static string BoatName(int level) => level switch
    {
        <= 1 => "แพไม้", 2 => "แพมีราว", 3 => "เรือแคนู", 4 => "เรือใบ", 5 => "เรือใบสองเสา",
        <= 9 => "เรือสำเภา", <= 19 => "เรือกำปั่น", _ => "เรือมหาสมุทร",
    };

    /// Bonuses from raft upgrades while at sea.
    float RaftSpeedBonus => 0.1f * Mathf.Max(0, raftLevel - 1);
    float RaftArmor => Mathf.Min(0.4f, 0.08f * Mathf.Max(0, raftLevel - 1));

    /// Using your boat opens the sea chart: sail back to any island you've visited, or on to a new one.
    void TrySail(int page = 0)
    {
        var d = new Dialog { speaker = $"แผนที่ทะเล - {BoatName(raftLevel)} Lv {raftLevel}" };
        int next = farthest + 1;
        bool canGoNew = raftLevel >= farthest;
        d.text = canGoNew
            ? $"เลือกเกาะที่จะล่องไป เกาะที่เคยไปแล้วจะยังเหมือนตอนที่จากมา"
            : $"ทะเลไปเกาะที่ {next} คลื่นแรงเกินไปสำหรับเรือ Lv {raftLevel} ต้องให้ช่างต่อเรืออัปเกรดเป็น Lv {farthest} ก่อน แต่กลับไปเกาะที่เคยไปได้";
        // visited islands, nearest first, a page at a time (there can be a hundred of them)
        var known = new System.Collections.Generic.List<int>();
        for (int n = farthest; n >= 1; n--) if (n != island) known.Add(n);
        known.Sort((a, b) => Mathf.Abs(a - island).CompareTo(Mathf.Abs(b - island)));
        const int perPage = 6;
        for (int i = page * perPage; i < known.Count && i < (page + 1) * perPage; i++)
        {
            int dest = known[i];
            d.options.Add(($"ล่องไปเกาะที่ {dest} {IslandName(dest)} ({BiomeNameOf(BiomeOf(dest))})", () => ChooseVoyage(dest), true));
        }
        if (page > 0) d.options.Add(("◀ หน้าก่อน", () => TrySail(page - 1), true));
        if ((page + 1) * perPage < known.Count) d.options.Add(($"หน้าถัดไป ▶ (อีก {known.Count - (page + 1) * perPage} เกาะ)", () => TrySail(page + 1), true));
        d.options.Add((canGoNew ? $"ออกสำรวจเกาะใหม่ (เกาะที่ {next}) ★" : $"เกาะใหม่ (เกาะที่ {next}) - ต้องใช้เรือ Lv {farthest}",
            () => ChooseVoyage(next), canGoNew));
        d.options.Add(("ยังไม่ออกเรือ", CloseDialog, true));
        dialog = d;
        craftOpen = false; skillsOpen = false;
    }

    void ChooseVoyage(int dest)
    {
        dialog = null;
        lastInteract = Time.time;
        destIsland = dest;
        // The voyage takes a while: warn once if the player is low on supplies.
        int food = 0;
        foreach (var kv in inv) if (FoodValue(kv.Key) > 0) food += kv.Value;
        bool lowSupplies = food < 3 || (thirst < 60f && flaskWater == 0);
        if (lowSupplies && Time.time > sailWarnUntil)
        {
            sailWarnUntil = Time.time + 4f;
            Hint("อาหารหรือน้ำไม่พอสำหรับการเดินทาง! เลือกเกาะอีกครั้งถ้าจะออกเรือเลย", 4f);
            return;
        }
        SaveGame(); // if you quit at sea you come back here, next to the raft
        BeginSailFade(false);
        Sfx.Play("win", 0.7f);
    }

    void BeginSailFade(bool landing)
    {
        arriving = landing;
        sailT = 0.0001f;
        sailApplied = false;
        craftOpen = false; skillsOpen = false;
        if (cursor != null) cursor.enabled = false;
    }

    void UpdateSail(float dt)
    {
        sailT += dt;
        if (!sailApplied && sailT >= 1.8f)
        {
            sailApplied = true;
            sailT = 1.8f;
            if (!arriving) StartVoyage(); // swap the island for open sea behind the fade
            else
            {
                EndVoyage();
                ArriveAt(destIsland);
                SaveSystem.Write(Snapshot()); // SaveGame() is skipped while sailing, so save the new island directly
                savedAt = Time.time;
            }
        }
        if (sailT >= 4.2f) sailT = 0f;
    }
}
