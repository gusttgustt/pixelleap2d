using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// Visiting other players' private islands (online). Every player online sends the server a copy
/// of their private island now and then (outside, the room in the house, the house level and the
/// pets at home). V lists who has one; pick a name and you sail over to their island and can walk
/// around, go into their house, meet their pets, use the town's shops and chat with them if
/// they're home. You can look but not touch: nothing on a friend's island can be picked, harvested
/// or built. H (or the button) takes you back to exactly where you were.
///
/// A visit is a private island loaded from the friend's copy with your own character and bag.
/// Your own game waits in visitBack (and in the save file, which isn't written during a visit);
/// what you buy or sell in their town comes home with you.
///
/// Rooms on the server: adventure islands are numbered 1, 2, 3...; a private island is
/// -(owner * 2) outside and -(owner * 2 + 1) inside the house, so the owner and their visitors
/// see each other.
public partial class IslandGame
{
    public bool visiting;
    bool visitLoad;                           // StartGame is loading a visit (so it doesn't end it)
    int visitOwner;
    string visitName = "";
    SaveData visitBack;                       // your own game, to go back to
    NetMsg visitData;                         // the friend's island, waiting for the fade
    readonly List<(int code, string name)> visitPets = new List<(int, string)>();
    float homeSendT = 5f;
    bool homesAsked;
    /// The server answered "homes": it knows private islands (rooms, visits). An older server
    /// doesn't, and then none of this is used (the game works as before, alone at home).
    bool serverHomes;

    int RoomKey => atHome ? -((visiting ? visitOwner : myId) * 2 + (inHouse ? 1 : 0)) : island;

    /// While visiting you can look around but only these do anything.
    static bool VisitAllowed(NodeKind k) => k is NodeKind.Signpost or NodeKind.Cottage or NodeKind.HouseDoor or NodeKind.Well or NodeKind.Fountain;

    // ------------------------------------------------------------------ sending your island
    void TickVisit(float dt)
    {
        if (!Online || visiting) return;
        homeSendT -= dt;
        if (homeSendT > 0f) return;
        homeSendT = atHome ? 45f : 120f;
        PublishHome();
    }

    void PublishHome()
    {
        if (!Online || !serverHomes || visiting || !homeMade) return;
        List<NodeSave> outside, room;
        if (atHome && !inHouse) { outside = new List<NodeSave>(); foreach (var n in nodes) if (n != null) outside.Add(ToSave(n)); }
        else outside = homeNodes;
        if (inHouse) { room = new List<NodeSave>(); foreach (var n in nodes) if (n != null && n.kind != NodeKind.HouseDoor) room.Add(ToSave(n)); }
        else room = houseNodes;
        var sb = new StringBuilder();
        for (int i = 0; i < pets.Count; i++)
            if (i != activePet) sb.Append(pets[i].code).Append(':').Append(pets[i].name).Append('|');
        Send(new NetMsg { t = "home", text = EncodeNodes(outside), room = EncodeNodes(room), lv = houseLevel, pets = sb.ToString() });
    }

    /// Compact text for a list of nodes: "kind,x,y,flags,hits,regrow,item,count;" (JSON is too big).
    static string EncodeNodes(List<NodeSave> list)
    {
        var sb = new StringBuilder(list.Count * 24);
        var ci = CultureInfo.InvariantCulture;
        foreach (var n in list)
        {
            if (n == null || n.kind == NodeKind.Drop) continue; // things lying on the ground stay private
            int flags = (n.available ? 1 : 0) | (n.flip ? 2 : 0) | (n.built ? 4 : 0);
            sb.Append((int)n.kind).Append(',')
              .Append(n.grid.x.ToString("0.##", ci)).Append(',').Append(n.grid.y.ToString("0.##", ci)).Append(',')
              .Append(flags).Append(',').Append(n.hits).Append(',').Append(Mathf.RoundToInt(n.regrow)).Append(',')
              .Append(n.item).Append(',').Append(n.count).Append(';');
        }
        return sb.ToString();
    }

