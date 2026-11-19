using System;
using UnityEngine;

[Serializable] public class CharInfo { public int used; public string name; public int look, lv; public long at, born; } // born: when the server made it (0 = the one from before characters)
[Serializable] public class CharList { public CharInfo[] list; }

/// The title screens: log in (or play without an account), then pick one of the account's three
/// characters, or make a new one. Each character has its own save on the server (Server/server.js,
/// "char") and its own files on this device (SaveSystem.Profile), so it plays online or offline.
/// The character list is kept on this device too, so the characters can be played offline when the
/// server can't be reached.
public partial class IslandGame
{
    enum TitleScreen { Login, Chars, Create }
    TitleScreen titleScreen;
    bool guestMode;            // playing without an account (the creator screen with online / offline)
    CharInfo[] chars;          // the account's characters (from the server, or kept on this device)
    int charSlot = -1;         // picked on the character screen
    int createSlot = -1;       // the empty slot the creator fills
    int pendingChar = -1;      // asked the server for this character ("char"), or will once logged in
    bool pendingCreate;        // make the character once logged in
    string newCharName = "";   // the creator's name box (account characters)
    string oldCharName = "";   // a name for a character from before names (starts as this device's old name)
    bool takeGuest = true;     // the creator moves the no-account progress into the new character
    int guestLevel;            // that progress's level (0 = there is none)
    float delArmUntil;         // "delete" pressed once: press again before this to delete
    /// The character being played: "<account>" for the first slot (the account's save from before
    /// characters), "<account>#<slot>" for the others. null = playing without an account.
    string charId;
    int charSlotPlaying = -1;

    const int CharSlots = 3;
    static string CharIdFor(string acct, int slot) => slot == 0 ? acct.ToLowerInvariant() : acct.ToLowerInvariant() + "#" + slot;
    static string CharsKey(string acct) => "chars:" + (acct ?? "").ToLowerInvariant();
    string TitleAcct => acctName ?? (SavedToken.Length > 0 ? SavedUser : null);
    bool TitleOnline => acctName != null && link != null && link.Connected && myId > 0;
    CharInfo CharAt(int i) => chars != null && i >= 0 && i < chars.Length && chars[i] != null && chars[i].used == 1 ? chars[i] : null;

    // ------------------------------------------------------------------ connecting from the title screen
    /// Opens the connection; `auth` = "login" / "register" / "token" (null = no account).
    void TitleConnect(string auth)
    {
        if (link != null) link.Close();
        link = NetLink.Open(NetLink.ServerUrl());
        helloSent = false;
        myId = 0;
        pendingAuth = auth;
        authSent = false;
        connecting = true;
        connectStart = Time.time;
        netStatus = "กำลังเชื่อมต่อ...";
    }

    void CloseTitleLink()
    {
        if (link != null) link.Close();
        link = null; myId = 0; connecting = false; pendingAuth = null;
        acctName = null;
    }

    /// Stop trying to connect (the button that says "cancel" while connecting).
    void CancelConnect()
    {
        CloseTitleLink();
        pendingChar = -1; pendingCreate = false;
        netStatus = titleScreen == TitleScreen.Chars && chars != null ? "ยกเลิกแล้ว (เล่นออฟไลน์ได้)" : "";
    }

    /// The connection couldn't be made, or dropped, on the title screen.
    void TitleConnectFailed(string why)
    {
        CloseTitleLink();
        pendingChar = -1; pendingCreate = false;
        netStatus = why;
    }

    /// Logged in: carry on with what the player pressed before the connection was up.
    void TitleAuthDone()
    {
        if (pendingCreate) { pendingCreate = false; SendCreate(); return; }
        if (pendingChar >= 0) SendChar(pendingChar);
    }

    // ------------------------------------------------------------------ the character list
    void OnCharList(string json)
    {
        CharList cl = null;
        try { cl = JsonUtility.FromJson<CharList>(json ?? ""); } catch { }
        if (cl == null || cl.list == null) return;
        chars = new CharInfo[CharSlots];
        for (int i = 0; i < CharSlots; i++) chars[i] = i < cl.list.Length && cl.list[i] != null ? cl.list[i] : new CharInfo();
        if (TitleAcct != null) { PlayerPrefs.SetString(CharsKey(TitleAcct), JsonUtility.ToJson(new CharList { list = chars })); PlayerPrefs.Save(); }
        if (CharAt(charSlot) == null) { charSlot = -1; for (int i = 0; i < CharSlots; i++) if (CharAt(i) != null) { charSlot = i; break; } }
    }

