using System.Collections.Generic;
using UnityEngine;

/// Crews (online). A player with a boat can take others on as crew: you apply to someone's crew
/// or they invite you, and the other side says yes. On an island a crew member goes up to the
/// captain and climbs aboard; when the captain sets sail, everyone aboard sails along on the
/// captain's boat and lands on the same island. Anyone not aboard stays behind, and the captain
/// has to come back for them. A crew lasts while you're both online.
///
/// Messages go through the server's "crew" relay (Server/server.js), which reaches a player
/// wherever they are, at sea too: ev = apply / invite / added / accepted / no / leave / kick /
/// board / unboard / sail / pos / land / boat. Every player's boat is moored at a spot of their
/// own, so the captain tells the crew on the island where it is ("boat") and they see it there.
public partial class IslandGame
{
    // as a crew member
    int crewCaptain;                 // the captain's id (0 = not in a crew)
    string crewCaptainName = "";
    bool aboard;                     // climbed aboard the captain's boat (on an island)
    bool crewRide;                   // at sea as a passenger on the captain's boat
    int crewBoatLook;                // what the captain's boat looks like
    Vector2 crewCaptainRaft;         // where the captain's boat is (passenger)
    float crewPosAt;                 // when it was last heard
    int crewCaptainLook;
    Vector2 capBoatGrid;             // where the captain's boat is moored on this island
    int capBoatLook, capBoatRoom;
    float capBoatAt = -100f;         // when the captain last told us
    SpriteRenderer capBoatSr;        // the captain's boat, drawn on our copy of the island
    // as a captain
    readonly List<(int id, string name)> crewMates = new List<(int, string)>();
    readonly HashSet<int> crewAboard = new HashSet<int>();
    readonly Dictionary<int, int> crewLooks = new Dictionary<int, int>();
    float crewPosT, crewBoatT;
    bool crewLandSent;
    readonly List<SpriteRenderer> crewDeck = new List<SpriteRenderer>();
    // either
    bool crewOpen;
    (int id, string name, string ev) crewAsk;          // someone asking (apply / invite), id 0 = none
    readonly Dictionary<int, float> crewAsked = new Dictionary<int, float>(); // asked, waiting for an answer (when): no spamming
    const float CrewAskWait = 20f;   // no answer this long: the button comes back

    /// Asked this player and still waiting for their answer.
    bool CrewAskPending(int id) => crewAsked.TryGetValue(id, out float at) && Time.time - at < CrewAskWait;

    const float BoardReach = 2.5f;   // tiles from the captain to climb aboard
    const float AboardLeash = 8f;    // walk further than this from the captain and you're off the boat

    bool IsCaptain => crewMates.Count > 0;
    /// How to climb aboard on this device (phones have no L key).
    string BoardHow => touchUsed ? "แตะปุ่ม \"ขึ้นเรือ\"" : "กด L (หรือคลิกปุ่ม \"ขึ้นเรือ\")";
    bool InCrew => crewCaptain != 0;

    void SendCrew(int to, string ev, int island = 0, int d = 0, float x = 0f, float y = 0f, int look = 0)
    {
        if (!Online || to <= 0) return;
        Send(new NetMsg { t = "crew", to = to, ev = ev, name = playerName, island = island, d = d, x = x, y = y, look = look, lv = BoatLookNow });
    }

