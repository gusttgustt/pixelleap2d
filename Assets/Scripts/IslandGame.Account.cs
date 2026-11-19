using UnityEngine;

/// Player accounts: log in with a name and password, then pick one of the account's characters
/// (IslandGame.Chars.cs). The server (Server/server.js) keeps a copy of each character's save file
/// and kept home island; the game saves on this device as always (each character in files of its
/// own) and sends the server a copy a few seconds after every change (SaveSystem.Changed).
/// Playing a character online takes whichever copy is newer; playing it offline uses this device's.
/// After the first login the device remembers a token, so the next visit logs in by itself.
public partial class IslandGame
{
    string acctUser = "", acctPass = "";
    string acctName;          // logged in as (null = not, or not connected right now)
    string pendingAuth;       // "login" / "register" / "token": waiting to send it once connected
    bool authSent;
    float authSentAt;
    bool cloudDirty, cloudHooked;
    float cloudPushT;
    // The character save's version on the server, as this device knows it. Every accepted save makes a
    // new one; a device that missed one (another device played meanwhile) gets "saveold" instead of
    // putting older progress back. Remembered per character, so picking it can tell whether this
    // device's own save still follows on from the server's or has been overtaken.
    int cloudVer;
    bool cloudWaiting, cloudStale; // a save is on its way / the server has newer progress than this device
    float cloudWaitT;
    static string VerKey(string charId) => "cloudVer:" + (charId ?? "").ToLowerInvariant();

    bool LoggedIn => acctName != null && Online && charId != null;
    static string SavedToken => PlayerPrefs.GetString("acctToken", "");
    static string SavedUser => PlayerPrefs.GetString("acctUser", "");

    // ------------------------------------------------------------------ title screen
    void AccountLogin(bool register)
    {
        acctUser = (acctUser ?? "").Trim();
        if (acctUser.Length < 3) { netStatus = "ใส่ชื่อบัญชีอย่างน้อย 3 ตัว"; return; }
        if ((acctPass ?? "").Length < 4) { netStatus = "ใส่รหัสผ่านอย่างน้อย 4 ตัว"; return; } // older accounts may have 4
        if (register && acctPass.Length < 8) { netStatus = "รหัสผ่านใหม่ต้องยาวอย่างน้อย 8 ตัว"; return; } // the server asks 8 for new accounts
        TitleConnect(register ? "register" : "login");
        netStatus = register ? "กำลังสมัครบัญชี..." : "กำลังเข้าสู่ระบบ...";
    }

    void AccountLogout()
    {
        string tok = SavedToken;
        if (link != null && link.Connected && tok.Length > 0) Send(new NetMsg { t = "logout", ev = tok });
        PlayerPrefs.DeleteKey("acctToken");
        PlayerPrefs.Save();
        acctName = null;
        CloseTitleLink();
        chars = null; charSlot = -1;
        titleScreen = TitleScreen.Login;
        netStatus = "ออกจากระบบแล้ว";
    }

    /// Before the game starts online: log in (with the password typed, or the device's token).
    /// Returns true while still waiting for the server's answer.
    bool TitleAuthStep()
    {
        if (pendingAuth == null) return false;
        if (!authSent)
        {
            if (pendingAuth == "token") Send(new NetMsg { t = "token", name = SavedUser, text = SavedToken, kind = 1 });
            else Send(new NetMsg { t = pendingAuth, name = acctUser, text = acctPass, kind = 1 });
            authSent = true;
            authSentAt = Time.time;
            return true;
        }
        if (Time.time - authSentAt > 15f) // no answer (an older server doesn't know accounts)
        {
            pendingAuth = null;
            CloseTitleLink();
            netStatus = "เซิร์ฟเวอร์ไม่ตอบการเข้าสู่ระบบ ลองใหม่อีกครั้ง หรือเล่นออฟไลน์";
        }
        return true;
    }

    void BeginOnline(SaveData data)
    {
        connecting = false;
        title = false;
        StartGame(data);
    }

    void OnAuth(NetMsg m)
    {
        if (reauthPending) { OnReauth(m); return; } // logging in again after a drop (IslandGame.Reconnect.cs)
        if (!title || pendingAuth == null) return;
        bool byToken = pendingAuth == "token";
        pendingAuth = null;
        acctPass = "";
        connecting = false;
        if (m.hits != 1)
        {
            CloseTitleLink();
            if (byToken) // the device's login ran out: log in again
            {
                PlayerPrefs.DeleteKey("acctToken");
                PlayerPrefs.Save();
                chars = null; charSlot = -1;
                titleScreen = TitleScreen.Login;
                netStatus = "ต้องเข้าสู่ระบบใหม่อีกครั้ง";
                return;
            }
            netStatus = string.IsNullOrEmpty(m.why) ? "เข้าสู่ระบบไม่สำเร็จ" : m.why;
            return;
        }
        acctName = m.name;
        acctUser = m.name;
        PlayerPrefs.SetString("acctUser", m.name);
        PlayerPrefs.SetString("acctToken", m.ev ?? "");
        PlayerPrefs.Save();
        netStatus = "";
        OnCharList(m.text); // the account's characters (IslandGame.Chars.cs)
        titleScreen = titleScreen == TitleScreen.Create && createSlot >= 0 ? TitleScreen.Create : TitleScreen.Chars;
        if (m.kind != 1) { netStatus = "เซิร์ฟเวอร์นี้ยังไม่รองรับตัวละครหลายตัว"; CloseTitleLink(); return; }
        TitleAuthDone();
    }

    // ------------------------------------------------------------------ keeping the server's copy
    void HookCloud()
    {
        if (cloudHooked) return;
        cloudHooked = true;
        SaveSystem.Changed += () => cloudDirty = true;
    }

    void TickCloud(float dt)
    {
        cloudPushT -= dt;
        if (cloudWaiting && (cloudWaitT -= dt) <= 0f) cloudWaiting = false; // no answer: try again
        if (!cloudDirty || !LoggedIn || cloudStale || cloudWaiting || cloudPushT > 0f) return;
        if (Flag(DevSaveFlag)) { cloudDirty = false; return; } // played with the dev keys: stays on this device (IslandGame.Quest.cs)
        cloudDirty = false;
        cloudPushT = 8f; // at most every 8 seconds (the game autosaves every 30)
        cloudWaiting = true; cloudWaitT = 20f; // the next save builds on the version this one gets
        Send(new NetMsg { t = "save", text = SaveSystem.RawSave(), room = SaveSystem.RawKeep(), ver = cloudVer });
    }

    /// The server took the save: it's version m.ver now.
    void OnSaved(NetMsg m)
    {
        cloudWaiting = false;
        cloudVer = m.ver;
        if (charId != null) { PlayerPrefs.SetInt(VerKey(charId), cloudVer); PlayerPrefs.Save(); }
    }

    /// The server has newer progress than this device (it played on another device after this one
    /// loaded): this device stops sending, so it can't put the older progress back.
    void OnSaveOld(NetMsg m)
    {
        cloudWaiting = false;
        cloudStale = true;
        Hint("บัญชีนี้มีความคืบหน้าใหม่กว่าจากอีกเครื่อง เครื่องนี้หยุดเซฟขึ้นบัญชีแล้ว กลับหน้าแรกแล้วเล่นออนไลน์ใหม่เพื่อโหลดตัวล่าสุด", 8f);
    }
}
