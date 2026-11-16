using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// Tribe Island: the village, its people, conversations, and clothing.
public partial class IslandGame
{
    public class Dialog
    {
        public string speaker, text;
        public readonly List<(string label, System.Action act, bool enabled)> options = new List<(string, System.Action, bool)>();
    }

    public readonly List<Villager> villagers = new List<Villager>();
    [System.NonSerialized] public Dialog dialog;
    Villager villagerTarget;

    /// What the weaver can teach, and the gift she asks for in return.
    static readonly (Item item, (Item item, int n)[] price)[] Lessons =
    {
        (Item.Cloth, new (Item, int)[0]),
        (Item.StrawHat, new (Item, int)[0]),
        (Item.LeafTunic, new[] { (Item.Fruit, 4) }),
        (Item.HideVest, new[] { (Item.CookedMeat, 2) }),
        (Item.TribalHeadband, new[] { (Item.RawFish, 3) }),
    };

    /// The trader's deals: give -> get.
    static readonly ((Item item, int n) give, (Item item, int n) get)[] Trades =
    {
        ((Item.RawFish, 3), (Item.Hide, 1)),
        ((Item.Fruit, 4), (Item.CookedMeat, 1)),
        ((Item.Fiber, 6), (Item.Rope, 2)),
        ((Item.Berry, 6), (Item.Cloth, 1)),
    };

    static readonly string[] Chatter =
    {
        "กองไฟช่วยไล่ปูตอนกลางคืน อยู่ใกล้หมู่บ้านไว้นะ!",
        "นาลาทอผ้าได้สวยที่สุดในทุกเกาะเลย",
        "ตาทามูบอกว่าทะเลหลังภูเขาไฟดุที่สุด",
        "พ่อของข้าแทงปลาด้วยหอกจนแก่เฒ่าเลยล่ะ",
        "ใบเรือผ้าทอทำให้แพแล่นฉิวเหนือคลื่นเลย!",
        "เคยกินหมูป่าย่างไหม? อร่อยที่สุดแล้ว",
    };

    // ------------------------------------------------------------------ village
    void BuildVillage()
    {
        Vector2 c = Center;
        foreach (var n in nodes.FindAll(n => (n.grid - c).magnitude < 4.6f)) RemoveNode(n);
        Spawn(NodeKind.Campfire, c, false); // the village fire: cooking, light, and enemies keep away
        Vector2[] huts = { c + new Vector2(2.8f, -0.8f), c + new Vector2(-0.8f, 2.8f), c + new Vector2(-2.6f, -1.8f) };
        foreach (var h in huts) if (IsLand(h)) Spawn(NodeKind.Hut, h, rnd.Next(2) == 0);
        Spawn(NodeKind.Totem, c + new Vector2(1.7f, 1.7f), false);

        stickCap = rockCap = obsidianCap = 0;
        foreach (var n in nodes)
        {
            if (n.kind == NodeKind.Stick) stickCap++;
            else if (n.kind == NodeKind.Rock) rockCap++;
        }
    }

    void SpawnVillagers()
    {
        var huts = nodes.FindAll(n => n.kind == NodeKind.Hut);
        Vector2 Near(int i, Vector2 fallback)
        {
            Vector2 home = i < huts.Count ? huts[i].grid + (Center - huts[i].grid).normalized * 1.3f : fallback;
            for (int t = 0; t < 12 && !CanWalk(home, 0.15f); t++) home = Center + Random.insideUnitCircle * 2.5f;
            return home;
        }
        AddVillager(VillagerRole.Weaver, "Nala", 0, Near(0, Center + new Vector2(1.5f, -1f)));
        AddVillager(VillagerRole.Elder, "Tamu", 1, Near(99, Center + new Vector2(-1.2f, 0.6f)));
        AddVillager(VillagerRole.Trader, "Kai", 2, Near(1, Center + new Vector2(-1f, 1.6f)));
        AddVillager(VillagerRole.Villager, "Mele", 3, Near(2, Center + new Vector2(-1.8f, -1.2f)));
        AddVillager(VillagerRole.Villager, "Pua", 3, Near(99, Center + new Vector2(0.8f, -1.8f)));
    }