    void OnCrew(NetMsg m)
    {
        string who = string.IsNullOrEmpty(m.name) ? "ผู้เล่น" : m.name;
        if (m.ev is "added" or "accepted" or "no" or "leave" or "kick") crewAsked.Remove(m.from); // answered: they can be asked again later
        switch (m.ev)
        {
            case "roster": // the captain's crew list
            case "cave":   // a crewmate went into a cave (IslandGame.Cave.cs)
                OnCaveCrew(m, who);
                break;
            case "apply": // someone wants to join my crew
                if (raftLevel <= 0) { SendCrew(m.from, "no"); return; }
                crewAsk = (m.from, who, "apply");
                Sfx.Play("pickup", 0.6f);
                break;
            case "invite": // a captain wants me
                if (InCrew) { SendCrew(m.from, "no"); return; }
                crewAsk = (m.from, who, "invite");
                Sfx.Play("pickup", 0.6f);
                break;
            case "added": // the captain took me on
            case "accepted": // the player I invited said yes
                if (m.ev == "added") { JoinCrew(m.from, who); Hint($"เป็นลูกเรือของ {who} แล้ว! เดินไปที่เรือของกัปตัน (หรือตัวกัปตัน) แล้ว{BoardHow}", 5f); }
                else { AddMate(m.from, who); crewBoatT = 0f; Hint($"{who} มาเป็นลูกเรือแล้ว", 4f); }
                break;
            case "no":
                Hint($"{who} ไม่รับคำขอลูกเรือ", 3f);
                break;
            case "leave": // a member left me
                RemoveMate(m.from);
                Hint($"{who} ออกจากลูกเรือแล้ว", 3f);
                break;
            case "kick": // my captain let me go
                if (m.from != crewCaptain) return;
                LeaveCrewLocal();
                Hint($"{who} ให้คุณออกจากลูกเรือแล้ว", 4f);
                break;
            case "board":
                if (!crewMates.Exists(c => c.id == m.from)) return;
                crewAboard.Add(m.from);
                crewLooks[m.from] = m.look;
                Hint($"{who} ขึ้นเรือแล้ว", 2.5f);
                break;
            case "unboard":
                crewAboard.Remove(m.from);
                break;
            case "sail": // my captain sets sail with me aboard
                if (m.from != crewCaptain || !aboard || voyage != null || dead) return;
                aboard = false;
                crewRide = true;
                crewBoatLook = m.lv;
                crewCaptainLook = m.look;
                crewCaptainRaft = new Vector2(-3f, 0f);
                crewPosAt = Time.time;
                dialog = null; craftOpen = false; skillsOpen = false; bagOpen = false;
                destIsland = Mathf.Max(1, m.island);
                destContinent = Mathf.Clamp(m.d, 0, Continents - 1);
                if (m.x >= 1f) ChannelOnSail(Mathf.RoundToInt(m.x), destIsland, destContinent); // land in the captain's channel (IslandGame.Channel.cs)
                voyageVisit = null;
                SaveGame();
                BeginSailFade(false);
                Hint($"ออกเรือไปกับ {crewCaptainName}! (เรือของกัปตันเป็นคนพาย คุณช่วยสู้ได้)", 5f);
                Sfx.Play("win", 0.6f);
                break;
            case "pos":
                if (m.from != crewCaptain || !crewRide) return;
                crewCaptainRaft = new Vector2(m.x, m.y);
                crewPosAt = Time.time;
                break;
            case "land":
                if (m.from != crewCaptain || !crewRide || voyage == null || arriving) return;
                BeginSailFade(true);
                break;
            case "boat": // where my captain's boat is moored on the island we're both on
                if (m.from != crewCaptain || voyage != null) return;
                capBoatGrid = new Vector2(m.x, m.y);
                capBoatLook = m.lv;
                capBoatRoom = m.island;
                capBoatAt = Time.time;
                break;
        }
    }

    void JoinCrew(int captain, string name)
    {
        crewCaptain = captain; crewCaptainName = name;
        aboard = false;
        crewMates.Clear(); crewAboard.Clear(); // a crew member isn't a captain as well
    }

    void AddMate(int id, string name)
    {
        if (!crewMates.Exists(c => c.id == id)) crewMates.Add((id, name));
    }

    void RemoveMate(int id)
    {
        crewMates.RemoveAll(c => c.id == id);
        crewAboard.Remove(id);
    }

    void LeaveCrewLocal()
    {
        crewCaptain = 0; crewCaptainName = ""; aboard = false;
        capBoatAt = -100f;
    }

    /// The captain's boat is known and on this island.
    bool CapBoatHere => InCrew && voyage == null && player != null && Time.time - capBoatAt < 8f && capBoatRoom == RoomKey;

    /// Close enough to climb aboard: next to the captain, or next to the captain's boat.
    bool NearCaptainOrBoat(float reach)
    {
        if (player == null) return false;
        if (remotes.TryGetValue(crewCaptain, out var cap) && cap != null && (cap.grid - player.grid).magnitude <= reach) return true;
        return CapBoatHere && (capBoatGrid - player.grid).magnitude <= reach + 0.6f;
    }