    static List<NodeSave> DecodeNodes(string text)
    {
        var list = new List<NodeSave>();
        if (string.IsNullOrEmpty(text)) return list;
        var ci = CultureInfo.InvariantCulture;
        int kinds = System.Enum.GetValues(typeof(NodeKind)).Length;
        foreach (var part in text.Split(';'))
        {
            var f = part.Split(',');
            if (f.Length < 8) continue;
            try
            {
                int kind = int.Parse(f[0], ci), flags = int.Parse(f[3], ci);
                if (kind < 0 || kind >= kinds) continue;
                list.Add(new NodeSave
                {
                    kind = (NodeKind)kind,
                    grid = new Vector2(float.Parse(f[1], ci), float.Parse(f[2], ci)),
                    available = (flags & 1) != 0, flip = (flags & 2) != 0, built = (flags & 4) != 0,
                    hits = int.Parse(f[4], ci), regrow = int.Parse(f[5], ci),
                    item = int.Parse(f[6], ci), count = int.Parse(f[7], ci),
                });
            }
            catch { }
        }
        return list;
    }

    // ------------------------------------------------------------------ choosing whom to visit
    void AskHomes()
    {
        if (!Online) { Hint("การเยี่ยมบ้านเพื่อนต้องเล่นออนไลน์", 2.5f); return; }
        if (!serverHomes) { Hint("เซิร์ฟเวอร์ตอนนี้ยังเป็นเวอร์ชันเก่า ยังเยี่ยมบ้านไม่ได้ (เจ้าของเซิร์ฟเวอร์ต้องเปิดเซิร์ฟเวอร์ใหม่)", 4f); return; }
        if (title || dead || player == null || voyage != null || Busy || homeT > 0f) return;
        homesAsked = true;
        Send(new NetMsg { t = "homes" });
        Hint("กำลังดูว่าใครเปิดบ้านให้เยี่ยมบ้าง...", 1.5f);
    }

    void OnHomes(NetPeer[] list)
    {
        if (!serverHomes)
        {
            serverHomes = true;
            if (atHome && player != null && !inRoom) { JoinRoom(); homeSendT = Mathf.Min(homeSendT, 2f); } // got home before the answer came
        }
        ShowHomes(list);
    }

    void ShowHomes(NetPeer[] list)
    {
        if (!homesAsked || player == null || Busy) return;
        homesAsked = false;
        craftOpen = false; skillsOpen = false; bagOpen = false;
        var d = new Dialog { speaker = "เยี่ยมบ้านเพื่อน" };
        int n = list?.Length ?? 0;
        d.text = n == 0
            ? "ตอนนี้ยังไม่มีผู้เล่นคนอื่นที่ออนไลน์และมีเกาะส่วนตัวเลย ลองใหม่ทีหลังนะ"
            : "เลือกบ้านที่อยากไปเยี่ยม ไปดูฟาร์ม บ้าน และสัตว์เลี้ยงของเขาได้ (ดูได้อย่างเดียว เก็บหรือสร้างอะไรไม่ได้)";
        for (int i = 0; i < n && i < 8; i++)
        {
            var p = list[i];
            d.options.Add(($"ไปเยี่ยมบ้านของ {p.name}", () => { CloseDialog(); Send(new NetMsg { t = "visit", id = p.id }); Hint($"กำลังขอเข้าบ้านของ {p.name}...", 2f); }, true));
        }
        d.options.Add(("ปิด", CloseDialog, true));
        dialog = d;
    }

    bool CanStartVisit(out string why)
    {
        why = null;
        if (title || dead || player == null) return false;
        if (voyage != null) { why = "กลางทะเลไปเยี่ยมใครไม่ได้ ขึ้นฝั่งก่อน"; return false; }
        if (Busy || homeT > 0f) return false;
        if (!atHome)
            foreach (var e in enemies)
                if (e != null && (e.grid - player.grid).magnitude < 5f) { why = "มีศัตรูอยู่ใกล้ๆ! จัดการให้เรียบร้อยก่อน"; return false; }
        return true;
    }