    void AddVillager(VillagerRole role, string name, int look, Vector2 home)
    {
        var v = new GameObject(name).AddComponent<Villager>();
        v.transform.SetParent(world);
        v.Init(role, name, look, home);
        villagers.Add(v);
    }

    /// The weaver has something new to teach (shown as a "!" over her head).
    public bool WeaverHasLessons
    {
        get
        {
            foreach (var l in Lessons) if (!learned.Contains(l.item)) return true;
            return false;
        }
    }

    // ------------------------------------------------------------------ conversations
    void OpenDialog(Villager v, string text = null)
    {
        villagerTarget = v;
        craftOpen = false; skillsOpen = false;
        var d = new Dialog { speaker = v.Title };
        if (QuestDialog(v, d, text)) // shipwright, doctor, lost child
        {
            d.options.Add(("ลาก่อน", CloseDialog, true));
            dialog = d;
            return;
        }
        switch (v.role)
        {
            case VillagerRole.Weaver:
                bool any = WeaverHasLessons;
                d.text = text ?? (learned.Count == 0
                    ? "ยินดีต้อนรับ นักเดินทาง! ข้าคือนาลา ช่างทอผ้า เสื้อผ้าเจ้าขาดรุ่งริ่งหมดแล้ว ให้ข้าสอนทำชุดใหม่สิ แค่นำของขวัญเล็กๆ มาให้ ข้าจะสอนให้"
                    : any ? "อยากเรียนอะไรต่อดีล่ะ?" : "ข้าสอนทุกอย่างที่รู้ให้หมดแล้ว สวมชุดของเจ้าอย่างภาคภูมิใจนะ!");
                foreach (var lesson in Lessons)
                {
                    if (learned.Contains(lesson.item)) continue;
                    var l = lesson;
                    d.options.Add(($"เรียนทำ{Name(l.item)} ({PriceText(l.price)})", () => Learn(v, l.item, l.price), CanPay(l.price)));
                }
                break;

            case VillagerRole.Elder:
                d.text = text ?? (raftLevel < island
                    ? "อา นักเดินเรือ ยิ่งไปไกลทะเลยิ่งดุ ทอผ้าทำใบเรือสิ แพที่มีใบเรือผ้าทอจะแล่นฝ่าคลื่นแรงได้"
                    : "แพของเจ้าแข็งแรงดี ไปเถิด ขอให้คลื่นลมเป็นใจ");
                bool canHeal = elderHealDay != Day;
                d.options.Add((canHeal ? "ขอให้รักษา" : "ขอให้รักษา (พรุ่งนี้)", () => ElderHeal(v), canHeal));
                d.options.Add(("ถามเรื่องหมู่เกาะ", () => OpenDialog(v,
                    "ผู้เฒ่าเล่าว่ามีสี่เกาะ: หาดเขียวขจี ป่างู บ้านของเรา และภูเขาไฟที่หมูป่าเดินเพ่นพ่าน ไกลออกไป... ก็เป็นเกาะแบบเดิม แต่ดุร้ายกว่า"), true));
                break;

            case VillagerRole.Trader:
                d.text = text ?? "ปลาแลกหนัง ผลไม้แลกเนื้อ! ไคแลกของยุติธรรมเสมอ";
                foreach (var trade in Trades)
                {
                    var t = trade;
                    d.options.Add(($"ให้{Name(t.give.item)} {t.give.n} -> ได้{Name(t.get.item)} {t.get.n}  (มี {Count(t.give.item)})",
                        () => Trade(v, t.give, t.get), Count(t.give.item) >= t.give.n));
                }
                break;

            case VillagerRole.FarmShop:
            case VillagerRole.BuildShop:
            case VillagerRole.Buyer:
            case VillagerRole.PetShop:
            case VillagerRole.Fisher:
                ShopDialog(v, d, text); // the private island's town (IslandGame.Shop.cs)
                break;

            default:
                var lines = biome == Biome.Tribal ? Chatter : TownChatter;
                d.text = text ?? lines[Random.Range(0, lines.Length)];
                break;
        }
        d.options.Add(("ลาก่อน", CloseDialog, true));
        dialog = d;
    }