    /// Stepping on and off the boat (L next to the captain).
    void ToggleAboard()
    {
        if (!InCrew) { crewOpen = !crewOpen; return; }
        if (aboard)
        {
            aboard = false;
            SendCrew(crewCaptain, "unboard");
            Hint("ลงจากเรือแล้ว ถ้ากัปตันออกเรือ คุณจะไม่ได้ไปด้วย", 3f);
            return;
        }
        if (!remotes.TryGetValue(crewCaptain, out var cap) || cap == null || player == null)
        {
            crewOpen = true;
            Hint($"กัปตัน {crewCaptainName} ไม่ได้อยู่บนเกาะนี้ ต้องรอให้กัปตันมารับ", 3.5f);
            return;
        }
        if (!NearCaptainOrBoat(BoardReach))
        {
            Hint(CapBoatHere ? $"เดินไปที่เรือของ {crewCaptainName} (มีป้ายบอกอยู่) หรือไปหาตัวกัปตัน แล้ว{BoardHow}"
                             : $"เดินไปให้ใกล้กัปตัน {crewCaptainName} ก่อน แล้ว{BoardHow}", 3f);
            return;
        }
        aboard = true;
        SendCrew(crewCaptain, "board", look: lookCode);
        Hint(touchUsed ? "ขึ้นเรือแล้ว! เมื่อกัปตันออกเรือ คุณจะไปด้วย (แตะ \"ลงจากเรือ\" เพื่อลง)" : "ขึ้นเรือแล้ว! เมื่อกัปตันออกเรือ คุณจะไปด้วย (L อีกครั้งเพื่อลงจากเรือ)", 4f);
        Sfx.Play("pickup", 0.6f);
    }

    /// The captain sets sail: everyone aboard comes along. Called from ChooseVoyage.
    void CrewSail(int dest, int c)
    {
        crewLandSent = false;
        if (!IsCaptain) return;
        int n = 0;
        foreach (var id in crewAboard)
            if (remotes.ContainsKey(id)) { SendCrew(id, "sail", dest, c, x: SailChannel, look: lookCode); n++; } // x: the channel we all sail in
        int left = crewMates.Count - n;
        if (n > 0) Hint($"ออกเรือพร้อมลูกเรือ {n} คน" + (left > 0 ? $" (อีก {left} คนไม่ได้ขึ้นเรือ ต้องกลับมารับ)" : ""), 4f);
        else if (crewMates.Count > 0) Hint("ไม่มีลูกเรือขึ้นเรือ ลูกเรือจะรออยู่ที่เกาะนี้", 3.5f);
        crewAboard.RemoveWhere(id => !remotes.ContainsKey(id));
    }

    /// Every frame on an island: walking away from the captain takes you off the boat; members
    /// who left the island are no longer aboard.
    void TickCrew()
    {
        if (!Online) { if (InCrew || IsCaptain) { LeaveCrewLocal(); crewMates.Clear(); crewAboard.Clear(); } ShowCapBoat(); return; }
        if (voyage != null || player == null) return;
        if (crewRide) crewRide = false; // landed
        if (aboard)
        {
            if (!remotes.TryGetValue(crewCaptain, out var cap) || cap == null) { aboard = false; Hint("กัปตันออกจากเกาะไปแล้ว รอให้กัปตันกลับมารับ", 4f); }
            else if (!NearCaptainOrBoat(AboardLeash)) { aboard = false; SendCrew(crewCaptain, "unboard"); Hint($"เดินห่างกัปตันและเรือเกินไป ลงจากเรือแล้ว (กลับไปที่เรือแล้ว{BoardHow})", 4f); }
        }
        if (crewAboard.Count > 0) crewAboard.RemoveWhere(id => !remotes.ContainsKey(id));
    }

    /// Every frame on an island, even with the sea chart or another window open (TickCrew is
    /// skipped then): the captain tells the crew where the boat is moored, the crew draws it.
    void CrewBoatTick()
    {
        if (!Online || voyage != null || player == null) { ShowCapBoat(); return; }
        crewBoatT -= Time.deltaTime;
        if (IsCaptain && crewBoatT <= 0f)
        {
            crewBoatT = 2f;
            var raft = RaftNode;
            if (raft != null && raftLevel > 0 && !atHome)
                foreach (var c in crewMates)
                    if (remotes.ContainsKey(c.id)) SendCrew(c.id, "boat", RoomKey, x: raft.grid.x, y: raft.grid.y);
        }
        ShowCapBoat();
    }