    void LoadCachedChars(string acct)
    {
        chars = null; charSlot = -1;
        string json = PlayerPrefs.GetString(CharsKey(acct), "");
        if (json.Length > 0) OnCharList(json);
    }

    /// "chars": the list after making or deleting a character.
    void OnChars(NetMsg m)
    {
        if (!string.IsNullOrEmpty(m.text)) OnCharList(m.text);
        if (!string.IsNullOrEmpty(m.why)) { netStatus = m.why; return; }
        int slot = m.hits;
        if (titleScreen == TitleScreen.Create && CharAt(slot) != null) // made: back to the list with it picked
        {
            string id = CharIdFor(TitleAcct, slot);
            WipeLocalChar(id); // nothing of an older one in this slot
            PlayerPrefs.SetString(BornKey(id), CharAt(slot).born.ToString());
            if (takeGuest && guestLevel > 0) // the progress played without an account becomes this character's
            {
                string prof = SaveSystem.ProfileFor(id);
                SaveSystem.MoveGuestInto(prof);
                MoveCharPrefs("", prof);
                guestLevel = 0;
            }
            PlayerPrefs.Save();
            charSlot = slot; createSlot = -1;
            titleScreen = TitleScreen.Chars;
            netStatus = $"สร้าง {CharAt(slot).name} แล้ว";
            Sfx.Play("pickup", 0.4f);
        }
        else if (CharAt(slot) == null && TitleAcct != null) // deleted: its files on this device go too
        {
            WipeLocalChar(CharIdFor(TitleAcct, slot));
            PlayerPrefs.Save();
            netStatus = "ลบตัวละครแล้ว";
        }
    }

    static string BornKey(string id) => "charBorn:" + id;
    static readonly string[] CharPrefKeys = { "tut_first_steps", "starter_pet_chosen" }; // IslandGame.Feel.cs / Pet.cs, per character

    /// A character's files and remembered state on this device, gone.
    static void WipeLocalChar(string id)
    {
        string prof = SaveSystem.ProfileFor(id);
        SaveSystem.DeleteProfile(prof);
        PlayerPrefs.DeleteKey(VerKey(id));
        PlayerPrefs.DeleteKey(BornKey(id));
        foreach (var k in CharPrefKeys) PlayerPrefs.DeleteKey(k + prof);
    }

    /// The tutorial step and "starter pet asked" go with a save that moves (and the files it left start over).
    static void MoveCharPrefs(string fromProfile, string toProfile)
    {
        foreach (var k in CharPrefKeys)
        {
            if (PlayerPrefs.HasKey(k + fromProfile) && !PlayerPrefs.HasKey(k + toProfile)) PlayerPrefs.SetInt(k + toProfile, PlayerPrefs.GetInt(k + fromProfile));
            PlayerPrefs.DeleteKey(k + fromProfile);
        }
    }

    /// Before characters, a logged-in account's save sat in the no-account files, marked with its
    /// owner ("saveOwner"). It moves into that account's first character straight away, so playing
    /// without an account never plays (or overwrites) someone's account save.
    static void MoveOldAccountSave()
    {
        string owner = PlayerPrefs.GetString("saveOwner", "");
        if (owner.Length == 0) return;
        string prof = SaveSystem.ProfileFor(CharIdFor(owner, 0));
        if (!SaveSystem.MoveGuestInto(prof)) return; // (tried again next time)
        MoveCharPrefs("", prof);
        PlayerPrefs.DeleteKey("saveOwner");
        PlayerPrefs.Save();
    }

    /// No character list kept on this device yet (the first visit since characters), but this
    /// device has the account's old save: it shows as the first character, so it plays offline too.
    void OldSaveAsFirstChar(string acct)
    {
        if (chars != null) return;
        var old = SaveSystem.ReadProfile(SaveSystem.ProfileFor(CharIdFor(acct, 0)));
        if (old == null) return;
        chars = new CharInfo[CharSlots];
        for (int i = 0; i < CharSlots; i++) chars[i] = new CharInfo();
        chars[0] = new CharInfo { used = 1, name = "", look = 0, lv = old.level };
        charSlot = 0;
    }

