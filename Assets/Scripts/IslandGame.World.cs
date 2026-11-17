using UnityEngine;

/// The world: four continents, each with its own chain of adventure islands 1, 2, 3... (the
/// further out, the tougher). Your private island sits on one continent (you can move it), and so
/// do your friends' islands. The sea chart (IslandGame.Chart.cs) shows them all.
///
/// The first continent is the island chain the game always had, so older saves carry on there.
/// Online, island n on continent c is room / seed key WorldKey(n, c): n itself on the first
/// continent, c * 100000 + n on the others.
public partial class IslandGame
{
    public const int Continents = 4;
    public int continent;               // the continent the adventure island you're on (or came from) is on
    public int homeContinent;           // where your private island is
    readonly int[] reach = new int[Continents]; // highest island reached on each continent
    public int destContinent;           // where the current voyage goes

    public static readonly string[] ContinentNames = { "ทวีปเขตร้อน", "ทวีปน้ำแข็ง", "ทวีปภูเขาไฟ", "ทวีปฟ้าและทะเลลึก" };
    static readonly string[] ContinentBlurb =
    {
        "หาดทราย ป่าเห็ด และเกาะหลังเต่า ทวีปแรกของทุกคน",
        "เกาะน้ำแข็ง พายุ และผลึกระยิบระยับ อากาศหนาวจัด",
        "ป่าดิบ ชนเผ่า และภูเขาไฟคุกรุ่น สัตว์ดุร้าย",
        "เกาะลอยฟ้า ฟองอากาศ และนครบาดาลใต้ทะเล",
    };
    /// Where each continent starts in the biome cycle of BiomeFor (0 Tropical, 3 Sky, 6 Ice, 12 Jungle).
    static readonly int[] ContinentStart = { 0, 6, 12, 3 };
    static readonly Color[] ContinentColor =
    {
        new Color(0.45f, 0.8f, 0.4f), new Color(0.75f, 0.9f, 1f), new Color(0.9f, 0.45f, 0.3f), new Color(0.6f, 0.75f, 1f),
    };

    public static Biome BiomeFor(int n, int c) => BiomeFor(n + ContinentStart[Mathf.Clamp(c, 0, Continents - 1)]);

    public static int WorldKey(int n, int c) => c <= 0 ? n : c * 100000 + n;

    public string ContinentName => ContinentNames[Mathf.Clamp(continent, 0, Continents - 1)];

    IslandSave Kept(int n, int c) => visited.Find(v => v.island == n && v.continent == c);

    void LoadWorld(SaveData d)
    {
        continent = Mathf.Clamp(d.continent, 0, Continents - 1);
        homeContinent = Mathf.Clamp(d.homeContinent, 0, Continents - 1);
        for (int c = 0; c < Continents; c++) reach[c] = d.reach != null && c < d.reach.Count ? d.reach[c] : 0;
        if (d.reach == null || d.reach.Count == 0) reach[0] = Mathf.Max(1, d.farthest); // older save: one chain
        reach[continent] = Mathf.Max(reach[continent], island);
    }

    void SaveWorld(SaveData d)
    {
        d.continent = continent;
        d.homeContinent = homeContinent;
        d.reach.Clear();
        d.reach.AddRange(reach);
    }

    /// Islands reached on a continent (0 = never been there).
    int ReachOn(int c) => reach[Mathf.Clamp(c, 0, Continents - 1)];

    // ------------------------------------------------------------------ moving your island
    const int MoveHomeCost = 150;

    void MoveHomeTo(int c)
    {
        if (c == homeContinent || visiting) return;
        if (Count(Item.GoldCoin) < MoveHomeCost) { Hint($"ต้องใช้เหรียญทอง {MoveHomeCost} เพื่อย้ายเกาะส่วนตัว", 2.5f); return; }
        inv[Item.GoldCoin] -= MoveHomeCost;
        homeContinent = c;
        homeSendT = Mathf.Min(homeSendT, 1f); // friends see it in the new place soon (IslandGame.Visit.cs)
        Sfx.Play("levelup", 0.6f);
        Hint($"ลากเกาะส่วนตัวไปตั้งที่{ContinentNames[c]}แล้ว! ทุกอย่างบนเกาะยังอยู่ครบ เพื่อนๆ จะเห็นบ้านคุณที่ทวีปนี้", 5f);
        SaveGame();
    }
}
