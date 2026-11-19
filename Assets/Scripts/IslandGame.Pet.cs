using System.Collections.Generic;
using UnityEngine;

/// Pets. Buy a puppy, kitten, bunny or parrot at the pet shop in the private island's town, or
/// catch a wild creature: knock it below half health and press P with a catching net
/// (Item.PetNet). One pet at a time walks with you on every island and now and then brings you
/// something it found; the rest play around your house on the private island (visitors see
/// them too). P opens the pet list. Pets are kept when you die, like the private island.
public partial class IslandGame
{
    public const int MaxPets = 7;
    readonly List<PetSave> pets = new List<PetSave>();
    int activePet = -1;
    Pet petObj;                                    // the one walking with you
    readonly List<Pet> petObjs = new List<Pet>();  // every pet made on this island (yours or a friend's)
    float petFindT;

    static readonly string[] PetNames = { "ถุงทอง", "ส้มจี๊ด", "ข้าวปั้น", "โมจิ", "ทองหยิบ", "ลูกชิ้น", "บัวลอย", "มะม่วง", "เต้าหู้", "ขนมปัง", "ปุยฝ้าย", "ช็อกโก", "ข้าวตัง", "น้ำแข็งใส", "ขนุน", "ชาไทย" };

    public static string PetSpecies(int code) => code switch
    {
        0 => "หมา", 1 => "แมว", 2 => "กระต่าย", 3 => "นกแก้ว", 4 => "ปลาทอง",
        _ => code >= 100 ? EnemyName((EnemyKind)(code - 100)) : "สัตว์เลี้ยง",
    };

    /// Small, ordinary creatures can be tamed; bosses, pack leaders and the strange ones can't.
    static bool Tameable(EnemyKind k) => k is EnemyKind.Crab or EnemyKind.Snake or EnemyKind.Boar or EnemyKind.Barnacle or EnemyKind.HermitCrab
        or EnemyKind.ChimeBird or EnemyKind.LanternFish or EnemyKind.TimeBeetle or EnemyKind.Snowling or EnemyKind.FrostBat
        or EnemyKind.PirateMonkey or EnemyKind.ThunderToad or EnemyKind.WorkerAnt or EnemyKind.GiantBee or EnemyKind.MirrorWisp;

    bool CatchTarget(Enemy e) => e != null && Tameable(e.kind) && !e.alpha;

    /// I'm the host and another player's net caught one of my creatures. Only a tameable creature
    /// below half health can be caught (as on their side); it leaves for everyone, with no loot or XP
    /// (by = -1). Anything else is refused: a net must not be a way to delete a boss.
    void CatchFromNet(Enemy target, int from)
    {
        if (!CatchTarget(target) || target.hp * 2 > target.maxHp) return;
        enemies.Remove(target);
        Vector3 at = target.transform.position;
        Send(new NetMsg { t = "kill", id = target.netId, by = -1, kind = (int)target.kind, x = at.x, y = at.y });
        Destroy(target.gameObject);
    }

    // ------------------------------------------------------------------ save data
    void LoadPets(SaveData d)
    {
        pets.Clear();
        foreach (var p in d.pets) if (p != null) pets.Add(new PetSave { code = p.code, name = p.name });
        activePet = d.activePet >= 0 && d.activePet < pets.Count ? d.activePet : -1;
    }

    void SavePets(SaveData d)
    {
        d.pets.Clear();
        foreach (var p in pets) d.pets.Add(new PetSave { code = p.code, name = p.name });
        d.activePet = activePet;
    }

    PetSave ActivePetSave => activePet >= 0 && activePet < pets.Count ? pets[activePet] : null;

    // ------------------------------------------------------------------ on the island
    /// Called by StartGame once the player stands on the island.
    void SpawnPets()
    {
        petObj = null;
        petObjs.Clear();
        petFindT = 0f;
        var a = ActivePetSave;
        if (a != null)
            petObj = MakePet(a.code, a.name, player.grid + new Vector2(-0.7f, 0.5f), () => player != null && voyage == null && !dead ? player.grid : (Vector2?)null);
        if (!atHome || inHouse) return; // the others play outside around the house
        int k = 0;
        if (visiting)
        {
            foreach (var (code, name) in visitPets) MakePet(code, name, RestSpot(k++), null);
            return;
        }
        for (int i = 0; i < pets.Count; i++)
            if (i != activePet) MakePet(pets[i].code, pets[i].name, RestSpot(k++), null);
    }