    void SendCreate()
    {
        string name = CleanName(newCharName);
        Send(new NetMsg { t = "charnew", hits = createSlot, name = name, look = look.Code });
        netStatus = "กำลังสร้างตัวละคร...";
    }

    void SendChar(int slot)
    {
        var c = CharAt(slot);
        pendingChar = slot;
        string name = c != null && !string.IsNullOrEmpty(c.name) ? c.name : CleanName(oldCharName);
        Send(new NetMsg { t = "char", hits = slot, name = name, look = c != null && c.look != 0 ? c.look : look.Code });
        netStatus = "กำลังโหลดตัวละคร...";
    }

    /// Becomes that character on this device: its files, its name and its look.
    void UseCharacter(string acct, int slot, string name, int lookC, long born)
    {
        charId = CharIdFor(acct, slot);
        charSlotPlaying = slot;
        SaveSystem.Profile = SaveSystem.ProfileFor(charId);
        // files on this device from an older character in this slot (deleted, maybe on another
        // device, and a new one made) are not this one's: they go, so they can't reach its save
        string want = born.ToString(), had = PlayerPrefs.GetString(BornKey(charId), "");
        if (born != 0 && had != want) WipeLocalChar(charId);
        PlayerPrefs.SetString(BornKey(charId), want);
        playerName = CleanName(name);
        Lang.Name(playerName); // my own name stays as I typed it in English too
        if (lookC != 0) look = CharacterLook.FromCode(lookC);
        if (!CharacterLook.CanChoose && look.preset == 0) look = CharacterLook.Default;
        lookCode = look.Code;
    }

    /// "char": the server's copy of the character picked. The newer of it and this device's wins.
    void OnCharSave(NetMsg m)
    {
        if (reconnecting && reauthChar) { OnReauthChar(m); return; } // getting back online (IslandGame.Reconnect.cs)
        if (!title || pendingChar < 0) return;
        pendingChar = -1;
        if (m.hits < 0) { netStatus = string.IsNullOrEmpty(m.why) ? "โหลดตัวละครไม่สำเร็จ" : m.why; return; }
        int slot = m.hits;
        var c = CharAt(slot);
        if (c != null) { c.name = m.name; c.look = m.look; }
        UseCharacter(acctName, slot, m.name, m.look, c != null ? c.born : 0);

        var cloud = SaveSystem.Parse(m.text);
        var local = SaveSystem.Read();
        // This device's save may only replace the server's if it follows on from the version the
        // server has now: if another device has saved since (the version moved on), the server's wins,
        // however new this device's clock says its save is. A save from before versions has none
        // remembered (-1): then, once, the newer save wins.
        int baseVer = PlayerPrefs.GetInt(VerKey(charId), -1);
        bool follows = baseVer < 0 || baseVer == m.ver;
        cloudVer = m.ver; cloudWaiting = false; cloudStale = false;
        SaveData start;
        if (cloud != null && (local == null || !follows || cloud.savedAt >= local.savedAt))
        {
            SaveSystem.WriteRaw(m.text, m.room);
            PlayerPrefs.SetInt(VerKey(charId), m.ver); // this device now holds the server's version
            start = cloud;
        }
        else if (local != null)
        {
            start = local;
            cloudDirty = true; cloudPushT = 0f; // the server gets this device's progress (played offline)
        }
        else
        {
            SaveSystem.WriteRaw("", m.room); // a new character
            PlayerPrefs.SetInt(VerKey(charId), m.ver);
            start = null;
        }
        PlayerPrefs.Save();
        HookCloud();
        BeginOnline(start);
        Hint($"เข้าเล่นเป็น {playerName} (บัญชี {acctName})", 4f);
    }

    // ------------------------------------------------------------------ the play buttons
    void PlayCharOnline(int slot)
    {
        if (!CharReady(slot)) return;
        if (TitleOnline) { SendChar(slot); return; }
        if (SavedToken.Length == 0) { titleScreen = TitleScreen.Login; netStatus = "เข้าสู่ระบบอีกครั้งเพื่อเล่นออนไลน์"; return; }
        pendingChar = slot;
        TitleConnect("token");
    }

