using UnityEngine;

/// Continents as a journey: each has IslandsPerContinent islands, the last one is the continent
/// boss's island (IslandGame.ContinentBoss.cs). Beat it and the next continent opens: you can sail
/// there and move your private island there. A continent you'd already reached before this rule
/// stays open (older saves carry on).
///
/// Every continent is harder than the last: creatures hit harder and take more beating, and the
/// government pays more attention (bounty) to what you do there.
public partial class IslandGame
{
    public const int IslandsPerContinent = 20;

    /// Creature toughness / damage and bounty, per continent.
    static readonly float[] ContinentHp = { 1f, 1.9f, 3.2f, 5f };
    static readonly float[] ContinentDmg = { 1f, 1.5f, 2.1f, 2.8f };
    static readonly float[] ContinentBounty = { 1f, 2f, 3.5f, 5.5f };

    int Cont => Mathf.Clamp(continent, 0, Continents - 1);
    float ContinentHpScale => atHome ? 1f : ContinentHp[Cont];
    float ContinentDmgScale => atHome ? 1f : ContinentDmg[Cont];
    float BountyScale => ContinentBounty[Mathf.Clamp(voyage != null ? destContinent : continent, 0, Continents - 1)];

    /// This island is the continent's last: its boss lives here.
    public bool OnBossIsland => !atHome && !inCave && island == IslandsPerContinent;

    /// The continent boss of c has been beaten.
    public bool ContinentCleared(int c) => Flag("cboss" + c);

    /// You may sail to (and move your home to) continent c.
    bool ContinentOpen(int c) => c <= 0 || ReachOn(c) > 0 || ContinentCleared(c - 1);

    string ContinentLockWhy(int c) =>
        $"ยังไปไม่ได้: ต้องผ่าน{ContinentNames[Mathf.Clamp(c - 1, 0, Continents - 1)]}ให้ครบ {IslandsPerContinent} เกาะ แล้วปราบบอสทวีปที่เกาะ {IslandsPerContinent} ก่อน";

    /// How far along the whole world: 0..79 (for recommended levels and the like).
    int WorldStep => Cont * IslandsPerContinent + island;
}