    Vector2 RestSpot(int i)
    {
        float ang = i * 2.4f;
        return WalkableNear(Center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (1.6f + 0.3f * (i % 3)) + new Vector2(0f, 1.2f));
    }

    Pet MakePet(int code, string name, Vector2 at, System.Func<Vector2?> follow)
    {
        var p = new GameObject("Pet " + name).AddComponent<Pet>();
        p.transform.SetParent(world);
        p.Init(code, name, at, follow);
        petObjs.Add(p);
        return p;
    }

    void RespawnPets()
    {
        homeSendT = Mathf.Min(homeSendT, 1f); // visitors should see the change soon (IslandGame.Visit.cs)
        foreach (var p in petObjs) if (p != null) Destroy(p.gameObject);
        if (player != null && world != null) SpawnPets();
    }

    /// The pet walking with you finds something every minute and a half.
    void TickPets(float dt)
    {
        var a = ActivePetSave;
        if (a == null || petObj == null) return;
        petFindT += dt;
        if (petFindT < 90f) return;
        petFindT = 0f;
        double r = rnd.NextDouble();
        bool shelly = a.code >= 100 && (EnemyKind)(a.code - 100) is EnemyKind.Crab or EnemyKind.HermitCrab or EnemyKind.Barnacle;
        Item it = a.code switch
        {
            0 => r < 0.12 ? Item.GoldCoin : r < 0.56 ? Item.Stick : Item.Stone,
            1 => r < 0.6 ? Item.RawFish : r < 0.85 ? Item.Feather : Item.GoldCoin,
            2 => r < 0.35 ? Item.Carrot : r < 0.75 ? Item.Berry : Item.Fiber,
            3 => r < 0.4 ? Item.Fruit : r < 0.72 ? Item.Feather : Item.GoldCoin,
            4 => r < 0.5 ? Item.RawFish : r < 0.85 ? Item.ShellShard : Item.GoldCoin,
            _ => shelly ? (r < 0.5 ? Item.ShellShard : Item.CrabMeat)
                        : r < 0.45 ? Item.Fiber : r < 0.8 ? Item.Berry : Item.Stick,
        };
        inv[it] = Count(it) + 1;
        Popup($"{a.name}คาบ{Name(it)}มาให้!", petObj.transform.position + Vector3.up * 0.7f, new Color(1f, 0.7f, 0.8f));
        Fx.Burst(petObj.transform.position + Vector3.up * 0.4f, new Color(1f, 0.45f, 0.55f), 5, 0.4f, 0.8f, 0.8f, 1.5f);
        Sfx.Play("pickup", 0.4f);
    }

    /// Names over pets when you're close to them.
    void DrawPetTags(float s)
    {
        if (player == null) return;
        var st = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
        foreach (var p in petObjs)
        {
            if (p == null || string.IsNullOrEmpty(p.petName) || (p.grid - player.grid).magnitude > 2.2f) continue;
            var gp = ToGui(p.transform.position + Vector3.up * 0.55f, s);
            ShadowLabel(new Rect(gp.x - 60, gp.y - 16, 120, 16), p.petName, st, new Color(1f, 0.8f, 0.88f));
        }
    }

    // ------------------------------------------------------------------ your first pet
    /// A new player picks a companion: dog, cat, bird or fish. Asked once per session until chosen
    /// (remembered in PlayerPrefs; anyone who already owns a pet is never asked).
    static string StarterPetKey => "starter_pet_chosen" + SaveSystem.Profile; // per character
    bool starterAsked;
    static readonly (int code, string label)[] StarterPets =
    {
        (0, "หมา: คาบกิ่งไม้ หิน บางทีก็เหรียญทองมาให้"),
        (1, "แมว: จับปลา และคาบขนนกมาให้"),
        (3, "นก (นกแก้ว): เก็บผลไม้ ขนนก และเหรียญทอง"),
        (4, "ปลา (ปลาทองในโหล ลอยตามคุณ): หาปลาและเปลือกหอย"),
    };

