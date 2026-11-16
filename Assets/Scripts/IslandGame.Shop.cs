using System.Collections.Generic;
using UnityEngine;

/// The private island's market town: four shopkeepers at their stalls around the fountain.
/// Money is gold coins (Item.GoldCoin, the same coins pirates drop).
public partial class IslandGame
{
    class Ware
    {
        public string name;
        public Item? item;      // goods, or
        public NodeKind? animal; // a farm animal, let out next to you, or
        public int? pet;         // a pet (IslandGame.Pet.cs)
        public int n = 1, price;
    }

    static readonly Ware[] FarmWares =
    {
        new Ware { item = Item.CarrotSeed, n = 3, price = 5 },
        new Ware { item = Item.TomatoSeed, n = 3, price = 8 },
        new Ware { item = Item.WheatSeed, n = 5, price = 4 },
        new Ware { item = Item.WateringCan, price = 15 },
        new Ware { item = Item.Hoe, price = 15 },
        new Ware { name = "ไก่ 1 ตัว", animal = NodeKind.Chicken, price = 25 },
        new Ware { name = "วัว 1 ตัว", animal = NodeKind.Cow, price = 70 },
    };
    static readonly Ware[] BuildWares =
    {
        new Ware { item = Item.Wood, n = 5, price = 8 },
        new Ware { item = Item.Stone, n = 5, price = 6 },
        new Ware { item = Item.Plank, n = 4, price = 10 },
        new Ware { item = Item.Rope, n = 3, price = 6 },
        new Ware { item = Item.Fiber, n = 5, price = 3 },
        new Ware { item = Item.Hammer, price = 10 },
    };
    static readonly Ware[] PetWares =
    {
        new Ware { name = "ลูกหมา 1 ตัว", pet = 0, price = 40 },
        new Ware { name = "ลูกแมว 1 ตัว", pet = 1, price = 40 },
        new Ware { name = "กระต่าย 1 ตัว", pet = 2, price = 30 },
        new Ware { name = "นกแก้ว 1 ตัว", pet = 3, price = 60 },
        new Ware { item = Item.PetNet, n = 2, price = 8 },
    };
    static readonly Ware[] FishWares =
    {
        new Ware { item = Item.FishingRod, price = 12 },
        new Ware { item = Item.WaterFlask, price = 10 },
        new Ware { item = Item.CookedFish, n = 2, price = 8 },
    };

    /// What the buyer pays for one (0 = not bought).
    public static int SellPrice(Item it) => it switch
    {
        Item.Carrot => 4, Item.Tomato => 6, Item.Wheat => 2, Item.Egg => 5, Item.Milk => 12,
        Item.CookedEgg => 8, Item.Salad => 18,
        Item.RawFish => 3, Item.CookedFish => 5, Item.Tuna => 14, Item.GoldenFish => 60,
        Item.Berry => 1, Item.Fruit => 2, Item.CookedFruit => 3, Item.RawMeat => 3, Item.CookedMeat => 5, Item.CrabMeat => 2, Item.CookedCrab => 4,
        Item.Wood => 1, Item.Stone => 1, Item.Plank => 2, Item.Bamboo => 2, Item.Hide => 4, Item.Cloth => 3, Item.Honey => 6,
        Item.Pearl => 15, Item.Coral => 5, Item.ShellShard => 3, Item.Feather => 3, Item.CrystalShard => 6, Item.Copper => 5,
        Item.Gear => 4, Item.IceShard => 3, Item.Fur => 4, Item.AncientRelic => 25, Item.Obsidian => 4, Item.Chitin => 3, Item.Nectar => 3,
        _ => 0,
    };

    static bool IsSeafood(Item it) => it is Item.RawFish or Item.CookedFish or Item.Tuna or Item.GoldenFish or Item.CrabMeat or Item.CookedCrab or Item.Pearl or Item.Coral or Item.ShellShard;

    int shopPage;
    bool shopSelling, sellAll;

