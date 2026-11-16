using System.Collections.Generic;
using UnityEngine;

/// The bag (B / I): items sorted into categories, with use, shortcut, drop and destroy. Next to it
/// the equipment panel: hat, outfit and weapon, and the stats they give. Under everything the
/// hotbar: 8 shortcuts (keys 1-8, or click / tap) that stay usable while the bag is open.
public partial class IslandGame
{
    public enum BagTab { All, Materials, Food, Gear, Special }
    public bool bagOpen;
    BagTab bagTab;
    bool sortByCount;
    Item? bagSelected;
    Vector2 bagScroll;
    bool confirmDestroy;
    public Item? weapon;
    public readonly Item?[] hotbar = new Item?[8];

    // ------------------------------------------------------------------ item kinds and gear stats
    public static bool IsWeapon(Item it) => it is Item.Spear or Item.ObsidianSpear;
    static bool IsUsable(Item it) => FoodValue(it) > 0 || it == Item.Antidote || IsPowerFruit(it) || it == Item.Sundial;

    static BagTab TabOf(Item it) =>
        FoodValue(it) > 0 ? BagTab.Food
        : IsWeapon(it) || IsClothing(it) || IsTool(it) ? BagTab.Gear
        : IsPowerFruit(it) || it is Item.Antidote or Item.GlowSpore or Item.Feather or Item.CloudCotton or Item.ShellShard
            or Item.Pearl or Item.Coral or Item.Gear or Item.TimeSand or Item.IceShard or Item.Fur ? BagTab.Special
        : BagTab.Materials;

    static string TabName(BagTab t) => t switch
    {
        BagTab.Materials => "วัตถุดิบ", BagTab.Food => "อาหาร", BagTab.Gear => "อุปกรณ์", BagTab.Special => "พิเศษ", _ => "ทั้งหมด",
    };

    /// What a piece of gear gives: damage, attack speed (fraction), max health, damage taken reduction, slower thirst.
    public static (int dmg, float atkSpd, float hp, float armor, float thirst) GearStats(Item it) => it switch
    {
        Item.Spear => (3, 0f, 0f, 0f, 0f),
        Item.ObsidianSpear => (6, 0.10f, 0f, 0f, 0f),
        Item.StrawHat => (0, 0f, 0f, 0f, 0.15f),
        Item.TribalHeadband => (1, 0.15f, 0f, 0f, 0f),
        Item.LeafTunic => (0, 0.10f, 15f, 0f, 0f),
        Item.HideVest => (0, 0f, 0f, 0.20f, 0f),
        _ => (0, 0f, 0f, 0f, 0f),
    };

    static string GearText(Item it)
    {
        var g = GearStats(it);
        var parts = new List<string>();
        if (g.dmg > 0) parts.Add($"ดาเมจ +{g.dmg}");
        if (g.atkSpd > 0) parts.Add($"ความเร็วโจมตี +{g.atkSpd * 100f:0}%");
        if (g.hp > 0) parts.Add($"พลังชีวิตสูงสุด +{g.hp:0}");
        if (g.armor > 0) parts.Add($"รับดาเมจ -{g.armor * 100f:0}%");
        if (g.thirst > 0) parts.Add($"หิวน้ำช้าลง {g.thirst * 100f:0}%");
        return string.Join(", ", parts);
    }

    float GearSum(System.Func<(int dmg, float atkSpd, float hp, float armor, float thirst), float> pick)
    {
        float s = 0f;
        if (hat != null) s += pick(GearStats(hat.Value));
        if (outfit != null) s += pick(GearStats(outfit.Value));
        if (weapon != null && Count(weapon.Value) > 0) s += pick(GearStats(weapon.Value));
        return s;
    }

    /// Attacks per second: slow to start, faster with gear and Agility, never above 3 per second.
    public float AttacksPerSecond => Mathf.Min(3f, 1.4f * (1f + GearSum(g => g.atkSpd) + 0.05f * SkillLv(Skill.Agility)));
    float AttackInterval => 1f / AttacksPerSecond;
    int GearDamage => Mathf.RoundToInt(GearSum(g => g.dmg));
    float GearArmor => GearSum(g => g.armor);

    /// Equip the best weapon you own if your hands are empty (crafting one, old saves).
    void AutoEquipWeapon()
    {
        if (weapon != null && Count(weapon.Value) > 0) return;
        weapon = Count(Item.ObsidianSpear) > 0 ? Item.ObsidianSpear : Count(Item.Spear) > 0 ? Item.Spear : (Item?)null;
    }