    /// Crew member: draw the captain's boat where it's moored on our copy of the island.
    void ShowCapBoat()
    {
        bool show = CapBoatHere && world != null;
        if (!show) { if (capBoatSr != null) capBoatSr.enabled = false; return; }
        if (capBoatSr == null) // made fresh on each island (the old one went with the old island)
        {
            capBoatSr = new GameObject("CaptainBoat").AddComponent<SpriteRenderer>();
            capBoatSr.transform.SetParent(world);
        }
        capBoatSr.enabled = true;
        capBoatSr.sprite = PixelArt.Boat(capBoatLook);
        Vector3 flat = GridToWorld(capBoatGrid);
        float bob = Mathf.Sin(Time.time * 2f) * 0.03f;
        capBoatSr.transform.position = flat + Vector3.up * (HeightAt(capBoatGrid) + bob);
        capBoatSr.sortingOrder = SortOrder(flat.y);
        // if my own boat is moored on the same spot, step the captain's aside so both show
        var mine = RaftNode;
        if (mine != null && (mine.grid - capBoatGrid).magnitude < 1.2f) capBoatSr.transform.position += new Vector3(0.9f, 0f, 0f);
    }

    /// A sign over the captain's boat (crew members only).
    void DrawCaptainBoatTag(float s)
    {
        if (capBoatSr == null || !capBoatSr.enabled || !CapBoatHere) return;
        var gp = ToGui(capBoatSr.transform.position + Vector3.up * (PixelArt.BoatHeight(capBoatLook) * 0.8f + 0.2f), s);
        bool near = NearCaptainOrBoat(BoardReach);
        string text = aboard ? $"เรือของ {crewCaptainName} <color=#9dff9d>(คุณอยู่บนเรือ)</color>" : $"เรือของ {crewCaptainName}";
        float bounce = aboard || near ? 0f : Mathf.Sin(Time.time * 3f) * 2f;
        ShadowLabel(new Rect(gp.x - 120, gp.y - 44 + bounce, 240, 18), text, smallCenter, new Color(0.75f, 0.95f, 1f));
        if (!aboard && !near) return;
        var st = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
        st.normal.textColor = aboard ? Color.white : new Color(1f, 0.9f, 0.4f);
        var br = new Rect(gp.x - 60, gp.y - 24, 120, 26);
        if (CrispGUI.Button(br, aboard ? "ลงจากเรือ" : touchUsed ? "ขึ้นเรือ" : "ขึ้นเรือ (L)", st)) ToggleAboard();
    }

    /// At sea, every frame after the boat moved. A passenger's boat goes where the captain's is;
    /// the captain tells the passengers where that is and shows them standing on deck.
    void CrewVoyageTick(float dt)
    {
        var v = voyage;
        if (v == null) return;
        if (crewRide)
        {
            if (Time.time - crewPosAt < 6f)
            {
                v.raft = Vector2.Lerp(v.raft, crewCaptainRaft, 1f - Mathf.Exp(-8f * dt));
                v.vel = Vector2.zero;
            }
            else if (crewPosAt > 0f) // the captain's gone quiet: take the oars yourself
            {
                crewPosAt = 0f;
                crewRide = false;
                Hint("ติดต่อกัปตันไม่ได้ คุณต้องพายเรือต่อเอง", 4f);
            }
            if (v.raftSr != null && crewRide) v.raftSr.sprite = PixelArt.Boat(crewBoatLook);
            ShowDeck(new List<int> { crewCaptainLook }, v);
            return;
        }
        if (!IsCaptain || crewAboard.Count == 0) { ShowDeck(null, v); return; }
        crewPosT -= dt;
        if (crewPosT <= 0f)
        {
            crewPosT = 0.15f;
            foreach (var id in crewAboard) SendCrew(id, "pos", x: v.raft.x, y: v.raft.y);
        }
        if (!crewLandSent && v.raft.x >= v.length)
        {
            crewLandSent = true;
            foreach (var id in crewAboard) SendCrew(id, "land");
        }
        var looks = new List<int>();
        foreach (var id in crewAboard) looks.Add(crewLooks.TryGetValue(id, out var lk) ? lk : 0);
        ShowDeck(looks, v);
    }

    /// The others on the boat, standing beside you on the deck.
    void ShowDeck(List<int> looks, Voyage v)
    {
        int n = looks?.Count ?? 0;
        crewDeck.RemoveAll(s => s == null);
        while (crewDeck.Count < n)
        {
            var sr = new GameObject("Crewmate").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(world);
            crewDeck.Add(sr);
        }
        float bob = Mathf.Sin(Time.time * 2f) * 0.04f;
        float deck = PixelArt.BoatDeck(crewRide ? crewBoatLook : BoatLookNow);
        for (int i = 0; i < crewDeck.Count; i++)
        {
            var sr = crewDeck[i];
            sr.enabled = i < n;
            if (i >= n) continue;
            float side = (i % 2 == 0 ? -1f : 1f) * (0.35f + 0.25f * (i / 2));
            sr.transform.position = (Vector3)v.raft + new Vector3(side, deck + bob + 0.02f, 0f);
            sr.sprite = PixelArt.Player(0, 0, null, null, looks[i]);
            sr.flipX = side > 0f;
            sr.sortingOrder = v.raftSr != null ? v.raftSr.sortingOrder + 1 : 0;
        }
    }

