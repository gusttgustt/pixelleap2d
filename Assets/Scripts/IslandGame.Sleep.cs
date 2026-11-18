using UnityEngine;

/// Sleep matters: energy (พลังงาน) runs down while you're awake, faster at night. Tired, you
/// walk and swing slower; worn out, you hit weaker and nod off now and then; empty, it starts to
/// cost health. A night's sleep fills it up and leaves you well rested (สดชื่น) for a while: a
/// little faster, stronger and quicker to heal. A bed in your house rests you best, a shelter a
/// bit less; a short nap is possible in the day when you're tired.
public partial class IslandGame
{
    public float energy = 100f;  // 0..100
    float restedT;               // seconds of "well rested" left
    float nodT = 12f;            // till the next nod-off when worn out

    const float EnergySeconds = 420f;  // awake time from full to empty (about a day and three quarters)
    const float TiredAt = 30f, WornAt = 10f;

    bool Tired => energy < TiredAt;
    bool WornOut => energy < WornAt;
    bool Rested => restedT > 0f;

    /// Walking speed from how rested you are.
    float EnergySpeed => Rested ? 1.1f : WornOut ? 0.7f : Tired ? 0.85f : 1f;
    /// Attack speed and damage from how rested you are.
    float EnergyAttack => Rested ? 1.1f : WornOut ? 0.7f : Tired ? 0.85f : 1f;

    void LoadSleep(SaveData d) { energy = Mathf.Clamp(d.energy, 0f, 100f); restedT = Mathf.Max(0f, d.restedT); }
    void SaveSleep(SaveData d) { d.energy = energy; d.restedT = restedT; }

    /// Every frame awake (TickSurvival). Home is restful too.
    void TickEnergy(float dt)
    {
        if (restedT > 0f) restedT -= dt;
        float rate = (IsNight ? 1.5f : 1f) * (atHome ? 0.5f : 1f);
        energy = Mathf.Max(0f, energy - dt * rate * 100f / EnergySeconds);
        if (Rested) health = Mathf.Min(MaxHealth, health + dt * 0.25f);
        if (energy <= 0f) health -= dt * 1f; // no sleep at all: it hurts
        if (WornOut && player != null && !Busy)
        {
            nodT -= dt;
            if (nodT <= 0f)
            {
                nodT = Random.Range(10f, 18f);
                Slow(1.2f);
                Popup("สัปหงก...", PlayerPos + Vector3.up * 1.1f, new Color(0.75f, 0.75f, 1f));
            }
        }
    }

    /// Asleep: how much energy comes back and how long you stay well rested.
    void Rest(bool bed, bool wholeNight)
    {
        float to = bed ? 100f : 85f;
        energy = wholeNight ? Mathf.Max(energy, to) : Mathf.Min(100f, energy + (bed ? 45f : 35f));
        if (wholeNight) restedT = bed ? DayLength : DayLength * 0.5f;
    }

    /// Under the hunger and thirst lines: energy, and whether you're rested or tired.
    float DrawSleepHud(float y)
    {
        if (player == null && voyage == null) return y;
        Color col = Rested ? new Color(0.55f, 0.95f, 0.75f) : WornOut ? new Color(1f, 0.45f, 0.45f) : Tired ? new Color(1f, 0.75f, 0.35f) : new Color(0.65f, 0.7f, 1f);
        string text = Rested ? $"พลังงาน {energy:0} · สดชื่น! เร็ว/แรงขึ้น ({Mathf.CeilToInt(restedT)} วิ)"
                    : WornOut ? $"พลังงาน {energy:0} · หมดแรง! ช้า อ่อนแรง สัปหงก ไปนอน"
                    : Tired ? $"พลังงาน {energy:0} · ง่วง เดินและตีช้าลง"
                    : $"พลังงาน {energy:0}";
        Bar(new Rect(14, y + 2, 180, 10), energy / 100f, col, text);
        return y + 19f;
    }
}
