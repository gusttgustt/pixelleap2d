using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// Private islands shared live (online): the owner and everyone visiting see the same island, and
/// visitors can help on the farm (plant, water, harvest, feed the animals).
///
/// Every node on a private island has an id that stays the same in the save and on every machine
/// (homeIdNext counts them up for the owner). Four times a second each player on a private island
/// compares its nodes with what it last told the others and sends what changed ("hnode": the
/// changed nodes, encoded as for visits, and the ids that are gone). The server passes it to
/// everyone on that island and also to the owner wherever they are: if the owner is away (on an
/// adventure island, inside the house, visiting someone else) the change goes into their stored
/// copy of the island, so a friend's help is kept. When someone arrives the owner sends the whole
/// island ("full") so the newcomer sees exactly what the owner sees. The owner's animals walk on the
/// owner's machine; visitors' copies follow them. What a visitor harvests, the owner gets too ("hgift").
public partial class IslandGame
{
    int homeIdNext;                                            // last id given to a private-island node
    public const int DoorId = int.MaxValue - 7;                // the doormat in the house (rebuilt with the room)
    readonly Dictionary<int, string> homeSig = new Dictionary<int, string>(); // what the others last heard
    float homeSyncT;

    int HomeOwner => visiting ? visitOwner : myId;
    bool HomeSyncOn => Online && serverHomes && atHome && inRoom && player != null;

    /// Ids for the private island: everything already there keeps its id, new things count on from the highest.
    void InitHomeIds(SaveData d)
    {
        int max = genCounter;
        foreach (var list in new[] { d.nodes, d.homeNodes, d.houseNodes, homeNodes, houseNodes })
            if (list != null) foreach (var n in list) if (n != null && n.id > max && n.id != DoorId) max = n.id;
        homeIdNext = Mathf.Max(homeIdNext, max);
        if (homeIdNext >= DoorId - 1000) homeIdNext = max; // never wrap into the door's id
    }