    void TickStarterPet()
    {
        if (starterAsked || title || player == null || dead || visiting || voyage != null || Busy || dialog != null) return;
        if (pets.Count > 0) { starterAsked = true; PlayerPrefs.SetInt(StarterPetKey, 1); return; }
        if (PlayerPrefs.GetInt(StarterPetKey, 0) != 0) { starterAsked = true; return; }
        if (Time.time - lastInteract < 1.5f) return; // let the island appear first
        starterAsked = true;
        var d = new Dialog { speaker = "เลือกสัตว์เลี้ยงคู่ใจตัวแรก", text = "เพื่อนตัวแรกจะเดินตามคุณไปทุกเกาะ และคาบของมาให้เป็นระยะ\nเลือกได้ 1 ตัวฟรี (ตัวอื่นซื้อเพิ่มได้ที่ร้านสัตว์เลี้ยงในตลาดเกาะส่วนตัว)" };
        foreach (var (code, label) in StarterPets)
        {
            int c = code;
            d.options.Add((label, () => ChooseStarterPet(c), true));
        }
        d.options.Add(("ไว้ทีหลัง", CloseDialog, true));
        dialog = d;
        craftOpen = false; skillsOpen = false; bagOpen = false;
    }

    void ChooseStarterPet(int code)
    {
        CloseDialog();
        var p = AddPet(code);
        activePet = pets.IndexOf(p);
        RespawnPets();
        PlayerPrefs.SetInt(StarterPetKey, 1);
        PlayerPrefs.Save();
        if (petObj != null) Fx.Burst(petObj.transform.position + Vector3.up * 0.4f, new Color(1f, 0.45f, 0.55f), 12, 0.8f, 1.2f, 1f, 2f);
        Sfx.Play("levelup", 0.5f);
        Hint($"ได้{PetSpecies(code)}ชื่อ \"{p.name}\" แล้ว! มันจะเดินตามคุณไปทุกที่ กด P เพื่อตั้งชื่อใหม่", 5f);
        SaveGame();
    }

    // ------------------------------------------------------------------ getting pets
    PetSave AddPet(int code)
    {
        var free = new List<string>(PetNames);
        foreach (var p in pets) free.Remove(p.name);
        if (free.Count == 0) free.AddRange(PetNames);
        var pet = new PetSave { code = code, name = free[rnd.Next(free.Count)] };
        pets.Add(pet);
        if (activePet < 0) { activePet = pets.Count - 1; RespawnPets(); }
        else if (atHome && !inHouse && !visiting) RespawnPets(); // it joins the others at home
        return pet;
    }

    /// P: catch the weakened creature you're facing, otherwise the pet list.
    void PetKey()
    {
        if (CatchTarget(enemyTarget)) TryCatch(enemyTarget);
        else OpenPetMenu();
    }

    void TryCatch(Enemy e)
    {
        string who = EnemyName(e.kind);
        if (Count(Item.PetNet) == 0) { Hint("ต้องมีตาข่ายจับสัตว์ (ประดิษฐ์ได้ใน Tab หรือซื้อที่ร้านสัตว์เลี้ยงในตลาดเกาะส่วนตัว)", 3.5f); return; }
        if (e.hp * 2 > e.maxHp) { Hint($"{who}ยังแรงดีอยู่ ตีให้เลือดเหลือไม่ถึงครึ่งก่อน แล้วค่อย{KeyOr("กด P", "แตะตาข่ายในช่องลัด")}", 3f); return; }
        if (pets.Count >= MaxPets) { Hint($"มีสัตว์เลี้ยงครบ {MaxPets} ตัวแล้ว ปล่อยบางตัวคืนธรรมชาติก่อน (P)", 3f); return; }
        inv[Item.PetNet]--;
        lastInteract = Time.time;
        player.PlayAction(PixelArt.AttackAnim, 14f);
        Vector3 at = e.transform.position;
        Fx.Burst(at + Vector3.up * 0.3f, new Color(0.95f, 0.88f, 0.65f), 10, 0.8f, 1f);
        float chance = 0.45f + 0.5f * (1f - e.hp / (float)Mathf.Max(1, e.maxHp));
        if (Random.value > chance)
        {
            Popup($"{who}ดิ้นหลุดตาข่าย!", at + Vector3.up * 0.6f, new Color(1f, 0.75f, 0.5f));
            Sfx.Play("hit", 0.4f);
            return;
        }
        enemies.Remove(e);
        if (e.puppet) Send(new NetMsg { t = "hit", to = hostId, id = e.netId, ev = "catch" }); // the host's creature: the host checks it and it goes for everyone
        else if (Shared) Send(new NetMsg { t = "kill", id = e.netId, by = -1, kind = (int)e.kind, x = at.x, y = at.y });
        Destroy(e.gameObject);
        var pet = AddPet(100 + (int)e.kind);
        Popup($"จับ{who}ได้!", at + Vector3.up * 0.7f, new Color(1f, 0.85f, 0.4f));
        Fx.Burst(at + Vector3.up * 0.4f, new Color(1f, 0.45f, 0.55f), 12, 0.8f, 1.2f, 1f, 2f);
        Sfx.Play("levelup", 0.5f);
        Hint($"จับ{who}มาเลี้ยงได้แล้ว! ชื่อ \"{pet.name}\"" + (pets[activePet] == pet ? " มันจะเดินตามคุณไปทุกที่" : " มันจะไปรออยู่ที่บ้านบนเกาะส่วนตัว") + " (กด P เพื่อตั้งชื่อ)", 5f);
        AddXp(3, at);
        SaveGame();
    }