    /// The shopkeepers, one per stall, and a couple of townsfolk strolling around the fountain.
    void SpawnHomeFolk()
    {
        var stalls = nodes.FindAll(n => n != null && n.kind == NodeKind.Stall && !n.built);
        stalls.Sort((a, b) => a.id.CompareTo(b.id));
        var shops = new[] { (VillagerRole.FarmShop, "ป้าบุญมี", 9), (VillagerRole.BuildShop, "ลุงทองดี", 4), (VillagerRole.Buyer, "เจ๊กิมหงวน", 17), (VillagerRole.Fisher, "น้าสายลม", 11) };
        for (int i = 0; i < shops.Length; i++)
        {
            Vector2 at = i < stalls.Count ? stalls[i].grid + (TownCenter - stalls[i].grid).normalized * 1.15f : TownCenter + new Vector2(i - 1.5f, 1.5f);
            var v = new GameObject("Shopkeeper").AddComponent<Villager>();
            v.transform.SetParent(world);
            v.Init(shops[i].Item1, shops[i].Item2, shops[i].Item3, WalkableNear(at));
            villagers.Add(v);
        }
        var pk = new GameObject("Shopkeeper").AddComponent<Villager>(); // the pet shop, by the fountain
        pk.transform.SetParent(world);
        pk.Init(VillagerRole.PetShop, "พี่ปุยฝ้าย", 20, WalkableNear(TownCenter + new Vector2(-1.7f, 2.3f)));
        villagers.Add(pk);
        for (int i = 0; i < 2; i++)
        {
            var v = new GameObject("Townsfolk").AddComponent<Villager>();
            v.transform.SetParent(world);
            v.Init(VillagerRole.Villager, i == 0 ? "มะลิ" : "ต้นกล้า", 10 - i, WalkableNear(TownCenter + new Vector2(-1.5f + 3f * i, 1.2f)));
            villagers.Add(v);
        }
    }

    static string WareName(Ware w) => w.name ?? (w.n > 1 ? $"{Name(w.item.Value)} x{w.n}" : Name(w.item.Value));

    void ShopDialog(Villager v, Dialog d, string text)
    {
        if (text == null) { shopPage = 0; shopSelling = v.role == VillagerRole.Buyer; } // just walked up
        int coins = Count(Item.GoldCoin);
        string greet = v.role switch
        {
            VillagerRole.FarmShop => "เมล็ดพันธุ์สดๆ ไก่ วัว มีครบจ้า ปลูกเยอะๆ แล้วเอาผลผลิตไปขายร้านเจ๊ได้เลย",
            VillagerRole.BuildShop => "อยากสร้างบ้าน รั้ว หรือม้านั่ง? ไม้กับหินลุงมีให้ทั้งหมด",
            VillagerRole.Fisher => "ทะเลรอบเกาะนี้ปลาชุม! มีเบ็ดสักคันแล้วไปยืนริมหาดดูสิ",
            VillagerRole.PetShop => "น้องๆ ทุกตัวขี้อ้อนมากเลยนะ! ตัวที่พาไปด้วยจะเดินตามไปทุกเกาะ และคาบของมาให้ด้วย อยากได้สัตว์ป่าก็ใช้ตาข่ายจับเอาเองได้",
            _ => "ผัก ไข่ นม ปลา ของจากเกาะไกลๆ เจ๊รับซื้อหมด ราคายุติธรรม!",
        };
        d.text = $"{text ?? greet}\n<color=#ffd84d>เหรียญทองของคุณ: {coins}</color>";
        if (shopSelling) { SellOptions(v, d); return; }

        var wares = v.role switch { VillagerRole.FarmShop => FarmWares, VillagerRole.BuildShop => BuildWares, VillagerRole.PetShop => PetWares, _ => FishWares };
        foreach (var ware in wares)
        {
            var w = ware;
            bool owned = w.item != null && IsTool(w.item.Value) && Count(w.item.Value) > 0;
            d.options.Add(owned ? ($"{WareName(w)} (มีแล้ว)", () => { }, false)
                                : ($"ซื้อ{WareName(w)} - {w.price} เหรียญ", () => Buy(v, w), coins >= w.price));
        }
        if (v.role == VillagerRole.Fisher) d.options.Add(("ขายปลาและของทะเล", () => { shopSelling = true; shopPage = 0; OpenDialog(v, "เอาอะไรมาขายล่ะ?"); }, true));
    }