    void PlayCharOffline(int slot)
    {
        if (!CharReady(slot) || TitleAcct == null) return;
        var c = CharAt(slot);
        string acct = TitleAcct;
        CloseTitleLink();
        if (string.IsNullOrEmpty(c.name)) { c.name = CleanName(oldCharName); c.look = look.Code; } // named on this device; the server learns it next time online
        UseCharacter(acct, slot, c.name, c.look, c.born);
        PlayerPrefs.SetString(CharsKey(acct), JsonUtility.ToJson(new CharList { list = chars }));
        PlayerPrefs.Save();
        title = false;
        StartGame(SaveSystem.Read());
        Hint($"เล่นออฟไลน์เป็น {playerName} - เซฟในเครื่องนี้ แล้วส่งขึ้นบัญชีตอนเล่นออนไลน์ครั้งหน้า", 6f);
    }

    /// A character picked, and named (one from before names needs a name first).
    bool CharReady(int slot)
    {
        var c = CharAt(slot);
        if (c == null) { netStatus = "เลือกตัวละครก่อน"; return false; }
        if (string.IsNullOrEmpty(c.name) && (oldCharName ?? "").Trim().Length == 0) { netStatus = "ตั้งชื่อให้ตัวละครนี้ก่อน"; return false; }
        return true;
    }

    void GuestPlayOnline()
    {
        KeepCharacter();
        charId = null; SaveSystem.Profile = "";
        TitleConnect(null);
    }

    void GuestPlayOffline()
    {
        KeepCharacter();
        charId = null; SaveSystem.Profile = "";
        CloseTitleLink();
        title = false;
        StartGame(SaveSystem.Read());
    }

    void OpenCreator(int slot)
    {
        createSlot = slot;
        newCharName = "";
        var guest = SaveSystem.ReadProfile("");
        guestLevel = guest != null ? Mathf.Max(1, guest.level) : 0;
        takeGuest = true;
        look = CharacterLook.Default;
        titleScreen = TitleScreen.Create;
        netStatus = "";
    }

