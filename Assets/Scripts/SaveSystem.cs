using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class SaveData
{
    public int version = SaveSystem.Version;
    public int seed;              // the island terrain is rebuilt from this
    public int island = 1;        // 1, 2, 3, ... (biome cycles Tropical -> Jungle -> Volcano)
    public bool fresh;            // true = island not generated yet (new run or just arrived by raft)
    public float clock, health, hunger, thirst;
    public int stickCap, rockCap, obsidianCap; // how many of each respawning material the island keeps
    public int level = 1, xp, skillPoints, flaskWater;
    public int raftLevel;         // 0 = no raft yet. Lv N can reach island N+1.
    public int boatLook;          // the boat look picked at the shipwright, 1-8 (0 = always the newest; IslandGame.Boat.cs)
    public int biomeId;           // biome + 1 (0 = older save: use the old 3-island cycle)
    public int hatId, outfitId;   // worn item + 1 (0 = nothing)
    public int elderHealDay;
    public List<int> learned = new List<int>(); // clothing recipes taught by the tribe
    public List<int> skills = new List<int>();
    public Vector2 player;
    public List<ItemStack> items = new List<ItemStack>();
    public List<NodeSave> nodes = new List<NodeSave>();
    public int farthest;          // highest island reached on the first continent (0 = older save: same as island)
    public int continent;         // which continent the adventure island is on (IslandGame.World.cs)
    public List<int> reach = new List<int>(); // highest island reached on each continent (empty = older save)
    public int homeContinent;     // where your private island is
    public List<IslandSave> visited = new List<IslandSave>(); // other islands, as you left them
    public List<string> flags = new List<string>();      // quests and story (see IslandGame.Quest.cs)
    public List<string> questKills = new List<string>(); // "<quest>|<enemy kind>|<count>"
    public int powerId, powerLv = 1, powerXp;            // special-fruit power (IslandGame.Power.cs)
    public int weaponId;                                 // equipped weapon + 1 (0 = none)
    public List<int> hotbar = new List<int>();           // shortcut slots: item + 1 (0 = empty)
    // Private island (IslandGame.Home.cs). While atHome, `nodes` holds the home island and the
    // adventure island you came from is kept in `visited` (island = advIsland).
    public bool atHome;
    public bool homeMade;                                // the private island has been visited once
    public int advIsland;                                // adventure island to go back to
    public Vector2 advPlayer;                            // where you stood on it
    public List<NodeSave> homeNodes = new List<NodeSave>(); // the private island while you're away
    public int homeStickCap, homeRockCap;
    public int homeLand;                                 // how many times the private island was made bigger (IslandGame.Home.cs)
    // Inside your house (IslandGame.House.cs). While inHouse, `nodes` is the room and homeNodes the
    // island outside; otherwise the room waits in houseNodes.
    public bool inHouse, roomMade;
    public int houseLevel;                               // 0 = no house built yet
    public Vector2 homeReturn;                           // where you stood outside before going in
    public List<NodeSave> houseNodes = new List<NodeSave>();
    // Pets (IslandGame.Pet.cs): all you own, and the one out walking with you (-1 = none).
    public List<PetSave> pets = new List<PetSave>();
    public int activePet = -1;
    public long savedAt;    // unix seconds of the last write: the newer of the cloud and local save wins (IslandGame.Account.cs)
}

[Serializable]
public class PetSave
{
    public int code;     // 0 dog, 1 cat, 2 rabbit, 3 parrot; 100 + EnemyKind = a tamed wild creature
    public string name;
}

/// An island you've been to and left: rebuilt from its seed, then its saved objects put back.
[Serializable]
public class IslandSave
{
    public int island, seed, biomeId;
    public int continent;
    public int stickCap, rockCap, obsidianCap;
    public List<NodeSave> nodes = new List<NodeSave>();
}

[Serializable]
public class ItemStack
{
    public Item item;
    public int count;
}

[Serializable]
public class NodeSave
{
    public NodeKind kind;
    public Vector2 grid;
    public bool available, flip;
    public int hits;
    public float regrow; // seconds left until it regrows
    public int item, count; // dropped items (NodeKind.Drop); crop type / growth stage on farm plots
    public bool built;      // put there by the player (can be taken down with the hammer)
    public int id;          // private island: the same on the owner's and visitors' machines (IslandGame.HomeSync.cs)
}

