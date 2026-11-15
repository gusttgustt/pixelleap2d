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
    public int biomeId;           // biome + 1 (0 = older save: use the old 3-island cycle)
    public int hatId, outfitId;   // worn item + 1 (0 = nothing)
    public int elderHealDay;
    public List<int> learned = new List<int>(); // clothing recipes taught by the tribe
    public List<int> skills = new List<int>();
    public Vector2 player;
    public List<ItemStack> items = new List<ItemStack>();
    public List<NodeSave> nodes = new List<NodeSave>();
    public int farthest;          // highest island reached (0 = older save: same as island)
    public List<IslandSave> visited = new List<IslandSave>(); // other islands, as you left them
    public List<string> flags = new List<string>();      // quests and story (see IslandGame.Quest.cs)
    public List<string> questKills = new List<string>(); // "<quest>|<enemy kind>|<count>"
    public int powerId, powerLv = 1, powerXp;            // special-fruit power (IslandGame.Power.cs)
    public int weaponId;                                 // equipped weapon + 1 (0 = none)
    public List<int> hotbar = new List<int>();           // shortcut slots: item + 1 (0 = empty)
}

/// An island you've been to and left: rebuilt from its seed, then its saved objects put back.
[Serializable]
public class IslandSave
{
    public int island, seed, biomeId;
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
    public int item, count; // dropped items (NodeKind.Drop)
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

    public static void Write(SaveData data)
    {
        try
        {
            // Write to a temp file first so a crash mid-write can't corrupt the save.
            string tmp = SavePath + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
            File.Copy(tmp, SavePath, true);
            File.Delete(tmp);
        }
        catch (Exception e) { Debug.LogWarning($"Save failed: {e.Message}"); }
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

    public static void Delete()
    {
        try { if (HasSave) File.Delete(SavePath); }
        catch (Exception e) { Debug.LogWarning($"Delete save failed: {e.Message}"); }
    }
}
