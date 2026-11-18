using UnityEngine;

/// Player accounts: log in with a name and password to play the same character on any device.
/// The server (Server/server.js) keeps a copy of the save file and the kept home island for each
/// account; the game still saves locally as always and sends the server a copy a few seconds after
/// every change (SaveSystem.Changed). Logging in takes whichever copy is newer (SaveData.savedAt).
/// After the first login the device remembers a token, so "play online" logs in by itself.
/// Logging in means playing online; offline play stays as it was (this device only).
public partial class IslandGame
{
    string acctUser = "", acctPass = "";
    string acctName;          // logged in as (null = guest)
    string pendingAuth;       // "login" / "register" / "token": waiting to send it once connected
    bool authSent;
    float authSentAt;
    bool cloudDirty, cloudHooked;
    float cloudPushT;
    // The account save's version on the server, as this device knows it. Every accepted save makes a
    // new one; a device that missed one (another device played meanwhile) gets "saveold" instead of
    // putting older progress back. Remembered per account, so a login can tell whether this device's
    // own save still follows on from the server's or has been overtaken.
    int cloudVer;
    bool cloudWaiting, cloudStale; // a save is on its way / the server has newer progress than this device
    float cloudWaitT;
    static string VerKey(string acct) => "cloudVer:" + (acct ?? "").ToLowerInvariant();

    bool LoggedIn => acctName != null && Online;
    static string SavedToken => PlayerPrefs.GetString("acctToken", "");
    static string SavedUser => PlayerPrefs.GetString("acctUser", "");

    // ------------------------------------------------------------------ title screen
    void AccountLogin(bool register)
    {
        acctUser = (acctUser ?? "").Trim();
        if (acctUser.Length < 3) { netStatus = "ใส่ชื่อบัญชีอย่างน้อย 3 ตัว"; return; }
        if ((acctPass ?? "").Length < 4) { netStatus = "ใส่รหัสผ่านอย่างน้อย 4 ตัว"; return; } // older accounts may have 4
        if (register && acctPass.Length < 8) { netStatus = "รหัสผ่านใหม่ต้องยาวอย่างน้อย 8 ตัว"; return; } // the server asks 8 for new accounts
        pendingAuth = register ? "register" : "login";
        authSent = false;
        PlayOnline();
        netStatus = register ? "กำลังสมัครบัญชี..." : "กำลังเข้าสู่ระบบ...";
    }

    void AccountLogout()
    {
        string tok = SavedToken;
        if (link != null && link.Connected && tok.Length > 0) Send(new NetMsg { t = "logout", ev = tok });
        PlayerPrefs.DeleteKey("acctToken");
        PlayerPrefs.Save();
        acctName = null;
        netStatus = "ออกจากระบบแล้ว";
    }

