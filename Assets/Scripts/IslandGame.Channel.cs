using UnityEngine;

/// Channels (online). Every adventure island, and every stretch of sea, comes in a few copies:
/// CH1, CH2, ... Players only see the others on the same island in the same channel, so friends
/// pick the same channel to meet (the who's-online list shows where everyone is and switches you
/// to a friend's channel with one click). Same island layout in every channel, but what's been
/// harvested or built is kept per channel. Private islands have no channels.
///
/// Rooms on the server are plain numbers, so a channel is just an offset on the room key: CH1 is
/// the old key unchanged (players on older builds are in CH1).
public partial class IslandGame
{
    public const int Channels = 5;
    const int ChannelStep = 10000000; // island room keys (WorldKey) stay well below this
    const int SeaChannelStep = 100;   // sea rooms: SeaRoomBase + channel offset + continent

    int channel = 1;                  // 1..Channels
    int pendingChannel;               // > 0: switch to it when you set sail for pendingIsland / pendingContinent
    int pendingIsland, pendingContinent;
    float channelSwitchAt = -100f;
    float lastHurtAt = -100f;         // DamagePlayer

    const float ChannelCooldown = 45f; // between switches: no hopping round for fresh trees and rocks
    const float ChannelFightCalm = 8f; // seconds since you were last hit
    const float ChannelEnemyNear = 6f; // tiles: a creature this close and you're in a fight

    bool PendingFits => pendingChannel > 0 && destIsland == pendingIsland && destContinent == pendingContinent;

    /// The channel the voyage being set off on sails in (the crew is told so they land in it too).
    float SailChannel => PendingFits ? pendingChannel : channel;

    /// Sailing to a friend in another channel: if you then set sail for that island, you're in
    /// their channel from the moment you leave. Sailing anywhere else forgets it.
    void ChannelOnSail(int to, int n, int c)
    {
        to = Mathf.Clamp(to, 1, Channels);
        pendingChannel = to == channel ? 0 : to;
        pendingIsland = n; pendingContinent = c;
        if (pendingChannel > 0) Hint($"ถ้าออกเรือไปเกาะที่ {n} จะเข้า CH{pendingChannel} (channel เดียวกับเพื่อน)", 3.5f);
    }

    /// Called as a room is joined: at sea for the friend's island it takes their channel; any
    /// other join (another island, home, a later trip) drops it.
    void ApplyPendingChannel()
    {
        if (pendingChannel <= 0) return;
        bool fits = voyage != null && PendingFits;
        int to = pendingChannel;
        pendingChannel = 0;
        if (!fits) return;
        channel = to;
        PlayerPrefs.SetInt("channel", channel);
        PlayerPrefs.Save();
    }

    /// A creature close by, or hit a moment ago.
    bool InFight()
    {
        if (Time.time - lastHurtAt < ChannelFightCalm) return true;
        if (player == null) return false;
        foreach (var e in enemies)
            if (e != null && (e.grid - player.grid).magnitude < ChannelEnemyNear) return true;
        return false;
    }

    /// The row of channel buttons in the who's-online panel, each with how many players are on
    /// this island (or these waters) in that channel.
    void DrawChannelPicker(Rect r, NetPeer[] list)
    {
        int myBase = inRoom && RoomChannel(RoomKey) > 0 ? RoomNoChannel(RoomKey) : int.MinValue;
        var counts = new int[Channels + 1];
        if (myBase != int.MinValue)
            foreach (var p in list)
                if (p != null && p.isl != 0 && RoomChannel(p.isl) is int ch && ch >= 1 && ch <= Channels && RoomNoChannel(p.isl) == myBase) counts[ch]++;
        ShadowLabel(new Rect(r.x, r.y + 4, 70, 18), "Channel:", small, new Color(1f, 0.85f, 0.45f));
        float bw = Mathf.Min(78f, (r.width - 74f) / Channels - 4f);
        var st = new GUIStyle(GUI.skin.button) { fontSize = 11, richText = true };
        string block = ChannelBlock();
        bool can = block == null;
        if (block != null) ShadowLabel(new Rect(r.x, r.yMax + 1, r.width, 14), block, small, new Color(1f, 0.6f, 0.5f));
        for (int i = 1; i <= Channels; i++)
        {
            bool mine = i == channel;
            string text = mine ? $"<b><color=#9dff9d>CH{i}</color></b>" : i == pendingChannel ? $"<color=#ffd27a>CH{i}</color>" : $"CH{i}";
            if (myBase != int.MinValue) text += $" <size=10>({counts[i]})</size>";
            GUI.enabled = !mine && can;
            if (CrispGUI.Button(new Rect(r.x + 74f + (i - 1) * (bw + 4f), r.y, bw, r.height), text, st)) { pendingChannel = 0; SwitchChannel(i); }
            GUI.enabled = true;
        }
    }

