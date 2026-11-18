using UnityEngine;

/// Getting back online by itself. A phone locked or a tab in the background stops the game, and
/// after a while the server lets the connection go; so does a Wi-Fi hiccup. Instead of carrying on
/// offline for good, the game tries again: hello, the device's login token if it was logged in,
/// then back into the room it's in. A few tries, further apart each time, then it gives up and says so.
/// Not after being replaced by another device (kicked), and not if the server has restarted
/// meanwhile (its islands come out differently now: back to the title screen for that).
public partial class IslandGame
{
    bool reconnecting, reauthPending, reconnectAbort;
    float reconnectAt, reconnectStart;
    int reconnectTries, reconnectSeed;
    string reconnectAcct;              // logged in before the drop: log in again with the device's token
    const int ReconnectTries = 8;      // about 2 + 4 + ... + 16 s, then 30 s apart: a few minutes in all

    /// From OnDisconnected: true when the game will try to get back online.
    bool StartReconnect()
    {
        if (title || kickedWhy != null) return false;
        reconnecting = true; reauthPending = false; reconnectAbort = false;
        reconnectTries = 0; reconnectAt = Time.time + 1f;
        reconnectSeed = worldSeed;
        reconnectAcct = acctName;
        return true;
    }

    /// Every frame in the game (after NetPoll).
    void TickReconnect()
    {
        if (!reconnecting) return;
        if (title) { reconnecting = false; return; } // went back to the title screen: that starts over anyway
        if (reconnectAbort) // the server restarted: its world isn't this one
        {
            reconnecting = false;
            if (link != null) link.Close();
            link = null; myId = 0; worldSeed = reconnectSeed;
            Hint("เซิร์ฟเวอร์เพิ่งเริ่มใหม่ - ตอนนี้เล่นออฟไลน์ กลับหน้าแรกแล้วเล่นออนไลน์อีกครั้งเพื่อเจอเพื่อน", 8f);
            return;
        }
        if (link == null)
        {
            if (Time.time < reconnectAt) return;
            if (reconnectTries >= ReconnectTries)
            {
                reconnecting = false;
                Hint("ต่อเซิร์ฟเวอร์ไม่ได้ - ตอนนี้เล่นออฟไลน์ (กลับหน้าแรกเพื่อเล่นออนไลน์อีกครั้ง)", 7f);
                return;
            }
            reconnectTries++;
            link = NetLink.Open(NetLink.ServerUrl());
            helloSent = false;
            reconnectStart = Time.time;
            return;
        }
        if (link.Connected && !helloSent) { Send(new NetMsg { t = "hello", name = playerName }); helloSent = true; }
        bool stuck = myId == 0 && (link.Dead || Time.time - reconnectStart > 12f);
        bool noLogin = reauthPending && Time.time - reconnectStart > 25f;
        if (stuck || noLogin)
        {
            link.Close(); link = null; myId = 0; reauthPending = false;
            reconnectAt = Time.time + Mathf.Min(30f, 2f * reconnectTries);
        }
    }

    /// The "welcome" came while getting back online.
    void ReconnectWelcomed()
    {
        if (worldSeed != reconnectSeed) { reconnectAbort = true; return; }
        if (reconnectAcct != null && SavedToken.Length > 0 && SavedUser.Length > 0)
        {
            reauthPending = true;
            Send(new NetMsg { t = "token", name = SavedUser, text = SavedToken });
            return;
        }
        Rejoined();
    }

    /// The answer to the token sent while getting back online.
    void OnReauth(NetMsg m)
    {
        reauthPending = false;
        if (m.hits == 1)
        {
            acctName = m.name;
            PlayerPrefs.SetString("acctToken", m.ev ?? "");
            // a save on its way when the line dropped may have arrived: then the server's version is ours + 1
            if (cloudWaiting && m.ver == cloudVer + 1) cloudVer = m.ver;
            cloudWaiting = false;
            if (m.ver != cloudVer) OnSaveOld(m); // another device saved meanwhile: don't put this one's progress over it
            else { PlayerPrefs.SetInt(VerKey(acctName), cloudVer); cloudDirty = true; cloudPushT = 0f; }
            PlayerPrefs.Save();
        }
        else Hint("ต่อเซิร์ฟเวอร์ใหม่แล้ว แต่ต้องเข้าสู่ระบบใหม่ (ตอนนี้เซฟอยู่ในเครื่องนี้)", 6f);
        Rejoined();
    }

    void Rejoined()
    {
        reconnecting = false;
        if (player != null || voyage != null) JoinRoom(); // a private island waits for "homes" first (IslandGame.Visit.cs)
        Hint("ต่อเซิร์ฟเวอร์ใหม่แล้ว", 3f);
    }
}