    // ------------------------------------------------------------------ drawing
    /// The title screen: a wooden frame and sign, then the screen it's on (looks: IslandGame.TitleUi.cs).
    void DrawTitle(float W, float H)
    {
        var frame = new Rect(Mathf.Round(W / 2f - 330), 20, 660, 514);
        Fill(new Rect(frame.x + 12, frame.y + 12, frame.width - 24, frame.height - 24), new Color(0.03f, 0.1f, 0.18f, 0.72f));
        WoodFrame(frame, 12, 3);
        Vine(frame.x + 6, frame.y + 14, 1, 1);
        Vine(frame.xMax - 10, frame.y + 14, -1, 1);
        Vine(frame.x + 6, frame.yMax - 56, 1, 1);
        Vine(frame.xMax - 10, frame.yMax - 56, -1, 1);

        var sign = new Rect(Mathf.Round(W / 2f - 210), 2, 420, 80);
        WoodBoard(sign, 5);
        ShadowLabel(new Rect(sign.x, sign.y + 6, sign.width, 44), "<b>Island Survival</b>", new GUIStyle(titleCenter) { fontSize = 36 }, TitleGold);
        ShadowLabel(new Rect(sign.x + 16, sign.y + 50, sign.width - 32, 22), "ตั้งชื่อตัวละครของคุณ แล้วล่องเรือจากเกาะสู่เกาะไปกับเพื่อนๆ", new GUIStyle(labelCenter) { fontSize = 13 }, Color.white);

        GUI.enabled = !connecting && pendingChar < 0;
        if (titleScreen == TitleScreen.Login) DrawLoginScreen(frame);
        else if (titleScreen == TitleScreen.Chars) DrawCharsScreen(frame);
        else DrawCreateScreen(frame);
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(netStatus))
            ShadowLabel(new Rect(frame.x + 30, 462, frame.width - 60, 32), netStatus, new GUIStyle(smallCenter) { wordWrap = true }, new Color(1f, 0.9f, 0.6f));
        string built = BuildTimeText();
        if (built != null) ShadowLabel(new Rect(frame.x + 20, 506, frame.width - 40, 14), built, new GUIStyle(smallCenter) { fontSize = 10 }, new Color(1f, 1f, 1f, 0.5f));
    }

    /// The bottom-right button: "cancel" while connecting, else whatever `label` does.
    bool CancelOr(Rect r, string label, Color tint, string[] icon, Color iconCol)
    {
        if (connecting || pendingChar >= 0)
        {
            GUI.enabled = true;
            if (StoneButton(r, "ยกเลิก", new Color(0.38f, 0.28f, 0.18f), 17)) CancelConnect();
            return false;
        }
        return StoneButton(r, label, tint, 17, icon, iconCol);
    }

    static readonly Color StoneGrey = new Color(0.32f, 0.32f, 0.34f);
    static readonly Color StoneBlue = new Color(0.19f, 0.27f, 0.39f);
    static readonly Color StoneBrown = new Color(0.38f, 0.28f, 0.18f);
    static readonly Color StoneRed = new Color(0.42f, 0.18f, 0.15f);

    /// The character walking on its island in a frame (the login and creator screens).
    void DrawStage(Rect stage, int code, bool turnable)
    {
        WoodFrame(stage, 10, 21);
        var view = new Rect(stage.x + 10, stage.y + 10, stage.width - 20, stage.height - 20);
        Vector2 feet = IslandStage(view);
        int[] walk = { 0, 1, 0, 2 };
        int frameNo = PixelArt.WalkFrames(code) == 4 ? 1 + (int)(Time.time * 8f) % 4 : walk[(int)(Time.time * 4f) % 4];
        SpriteStanding(PixelArt.Player(turnable ? previewDir : 0, frameNo, null, null, code), feet, 178f);
        if (!turnable) return;
        if (GUI.Button(view, GUIContent.none, GUIStyle.none)) previewDir = 1 - previewDir;
        if (StoneButton(new Rect(stage.x, stage.yMax + 6, stage.width, 28), previewDir == 0 ? "หันหลัง" : "หันหน้า", StoneGrey, 13, IconTurn))
            previewDir = 1 - previewDir;
    }

    // ---- 1. log in
    void DrawLoginScreen(Rect frame)
    {
        DrawStage(new Rect(frame.x + 30, 96, 236, 302), look.Code, false);
        float x = frame.x + 290, w = 340, y = 96;
        Heading(new Rect(x, y, w, 22), "เข้าสู่ระบบ", TextAnchor.MiddleCenter);
        ShadowLabel(new Rect(x, y + 24, w, 18), "หนึ่งบัญชีสร้างได้ 3 ตัวละคร · เล่นได้ทั้งออนไลน์และออฟไลน์", new GUIStyle(smallCenter) { fontSize = 12 }, new Color(1f, 1f, 1f, 0.75f));
        bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.enabled;
        acctUser = Slot(new Rect(x, y + 52, w, 34), acctUser, 20, IconPerson, "ชื่อบัญชี");
        acctPass = Slot(new Rect(x, y + 94, w, 34), acctPass, 32, IconLock, "รหัสผ่าน", true);
        if (enter && (acctUser ?? "").Length > 0 && (acctPass ?? "").Length > 0) { AccountLogin(false); Event.current.Use(); }
        if (StoneButton(new Rect(x, y + 138, w, 30), "ยังไม่มีบัญชี? สมัครบัญชีใหม่", StoneGrey, 13)) AccountLogin(true);

        Fill(new Rect(x, y + 192, w, 1), new Color(1f, 1f, 1f, 0.2f));
        ShadowLabel(new Rect(x, y + 200, w, 18), "ไม่อยากสมัคร? เล่นแบบไม่ล็อกอินได้ (เซฟอยู่ในเบราว์เซอร์นี้เท่านั้น)", new GUIStyle(smallCenter) { fontSize = 12 }, new Color(1f, 1f, 1f, 0.65f));

        if (StoneButton(new Rect(frame.x + 30, 410, 356, 48), "เข้าสู่ระบบ", StoneBlue, 17, IconPerson, new Color(0.85f, 0.95f, 1f))) AccountLogin(false);
        if (CancelOr(new Rect(frame.x + 396, 410, 234, 48), "เล่นแบบไม่ล็อกอิน", StoneBrown, IconDoor, new Color(0.3f, 0.2f, 0.12f)))
        {
            guestMode = true;
            titleScreen = TitleScreen.Create;
            netStatus = "";
        }
    }

    // ---- 2. pick a character
    void DrawCharsScreen(Rect frame)
    {
        const float gap = 15f;
        float cw = (600f - gap * 2) / 3f, top = 92f, ch = 236f;
        Vector2 mouse = Event.current.mousePosition;
        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 4f);
        for (int i = 0; i < CharSlots; i++)
        {
            var r = new Rect(Mathf.Round(frame.x + 30 + i * (cw + gap)), top, Mathf.Round(cw), ch);
            var c = CharAt(i);
            bool picked = c != null && charSlot == i, hover = GUI.enabled && r.Contains(mouse);
            if (picked)
            {
                Fill(new Rect(r.x - 6, r.y - 6, r.width + 12, r.height + 12), new Color(1f, 0.85f, 0.3f, 0.25f * pulse));
                Fill(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(1f, 0.88f, 0.35f, 0.95f));
            }
            else if (hover) Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(1f, 1f, 1f, 0.5f));
            WoodFrame(r, 8, 40 + i * 9);
            var view = new Rect(r.x + 8, r.y + 8, r.width - 16, r.height - 16);
            if (c != null)
            {
                Vector2 feet = IslandStage(view);
                int code = c.look != 0 ? c.look : look.Code;
                int frameNo = picked ? (PixelArt.WalkFrames(code) == 4 ? 1 + (int)(Time.time * 8f) % 4 : new[] { 0, 1, 0, 2 }[(int)(Time.time * 4f) % 4]) : 0;
                GUI.color = picked || hover ? Color.white : new Color(0.85f, 0.85f, 0.88f);
                SpriteStanding(PixelArt.Player(0, frameNo, null, null, code), new Vector2(feet.x, feet.y - 6), 140f);
                GUI.color = Color.white;
                // the name plate
                var plate = new Rect(view.x, view.yMax - 44, view.width, 44);
                Fill(plate, new Color(0.05f, 0.04f, 0.03f, 0.78f));
                Fill(new Rect(plate.x, plate.y, plate.width, 2), picked ? TitleGold : new Color(0.5f, 0.36f, 0.2f));
                string name = string.IsNullOrEmpty(c.name) ? "ตัวละครเดิม (ยังไม่มีชื่อ)" : c.name;
                ShadowLabel(new Rect(plate.x + 4, plate.y + 3, plate.width - 8, 22), $"<b>{name}</b>", new GUIStyle(labelCenter) { fontSize = 15 }, picked ? TitleGold : Color.white);
                ShadowLabel(new Rect(plate.x + 4, plate.y + 23, plate.width - 8, 18), $"เลเวล {Mathf.Max(1, c.lv)}", new GUIStyle(smallCenter) { fontSize = 12 }, new Color(1f, 1f, 1f, 0.75f));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none) && !picked) { charSlot = i; delArmUntil = 0f; netStatus = ""; Sfx.Play("pickup", 0.3f); }
            }
            else
            {
                Fill(view, hover ? new Color(0.14f, 0.2f, 0.32f) : new Color(0.08f, 0.12f, 0.2f));
                float ps = 6f;
                var plus = new[] { "..XX..", "..XX..", "XXXXXX", "XXXXXX", "..XX..", "..XX.." };
                PixelIcon(view.center.x - 18, view.center.y - 40, ps, plus, new Color(1f, 1f, 1f, hover ? 0.9f : 0.5f), Color.clear, Color.clear);
                ShadowLabel(new Rect(view.x, view.center.y + 4, view.width, 22), "สร้างตัวละคร", new GUIStyle(labelCenter) { fontSize = 15 }, new Color(1f, 1f, 1f, hover ? 1f : 0.7f));
                ShadowLabel(new Rect(view.x, view.center.y + 26, view.width, 18), $"ช่องที่ {i + 1}", new GUIStyle(smallCenter) { fontSize = 12 }, new Color(1f, 1f, 1f, 0.5f));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) OpenCreator(i);
            }
        }
        if (chars == null)
            ShadowLabel(new Rect(frame.x + 30, top + ch / 2f - 60, 600, 20), connecting ? "กำลังโหลดตัวละคร..." : "ยังไม่มีรายชื่อตัวละครในเครื่องนี้ - เล่นออนไลน์เพื่อโหลด", new GUIStyle(labelCenter), TitleGold);

        // the account row
        float y = top + ch + 10;
        string acct = TitleAcct ?? "";
        string state = TitleOnline ? "<color=#9dff9d>ออนไลน์</color>" : connecting ? "กำลังเชื่อมต่อ..." : "<color=#ffcf8a>ออฟไลน์</color>";
        ShadowLabel(new Rect(frame.x + 30, y + 4, 300, 22), $"บัญชี <b>{acct}</b>  ·  {state}", new GUIStyle(label) { fontSize = 14, richText = true });
        var sel = CharAt(charSlot);
        if (sel != null)
        {
            bool armed = Time.time < delArmUntil;
            if (StoneButton(new Rect(frame.x + 368, y, 124, 30), armed ? "กดอีกครั้งเพื่อลบ" : "ลบตัวละคร", armed ? new Color(0.6f, 0.15f, 0.12f) : StoneRed, 13))
            {
                if (!armed) { delArmUntil = Time.time + 3f; netStatus = $"จะลบ {(string.IsNullOrEmpty(sel.name) ? "ตัวละครนี้" : sel.name)} และความคืบหน้าทั้งหมด - กดอีกครั้งเพื่อยืนยัน"; }
                else if (!TitleOnline) { delArmUntil = 0f; netStatus = "ต้องออนไลน์ก่อนถึงจะลบตัวละครได้"; }
                else { delArmUntil = 0f; Send(new NetMsg { t = "chardel", hits = charSlot }); netStatus = "กำลังลบ..."; }
            }
        }
        if (StoneButton(new Rect(frame.x + 500, y, 130, 30), "ออกจากระบบ", StoneGrey, 13)) AccountLogout();

        if (sel != null && string.IsNullOrEmpty(sel.name)) // a character from before names: named once
            oldCharName = Slot(new Rect(frame.x + 30, y + 36, 600, 28), oldCharName, 16, IconPen, "ตั้งชื่อให้ตัวละครนี้ (ใช้ชื่อนี้ตลอดไป)", false, true);

        GUI.enabled = GUI.enabled && sel != null;
        if (StoneButton(new Rect(frame.x + 30, 410, 356, 48), "เล่นออนไลน์ (เจอผู้เล่นอื่น)", StoneBlue, 17, IconGlobe, new Color(0.85f, 0.95f, 1f))) PlayCharOnline(charSlot);
        GUI.enabled = !connecting && pendingChar < 0 && sel != null; // (while connecting the button is "cancel": CancelOr)
        if (CancelOr(new Rect(frame.x + 396, 410, 234, 48), "เล่นออฟไลน์", StoneBrown, IconDoor, new Color(0.3f, 0.2f, 0.12f))) PlayCharOffline(charSlot);
    }

    // ---- 3. make a character (or, without an account, the one to play)
    void DrawCreateScreen(Rect frame)
    {
        DrawStage(new Rect(frame.x + 30, 96, 236, 268), look.Code, true);
        float x = frame.x + 290, w = 340, y = 92;
        Heading(new Rect(x, y, w, 20), guestMode ? "เลือกตัวละคร" : $"สร้างตัวละคร  <size=11>(ช่องที่ {createSlot + 1})</size>", TextAnchor.MiddleCenter);
        DrawPortraitCards(x + 4, y + 26, w - 8, 98);
        ShadowLabel(new Rect(x, y + 130, w, 18), $"<b>{CharacterLook.Presets[Mathf.Clamp(look.preset, 0, CharacterLook.Presets.Length - 1)]}</b>  <size=11>· หมวก/ชุดที่สวมไม่แสดงบนตัว แต่ยังได้ค่าพลัง</size>",
            new GUIStyle(smallCenter) { richText = true }, new Color(1f, 1f, 1f, 0.85f));
        if (guestMode) playerName = Slot(new Rect(x, y + 152, w, 32), playerName, 16, IconPen, "ชื่อตัวละคร", false, true);
        else newCharName = Slot(new Rect(x, y + 152, w, 32), newCharName, 16, IconPen, "ชื่อตัวละคร", false, true);
        var note = new GUIStyle(small) { wordWrap = true, fontSize = 12 };
        if (guestMode)
        {
            ShadowLabel(new Rect(x, y + 196, w, 54), "ไม่ล็อกอิน: เซฟอยู่ในเบราว์เซอร์นี้เท่านั้น ถ้าล้างเบราว์เซอร์หรือเปลี่ยนเครื่องจะเริ่มใหม่ — สมัครบัญชีเพื่อเล่นตัวเดิมได้ทุกเครื่อง", note, new Color(1f, 1f, 1f, 0.7f));
            if (StoneButton(new Rect(x, y + 258, w, 30), "‹ กลับไปหน้าเข้าสู่ระบบ", StoneGrey, 13)) { guestMode = false; titleScreen = TitleScreen.Login; netStatus = ""; }
            if (StoneButton(new Rect(frame.x + 30, 410, 356, 48), "เล่นออนไลน์ (เจอผู้เล่นอื่น)", StoneBlue, 17, IconGlobe, new Color(0.85f, 0.95f, 1f))) GuestPlayOnline();
            if (CancelOr(new Rect(frame.x + 396, 410, 234, 48), "เล่นออฟไลน์", StoneBrown, IconDoor, new Color(0.3f, 0.2f, 0.12f))) GuestPlayOffline();
            return;
        }
        ShadowLabel(new Rect(x, y + 192, w, 34), "ชื่อและหน้าตาจะผูกกับตัวละครนี้ เปลี่ยนภายหลังไม่ได้ · การสร้างตัวละครต้องต่อเซิร์ฟเวอร์", note, new Color(1f, 1f, 1f, 0.7f));
        if (guestLevel > 0) // progress from playing without an account: it can become this character's
        {
            var box = new Rect(x, y + 230, w, 26);
            Fill(new Rect(box.x, box.y + 4, 18, 18), new Color(0.08f, 0.07f, 0.07f));
            Fill(new Rect(box.x + 2, box.y + 6, 14, 14), takeGuest ? TitleGold : new Color(0.25f, 0.25f, 0.27f));
            if (takeGuest) PixelIcon(box.x + 4, box.y + 9, 2f, new[] { ".....X", "....X.", "X..X..", ".XX...", "..X..." }, new Color(0.15f, 0.1f, 0.05f), Color.clear, Color.clear);
            ShadowLabel(new Rect(box.x + 26, box.y, w - 26, 26), $"ย้ายความคืบหน้าจากการเล่นแบบไม่ล็อกอิน (เลเวล {guestLevel}) มาเป็นของตัวนี้", new GUIStyle(small) { fontSize = 12, alignment = TextAnchor.MiddleLeft }, Color.white);
            if (GUI.Button(box, GUIContent.none, GUIStyle.none)) takeGuest = !takeGuest;
        }
        if (StoneButton(new Rect(x, y + 264, w, 28), "‹ กลับไปเลือกตัวละคร", StoneGrey, 13)) { createSlot = -1; titleScreen = TitleScreen.Chars; netStatus = ""; }
        if (StoneButton(new Rect(frame.x + 30, 410, 356, 48), "สร้างตัวละคร", StoneBlue, 17, IconPen, new Color(0.85f, 0.95f, 1f)))
        {
            if ((newCharName ?? "").Trim().Length == 0) netStatus = "ตั้งชื่อตัวละครก่อน";
            else if (TitleOnline) SendCreate();
            else if (SavedToken.Length == 0) { titleScreen = TitleScreen.Login; netStatus = "เข้าสู่ระบบอีกครั้งเพื่อสร้างตัวละคร"; }
            else { pendingCreate = true; TitleConnect("token"); }
        }
        if (CancelOr(new Rect(frame.x + 396, 410, 234, 48), "กลับ", StoneBrown, IconDoor, new Color(0.3f, 0.2f, 0.12f))) { createSlot = -1; titleScreen = TitleScreen.Chars; netStatus = ""; }
    }
}