    void ToggleWeapon(Item it)
    {
        if (Count(it) <= 0) return;
        weapon = weapon == it ? (Item?)null : it;
        Sfx.Play("craft", 0.4f);
        Popup(weapon == it ? $"ถือ{Name(it)}" : $"เก็บ{Name(it)}", PlayerPos + Vector3.up * 1f, Color.white);
    }

    // ------------------------------------------------------------------ using items
    /// Clicking or a hotbar key: eat, drink a potion, eat a power fruit, put on gear.
    void UseItem(Item it)
    {
        if (Count(it) <= 0) return;
        if (FoodValue(it) > 0) Eat(it);
        else if (it == Item.Antidote) UseAntidote();
        else if (IsPowerFruit(it)) EatPowerFruit(it);
        else if (IsClothing(it)) ToggleWear(it);
        else if (IsWeapon(it)) ToggleWeapon(it);
        else if (it == Item.WaterFlask) DrinkFlask();
        else if (it == Item.Sundial) UseSundial();
        else Hint($"{Name(it)}: ใช้ในการประดิษฐ์ (Tab)", 2f);
    }

    /// New usable items go onto a free hotbar slot automatically.
    void AutoHotbar(Item it)
    {
        if (!(IsUsable(it) || IsWeapon(it))) return;
        foreach (var h in hotbar) if (h == it) return;
        for (int i = 0; i < hotbar.Length; i++) if (hotbar[i] == null) { hotbar[i] = it; return; }
    }

    void ToggleHotbar(Item it)
    {
        for (int i = 0; i < hotbar.Length; i++) if (hotbar[i] == it) { hotbar[i] = null; return; }
        for (int i = 0; i < hotbar.Length; i++) if (hotbar[i] == null) { hotbar[i] = it; return; }
        Hint("ช่องลัดเต็มแล้ว เอาของออกจากช่องลัดก่อน", 2f);
    }