    // ------------------------------------------------------------------ the crew panel
    void DrawCrewButton(float W)
    {
        if (!Online || title || voyage != null || player == null || dead) return;
        var st = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold };
        st.normal.textColor = aboard ? new Color(0.6f, 1f, 0.7f) : new Color(0.7f, 0.9f, 1f);
        st.hover.textColor = Color.white;
        bool canBoard = InCrew && !aboard && NearCaptainOrBoat(BoardReach);
        if (!canBoard && !aboard) return; // otherwise the crew button is in the menu (IslandGame.Menu.cs)
        string label = InCrew ? (aboard ? $"บนเรือ {crewCaptainName} (L)" : canBoard ? "ขึ้นเรือกัปตัน (L)" : $"ลูกเรือของ {crewCaptainName} (L)")
                     : IsCaptain ? $"ลูกเรือ {crewAboard.Count}/{crewMates.Count} บนเรือ (L)" : "ลูกเรือ (L)";
        var r = new Rect(W - 182, hudButtonsBottom + 4f, 172, 22);
        if (canBoard) st.normal.textColor = new Color(1f, 0.9f, 0.4f);
        if (CrispGUI.Button(r, label, st)) { if (canBoard) ToggleAboard(); else crewOpen = !crewOpen; }
        hudButtonsBottom = r.yMax;
    }

    void DrawCrewPanel(float W, float H)
    {
        if (crewAsk.id != 0) DrawCrewAsk(W);
        if (!crewOpen || !Online || title || voyage != null || player == null) return;
        var box = new Rect(W - 372, hudButtonsBottom + 8f, 360, 0f);
        var others = new List<RemotePlayer>();
        foreach (var rp in remotes.Values) if (rp != null) others.Add(rp);
        box.height = 78f + Mathf.Max(1, others.Count) * 28f + (IsCaptain ? 24f : 0f);
        Fill(box, new Color(0.06f, 0.08f, 0.12f, 0.94f));
        Fill(new Rect(box.x, box.y, box.width, 3), new Color(0.5f, 0.8f, 1f, 0.9f));
        var titleSt = new GUIStyle(label) { fontSize = 14, richText = true };
        var line = new GUIStyle(small) { fontSize = 12, richText = true, wordWrap = false };
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 11 };
        ShadowLabel(new Rect(box.x + 10, box.y + 6, 260, 20), "ลูกเรือ", titleSt, new Color(0.7f, 0.9f, 1f));
        if (CrispGUI.Button(new Rect(box.xMax - 62, box.y + 6, 54, 20), "ปิด", btn)) { crewOpen = false; return; }

        string status = InCrew ? $"ลูกเรือของ <b>{crewCaptainName}</b> · {(aboard ? "<color=#9dff9d>อยู่บนเรือ</color>" : "ยังไม่ขึ้นเรือ")}"
                      : IsCaptain ? $"คุณเป็นกัปตัน · ลูกเรือ {crewMates.Count} คน ขึ้นเรือแล้ว {crewAboard.Count} คน"
                      : raftLevel > 0 ? "ชวนคนบนเกาะนี้มาเป็นลูกเรือของคุณ" : "สมัครเป็นลูกเรือของคนที่มีเรือ";
        if (InCrew && !aboard && remotes.ContainsKey(crewCaptain) && !CapBoatHere)
            status += " <color=#ffb08a>(ไม่เห็นเรือ: ให้กัปตันอัปเดตเกม)</color>";
        CrispGUI.Label(new Rect(box.x + 10, box.y + 30, box.width - (InCrew ? 120 : 20), 18), status, line);
        if (InCrew && CrispGUI.Button(new Rect(box.xMax - 104, box.y + 28, 96, 22), aboard ? "ลงจากเรือ" : "ขึ้นเรือ", btn)) ToggleAboard();
        float y = box.y + 52;
        if (others.Count == 0) CrispGUI.Label(new Rect(box.x + 10, y + 4, box.width - 20, 18), "<color=#b8c0c8>ยังไม่มีผู้เล่นคนอื่นบนเกาะนี้</color>", line);
        foreach (var rp in others)
        {
            int id = rp.netId;
            bool mate = crewMates.Exists(c => c.id == id);
            bool isCap = id == crewCaptain;
            string tag = isCap ? " <color=#ffe27a>(กัปตัน)</color>" : mate ? (crewAboard.Contains(id) ? " <color=#9dff9d>(ลูกเรือ · บนเรือ)</color>" : " <color=#bfe4ff>(ลูกเรือ)</color>") : "";
            CrispGUI.Label(new Rect(box.x + 10, y + 4, 200, 18), rp.playerName + tag, line);
            var br = new Rect(box.xMax - 130, y, 120, 22);
            if (mate) { if (CrispGUI.Button(br, "ให้ออกจากลูกเรือ", btn)) { RemoveMate(id); crewAsked.Remove(id); SendCrew(id, "kick"); } }
            else if (isCap) { if (CrispGUI.Button(br, "ออกจากลูกเรือ", btn)) { SendCrew(id, "leave"); crewAsked.Remove(id); LeaveCrewLocal(); } }
            else if (!InCrew && raftLevel > 0)
            {
                bool wait = CrewAskPending(id);
                GUI.enabled = !wait;
                if (CrispGUI.Button(br, wait ? "ชวนแล้ว รอตอบ..." : "ชวนเป็นลูกเรือ", btn)) { crewAsked[id] = Time.time; SendCrew(id, "invite"); Hint($"ชวน {rp.playerName} แล้ว รอตอบรับ", 2.5f); }
                GUI.enabled = true;
            }
            else if (!InCrew && !IsCaptain)
            {
                bool wait = CrewAskPending(id);
                GUI.enabled = !wait;
                if (CrispGUI.Button(br, wait ? "สมัครแล้ว รอตอบ..." : "สมัครเป็นลูกเรือ", btn)) { crewAsked[id] = Time.time; SendCrew(id, "apply"); Hint($"สมัครเป็นลูกเรือของ {rp.playerName} แล้ว รอตอบรับ", 2.5f); }
                GUI.enabled = true;
            }
            y += 28f;
        }
        if (IsCaptain)
            CrispGUI.Label(new Rect(box.x + 10, y + 2, box.width - 20, 18), "<color=#b8c0c8>ออกเรือแล้ว คนบนเรือไปด้วย คนที่ไม่ขึ้นต้องกลับมารับ</color>", line);
    }

    void DrawCrewAsk(float W)
    {
        var box = new Rect(W / 2f - 170f, 150f, 340f, 74f);
        Fill(box, new Color(0.06f, 0.08f, 0.12f, 0.95f));
        Fill(new Rect(box.x, box.y, box.width, 3), new Color(1f, 0.85f, 0.3f, 0.9f));
        var st = new GUIStyle(labelCenter) { fontSize = 13, richText = true, wordWrap = true };
        string q = crewAsk.ev == "apply" ? $"<b>{crewAsk.name}</b> ขอสมัครเป็นลูกเรือของคุณ" : $"<b>{crewAsk.name}</b> ชวนคุณเป็นลูกเรือ";
        CrispGUI.Label(new Rect(box.x + 10, box.y + 8, box.width - 20, 22), q, st);
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 12 };
        if (CrispGUI.Button(new Rect(box.x + 30, box.y + 38, 130, 26), crewAsk.ev == "apply" ? "รับเป็นลูกเรือ" : "ตกลง", btn))
        {
            if (crewAsk.ev == "apply") { AddMate(crewAsk.id, crewAsk.name); SendCrew(crewAsk.id, "added"); crewBoatT = 0.5f; Hint($"{crewAsk.name} เป็นลูกเรือของคุณแล้ว", 3f); }
            else { JoinCrew(crewAsk.id, crewAsk.name); SendCrew(crewAsk.id, "accepted"); Hint($"เป็นลูกเรือของ {crewAsk.name} แล้ว! เดินไปที่เรือของกัปตัน (หรือตัวกัปตัน) แล้ว{BoardHow}", 5f); }
            crewAsk = (0, "", "");
        }
        if (CrispGUI.Button(new Rect(box.xMax - 160, box.y + 38, 130, 26), "ไม่", btn))
        {
            SendCrew(crewAsk.id, "no");
            crewAsk = (0, "", "");
        }
    }
}