    /// The server sent a friend's island.
    void OnVisitData(NetMsg m)
    {
        if (string.IsNullOrEmpty(m.text)) { Hint("เข้าบ้านนั้นไม่ได้ เจ้าของอาจออฟไลน์ไปแล้ว", 3f); return; }
        if (m.id == myId) { Hint("นี่บ้านของคุณเองนะ กด H เพื่อกลับเกาะส่วนตัว", 2.5f); return; }
        if (!CanStartVisit(out var why)) { if (why != null) Hint(why, 2.5f); return; }
        visitData = m;
        BeginWarp(Warp.Visit);
    }

    // ------------------------------------------------------------------ going and coming back
    /// After the fade: load the friend's island (StartGame) with your own character.
    void DoVisit()
    {
        var m = visitData;
        visitData = null;
        if (m == null) return;
        if (!visiting)
        {
            visitBack = Snapshot();
            SaveSystem.Write(visitBack); // if the game closes during the visit, you wake up back home
        }
        var d = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(visitBack)); // a copy
        d.items.Clear();
        foreach (var kv in inv) if (kv.Value > 0) d.items.Add(new ItemStack { item = kv.Key, count = kv.Value });
        d.health = health; d.hunger = hunger; d.thirst = thirst;
        SavePets(d);
        d.atHome = true; d.inHouse = false; d.homeMade = true;
        d.fresh = false;
        d.seed = HomeSeed;
        d.biomeId = (int)Biome.Tropical + 1;
        d.nodes = DecodeNodes(m.text);
        d.homeNodes.Clear();
        d.houseNodes = DecodeNodes(m.room);
        d.houseLevel = Mathf.Clamp(m.lv, 0, MaxHouseLevel);
        d.roomMade = d.houseNodes.Count > 0;
        d.stickCap = 0; d.rockCap = 0; d.obsidianCap = 0;
        d.player = new Vector2(-99f, -99f); // arrive in front of their house

        visiting = true;
        visitOwner = m.id;
        visitName = string.IsNullOrEmpty(m.name) ? "เพื่อน" : m.name;
        visitPets.Clear();
        foreach (var part in (m.pets ?? "").Split('|'))
        {
            int c = part.IndexOf(':');
            if (c > 0 && int.TryParse(part.Substring(0, c), out int code)) visitPets.Add((code, part.Substring(c + 1)));
        }
        StartGame(d);
        Send(new NetMsg { t = "knock", to = visitOwner });
        Hint($"มาถึงบ้านของ {visitName} แล้ว! เดินชมได้ตามสบาย กด Enter เพื่อทักทาย กด H เพื่อกลับ", 7f);
    }

    void EndVisit()
    {
        if (!visiting) return;
        BeginWarp(Warp.EndVisit);
    }

    void DoEndVisit()
    {
        if (!visiting || visitBack == null) { visiting = false; return; }
        var d = visitBack;
        visitBack = null;
        visiting = false;
        // what you bought or sold in their town comes home with you
        d.items.Clear();
        foreach (var kv in inv) if (kv.Value > 0) d.items.Add(new ItemStack { item = kv.Key, count = kv.Value });
        d.health = health; d.hunger = hunger; d.thirst = thirst;
        SavePets(d);
        StartGame(d);
        homeSendT = 2f;
    }

    /// Lost the server while visiting: straight back home, no fade.
    void VisitDisconnected()
    {
        if (!visiting) return;
        homeT = 0f;
        DoEndVisit();
        SaveGame();
    }

    void OnKnock(NetMsg m)
    {
        string who = string.IsNullOrEmpty(m.name) ? "มีคน" : m.name;
        AddChat("", $"{who} มาเยี่ยมเกาะส่วนตัวของคุณ!");
        Hint(atHome ? $"{who} มาเยี่ยมเกาะส่วนตัวของคุณ! ทักทายด้วย Enter" : $"{who} แวะไปเยี่ยมเกาะส่วนตัวของคุณ (ตอนนี้คุณไม่อยู่บ้าน)", 5f);
        Sfx.Play("win", 0.4f);
    }
}