    void HandleHotbarKeys(UnityEngine.InputSystem.Keyboard kb)
    {
        if (kb == null || craftOpen || skillsOpen || dialog != null || chatOpen) return;
        if (kb.bKey.wasPressedThisFrame || kb.iKey.wasPressedThisFrame) ToggleBag();
        for (int i = 0; i < hotbar.Length; i++)
            if (kb[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.Digit1 + i)].wasPressedThisFrame && hotbar[i] != null)
                UseItem(hotbar[i].Value);
    }

    void ToggleBag()
    {
        bagOpen = !bagOpen;
        if (bagOpen) { craftOpen = false; skillsOpen = false; }
        confirmDestroy = false;
    }

    // ------------------------------------------------------------------ drop / destroy
    void DropItem(Item it, int n)
    {
        n = Mathf.Min(n, Count(it));
        if (n <= 0 || player == null) return;
        inv[it] -= n;
        UnequipIfGone(it);
        var node = Spawn(NodeKind.Drop, player.grid + new Vector2(0.4f, 0.4f), false);
        node.dropItem = it; node.dropCount = n;
        node.Refresh();
        Popup($"ทิ้ง{Name(it)} {n}", PlayerPos + Vector3.up * 1f, new Color(1f, 0.9f, 0.7f));
    }

    void DestroyItem(Item it)
    {
        int n = Count(it);
        if (n <= 0) return;
        inv[it] = 0;
        UnequipIfGone(it);
        for (int i = 0; i < hotbar.Length; i++) if (hotbar[i] == it) hotbar[i] = null;
        Fx.Burst(PlayerPos + Vector3.up * 0.5f, new Color(0.6f, 0.6f, 0.6f), 10, 1f, 1f);
        Popup($"ทำลาย{Name(it)} {n} ชิ้น", PlayerPos + Vector3.up * 1f, new Color(1f, 0.5f, 0.5f));
        bagSelected = null;
        confirmDestroy = false;
    }

    void UnequipIfGone(Item it)
    {
        if (Count(it) > 0) return;
        if (hat == it) hat = null;
        if (outfit == it) outfit = null;
        if (weapon == it) weapon = null;
        if (it == Item.WaterFlask) flaskWater = 0;
    }

    void PickUpDrop(WorldNode n)
    {
        inv[n.dropItem] = Count(n.dropItem) + n.dropCount;
        Popup($"+{n.dropCount} {Name(n.dropItem)}", n.transform.position + Vector3.up * 0.5f, new Color(1f, 0.95f, 0.6f));
        Sfx.Play("pickup", 0.35f);
        RemoveNode(n);
    }

    // ------------------------------------------------------------------ save
    void SaveBag(SaveData d)
    {
        d.weaponId = weapon.HasValue ? (int)weapon.Value + 1 : 0;
        d.hotbar.Clear();
        foreach (var h in hotbar) d.hotbar.Add(h.HasValue ? (int)h.Value + 1 : 0);
    }

    void LoadBag(SaveData d)
    {
        weapon = d.weaponId > 0 ? (Item)(d.weaponId - 1) : (Item?)null;
        System.Array.Clear(hotbar, 0, hotbar.Length);
        if (d.hotbar.Count == 0) // older save: fill the shortcuts with what you can use
            foreach (var st in d.items) { if (st.count > 0) AutoHotbar(st.item); }
        else for (int i = 0; i < hotbar.Length && i < d.hotbar.Count; i++) hotbar[i] = d.hotbar[i] > 0 ? (Item)(d.hotbar[i] - 1) : (Item?)null;
        AutoEquipWeapon();
        bagOpen = false; bagSelected = null;
    }

    // ------------------------------------------------------------------ HUD: hotbar
    const float SlotSize = 40f, SlotGap = 4f;
    Rect HotbarRect(float W, float H) { float w = hotbar.Length * (SlotSize + SlotGap) - SlotGap; return new Rect(W / 2f - w / 2f, H - 76f, w, SlotSize); }

    void DrawHotbar(float W, float H)
    {
        var bar = HotbarRect(W, H);
        Fill(new Rect(bar.x - 4, bar.y - 4, bar.width + 8, bar.height + 8), new Color(0f, 0f, 0f, 0.35f));
        Item? hover = null;
        for (int i = 0; i < hotbar.Length; i++)
        {
            var r = new Rect(bar.x + i * (SlotSize + SlotGap), bar.y, SlotSize, SlotSize);
            GUI.Box(r, GUIContent.none);
            ShadowLabel(new Rect(r.x + 2, r.y, 12, 14), (i + 1).ToString(), new GUIStyle(small) { fontSize = 9 }, new Color(1f, 1f, 1f, 0.6f));
            var it = hotbar[i];
            if (it == null) continue;
            int n = Count(it.Value);
            GUI.color = n > 0 ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            Icon(new Rect(r.x + 5, r.y + 5, SlotSize - 10, SlotSize - 10), PixelArt.Icon(it.Value));
            GUI.color = Color.white;
            if (IsWorn(it.Value) || weapon == it) { Fill(new Rect(r.x, r.y, r.width, 2), new Color(1f, 0.85f, 0.3f)); Fill(new Rect(r.x, r.yMax - 2, r.width, 2), new Color(1f, 0.85f, 0.3f)); }
            string badge = IsWorn(it.Value) || weapon == it ? "<color=#ffd84d>E</color>" : IsTool(it.Value) || IsClothing(it.Value) || IsWeapon(it.Value) ? "" : n.ToString();
            ShadowLabel(new Rect(r.x, r.y + r.height - 16, r.width - 3, 16), badge, smallRight);
            if (r.Contains(Event.current.mousePosition)) hover = it;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) UseItem(it.Value);
        }
        if (hover != null && !bagOpen)
        {
            var h = hover.Value;
            string extra = FoodValue(h) > 0 ? $"  (อิ่ม +{FoodValue(h)})" : IsClothing(h) || IsWeapon(h) ? $"  {GearText(h)}" : "";
            ShadowLabel(new Rect(0, bar.y - 22, W, 20), Name(h) + extra, labelCenter);
        }
        if (!bagOpen) ShadowLabel(new Rect(bar.xMax + 10, bar.y + 12, 120, 18), touchUsed ? "" : "B กระเป๋า", small, new Color(1f, 1f, 1f, 0.6f));
    }

    // ------------------------------------------------------------------ HUD: bag + equipment
    void DrawBag(float W, float H)
    {
        var bar = HotbarRect(W, H);
        float pw = Mathf.Min(W - 20f, 640f);
        float top = 60f, ph = Mathf.Max(220f, bar.y - 10f - top); // never covers the hotbar
        var panel = new Rect(W / 2f - pw / 2f, top, pw, ph);
        Fill(panel, new Color(0.08f, 0.06f, 0.04f, 0.92f));
        Fill(new Rect(panel.x, panel.y, panel.width, 2), new Color(1f, 0.85f, 0.3f, 0.8f));
        ShadowLabel(new Rect(panel.x + 12, panel.y + 6, 200, 22), "กระเป๋า", label, new Color(1f, 0.85f, 0.3f));
        if (GUI.Button(new Rect(panel.xMax - 34, panel.y + 6, 26, 22), "X")) { bagOpen = false; return; }

        // --- equipment column on the right
        float ew = 190f;
        var eq = new Rect(panel.xMax - ew - 8, panel.y + 34, ew, ph - 42);
        Fill(eq, new Color(1f, 1f, 1f, 0.05f));
        ShadowLabel(new Rect(eq.x + 8, eq.y + 4, ew, 20), "สวมใส่", label);
        var pv = PixelArt.Player(0, 0, hat, outfit, lookCode);
        Icon(new Rect(eq.x + ew - 60, eq.y + 6, 50, 80), pv);
        float ey = eq.y + 28;
        EquipSlot(new Rect(eq.x + 8, ey, ew - 70, 30), "หมวก", hat, () => { if (hat != null) ToggleWear(hat.Value); }); ey += 34;
        EquipSlot(new Rect(eq.x + 8, ey, ew - 70, 30), "ชุด", outfit, () => { if (outfit != null) ToggleWear(outfit.Value); }); ey += 34;
        EquipSlot(new Rect(eq.x + 8, ey, ew - 70, 30), "อาวุธ", weapon, () => { if (weapon != null) ToggleWeapon(weapon.Value); }); ey += 40;
        var st = new GUIStyle(small) { wordWrap = true };
        string stats = $"ดาเมจ {WeaponDamage}\nโจมตี {AttacksPerSecond:0.0} ครั้ง/วิ (สูงสุด 3)\nพลังชีวิตสูงสุด {MaxHealth:0}";
        if (GearArmor > 0f) stats += $"\nรับดาเมจ -{GearArmor * 100f:0}%";
        if (power != Power.None) stats += $"\nพลัง{PowerName(power)} Lv {powerLv}";
        GUI.Label(new Rect(eq.x + 8, ey, ew - 16, eq.yMax - ey), stats, st);

        // --- tabs and sort
        float lx = panel.x + 10, lw = pw - ew - 30;
        float ty = panel.y + 32;
        var tabs = (BagTab[])System.Enum.GetValues(typeof(BagTab));
        float tw = (lw - 70f) / tabs.Length;
        for (int i = 0; i < tabs.Length; i++)
        {
            var tst = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            if (tabs[i] == bagTab) tst.normal.textColor = new Color(1f, 0.85f, 0.3f);
            if (GUI.Button(new Rect(lx + i * tw, ty, tw - 2, 22), TabName(tabs[i]), tst)) { bagTab = tabs[i]; bagSelected = null; }
        }
        if (GUI.Button(new Rect(lx + lw - 66, ty, 66, 22), sortByCount ? "เรียง: จำนวน" : "เรียง: ชนิด", new GUIStyle(GUI.skin.button) { fontSize = 10 })) sortByCount = !sortByCount;

        // --- item grid
        var list = new List<Item>();
        foreach (Item it in System.Enum.GetValues(typeof(Item)))
            if (Count(it) > 0 && (bagTab == BagTab.All || TabOf(it) == bagTab)) list.Add(it);
        if (sortByCount) list.Sort((a, b) => Count(b).CompareTo(Count(a)));
        else list.Sort((a, b) => TabOf(a) != TabOf(b) ? TabOf(a).CompareTo(TabOf(b)) : a.CompareTo(b));

        float detailH = 84f;
        var gridView = new Rect(lx, ty + 28, lw, ph - (ty + 28 - panel.y) - detailH - 8);
        int cols = Mathf.Max(1, Mathf.FloorToInt(lw / (SlotSize + SlotGap)) - 0);
        int rows = Mathf.CeilToInt(list.Count / (float)cols);
        var content = new Rect(0, 0, gridView.width - 16, Mathf.Max(gridView.height, rows * (SlotSize + SlotGap)));
        bagScroll = GUI.BeginScrollView(gridView, bagScroll, content, false, false);
        for (int i = 0; i < list.Count; i++)
        {
            var it = list[i];
            var r = new Rect((i % cols) * (SlotSize + SlotGap), (i / cols) * (SlotSize + SlotGap), SlotSize, SlotSize);
            GUI.Box(r, GUIContent.none);
            if (bagSelected == it) { Fill(new Rect(r.x, r.y, r.width, 2), Color.white); Fill(new Rect(r.x, r.yMax - 2, r.width, 2), Color.white); }
            Icon(new Rect(r.x + 5, r.y + 5, SlotSize - 10, SlotSize - 10), PixelArt.Icon(it));
            string badge = IsWorn(it) || weapon == it ? "<color=#ffd84d>E</color>" : Count(it).ToString();
            ShadowLabel(new Rect(r.x, r.y + r.height - 16, r.width - 3, 16), badge, smallRight);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { bagSelected = it; confirmDestroy = false; }
        }
        if (list.Count == 0) ShadowLabel(new Rect(0, 8, content.width, 20), "ไม่มีของในหมวดนี้", smallCenter, new Color(1f, 1f, 1f, 0.5f));
        GUI.EndScrollView();

        // --- selected item details and actions
        var det = new Rect(lx, panel.yMax - detailH - 6, lw, detailH);
        Fill(det, new Color(1f, 1f, 1f, 0.05f));
        if (bagSelected == null || Count(bagSelected.Value) <= 0)
        {
            ShadowLabel(new Rect(det.x + 8, det.y + 8, det.width - 16, 40), "คลิกของในกระเป๋าเพื่อดูรายละเอียด ใช้งาน ใส่ช่องลัด ทิ้ง หรือทำลาย", small, new Color(1f, 1f, 1f, 0.6f));
            return;
        }
        var sel = bagSelected.Value;
        string info = FoodValue(sel) > 0 ? $"อาหาร: อิ่ม +{FoodValue(sel)}" + (HealValue(sel) > 0 ? $", พลังชีวิต +{HealValue(sel)}" : "")
                    : IsClothing(sel) || IsWeapon(sel) ? GearText(sel)
                    : IsPowerFruit(sel) ? $"กินแล้วได้พลัง{PowerName(PowerOf(sel))}"
                    : TabOf(sel) == BagTab.Special ? "ของหายาก ใช้ทำของพิเศษหรือส่งภารกิจ"
                    : "วัตถุดิบสำหรับประดิษฐ์";
        ShadowLabel(new Rect(det.x + 8, det.y + 4, det.width - 16, 20), $"<b>{Name(sel)}</b>  x{Count(sel)}   <size=11>{info}</size>", label);
        float bx = det.x + 8, by = det.y + 30, bw = (det.width - 16 - 4 * 4) / 5f;
        var bst = new GUIStyle(GUI.skin.button) { fontSize = 11 };
        string useLabel = IsClothing(sel) ? (IsWorn(sel) ? "ถอด" : "สวม") : IsWeapon(sel) ? (weapon == sel ? "เก็บ" : "ถือ")
                        : FoodValue(sel) > 0 ? "กิน" : IsPowerFruit(sel) ? "กิน" : sel == Item.Antidote || sel == Item.Sundial ? "ใช้" : "-";
        GUI.enabled = useLabel != "-";
        if (GUI.Button(new Rect(bx, by, bw, 24), useLabel, bst)) UseItem(sel);
        GUI.enabled = true;
        bool onBar = System.Array.IndexOf(hotbar, (Item?)sel) >= 0;
        if (GUI.Button(new Rect(bx + (bw + 4), by, bw, 24), onBar ? "เอาออกช่องลัด" : "ใส่ช่องลัด", bst)) ToggleHotbar(sel);
        if (GUI.Button(new Rect(bx + 2 * (bw + 4), by, bw, 24), "ทิ้ง 1", bst)) DropItem(sel, 1);
        if (GUI.Button(new Rect(bx + 3 * (bw + 4), by, bw, 24), "ทิ้งทั้งหมด", bst)) DropItem(sel, Count(sel));
        var dst = new GUIStyle(bst); dst.normal.textColor = new Color(1f, 0.5f, 0.5f);
        if (GUI.Button(new Rect(bx + 4 * (bw + 4), by, bw, 24), confirmDestroy ? "ยืนยันทำลาย!" : "ทำลาย", dst))
        {
            if (confirmDestroy) DestroyItem(sel); else confirmDestroy = true;
        }
        if (confirmDestroy) ShadowLabel(new Rect(det.x + 8, by + 26, det.width - 16, 18), "ทำลายแล้วของจะหายไปถาวร กดอีกครั้งเพื่อยืนยัน", small, new Color(1f, 0.6f, 0.6f));
    }

    void EquipSlot(Rect r, string name, Item? it, System.Action takeOff)
    {
        GUI.Box(r, GUIContent.none);
        ShadowLabel(new Rect(r.x + 4, r.y + 6, 44, 18), name, small, new Color(1f, 1f, 1f, 0.7f));
        if (it == null) { ShadowLabel(new Rect(r.x + 46, r.y + 6, r.width - 50, 18), "-", small, new Color(1f, 1f, 1f, 0.4f)); return; }
        Icon(new Rect(r.x + 46, r.y + 3, 24, 24), PixelArt.Icon(it.Value));
        ShadowLabel(new Rect(r.x + 74, r.y + 6, r.width - 76, 18), Name(it.Value), small);
        if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { takeOff(); }
    }
}