    /// Before the game starts online: log in (with the password typed, or the device's token).
    /// Returns true while still waiting for the server's answer.
    bool TitleAuthStep()
    {
        if (pendingAuth == null) return false;
        if (!authSent)
        {
            if (pendingAuth == "token") Send(new NetMsg { t = "token", name = SavedUser, text = SavedToken });
            else Send(new NetMsg { t = pendingAuth, name = acctUser, text = acctPass });
            authSent = true;
            authSentAt = Time.time;
            return true;
        }
        if (Time.time - authSentAt > 15f) // an older server doesn't know accounts: play as a guest
        {
            pendingAuth = null;
            netStatus = "";
            AddChat("", "เซิร์ฟเวอร์นี้ยังไม่รองรับบัญชีผู้เล่น เล่นแบบไม่ล็อกอินไปก่อน");
            BeginOnline(SaveSystem.Read());
            return true;
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
        if (m.hits != 1)
        {
            if (byToken) // the device's login ran out: carry on as a guest
            {
                PlayerPrefs.DeleteKey("acctToken");
                PlayerPrefs.Save();
                BeginOnline(SaveSystem.Read());
                Hint("ต้องเข้าสู่ระบบใหม่ (ตอนนี้เล่นแบบไม่ล็อกอิน เซฟอยู่ในเครื่องนี้)", 5f);
                return;
            }
            netStatus = string.IsNullOrEmpty(m.why) ? "เข้าสู่ระบบไม่สำเร็จ" : m.why;
            if (link != null) link.Close();
            link = null; myId = 0; connecting = false;
            return;
        }
        acctName = m.name;
        acctUser = m.name;
        PlayerPrefs.SetString("acctUser", m.name);
        PlayerPrefs.SetString("acctToken", m.ev ?? "");

        var cloud = SaveSystem.Parse(m.text);
        var local = SaveSystem.Read();
        string owner = PlayerPrefs.GetString("saveOwner", "");
        bool guest = owner.Length == 0;                       // played without logging in on this device
        bool localMine = guest || owner == m.name.ToLowerInvariant();
        // This device's save may only replace the account's if it follows on from the version the
        // server has now: if another device has saved since (the version moved on), the server's wins,
        // however new this device's clock says its save is. A game from before versions has none
        // remembered (-1): then, once, the newer save wins as before.
        int baseVer = PlayerPrefs.GetInt(VerKey(m.name), -1);
        bool follows = baseVer < 0 ? true : baseVer == m.ver;
        cloudVer = m.ver; cloudWaiting = false; cloudStale = false;
        SaveData start;
        // the account's character wins over a guest's (maybe someone else's) progress; a guest's
        // progress only fills an account that has nothing yet
        if (cloud != null && (local == null || guest || !localMine || !follows || cloud.savedAt >= local.savedAt))
        {
            SaveSystem.WriteRaw(m.text, m.room);
            PlayerPrefs.SetInt(VerKey(m.name), m.ver); // this device now holds the server's version
            start = cloud;
        }
        else if (local != null && localMine)
        {
            start = local;
            cloudDirty = true; cloudPushT = 0f; // the account gets this device's progress
        }
        else
        {
            SaveSystem.WriteRaw("", m.room); // a new account on someone else's device: their save isn't yours
            start = null;
        }
        PlayerPrefs.SetString("saveOwner", m.name.ToLowerInvariant());
        PlayerPrefs.Save();
        HookCloud();
        BeginOnline(start);
        Hint($"เข้าสู่ระบบแล้ว: {acctName} เล่นตัวนี้ต่อจากเครื่องไหนก็ได้", 5f);
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
        if (acctName != null) { PlayerPrefs.SetInt(VerKey(acctName), cloudVer); PlayerPrefs.Save(); }
    }

    /// The server has newer progress than this device (it played on another device after this one
    /// loaded): this device stops sending, so it can't put the older progress back.
    void OnSaveOld(NetMsg m)
    {
        cloudWaiting = false;
        cloudStale = true;
        Hint("บัญชีนี้มีความคืบหน้าใหม่กว่าจากอีกเครื่อง เครื่องนี้หยุดเซฟขึ้นบัญชีแล้ว กลับหน้าแรกแล้วเล่นออนไลน์ใหม่เพื่อโหลดตัวล่าสุด", 8f);
    }

    void DrawAccount(float x, float y, float w)
    {
        var note = new GUIStyle(small) { wordWrap = true };
        ShadowLabel(new Rect(x, y, w, 20), "บัญชีผู้เล่น  <size=11>(เล่นตัวเดิมได้จากทุกเครื่อง)</size>", label, new Color(0.7f, 0.9f, 1f));
        y += 22;
        if (acctName != null || (SavedToken.Length > 0 && SavedUser.Length > 0))
        {
            string who = acctName ?? SavedUser;
            ShadowLabel(new Rect(x, y + 2, w - 110, 22), $"เข้าสู่ระบบเป็น <b>{who}</b>", label, new Color(0.75f, 1f, 0.75f));
            if (CrispGUI.Button(new Rect(x + w - 104, y, 104, 24), "ออกจากระบบ")) AccountLogout();
            ShadowLabel(new Rect(x, y + 28, w, 36), "กด \"เล่นออนไลน์\" แล้วจะได้ตัวละครจากบัญชีนี้", note, new Color(1f, 1f, 1f, 0.7f));
            return;
        }
        var field = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
        ShadowLabel(new Rect(x, y + 3, 70, 20), "ชื่อบัญชี", small);
        acctUser = GUI.TextField(new Rect(x + 72, y, w - 72, 24), acctUser ?? "", 20, field);
        y += 28;
        ShadowLabel(new Rect(x, y + 3, 70, 20), "รหัสผ่าน", small);
        acctPass = GUI.PasswordField(new Rect(x + 72, y, w - 72, 24), acctPass ?? "", '*', 32, field);
        y += 30;
        float bw = (w - 6) / 2f;
        if (CrispGUI.Button(new Rect(x, y, bw, 26), "เข้าสู่ระบบ")) AccountLogin(false);
        if (CrispGUI.Button(new Rect(x + bw + 6, y, bw, 26), "สมัครบัญชีใหม่")) AccountLogin(true);
    }
}
