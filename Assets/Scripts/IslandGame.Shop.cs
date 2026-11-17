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
        new Ware { name = "ปลาทองในโหล 1 ตัว", pet = 4, price = 35 },
        new Ware { item = Item.PetNet, n = 2, price = 8 },
    };
    static readonly Ware[] FishWares =
    {
        new Ware { item = Item.FishingRod, price = 12 },
        new Ware { item = Item.WaterFlask, price = 10 },
        new Ware { item = Item.CookedFish, n = 2, price = 12 }, // 6 each: more than the 5 she pays back for one
    };

    /// What the buyer pays for one (0 = not bought).
    public static int SellPrice(Item it) => it switch
    {
        Item.Carrot => 4, Item.Tomato => 6, Item.Wheat => 2, Item.Egg => 5, Item.Milk => 12,
        Item.CookedEgg => 8, Item.Salad => 18,
        Item.RawFish => 3, Item.CookedFish => 5, Item.Tuna => 14, Item.GoldenFish => 60,
        Item.Berry => 1, Item.Fruit => 2, Item.CookedFruit => 3, Item.RawMeat => 3, Item.CookedMeat => 5, Item.CrabMeat => 2, Item.CookedCrab => 4,
        Item.Wood => 1, Item.Stone => 1, Item.Plank => 1, // 2 wood make 2 planks: a plank can't be worth more than the wood bought for it Item.Bamboo => 2, Item.Hide => 4, Item.Cloth => 3, Item.Honey => 6,
        Item.Pearl => 15, Item.Coral => 5, Item.ShellShard => 3, Item.Feather => 3, Item.CrystalShard => 6, Item.Copper => 5,
        Item.Gear => 4, Item.IceShard => 3, Item.Fur => 4, Item.AncientRelic => 25, Item.Obsidian => 4, Item.Chitin => 3, Item.Nectar => 3,
        _ => 0,
    };

    static bool IsSeafood(Item it) => it is Item.RawFish or Item.CookedFish or Item.Tuna or Item.GoldenFish or Item.CrabMeat or Item.CookedCrab or Item.Pearl or Item.Coral or Item.ShellShard;


    bool shopSelling;

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
    static Sprite WareIcon(Ware w) => w.pet != null ? PixelArt.Pet(w.pet.Value, 0)
                                    : w.animal == NodeKind.Cow ? PixelArt.Cow(0)
                                    : w.animal != null ? PixelArt.Chicken(0)
                                    : PixelArt.Icon(w.item.Value);
    /// Goods you can buy several of at once (not tools, animals or pets).
    static bool Stackable(Ware w) => w.item != null && !IsTool(w.item.Value);

    static bool ShopSells(VillagerRole r) => r != VillagerRole.Buyer;                          // has a "buy" tab
    static bool ShopBuys(VillagerRole r) => r is VillagerRole.Buyer or VillagerRole.Fisher;    // has a "sell" tab
    static Ware[] WaresOf(VillagerRole r) => r switch { VillagerRole.FarmShop => FarmWares, VillagerRole.BuildShop => BuildWares, VillagerRole.PetShop => PetWares, _ => FishWares };
    static readonly Item[] Produce = { Item.Carrot, Item.Tomato, Item.Egg, Item.Milk };

    Vector2 shopScroll;

    /// Walking up to a stall opens the shop window (drawn by DrawShop); `text` is the keeper's latest line.
    void ShopDialog(Villager v, Dialog d, string text)
    {
        if (text == null) { shopSelling = !ShopSells(v.role); shopScroll = Vector2.zero; } // just walked up
        d.shop = v;
        d.text = text ?? v.role switch
        {
            VillagerRole.FarmShop => "เมล็ดพันธุ์สดๆ ไก่ วัว มีครบจ้า ปลูกเยอะๆ แล้วเอาผลผลิตไปขายร้านเจ๊ได้เลย",
            VillagerRole.BuildShop => "อยากสร้างบ้าน รั้ว หรือม้านั่ง? ไม้กับหินลุงมีให้ทั้งหมด",
            VillagerRole.Fisher => "ทะเลรอบเกาะนี้ปลาชุม! มีเบ็ดสักคันแล้วไปยืนริมหาดดูสิ จะขายปลาก็กดแท็บ \"ขายของ\" ได้เลย",
            VillagerRole.PetShop => "น้องๆ ทุกตัวขี้อ้อนมากเลยนะ! ตัวที่พาไปด้วยจะเดินตามไปทุกเกาะ และคาบของมาให้ด้วย อยากได้สัตว์ป่าก็ใช้ตาข่ายจับเอาเองได้",
            _ => "ผัก ไข่ นม ปลา ของจากเกาะไกลๆ เจ๊รับซื้อหมด ราคายุติธรรม!",
        };
    }

    /// Buy `times` lots of a ware at once.
    void Buy(Villager v, Ware w, int times = 1)
    {
        if (!Stackable(w)) times = 1;
        int cost = w.price * times;
        if (times <= 0 || Count(Item.GoldCoin) < cost) return;
        if (w.pet != null && pets.Count >= MaxPets) { OpenDialog(v, $"โอ๊ะ มีสัตว์เลี้ยงครบ {MaxPets} ตัวแล้วนี่ ดูแลไม่ไหวหรอก"); return; }
        inv[Item.GoldCoin] -= cost;
        Popup($"-{cost} เหรียญทอง", PlayerPos + Vector3.up * 1f, new Color(1f, 0.75f, 0.3f));
        if (w.pet != null)
        {
            var p = AddPet(w.pet.Value);
            Fx.Burst(PlayerPos + Vector3.up * 0.4f, new Color(1f, 0.45f, 0.55f), 12, 0.8f, 1.2f, 1f, 2f);
            Sfx.Play("craft", 0.5f);
            Hint($"ได้{PetSpecies(p.code)}ชื่อ \"{p.name}\"! " + (ActivePetSave == p ? "มันจะเดินตามคุณไปทุกที่" : "มันไปเล่นอยู่แถวบ้านแล้ว") + $" ({KeyOr("กด P", "เมนู ☰ > สัตว์เลี้ยง")} เพื่อตั้งชื่อ)", 5f);
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
        else Give(w.item.Value, w.n * times, null);
        Sfx.Play("craft", 0.5f);
        OpenDialog(v, times > 1 ? $"ขอบใจจ้า! ได้{Name(w.item.Value)} {w.n * times} ชิ้น จ่ายไป {cost} เหรียญ" : $"ขอบใจจ้า! ได้{WareName(w)}แล้ว");
    }

    /// What this stall will buy from your bag, in a steady order (rows don't jump around while you sell).
    List<Item> SellList(Villager v)
    {
        var list = new List<Item>();
        foreach (Item it in System.Enum.GetValues(typeof(Item)))
            if (Count(it) > 0 && SellPrice(it) > 0 && (v.role != VillagerRole.Fisher || IsSeafood(it))) list.Add(it);
        return list;
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

    /// Sell every farm crop, egg and milk in one go (the buyer only).
    void SellProduce(Villager v)
    {
        int got = 0, pieces = 0;
        foreach (var it in Produce)
        {
            int n = Count(it);
            if (n <= 0) continue;
            got += n * SellPrice(it); pieces += n;
            inv[it] = 0;
        }
        if (pieces == 0) return;
        inv[Item.GoldCoin] = Count(Item.GoldCoin) + got;
        Popup($"+{got} เหรียญทอง", PlayerPos + Vector3.up * 1f, new Color(1f, 0.85f, 0.3f));
        Sfx.Play("pickup", 0.5f);
        OpenDialog(v, $"ขายผลผลิต {pieces} ชิ้น ได้ {got} เหรียญ ขอบใจจ้า!");
    }

    // ------------------------------------------------------------------ the shop window
    void DrawShop(float W, float H, Dialog d)
    {
        var v = d.shop;
        if (v == null) { CloseDialog(); return; }
        int coins = Count(Item.GoldCoin);
        float pw = Mathf.Min(600f, W - 16f);
        // below the top-right HUD buttons (home / build, IslandGame.Home.cs) and above the hotbar
        float ph = Mathf.Min(420f, H - 80f - 92f);
        var box = new Rect(W / 2f - pw / 2f, Mathf.Max(80f, (H - ph) / 2f - 30f), pw, ph);
        Fill(box, new Color(0.08f, 0.06f, 0.04f, 0.97f));
        Fill(new Rect(box.x, box.y, box.width, 3), new Color(1f, 0.85f, 0.3f, 0.9f));
        var wrap = new GUIStyle(small) { wordWrap = true, fontSize = 12, richText = true };
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 12, richText = true };

        // header: who, your coins, close
        ShadowLabel(new Rect(box.x + 14, box.y + 8, pw - 200, 22), v.Title, label, new Color(1f, 0.85f, 0.3f));
        Icon(new Rect(box.xMax - 178, box.y + 8, 20, 20), PixelArt.Icon(Item.GoldCoin));
        ShadowLabel(new Rect(box.xMax - 154, box.y + 8, 80, 22), $"{coins}", label, new Color(1f, 0.85f, 0.3f));
        if (CrispGUI.Button(new Rect(box.xMax - 72, box.y + 6, 62, 24), "ปิด (E)", btn)) { CloseDialog(); return; }
        float textH = Mathf.Min(48f, wrap.CalcHeight(new GUIContent(d.text), pw - 28f));
        CrispGUI.Label(new Rect(box.x + 14, box.y + 32, pw - 28, textH), $"<color=#f0e6d0>{d.text}</color>", wrap);
        float y = box.y + 36f + textH;

        // buy / sell tabs
        bool canBuy = ShopSells(v.role), canSell = ShopBuys(v.role);
        if (!canBuy) shopSelling = true;
        if (!canSell) shopSelling = false;
        if (canBuy && canSell)
        {
            float tw = (pw - 28f) / 2f;
            for (int t = 0; t < 2; t++)
            {
                bool on = (t == 1) == shopSelling;
                string name = t == 0 ? "ซื้อของ" : "ขายของ";
                var r = new Rect(box.x + 14 + t * tw, y, tw - 4, 28);
                if (on) Fill(r, new Color(1f, 0.85f, 0.3f, 0.35f));
                if (CrispGUI.Button(r, on ? $"<b>{name}</b>" : name, btn)) { shopSelling = t == 1; shopScroll = Vector2.zero; }
            }
            y += 32f;
        }

        // the buyer's one-tap "sell all produce"
        if (shopSelling && v.role == VillagerRole.Buyer)
        {
            int total = 0, pieces = 0;
            foreach (var it in Produce) { total += Count(it) * SellPrice(it); pieces += Count(it); }
            GUI.enabled = pieces > 0;
            bool click = CrispGUI.Button(new Rect(box.x + 14, y, pw - 28, 26),
                pieces > 0 ? $"ขายผัก ไข่ นม ทั้งหมด ({pieces} ชิ้น) ได้ <color=#ffd84d>{total} เหรียญ</color>" : "ขายผัก ไข่ นม ทั้งหมด (ไม่มีในกระเป๋า)", btn);
            GUI.enabled = true;
            if (click) { SellProduce(v); return; }
            y += 30f;
        }

        const float footH = 22f, rowH = 46f;
        var view = new Rect(box.x + 8, y, pw - 16, box.yMax - y - footH - 4);
        if (shopSelling) DrawSellRows(v, view, rowH, btn);
        else DrawBuyRows(v, view, rowH, btn, coins);

        string tip = shopSelling ? "ราคาที่ร้านรับซื้อต่อชิ้น · กด \"ขายหมด\" เพื่อขายทั้งกอง"
                   : canSell ? "ราคาแดง = เหรียญไม่พอ · x5 ซื้อทีเดียว 5 ชุด"
                   : "ราคาแดง = เหรียญไม่พอ · x5 ซื้อทีเดียว 5 ชุด · ขายของได้ที่ร้านเจ๊กิมหงวน";
        CrispGUI.Label(new Rect(box.x + 14, box.yMax - footH, pw - 28, footH - 2), $"<color=#aaaaaa>{tip}</color>", small);
    }

    void DrawBuyRows(Villager v, Rect view, float rowH, GUIStyle btn, int coins)
    {
        var wares = WaresOf(v.role);
        var content = new Rect(0, 0, view.width - 18, wares.Length * (rowH + 4f));
        shopScroll = CrispGUI.BeginScrollView(view, shopScroll, content, false, false);
        for (int i = 0; i < wares.Length; i++)
        {
            var w = wares[i];
            var row = new Rect(2, i * (rowH + 4f), content.width - 4, rowH);
            GUI.Box(row, GUIContent.none);
            Icon(new Rect(row.x + 6, row.y + 7, 32, 32), WareIcon(w));
            ShadowLabel(new Rect(row.x + 46, row.y + 3, 260, 20), WareName(w), label);
            bool owned = w.item != null && IsTool(w.item.Value) && Count(w.item.Value) > 0;
            bool full = w.pet != null && pets.Count >= MaxPets;
            string have = w.pet != null ? $"สัตว์เลี้ยง {pets.Count}/{MaxPets}" : w.animal != null ? "ปล่อยไว้ข้างตัวคุณ" : $"มีอยู่ {Count(w.item.Value)}";
            string priceCol = coins >= w.price ? "#ffd84d" : "#ff8a8a";
            CrispGUI.Label(new Rect(row.x + 46, row.y + 23, 260, 18), $"<color={priceCol}>{w.price} เหรียญ</color>  <color=#bbbbbb>· {have}</color>", small);

            float bx = row.xMax - 6;
            if (owned || full)
            {
                GUI.enabled = false;
                CrispGUI.Button(new Rect(bx - 96, row.y + 9, 96, 28), owned ? "มีแล้ว" : "เต็มแล้ว", btn);
                GUI.enabled = true;
                continue;
            }
            bool bought = false;
            if (Stackable(w))
            {
                int five = w.price * 5;
                GUI.enabled = coins >= five;
                if (CrispGUI.Button(new Rect(bx - 86, row.y + 9, 86, 28), $"x5 ({five})", btn)) { Buy(v, w, 5); bought = true; }
                bx -= 92;
            }
            GUI.enabled = coins >= w.price;
            if (!bought && CrispGUI.Button(new Rect(bx - 86, row.y + 9, 86, 28), $"ซื้อ ({w.price})", btn)) { Buy(v, w); bought = true; }
            GUI.enabled = true;
            if (bought) break;
        }
        CrispGUI.EndScrollView();
    }

    void DrawSellRows(Villager v, Rect view, float rowH, GUIStyle btn)
    {
        var list = SellList(v);
        var content = new Rect(0, 0, view.width - 18, Mathf.Max(30f, list.Count * (rowH + 4f)));
        shopScroll = CrispGUI.BeginScrollView(view, shopScroll, content, false, false);
        if (list.Count == 0)
            CrispGUI.Label(new Rect(8, 8, content.width - 16, 20), v.role == VillagerRole.Fisher ? "<color=#cccccc>ไม่มีปลาหรือของทะเลในกระเป๋า</color>" : "<color=#cccccc>ไม่มีของที่ขายได้ในกระเป๋า</color>", small);
        for (int i = 0; i < list.Count; i++)
        {
            var it = list[i];
            int have = Count(it), price = SellPrice(it);
            var row = new Rect(2, i * (rowH + 4f), content.width - 4, rowH);
            GUI.Box(row, GUIContent.none);
            Icon(new Rect(row.x + 6, row.y + 7, 32, 32), PixelArt.Icon(it));
            ShadowLabel(new Rect(row.x + 46, row.y + 3, 220, 20), $"{Name(it)}  <size=11><color=#bbbbbb>มี {have}</color></size>", label);
            CrispGUI.Label(new Rect(row.x + 46, row.y + 23, 220, 18), $"<color=#ffd84d>ชิ้นละ {price} เหรียญ</color>", small);

            float bx = row.xMax - 6;
            int sell = 0;
            if (CrispGUI.Button(new Rect(bx - 116, row.y + 9, 116, 28), $"ขายหมด (+{have * price})", btn)) sell = have;
            bx -= 122;
            if (have >= 10) { if (CrispGUI.Button(new Rect(bx - 62, row.y + 9, 62, 28), "ขาย 10", btn)) sell = 10; bx -= 68; }
            if (have > 1 && CrispGUI.Button(new Rect(bx - 62, row.y + 9, 62, 28), "ขาย 1", btn)) sell = 1;
            if (sell > 0) { Sell(v, it, sell); break; }
        }
        CrispGUI.EndScrollView();
    }
}
