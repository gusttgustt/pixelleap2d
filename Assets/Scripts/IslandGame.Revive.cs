using UnityEngine;

/// Online with friends on the island, falling in a fight doesn't end the run at once: you're
/// knocked out ("downed") where you fell, and a friend can walk up and hold E (or the action
/// button) next to you to bring you back. Nobody comes in time, or no friend is left on the
/// island, and it's the usual death (you respawn and keep everything). You can also give up and respawn yourself.
public partial class IslandGame
{
    public bool downed;          // knocked out, waiting for a friend
    float downedT;               // seconds left to be revived
    float reviveT;               // how long I've been holding E over a downed friend
    RemotePlayer reviveTarget;

    const float DownedSeconds = 90f, ReviveSeconds = 2.5f, ReviveRange = 1.3f;

    /// Someone here could come and help: online, on the same adventure island, not at sea.
    bool FriendsCanRevive => Shared && !atHome && voyage == null && remotes.Count > 0;

    /// Instead of dying: lie down and wait for a friend.
    void GoDown()
    {
        downed = true;
        downedT = DownedSeconds;
        reviveT = 0f;
        AddChat("", "คุณหมดสติ! รอเพื่อนมาชุบชีวิต");
        Send(new NetMsg { t = "chat", text = "หมดสติแล้ว! มาช่วยชุบชีวิตหน่อย (ยืนข้างๆ แล้วกด E ค้างไว้)" });
    }

    /// While downed: keep telling the others where I lie, and count down.
    void TickDowned(float dt)
    {
        NetTick(dt); // others see me lying there (and, if I'm the host, the creatures keep going)
        downedT -= dt;
        if (downedT <= 0f || !FriendsCanRevive) FinalDeath();
    }

    /// Nobody came: the run is over (the usual death screen stays up).
    void FinalDeath()
    {
        if (!downed) return;
        downed = false;
        if (Shared) { AddChat("", "คุณตายแล้ว"); LeaveRoom(); } // someone else looks after the creatures now
    }

    /// The respawn button / R key: come back to life with everything you had (level, items,
    /// skills, boat, private island...), in the middle of the island you were on.
    void Respawn()
    {
        FinalDeath();
        var d = SaveSystem.Read(); // written when you fell (CheckDeath)
        if (d == null) { StartGame(null); return; } // no save at all: a brand new run
        if (voyage != null) EndVoyage(); // lost at sea: back to the last island
        StartGame(Reborn(d));
        Hint("คุณฟื้นขึ้นมาแล้ว! ของและเลเวลยังอยู่ครบ", 4f);
    }

    /// A save made at death, fixed up to wake up alive: full health, not starving, in the middle of the island.
    static SaveData Reborn(SaveData d)
    {
        d.health = 9999f;                // StartGame caps it at MaxHealth
        d.hunger = Mathf.Max(d.hunger, 60f);
        d.thirst = Mathf.Max(d.thirst, 60f);
        d.player = new Vector2(-1f, -1f); // not land: StartGame puts you in the middle of the island
        return d;
    }

    /// A friend held E next to me long enough.
    void Revived(int by)
    {
        if (!downed) return;
        downed = false;
        dead = false;
        health = MaxHealth * 0.35f;
        hunger = Mathf.Max(hunger, 35f); // don't fall over again at once from hunger or thirst
        thirst = Mathf.Max(thirst, 35f);
        poisonT = 0f;
        string who = remotes.TryGetValue(by, out var r) && r != null ? r.playerName : "เพื่อน";
        AddChat("", $"{who} ชุบชีวิตคุณแล้ว!");
        Hint($"{who} ชุบชีวิตคุณแล้ว! รีบหาอะไรกินฟื้นพลัง", 4f);
        Fx.Burst(PlayerPos + Vector3.up * 0.4f, new Color(1f, 0.95f, 0.55f), 14, 1.2f, 1f);
        Sfx.Play("craft", 0.6f);
        SaveGame(); // the save from when I fell has health 0: write the revived state
    }

    /// Reviver side, called from HandleInput. True while I'm busy reviving (other actions wait).
    bool TickRevive(bool held)
    {
        reviveTarget = DownedFriendNear();
        if (reviveTarget == null || !held) { reviveT = 0f; return false; }
        reviveT += Time.deltaTime;
        if (reviveT >= ReviveSeconds)
        {
            reviveT = 0f;
            Send(new NetMsg { t = "rev", to = reviveTarget.netId });
            Fx.Burst(reviveTarget.transform.position + Vector3.up * 0.4f, new Color(1f, 0.95f, 0.55f), 14, 1.2f, 1f);
            Sfx.Play("craft", 0.6f);
            Popup($"ชุบชีวิต {reviveTarget.playerName}!", reviveTarget.transform.position + Vector3.up * 1.1f, new Color(1f, 0.95f, 0.55f));
            reviveTarget.downed = false; // until their next update says otherwise
        }
        return true;
    }

    /// A friend on this island just fell.
    public void FriendDowned(RemotePlayer r)
    {
        if (dead) return;
        Hint($"{r.playerName} หมดสติ! เดินไปข้างๆ แล้วกด E ค้างไว้เพื่อชุบชีวิต", 5f);
        Sfx.Play("hurt", 0.4f);
    }

    RemotePlayer DownedFriendNear()
    {
        if (player == null || !Shared) return null;
        RemotePlayer best = null;
        float bd = ReviveRange;
        foreach (var r in remotes.Values)
        {
            if (r == null || !r.downed) continue;
            float d = (r.grid - player.grid).magnitude;
            if (d < bd) { bd = d; best = r; }
        }
        return best;
    }

    /// Over a downed friend: what to press, and the progress while holding it.
    void DrawReviveTag(RemotePlayer r, Vector2 gp)
    {
        bool near = r == reviveTarget && !dead;
        string text = near ? (ShowTouch ? "กดปุ่มค้างไว้เพื่อชุบชีวิต" : "[E] ค้างไว้เพื่อชุบชีวิต") : "หมดสติ! ไปช่วยชุบชีวิต";
        ShadowLabel(new Rect(gp.x - 110, gp.y - 38, 220, 20), text, labelCenter,
            new Color(1f, 0.85f, 0.3f, 0.65f + 0.35f * Mathf.Sin(Time.time * 5f)));
        if (near && reviveT > 0f)
        {
            Fill(new Rect(gp.x - 31, gp.y - 15, 62, 7), new Color(0f, 0f, 0f, 0.7f));
            Fill(new Rect(gp.x - 30, gp.y - 14, 60f * Mathf.Clamp01(reviveT / ReviveSeconds), 5), new Color(1f, 0.9f, 0.4f));
        }
    }
}
