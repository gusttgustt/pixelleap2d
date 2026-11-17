using UnityEngine;

/// Who's online (O, or click the online line top right): how many people are playing on the
/// server in all, and where each of them is - which island on which continent, at sea, or on a
/// private island - with a button to sail to them. You only see other players on the same island
/// (or the same sea), so this is how friends find each other.
public partial class IslandGame
{
    int onlineTotal = -1;          // everyone on the server (-1: not heard yet / older server)
    NetPeer[] whoList;
    bool whoOpen;
    float whoAskAt;
    Vector2 whoScroll;

    void ToggleWho()
    {
        if (!Online) { Hint("ต้องเล่นออนไลน์ก่อนถึงจะเห็นผู้เล่นคนอื่น", 2.5f); return; }
        whoOpen = !whoOpen;
        if (!whoOpen) return;
        craftOpen = false; skillsOpen = false; bagOpen = false;
        whoAskAt = 0f; // ask right away
    }

    /// Ask the server now and then (every few seconds while the list is open).
    void AskWho()
    {
        if (!Online || Time.unscaledTime < whoAskAt) return;
        whoAskAt = Time.unscaledTime + (whoOpen ? 4f : 10f);
        Send(new NetMsg { t = "who" });
    }

    void OnWho(NetMsg m)
    {
        onlineTotal = m.hits;
        whoList = m.players;
    }

    /// Where a room is, in words. n / c: the adventure island and continent (n = 0 when not on one).
    string WhereOf(int isl, int who, out int n, out int c)
    {
        n = 0; c = 0;
        if (isl == 0) return "<color=#aaaaaa>กำลังเดินทาง/โหลด</color>";
        if (isl >= SeaRoomBase) return $"<color=#7fd0ff>ล่องเรือไป{ContinentNames[Mathf.Clamp(isl - SeaRoomBase, 0, Continents - 1)]}</color>";
        if (isl < 0)
        {
            int owner = -isl / 2;
            string inside = (-isl) % 2 == 1 ? " (ในบ้าน)" : "";
            if (owner == who) return $"<color=#9dff9d>เกาะส่วนตัวของตัวเอง{inside}</color>";
            string ownerName = "เพื่อน";
            if (whoList != null) foreach (var p in whoList) if (p != null && p.id == owner) ownerName = p.name;
            return $"<color=#ffb0d0>เยี่ยมบ้าน {ownerName}{inside}</color>";
        }
        c = Mathf.Clamp(isl / 100000, 0, Continents - 1);
        n = isl % 100000;
        return $"เกาะที่ {n} {IslandName(n + c * 31)} <color=#cccccc>({ContinentNames[c]})</color>";
    }

    void DrawWho(float W, float H)
    {
        if (!Online) { whoOpen = false; return; }
        float pw = Mathf.Min(520f, W - 20f), ph = Mathf.Min(420f, H - 140f);
        var box = new Rect(W / 2f - pw / 2f, Mathf.Max(50f, H / 2f - ph / 2f - 30f), pw, ph);
        Fill(box, new Color(0.06f, 0.07f, 0.1f, 0.95f));
        Fill(new Rect(box.x, box.y, box.width, 3f), new Color(0.5f, 1f, 0.6f));
        string total = onlineTotal >= 0 ? $"ออนไลน์ทั้งหมด {onlineTotal} คน" : "กำลังดูว่าใครออนไลน์...";
        ShadowLabel(new Rect(box.x + 14, box.y + 8, pw - 120, 24), $"{total}  <size=12><color=#aaaaaa>(O ปิด)</color></size>", label, new Color(0.6f, 1f, 0.65f));
        if (CrispGUI.Button(new Rect(box.xMax - 40, box.y + 8, 28, 24), "X")) { whoOpen = false; return; }
        ShadowLabel(new Rect(box.x + 14, box.y + 34, pw - 28, 18), "จะเห็นกันในเกมได้ต้องอยู่เกาะเดียวกัน (เลขเกาะและทวีปเดียวกัน) หรือแล่นเรือไปทวีปเดียวกัน", small, new Color(1f, 1f, 1f, 0.65f));

        var list = whoList ?? new NetPeer[0];
        float rowH = 34f;
        var view = new Rect(box.x + 10, box.y + 58, pw - 20, ph - 68);
        var content = new Rect(0, 0, view.width - 16, Mathf.Max(view.height, list.Length * rowH));
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 11, richText = true };
        var rowSt = new GUIStyle(small) { richText = true, fontSize = 13 };
        int myRoom = inRoom ? RoomKey : int.MinValue;
        whoScroll = CrispGUI.BeginScrollView(view, whoScroll, content, false, false);
        for (int i = 0; i < list.Length; i++)
        {
            var p = list[i];
            if (p == null) continue;
            var r = new Rect(0, i * rowH, content.width, rowH - 3);
            bool me = p.c == 1 || p.id == myId;
            bool here = !me && p.isl != 0 && p.isl == myRoom;
            Fill(r, me ? new Color(0.5f, 1f, 0.6f, 0.10f) : here ? new Color(1f, 0.85f, 0.3f, 0.12f) : new Color(1f, 1f, 1f, 0.04f));
            string where = WhereOf(p.isl, p.id, out int n, out int c);
            string who = me ? $"<b>{p.name}</b> <color=#9dff9d>(คุณ)</color>" : here ? $"<b>{p.name}</b> <color=#ffe27a>(อยู่ที่เดียวกับคุณ)</color>" : $"<b>{p.name}</b>";
            ShadowLabel(new Rect(r.x + 8, r.y + 1, r.width * 0.4f, 16), who, rowSt);
            ShadowLabel(new Rect(r.x + 8, r.y + 16, r.width - 130, 16), where, new GUIStyle(rowSt) { fontSize = 11 });
            if (me || here) continue;
            var br = new Rect(r.xMax - 118, r.y + 4, 112, r.height - 8);
            if (n > 0)
            {
                GUI.enabled = voyage == null;
                if (CrispGUI.Button(br, "ล่องเรือไปหา", btn)) { whoOpen = false; SailToward(n, c); }
                GUI.enabled = true;
            }
            else if (p.isl < 0 && -p.isl / 2 == p.id)
            {
                GUI.enabled = voyage == null;
                if (CrispGUI.Button(br, "ไปเยี่ยมบ้าน", btn)) { whoOpen = false; OpenChart(true); }
                GUI.enabled = true;
            }
        }
        if (list.Length == 0) ShadowLabel(new Rect(0, 8, content.width, 20), "กำลังโหลดรายชื่อ...", smallCenter, new Color(1f, 1f, 1f, 0.5f));
        CrispGUI.EndScrollView();
    }

    /// Open the sea chart on a friend's island so you can sail there.
    void SailToward(int n, int c)
    {
        if (voyage != null) return;
        OpenChart();
        ChartContinent(c);
        ChartSelect(n);
        chartScrollGoal = Mathf.Max(0f, chartSel - 1f);
        if (n > ReachOn(c) + 1) Hint($"เกาะที่ {n} อยู่ไกลกว่าที่คุณเคยไป ต้องสำรวจไปทีละเกาะก่อน", 4f);
    }
}