    // ------------------------------------------------------------------ what changed
    static string Sig(WorldNode n)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(40);
        sb.Append((int)n.kind).Append(',');
        if (n.IsAnimal) sb.Append(Mathf.RoundToInt(n.grid.x * 4f)).Append(',').Append(Mathf.RoundToInt(n.grid.y * 4f)); // where it wandered, roughly
        else sb.Append(n.grid.x.ToString("0.##", ci)).Append(',').Append(n.grid.y.ToString("0.##", ci));
        sb.Append(',').Append(n.available ? 1 : 0).Append(n.built ? 1 : 0).Append(',').Append(n.hits)
          .Append(',').Append((int)n.dropItem).Append(',').Append(n.dropCount);
        return sb.ToString();
    }

    static bool Synced(WorldNode n) => n != null && n.kind != NodeKind.Drop && n.kind != NodeKind.HouseDoor;

    /// Right after loading: everyone here started from the same picture.
    void ResetHomeSync()
    {
        homeSig.Clear();
        homeSyncT = 0.5f;
        if (!atHome) return;
        foreach (var n in nodes) if (Synced(n)) homeSig[n.id] = Sig(n);
    }

    void TickHomeSync(float dt)
    {
        if (!HomeSyncOn) return;
        homeSyncT -= dt;
        if (homeSyncT > 0f) return;
        homeSyncT = 0.25f;
        bool someone = visiting || remotes.Count > 0; // the owner alone at home has no one to tell
        var changed = new List<NodeSave>();
        var seen = new HashSet<int>();
        foreach (var n in nodes)
        {
            if (!Synced(n)) continue;
            seen.Add(n.id);
            string sig = Sig(n);
            if (homeSig.TryGetValue(n.id, out var old) && old == sig) continue;
            homeSig[n.id] = sig;
            changed.Add(ToSave(n));
        }
        var gone = new List<int>();
        foreach (var id in homeSig.Keys) if (!seen.Contains(id)) gone.Add(id);
        foreach (var id in gone) homeSig.Remove(id);
        if (!someone || (changed.Count == 0 && gone.Count == 0)) return;
        Send(new NetMsg { t = "hnode", id = HomeOwner, text = EncodeNodes(changed), room = string.Join(",", gone), fl = inHouse ? 1 : 0, lv = houseLevel });
        if (!visiting) homeSendT = Mathf.Min(homeSendT, 5f); // visitors who come later get the new picture too
    }

    /// Owner at home: the whole island to someone who just arrived (to = 0: everyone here).
    void SendFullHome(int to)
    {
        if (!HomeSyncOn || visiting) return;
        var all = new List<NodeSave>();
        foreach (var n in nodes) if (Synced(n)) { all.Add(ToSave(n)); homeSig[n.id] = Sig(n); }
        Send(new NetMsg { t = "hnode", to = to, id = myId, ev = "full", text = EncodeNodes(all), fl = inHouse ? 1 : 0, lv = houseLevel });
    }

    // ------------------------------------------------------------------ changes coming in
    void OnHomeNodes(NetMsg m)
    {
        if (m.from == myId) return;
        bool ins = m.fl == 1;
        bool full = m.ev == "full";
        var list = DecodeNodes(m.text);
        var gone = new List<int>();
        foreach (var part in (m.room ?? "").Split(','))
            if (int.TryParse(part, out int id)) gone.Add(id);

        // the island we're standing on, and it's this one (inside or outside the house)
        if (atHome && HomeOwner == m.id && inHouse == ins && player != null)
        {
            if (visiting && m.lv > 0 && m.lv != houseLevel) { houseLevel = Mathf.Clamp(m.lv, 1, MaxHouseLevel); foreach (var c in nodes) if (c != null && c.kind == NodeKind.Cottage) c.Refresh(); }
            var keep = new HashSet<int>();
            foreach (var s in list)
            {
                if (s.id <= 0) continue;
                keep.Add(s.id);
                if (nodeById.TryGetValue(s.id, out var node) && node != null) node.ApplyState(s, visiting);
                else
                {
                    node = Spawn(s.kind, s.grid, s.flip, s.id);
                    node.built = s.built;
                    node.ApplyState(s, false);
                    if (s.id > homeIdNext && !visiting) homeIdNext = s.id;
                }
                homeSig[s.id] = Sig(node);
            }
            if (full) // whatever the owner doesn't have isn't there
                foreach (var n in nodes.FindAll(n => Synced(n) && !keep.Contains(n.id)))
                    gone.Add(n.id);
            foreach (var id in gone)
            {
                homeSig.Remove(id);
                if (nodeById.TryGetValue(id, out var node) && node != null && Synced(node)) RemoveNode(node);
            }
            return;
        }

        // my own island while I'm somewhere else: keep a friend's help in the stored copy
        if (m.id != myId || full) return;
        var stored = StoredHomeList(ins);
        if (stored == null) return;
        foreach (var s in list)
        {
            if (s.id <= 0) continue;
            int i = stored.FindIndex(x => x != null && x.id == s.id);
            if (i >= 0)
            {
                var old = stored[i];
                if (old.kind is NodeKind.Chicken or NodeKind.Cow) s.grid = old.grid; // animals stay where they were
                stored[i] = s;
            }
            else stored.Add(s);
        }
        foreach (var id in gone) stored.RemoveAll(x => x != null && x.id == id);
        homeSendT = Mathf.Min(homeSendT, 2f);
    }

    /// Where my private island (outside, or the room in the house) is kept while I'm not on it.
    List<NodeSave> StoredHomeList(bool ins)
    {
        if (visiting)
        {
            var b = visitBack;
            if (b == null) return null;
            if (b.atHome && b.inHouse == ins) return b.nodes;
            return ins ? b.houseNodes : b.homeNodes;
        }
        if (!homeMade) return null;
        return ins ? houseNodes : homeNodes;
    }

    // ------------------------------------------------------------------ sharing the harvest
    /// Harvesting on a friend's farm: they get the same.
    void GiftOwner(Item it, int count)
    {
        if (!visiting || !Online || count <= 0) return;
        Send(new NetMsg { t = "hgift", to = visitOwner, kind = (int)it, hits = count, name = playerName });
    }

    void OnHomeGift(NetMsg m)
    {
        if (m.hits <= 0 || m.kind < 0 || m.kind >= System.Enum.GetValues(typeof(Item)).Length) return;
        var it = (Item)m.kind;
        // only what a farm gives (crops, milk, eggs), and no more than one harvest can: a changed
        // game on the visitor's side can't hand out boss materials or gold this way
        if (it is not (Item.Tomato or Item.Wheat or Item.Carrot or Item.Milk or Item.Egg)) return;
        int n = Mathf.Min(m.hits, 8);
        inv[it] = Count(it) + n;
        AutoHotbar(it);
        string who = string.IsNullOrEmpty(m.name) ? "เพื่อน" : m.name;
        AddChat("", $"{who} ช่วยดูแลฟาร์มของคุณ: +{n} {Name(it)}");
        Hint($"{who} ช่วยเก็บที่ฟาร์มของคุณ คุณได้ {Name(it)} +{n} ด้วย!", 4f);
        Sfx.Play("pickup", 0.4f);
        if (!visiting) autosaveTimer = AutosaveInterval; // keep it
    }
}