    void CloseDialog() { dialog = null; renamePet = -1; lastInteract = Time.time; }

    bool CanPay((Item item, int n)[] price)
    {
        foreach (var p in price) if (Count(p.item) < p.n) return false;
        return true;
    }

    static string PriceText((Item item, int n)[] price)
    {
        if (price.Length == 0) return "ฟรี";
        var parts = new List<string>();
        foreach (var p in price) parts.Add($"{Name(p.item)} {p.n}");
        return "ของขวัญ: " + string.Join(", ", parts);
    }

    void Learn(Villager v, Item item, (Item item, int n)[] price)
    {
        if (!CanPay(price)) return;
        foreach (var p in price) inv[p.item] -= p.n;
        learned.Add(item);
        Sfx.Play("levelup", 0.5f);
        Popup($"เรียนรู้แล้ว: {Name(item)}!", PlayerPos + Vector3.up * 1f, new Color(1f, 0.85f, 0.3f));
        OpenDialog(v, $"ดูให้ดีนะ... แบบนี้! ตอนนี้เจ้าทำ{Name(item)}ได้แล้ว กด Tab เพื่อประดิษฐ์ แล้วคลิกในกระเป๋าเพื่อสวม");
    }

    void ElderHeal(Villager v)
    {
        elderHealDay = Day;
        health = MaxHealth;
        hunger = Mathf.Min(100f, hunger + 20f);
        thirst = Mathf.Min(100f, thirst + 20f);
        Sfx.Play("drink", 0.6f);
        Fx.Burst(PlayerPos + Vector3.up * 0.4f, new Color(0.6f, 1f, 0.6f), 16, 1f, 1.6f, 1f, 2f);
        Popup("หายดีแล้ว", PlayerPos + Vector3.up * 1f, new Color(0.6f, 1f, 0.6f));
        OpenDialog(v, "ดื่มนี่ แล้วพักข้างกองไฟ... นั่นไง แข็งแรงแล้ว ถ้าต้องการข้าอีก มาใหม่พรุ่งนี้นะ");
    }

    void Trade(Villager v, (Item item, int n) give, (Item item, int n) get)
    {
        if (Count(give.item) < give.n) return;
        inv[give.item] -= give.n;
        inv[get.item] = Count(get.item) + get.n;
        Sfx.Play("pickup", 0.5f);
        Popup($"+{get.n} {Name(get.item)}", PlayerPos + Vector3.up * 1f, new Color(1f, 0.95f, 0.6f));
        OpenDialog(v, "ยินดีที่ได้แลก! เอาอะไรอีกไหม?");
    }

    void HandleDialog(Keyboard kb)
    {
        if (dialog == null) return;
        if (renamePet >= 0) return; // typing a pet's name: keys go to the text box (IslandGame.Pet.cs)
        if (touchActionPressed) { CloseDialog(); return; } // the E button on a touch screen
        if (kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) { CloseDialog(); return; }
        for (int i = 0; i < dialog.options.Count && i < 9; i++)
            if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
            {
                var o = dialog.options[i];
                if (o.enabled) o.act();
                return;
            }
    }

    // ------------------------------------------------------------------ clothing
    /// Put on / take off a piece of clothing (one hat and one outfit at a time).
    public void ToggleWear(Item it)
    {
        if (!IsClothing(it) || Count(it) == 0) return;
        bool on;
        if (IsHat(it)) { on = hat != it; hat = on ? it : (Item?)null; }
        else { on = outfit != it; outfit = on ? it : (Item?)null; }
        health = Mathf.Min(health, MaxHealth);
        Sfx.Play("craft", 0.4f);
        Popup(on ? $"สวม{Name(it)}" : $"ถอด{Name(it)}", PlayerPos + Vector3.up * 1f, Color.white);
    }
}