/// Reads and writes the save file as JSON in Application.persistentDataPath.
public static class SaveSystem
{
    // Bump when terrain generation or save format changes; older saves then start a new island.
    // v2: freshwater ponds, thirst, respawning sticks/rocks.
    // v3: hills, new items/structures (spear, knife, fish, crab, shelter, raft).
    // v4: island chain with biomes, XP / levels / skills, new items.
    // v5: the raft is kept and upgraded (raftLevel). v4 saves still load (migrated on load).
    // v6: Tribe Island (biome stored per save), clothing. v4/v5 saves still load.
    public const int Version = 6;
    const int OldestReadable = 4;
    public static string SavePath => Path.Combine(Application.persistentDataPath, "island_save.json");

    public static bool HasSave => File.Exists(SavePath);

    /// Any change to the save files (written, deleted on death, home kept): an account copies them to the server.
    public static Action Changed;
    static void Touch() { try { Changed?.Invoke(); } catch (Exception e) { Debug.LogWarning(e.Message); } }

    /// The save and kept-home files as they are on disk ("" = none), for the account copy.
    public static string RawSave() { try { return File.Exists(SavePath) ? File.ReadAllText(SavePath) : ""; } catch { return ""; } }
    public static string RawKeep() { try { return File.Exists(HomeKeepPath) ? File.ReadAllText(HomeKeepPath) : ""; } catch { return ""; } }

    /// Puts the account's copy in place of the files on this device.
    public static void WriteRaw(string save, string keep)
    {
        try
        {
            if (string.IsNullOrEmpty(save)) { if (File.Exists(SavePath)) File.Delete(SavePath); }
            else File.WriteAllText(SavePath, save);
            if (string.IsNullOrEmpty(keep)) { if (File.Exists(HomeKeepPath)) File.Delete(HomeKeepPath); }
            else File.WriteAllText(HomeKeepPath, keep);
        }
        catch (Exception e) { Debug.LogWarning($"Writing the account's save failed: {e.Message}"); }
    }

    public static SaveData Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(text);
            if (data == null || data.version < OldestReadable || data.version > Version) return null;
            return data;
        }
        catch { return null; }
    }

    public static void Write(SaveData data)
    {
        try
        {
            // Write to a temp file first so a crash mid-write can't corrupt the save.
            data.savedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string tmp = SavePath + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(data)); // compact: it also goes to the server for accounts
            File.Copy(tmp, SavePath, true);
            File.Delete(tmp);
        }
        catch (Exception e) { Debug.LogWarning($"Save failed: {e.Message}"); }
        Touch();
    }

    public static SaveData Read()
    {
        if (!HasSave) return null;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            if (data == null || data.version < OldestReadable || data.version > Version) return null;
            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Load failed, starting a new island: {e.Message}");
            return null;
        }
    }

    /// The private island outlives a death on an adventure island: it's kept in its own small
    /// file and handed to the next run (IslandGame.Home.cs).
    static string HomeKeepPath => Path.Combine(Application.persistentDataPath, "island_home_keep.json");

    public static void WriteHomeKeep(SaveData data)
    {
        try { File.WriteAllText(HomeKeepPath, JsonUtility.ToJson(data)); }
        catch (Exception e) { Debug.LogWarning($"Keeping the home island failed: {e.Message}"); }
        Touch();
    }

    /// Reads and removes the kept home island (null if there is none).
    public static SaveData TakeHomeKeep()
    {
        try
        {
            if (!File.Exists(HomeKeepPath)) return null;
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(HomeKeepPath));
            File.Delete(HomeKeepPath);
            Touch();
            return data;
        }
        catch (Exception e) { Debug.LogWarning($"Reading the kept home island failed: {e.Message}"); return null; }
    }

    public static void Delete()
    {
        try { if (HasSave) File.Delete(SavePath); }
        catch (Exception e) { Debug.LogWarning($"Delete save failed: {e.Message}"); }
        Touch();
    }
}