    // ------------------------------------------------------------------ the pet list
    void OpenPetMenu()
    {
        if (player == null || Busy) return;
        craftOpen = false; skillsOpen = false; bagOpen = false;
        ShowPetList();
    }

    /// Every pet you own; pick one for what to do with it.
    void ShowPetList()
    {
        renamePet = -1;
        var d = new Dialog { speaker = $"สัตว์เลี้ยงของคุณ ({pets.Count}/{MaxPets})" };
        var a = ActivePetSave;
        if (pets.Count == 0)
            d.text = "ยังไม่มีสัตว์เลี้ยงเลย\n" +
                     "• ซื้อลูกหมา ลูกแมว กระต่าย หรือนกแก้ว ได้ที่ร้านสัตว์เลี้ยงในตลาดเกาะส่วนตัว (H)\n" +
                     "• หรือจับสัตว์ป่า: ทำตาข่ายจับสัตว์ (Tab) ตีสัตว์ให้เลือดเหลือไม่ถึงครึ่ง แล้วยืนหันหน้าไปหามันกด P";
        else
            d.text = (a != null ? $"{a.name} ({PetSpecies(a.code)}) กำลังเดินตามคุณ และจะคาบของมาให้เป็นครั้งคราว" : "ตอนนี้ไม่มีตัวไหนเดินตามคุณ") +
                     "\nตัวอื่นๆ เล่นอยู่แถวบ้านบนเกาะส่วนตัว เลือกตัวที่ต้องการเพื่อพาไปด้วย ตั้งชื่อ หรือปล่อย";
        for (int i = 0; i < pets.Count; i++)
        {
            int idx = i;
            var p = pets[i];
            d.options.Add(($"{p.name} ({PetSpecies(p.code)}){(i == activePet ? "  - เดินตามอยู่" : "")}", () => ShowPet(idx), true));
        }
        d.options.Add(("ปิด", CloseDialog, true));
        dialog = d;
    }

    void ShowPet(int i)
    {
        renamePet = -1;
        if (i < 0 || i >= pets.Count) { ShowPetList(); return; }
        var p = pets[i];
        var d = new Dialog { speaker = $"{p.name} ({PetSpecies(p.code)})" };
        d.text = i == activePet ? $"{p.name}กำลังเดินตามคุณไปทุกที่" : $"{p.name}กำลังเล่นอยู่แถวบ้านบนเกาะส่วนตัว";
        if (i == activePet) d.options.Add(($"ให้{p.name}กลับไปพักที่บ้าน", () => SetActivePet(-1), true));
        else d.options.Add(($"พา{p.name}ไปด้วย", () => SetActivePet(i), true));
        d.options.Add(("ตั้งชื่อใหม่", () => StartRename(i), true));
        d.options.Add(($"ปล่อย{p.name}คืนธรรมชาติ...", () => ConfirmRelease(i), true));
        d.options.Add(("◀ กลับ", ShowPetList, true));
        dialog = d;
    }