    void Buy(Villager v, Ware w)
    {
        if (Count(Item.GoldCoin) < w.price) return;
        if (w.pet != null && pets.Count >= MaxPets) { OpenDialog(v, $"โอ๊ะ มีสัตว์เลี้ยงครบ {MaxPets} ตัวแล้วนี่ ดูแลไม่ไหวหรอก"); return; }
        inv[Item.GoldCoin] -= w.price;
        if (w.pet != null)
        {
            var p = AddPet(w.pet.Value);
            Fx.Burst(PlayerPos + Vector3.up * 0.4f, new Color(1f, 0.45f, 0.55f), 12, 0.8f, 1.2f, 1f, 2f);
            Sfx.Play("craft", 0.5f);
            Hint($"ได้{PetSpecies(p.code)}ชื่อ \"{p.name}\"! " + (ActivePetSave == p ? "มันจะเดินตามคุณไปทุกที่" : "มันไปเล่นอยู่แถวบ้านแล้ว") + " (กด P เพื่อตั้งชื่อ)", 5f);
            OpenDialog(v, $"ฝาก{p.name}ด้วยนะ เลี้ยงให้ดีๆ ล่ะ!");
            SaveGame();
            return;
        }
        if (w.animal != null)
        {
            var g = WalkableNear(player.grid + player.facing.normalized * 1.2f);
            var n = Spawn(w.animal.Value, g, false);
            Fx.Burst(n.transform.position + Vector3.up * 0.3f, new Color(1f, 0.85f, 0.5f), 10, 0.8f, 1f, 1f, 2f);
            Hint($"ได้{w.name}! สัตว์จะเดินเล่นแถวที่ปล่อย ทำรั้วล้อมคอกไว้ได้ (G)", 4f);
        }
        else Give(w.item.Value, w.n, null);
        Sfx.Play("craft", 0.5f);
        OpenDialog(v, $"ขอบใจจ้า! ได้{WareName(w)}แล้ว");
    }

    void SellOptions(Villager v, Dialog d)
    {
        var list = new List<Item>();
        foreach (Item it in System.Enum.GetValues(typeof(Item)))
            if (Count(it) > 0 && SellPrice(it) > 0 && (v.role != VillagerRole.Fisher || IsSeafood(it))) list.Add(it);
        list.Sort((a, b) => (SellPrice(b) * Count(b)).CompareTo(SellPrice(a) * Count(a)));
        if (list.Count == 0) d.text += "\n(ไม่มีของที่ขายได้ในกระเป๋า)";
        const int perPage = 6;
        shopPage = Mathf.Clamp(shopPage, 0, Mathf.Max(0, (list.Count - 1) / perPage));
        for (int i = shopPage * perPage; i < list.Count && i < (shopPage + 1) * perPage; i++)
        {
            var it = list[i];
            int n = sellAll ? Count(it) : 1;
            d.options.Add(($"ขาย{Name(it)} x{n} ได้ {n * SellPrice(it)} เหรียญ  (มี {Count(it)}, ชิ้นละ {SellPrice(it)})", () => Sell(v, it, n), true));
        }
        d.options.Add((sellAll ? "โหมด: ขายทั้งหมด (กดเพื่อขายทีละชิ้น)" : "โหมด: ขายทีละชิ้น (กดเพื่อขายทั้งหมด)", () => { sellAll = !sellAll; OpenDialog(v, "เปลี่ยนวิธีขายแล้ว"); }, true));
        if (shopPage > 0) d.options.Add(("◀ หน้าก่อน", () => { shopPage--; OpenDialog(v, "เลือกของที่จะขาย"); }, true));
        if ((shopPage + 1) * perPage < list.Count) d.options.Add(("หน้าถัดไป ▶", () => { shopPage++; OpenDialog(v, "เลือกของที่จะขาย"); }, true));
        if (v.role == VillagerRole.Fisher) d.options.Add(("ดูของที่ขาย", () => { shopSelling = false; OpenDialog(v, "เลือกดูได้เลย"); }, true));
    }

    void Sell(Villager v, Item it, int n)
    {
        n = Mathf.Min(n, Count(it));
        if (n <= 0) return;
        int got = n * SellPrice(it);
        inv[it] -= n;
        UnequipIfGone(it);
        inv[Item.GoldCoin] = Count(Item.GoldCoin) + got;
        Popup($"+{got} เหรียญทอง", PlayerPos + Vector3.up * 1f, new Color(1f, 0.85f, 0.3f));
        Sfx.Play("pickup", 0.5f);
        OpenDialog(v, $"ขาย{Name(it)} {n} ชิ้น ได้ {got} เหรียญ");
    }
}