    int ChannelOffset => (Mathf.Clamp(channel, 1, Channels) - 1) * ChannelStep;
    int SeaChannelOffset => (Mathf.Clamp(channel, 1, Channels) - 1) * SeaChannelStep;

    void LoadChannel() => channel = Mathf.Clamp(PlayerPrefs.GetInt("channel", 1), 1, Channels);

    /// The channel a room key belongs to (0: a private island, which has none).
    static int RoomChannel(int isl)
    {
        if (isl >= SeaRoomBase) return 1 + (isl - SeaRoomBase) / SeaChannelStep;
        if (CaveRoom(isl)) return 1 + (isl - CaveRoomBase) / ChannelStep; // IslandGame.Cave.cs
        if (PrisonRoom(isl)) return 1 + (isl - PrisonRoomBase) / ChannelStep; // IslandGame.Prison.cs
        if (isl > 0) return 1 + isl / ChannelStep;
        return 0;
    }

    /// The room key without its channel (the island's WorldKey, or SeaRoomBase + continent).
    static int RoomNoChannel(int isl)
    {
        if (isl >= SeaRoomBase) return SeaRoomBase + (isl - SeaRoomBase) % SeaChannelStep;
        if (CaveRoom(isl)) return CaveRoomBase + (isl - CaveRoomBase) % ChannelStep;
        if (PrisonRoom(isl)) return PrisonRoomBase + (isl - PrisonRoomBase) % ChannelStep;
        if (isl > 0) return isl % ChannelStep;
        return isl;
    }

    /// Where a channel switch can happen right now (and why not).
    string ChannelBlock()
    {
        if (!Online) return "ต้องเล่นออนไลน์ก่อน";
        if (dead) return "ตอนนี้ย้าย channel ไม่ได้";
        if (inCave) return "ออกจากถ้ำก่อนค่อยย้าย channel";
        if (voyage != null || sailT > 0f) return "ย้าย channel กลางทะเลไม่ได้ ขึ้นฝั่งก่อน";
        if (Busy || homeT > 0f) return "รอให้เสร็จก่อนค่อยย้าย channel";
        if (atHome || visiting) return null; // only picks the channel for later: nothing to exploit
        float wait = ChannelCooldown - (Time.time - channelSwitchAt);
        if (wait > 0f) return $"เพิ่งย้าย channel รออีก {Mathf.CeilToInt(wait)} วินาที";
        if (InFight()) return "กำลังต่อสู้อยู่ ย้าย channel ไม่ได้ (หนีให้พ้นสัตว์ร้ายก่อน)";
        return null;
    }

    /// Move to another channel: on an adventure island you leave this copy of it and land in the
    /// other one (in the middle of the island, with everything you carry). At home it only picks
    /// the channel for the next island you go to.
    public void SwitchChannel(int to)
    {
        to = Mathf.Clamp(to, 1, Channels);
        if (to == channel) return;
        string why = ChannelBlock();
        if (why != null) { Hint(why, 2.5f); return; }
        channel = to;
        pendingChannel = 0;
        PlayerPrefs.SetInt("channel", channel);
        PlayerPrefs.Save();
        if (atHome || visiting || player == null)
        {
            Hint($"เลือก CH{channel} แล้ว ไปเกาะผจญภัยครั้งต่อไปจะเข้า CH{channel}", 3.5f);
            return;
        }
        if (aboard && InCrew) { aboard = false; SendCrew(crewCaptain, "unboard"); } // the captain stays in the old channel
        channelSwitchAt = Time.time;
        SaveGame();
        var d = Snapshot();
        d.player = new Vector2(-1f, -1f); // the middle of the island
        // StartGame resets these for a new arrival; a channel switch isn't one, so they come along
        // (no curing poison, refilling air or warming up, or skipping a power's cooldown, by switching)
        float keepPoison = poisonT, keepAir = air, keepInk = inkT, keepWarmth = warmth;
        float keepPowerCd = powerCd, keepInvisible = invisibleT, keepBubble = bubbleT;
        int keepBubbleHits = bubbleHits;
        StartGame(d);                     // rebuilt from the same seed, then the new channel's changes (JoinRoom)
        poisonT = keepPoison; air = keepAir; inkT = keepInk; warmth = keepWarmth;
        powerCd = keepPowerCd; invisibleT = keepInvisible; bubbleT = keepBubble; bubbleHits = keepBubbleHits;
        Hint($"ย้ายมา CH{channel} แล้ว ({BiomeName} เกาะที่ {island})", 3.5f);
        Sfx.Play("pickup", 0.6f);
    }
}