    void SetActivePet(int i)
    {
        activePet = i >= 0 && i < pets.Count ? i : -1;
        RespawnPets();
        CloseDialog();
        var a = ActivePetSave;
        Hint(a != null ? $"{a.name}มาเดินด้วยแล้ว!" : "ส่งสัตว์เลี้ยงกลับไปพักที่บ้านแล้ว", 2f);
        SaveGame();
    }

    void ConfirmRelease(int i)
    {
        if (i < 0 || i >= pets.Count) { ShowPetList(); return; }
        var a = pets[i];
        var d = new Dialog { speaker = "ปล่อยคืนธรรมชาติ", text = $"ปล่อย{a.name} ({PetSpecies(a.code)}) ไปจริงๆ เหรอ? มันจะไม่กลับมาอีกแล้ว" };
        d.options.Add(($"ใช่ ปล่อย{a.name}ไป", () =>
        {
            if (i == activePet && petObj != null) Fx.Burst(petObj.transform.position + Vector3.up * 0.4f, new Color(1f, 0.45f, 0.55f), 10, 0.8f, 1.2f, 1f, 2f);
            pets.RemoveAt(i);
            activePet = i == activePet ? -1 : i < activePet ? activePet - 1 : activePet;
            RespawnPets();
            CloseDialog();
            Hint($"ลาก่อน {a.name}...", 3f);
            SaveGame();
        }, true));
        d.options.Add(("ไม่ เก็บไว้", () => ShowPet(i), true));
        dialog = d;
    }

    // ------------------------------------------------------------------ naming a pet
    public const int MaxPetName = 12;
    int renamePet = -1;     // the pet being named (a text box shows in the dialog)
    string renameText = "";
    float renameOpenedAt;

    void StartRename(int i)
    {
        if (i < 0 || i >= pets.Count) return;
        var p = pets[i];
        renamePet = i;
        renameText = p.name;
        renameOpenedAt = Time.time;
        var d = new Dialog { speaker = $"ตั้งชื่อ{PetSpecies(p.code)}ของคุณ", text = $"พิมพ์ชื่อใหม่ให้{p.name} (ไม่เกิน {MaxPetName} ตัวอักษร)\n\n\n" };
        d.options.Add(("บันทึกชื่อ (Enter)", FinishRename, true));
        d.options.Add(("ยกเลิก (Esc)", () => ShowPet(i), true));
        dialog = d;
    }

    static string CleanPetName(string n)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in (n ?? "").Trim())
            if (ch >= ' ' && ch != '|' && ch != ':' && ch != ';' && ch != ',') sb.Append(ch); // those split the data sent to visitors
        string s = sb.ToString().Trim();
        return s.Length > MaxPetName ? s.Substring(0, MaxPetName) : s;
    }

    void FinishRename()
    {
        int i = renamePet;
        renamePet = -1;
        if (i < 0 || i >= pets.Count) { CloseDialog(); return; }
        string n = CleanPetName(renameText);
        if (n.Length == 0) { Hint("ชื่อว่างไม่ได้นะ", 2f); StartRename(i); return; }
        string old = pets[i].name;
        pets[i].name = n;
        RespawnPets(); // name tags (and visitors) get the new name
        SaveGame();
        if (n != old) Hint($"ตั้งชื่อใหม่แล้ว: {n}!", 2.5f);
        if (i == activePet && petObj != null) Fx.Burst(petObj.transform.position + Vector3.up * 0.4f, new Color(1f, 0.45f, 0.55f), 6, 0.5f, 0.8f, 0.8f, 1.5f);
        ShowPet(i);
    }

    /// The text box inside the naming dialog (called by DrawDialog).
    void DrawRenameField(Rect r)
    {
        var e = Event.current;
        if (e.type == EventType.KeyDown && Time.time - renameOpenedAt > 0.2f)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.Use(); FinishRename(); return; }
            if (e.keyCode == KeyCode.Escape) { e.Use(); ShowPet(renamePet); return; }
        }
        Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(1f, 0.85f, 0.3f, 0.6f));
        GUI.SetNextControlName("petname");
        renameText = GUI.TextField(r, renameText ?? "", MaxPetName + 4);
        GUI.FocusControl("petname");
    }
}
