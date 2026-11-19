using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// The sea chart (using your boat, or V): the world's four continents as tabs; on each, a picture
/// of the sea with that continent's adventure islands along the route. Islands you've been to are
/// drawn in their own colours with their names, the next new island has a star, and the ones
/// further out are grey shapes in the fog marked "ยังไม่เคยไป". The first spot on the route is
/// where your private island sits (or could be moved to), and under the sea are your friends'
/// islands on that continent: pick one and sail there (past the sharks, IslandGame.Visit.cs).
public partial class IslandGame
{
    int chartC;                    // the continent shown
    int chartSel = 1;              // the island picked (0 = the private-island spot), -1 = a friend's
    NetPeer chartFriend;           // the friend's island picked
    float chartScroll, chartScrollGoal;
    const float ChartGap = 112f;   // pixels between islands on the chart
    const int ChartFog = 3;        // unknown islands shown past the next new one

    int ChartLast => Mathf.Min(ReachOn(chartC) + 1 + ChartFog, IslandsPerContinent); // the boss island is the last (IslandGame.Continent.cs)

    void OpenChart(bool friends = false)
    {
        if (inCave) { Hint("ออกจากถ้ำก่อน แล้วค่อยเปิดแผนที่ทะเล", 2.5f); return; } // no boat down here (IslandGame.Cave.cs)
        dialog = new Dialog { chart = true, speaker = raftLevel > 0 ? $"แผนที่โลก - {BoatName(raftLevel)} Lv {raftLevel}" : "แผนที่โลก" };
        craftOpen = false; skillsOpen = false; bagOpen = false;
        chartC = atHome ? homeContinent : continent;
        chartFriend = null;
        int reachHere = ReachOn(chartC);
        chartSel = atHome ? 0 : CanReachNew(chartC) ? reachHere + 1 : island > 1 ? island - 1 : Mathf.Max(1, reachHere);
        if (!atHome && chartSel == island && chartC == continent) chartSel = reachHere + 1;
        // Keep the island you're on in view: if the next new island is too far along the route
        // to show both, start on the island you're on instead.
        if (!atHome && chartC == continent && chartSel - island > 4) chartSel = island;
        chartScrollGoal = chartScroll = Mathf.Max(0f, (atHome ? chartSel : Mathf.Min(chartSel, island)) - 1f);
        RequestFriends(); // online: whose islands to show (IslandGame.Visit.cs)
        if (friends) Hint(Online ? "บ้านเพื่อนอยู่ด้านล่างแผนที่ เลือกทวีปด้านบนเพื่อดูบ้านเพื่อนที่อื่น" : "การไปบ้านเพื่อนต้องเล่นออนไลน์", 3.5f);
    }

    bool CanReachNew(int c) => raftLevel > 0 && raftLevel >= BoatNeedOn(c); // IslandGame.World.cs

    enum ChartState { Here, Visited, Next, Locked, Fog }

    ChartState StateOf(int n, int c)
    {
        if (!atHome && n == island && c == continent) return ChartState.Here;
        int r = ReachOn(c);
        if (n <= r) return ChartState.Visited;
        if (n > IslandsPerContinent) return ChartState.Fog;
        if (n == r + 1) return CanReachNew(c) && ContinentOpen(c) ? ChartState.Next : ChartState.Locked;
        return ChartState.Fog;
    }

    /// Sailing to an adventure island needs your boat (you're standing by it on an island).
    bool CanSailTo(int n, int c, out string why)
    {
        why = null;
        var st = StateOf(n, c);
        if (st is ChartState.Here or ChartState.Locked or ChartState.Fog) { if (!ContinentOpen(c)) why = ContinentLockWhy(c); return false; }
        if (atHome) { why = $"เรือของคุณจอดอยู่ที่เกาะ {island} ({ContinentName}) กด H กลับไปที่เรือก่อน"; return false; }
        if (raftLevel <= 0) { why = "ต้องมีแพหรือเรือก่อน"; return false; }
        return true;
    }

    void ChartKeys(Keyboard kb)
    {
        if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) ChartSelect(chartSel <= 0 ? 0 : chartSel - 1);
        if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) ChartSelect(chartSel < 0 ? 0 : chartSel + 1);
        for (int c = 0; c < Continents; c++)
            if (kb[(Key)((int)Key.Digit1 + c)].wasPressedThisFrame) ChartContinent(c);
        if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) ChartGo();
    }

    void ChartSelect(int n)
    {
        chartFriend = null;
        chartSel = Mathf.Clamp(n, 0, ChartLast);
        Sfx.Play("pickup", 0.2f);
    }

    void ChartContinent(int c)
    {
        if (c == chartC) return;
        chartC = c;
        chartFriend = null;
        chartSel = !atHome && c == continent ? island : c == homeContinent ? 0 : 1;
        chartScrollGoal = chartScroll = Mathf.Max(0f, chartSel - 1f);
        Sfx.Play("pickup", 0.3f);
    }

    /// Enter / the big button: do what the picked spot offers.
    void ChartGo()
    {
        if (chartFriend != null) { SailToFriend(chartFriend); return; }
        if (chartSel == 0)
        {
            if (chartC == homeContinent) { if (!atHome) { CloseDialog(); ToggleHome(); } return; }
            MoveHomeTo(chartC);
            return;
        }
        if (!CanSailTo(chartSel, chartC, out var why)) { if (why != null) Hint(why, 3f); return; }
        ChooseVoyage(chartSel, chartC);
    }

    // ------------------------------------------------------------------ drawing
    void DrawChart(float W, float H)
    {
        float pw = Mathf.Min(820f, W - 16f);
        float ph = Mathf.Min(500f, H - 80f - 60f);
        var box = new Rect(W / 2f - pw / 2f, Mathf.Max(80f, (H - ph) / 2f - 30f), pw, ph);
        Fill(box, new Color(0.08f, 0.06f, 0.04f, 0.97f));
        Fill(new Rect(box.x, box.y, box.width, 3), new Color(1f, 0.85f, 0.3f, 0.9f));
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 13, richText = true };
        ShadowLabel(new Rect(box.x + 14, box.y + 6, pw - 330, 22), dialog.speaker, label, new Color(1f, 0.85f, 0.3f));
        if (CrispGUI.Button(new Rect(box.xMax - 82, box.y + 5, 72, 24), "ปิด (E)", btn)) { CloseDialog(); return; }
        string where = atHome ? $"เกาะส่วนตัว ({ContinentNames[homeContinent]})" : $"เกาะที่ {island} {IslandName(island + continent * 31)} ({ContinentNames[continent]})";
        var hereSt = new GUIStyle(small) { richText = true, alignment = TextAnchor.MiddleRight };
        if (CrispGUI.Button(new Rect(box.xMax - 300, box.y + 5, 212, 24), $"<color=#9dff9d>ตอนนี้อยู่:</color> {where}", new GUIStyle(btn) { fontSize = 11 }))
        { // jump the chart back to where you are
            if (atHome) { ChartContinent(homeContinent); ChartSelect(0); }
            else { ChartContinent(continent); ChartSelect(island); }
            chartScrollGoal = Mathf.Max(0f, chartSel - 1f);
        }

        // ---- continents: a pixel-art picture of each, with its weather (IslandGame.ChartArt.cs)
        float tabY = box.y + 34f, tabW = (pw - 20f) / Continents, tabH = Mathf.Clamp(ph * 0.2f, 56f, 96f);
        for (int c = 0; c < Continents; c++)
        {
            var r = new Rect(box.x + 10 + c * tabW + 2f, tabY, tabW - 4f, tabH);
            bool on = c == chartC;
            int r0 = ReachOn(c), friends = 0;
            foreach (var f in chartFriends) if (f != null && f.c == c) friends++;
            var marks = new List<string>();
            if (c == homeContinent) marks.Add("<color=#9dff9d>บ้าน</color>");
            if (!atHome && c == continent) marks.Add("<color=#ffe27a>คุณอยู่</color>");
            if (friends > 0) marks.Add($"<color=#ffb0d0>เพื่อน {friends}</color>");
            string sub = (r0 > 0 ? $"ไปมาแล้ว {r0} เกาะ" : "<color=#b8c0c8>ยังไม่เคยมา</color>") + (marks.Count > 0 ? " · " + string.Join(" · ", marks) : "");
            if (ContinentCard(r, c, on, sub)) ChartContinent(c);
        }

        // ---- the sea and the route
        float friendsH = 42f, infoH = 108f, mapY = tabY + tabH + 8f;
        var map = new Rect(box.x + 10, mapY, pw - 20, ph - (mapY - box.y) - friendsH - infoH - 10f);
        int visible = Mathf.Max(3, Mathf.FloorToInt(map.width / ChartGap));
        float maxScroll = Mathf.Max(0f, ChartLast - visible + 1f);
        if (chartSel >= 0)
        {
            if (chartSel < chartScrollGoal + 0.5f) chartScrollGoal = chartSel - 0.5f;
            if (chartSel > chartScrollGoal + visible - 1.5f) chartScrollGoal = chartSel - visible + 1.5f;
        }
        var e = Event.current;
        if (e.type == EventType.ScrollWheel && map.Contains(e.mousePosition)) { chartScrollGoal += (e.delta.y + e.delta.x) * 0.15f; e.Use(); }
        chartScrollGoal = Mathf.Clamp(chartScrollGoal, 0f, maxScroll);
        chartScroll = Mathf.Lerp(chartScroll, chartScrollGoal, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));

        CrispGUI.BeginGroup(map);
        Fill(new Rect(0, 0, map.width, map.height), Color.Lerp(new Color(0.13f, 0.36f, 0.55f), ContinentColor[chartC], 0.12f));
        for (int i = 0; i < 60; i++) // waves drifting slowly
        {
            float wx = Mathf.Repeat(i * 97.3f - chartScroll * ChartGap * 0.6f + Time.unscaledTime * 6f, map.width + 40f) - 20f;
            float wy = Mathf.Repeat(i * 53.7f, map.height - 8f) + 4f;
            Fill(new Rect(wx, wy, 10f + (i % 3) * 4f, 2f), new Color(0.45f, 0.7f, 0.9f, 0.35f));
        }
        float baseX = 84f - chartScroll * ChartGap;
        float midY = map.height * 0.4f;
        Vector2 At(int n) => new Vector2(baseX + n * ChartGap, midY + Mathf.Sin(n * 1.7f) * map.height * 0.1f);

        int reachC = ReachOn(chartC);
        for (int n = 0; n < ChartLast; n++) // the route, dotted
        {
            Vector2 a = At(n), b = At(n + 1);
            if (b.x < -60f || a.x > map.width + 60f) continue;
            var dot = n + 1 <= reachC + 1 ? new Color(1f, 0.95f, 0.75f, 0.85f) : new Color(1f, 1f, 1f, 0.25f);
            for (int s = 2; s < 11; s++)
            {
                var p = Vector2.Lerp(a, b, s / 12f);
                Fill(new Rect(p.x - 1.5f, p.y - 1.5f, 3f, 3f), dot);
            }
        }
        float fogX = At(reachC + 1).x + ChartGap * 0.5f; // fog over what you haven't found
        if (fogX < map.width)
            for (int i = 0; i < 8; i++)
                Fill(new Rect(fogX + i * 14f, 0, map.width - fogX - i * 14f + 1f, map.height), new Color(0.04f, 0.07f, 0.12f, 0.08f));

        var tag = new GUIStyle(smallCenter) { fontSize = 12, richText = true, wordWrap = false };
        int first = Mathf.Max(0, Mathf.FloorToInt(chartScroll) - 1), last = Mathf.Min(ChartLast, Mathf.CeilToInt(chartScroll + visible) + 1);
        for (int n = first; n <= last; n++)
        {
            Vector2 c = At(n);
            bool sel = chartFriend == null && n == chartSel;
            float bob = sel ? Mathf.Sin(Time.unscaledTime * 4f) * 2f : 0f;
            var r = new Rect(c.x - 38f, c.y - 24f + bob, 76f, 48f);
            if (sel) Outline(new Rect(r.x - 6, r.y - 4, r.width + 12, r.height + 8), new Color(1f, 0.85f, 0.3f, 0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f)));
            string line1, line2;
            if (n == 0) // where a private island sits
            {
                bool mine = chartC == homeContinent;
                GUI.DrawTexture(r, ChartIslandTex(mine ? -3 : -1), ScaleMode.StretchToFill);
                if (!mine) ShadowLabel(new Rect(c.x - 20, c.y - 20 + bob, 40, 32), "+", titleCenter, new Color(1f, 1f, 1f, 0.7f));
                if (mine && atHome) ShadowLabel(new Rect(c.x - 60, r.y - 20, 120, 18), "<color=#9dff9d><b>▼ คุณอยู่ที่นี่</b></color>", tag);
                line1 = mine ? "<b>เกาะส่วนตัว</b>" : "<b>ที่ตั้งเกาะ</b>";
                line2 = mine ? "<color=#9dff9d>บ้านของคุณ</color>" : "<color=#e6ecf2>ย้ายบ้านมาได้</color>";
            }
            else
            {
                var st = StateOf(n, chartC);
                bool fog = st is ChartState.Fog or ChartState.Locked;
                GUI.DrawTexture(r, ChartIslandTex(st == ChartState.Fog ? -1 : st is ChartState.Next or ChartState.Locked ? -2 : (int)BiomeOf(n, chartC)), ScaleMode.StretchToFill);
                if (fog || st == ChartState.Next)
                    ShadowLabel(new Rect(c.x - 20, c.y - 20 + bob, 40, 32), st == ChartState.Next ? "!" : "?", titleCenter, st == ChartState.Next ? new Color(1f, 0.9f, 0.35f) : new Color(1f, 1f, 1f, 0.8f));
                if (st == ChartState.Here)
                {
                    float hb = Mathf.Sin(Time.unscaledTime * 3f) * 2f;
                    Icon(new Rect(c.x + 14, r.y - 6 + hb, 26, 26), BoatSprite); // your boat moored here
                    ShadowLabel(new Rect(c.x - 60, r.y - 20 + hb, 120, 18), "<color=#9dff9d><b>▼ คุณอยู่ที่นี่</b></color>", tag);
                }
                line1 = n == IslandsPerContinent ? (ContinentCleared(chartC) ? "<b>เกาะ 20</b> <color=#9dff9d>✓</color>" : "<b><color=#ff8a7a>เกาะ 20 · บอสทวีป</color></b>") : $"<b>เกาะ {n}</b>";
                line2 = st switch
                {
                    ChartState.Fog => "<color=#e6ecf2>ยังไม่เคยไป</color>",
                    ChartState.Locked => ContinentOpen(chartC) ? "<color=#ff9a8a>ยังไม่เคยไป (เรือไม่พอ)</color>" : "<color=#ff9a8a>ทวีปยังไม่ปลดล็อค</color>",
                    ChartState.Next => "<color=#ffe27a>เกาะใหม่! ยังไม่เคยไป</color>",
                    _ => $"<color=#fff2d0>{IslandName(n + chartC * 31)}</color>",
                };
            }
            ShadowLabel(new Rect(c.x - 60, r.yMax + 1, 120, 17), line1, tag);
            ShadowLabel(new Rect(c.x - 60, r.yMax + 16, 120, 17), line2, tag);
            if (CrispGUI.Button(new Rect(r.x - 8, r.y - 6, r.width + 16, r.height + 42), GUIContent.none, GUIStyle.none))
            {
                if (chartFriend == null && chartSel == n && e.clickCount >= 2) { CrispGUI.EndGroup(); ChartGo(); return; }
                ChartSelect(n);
            }
        }
        CrispGUI.EndGroup();
        if (CrispGUI.Button(new Rect(map.x + 4, map.y + map.height / 2 - 18, 28, 36), "◀", btn)) chartScrollGoal -= visible - 1;
        if (CrispGUI.Button(new Rect(map.xMax - 32, map.y + map.height / 2 - 18, 28, 36), "▶", btn)) chartScrollGoal += visible - 1;

        // ---- friends' islands on this continent
        var fr = new Rect(box.x + 10, map.yMax + 4, pw - 20, friendsH - 6);
        Fill(fr, new Color(1f, 1f, 1f, 0.05f));
        ShadowLabel(new Rect(fr.x + 6, fr.y + 8, 110, 20), "บ้านเพื่อนที่นี่:", small, new Color(1f, 0.75f, 0.85f));
        float fx = fr.x + 112f;
        string none = !Online ? "เล่นออนไลน์เพื่อดูและล่องเรือไปบ้านเพื่อน" : !serverHomes ? "เซิร์ฟเวอร์ยังไม่รองรับการเยี่ยมบ้าน" : !friendsKnown ? "กำลังดูว่าใครเปิดบ้านอยู่..." : null;
        int shown = 0;
        if (none == null)
            foreach (var f in chartFriends)
            {
                if (f == null || f.c != chartC) continue;
                var cr = new Rect(fx, fr.y + 4, 150, fr.height - 8);
                if (cr.xMax > fr.xMax - 4) break;
                bool on = chartFriend != null && chartFriend.id == f.id;
                if (CrispGUI.Button(cr, $"{(on ? "<b><color=#ffe27a>" : "")}บ้าน {f.name}{(on ? "</color></b>" : "")}", btn)) { chartFriend = f; chartSel = -1; Sfx.Play("pickup", 0.2f); }
                fx += 156f;
                shown++;
            }
        if (none == null && shown == 0) none = "ยังไม่มีบ้านเพื่อนที่ออนไลน์อยู่ในทวีปนี้ (ลองดูทวีปอื่นด้านบน)";
        if (none != null) ShadowLabel(new Rect(fx, fr.y + 8, fr.xMax - fx - 6, 20), none, small, new Color(0.8f, 0.8f, 0.8f));

        // ---- what you picked
        var info = new Rect(box.x + 14, fr.yMax + 6, pw - 28, infoH);
        var wrap = new GUIStyle(small) { wordWrap = true, fontSize = 12, richText = true };
        string title, text, action = null;
        bool can = false;
        if (chartFriend != null)
        {
            title = $"บ้านของ {chartFriend.name}  <size=12><color=#cccccc>({ContinentNames[chartC]})</color></size>";
            text = "ล่องเรือไปเยี่ยม ระหว่างทางมีฉลามเหมือนออกทะเลปกติ ไปถึงแล้วช่วยปลูกผัก เลี้ยงสัตว์ และซื้อของในตลาดได้ กด H เพื่อกลับ";
            action = "ล่องเรือไปเยี่ยม (Enter)";
            can = Online && voyage == null;
        }
        else if (chartSel == 0)
        {
            bool mine = chartC == homeContinent;
            title = mine ? $"เกาะส่วนตัวของคุณ  <size=12><color=#cccccc>({ContinentNames[chartC]})</color></size>" : $"ย้ายเกาะส่วนตัวมาที่{ContinentNames[chartC]}";
            text = mine ? (atHome ? "คุณอยู่บนเกาะส่วนตัวแล้ว" : "กด H เพื่อกลับบ้านได้ทุกเมื่อ ไม่ต้องล่องเรือ")
                 : !ContinentOpen(chartC) ? $"<color=#ff9a8a>ย้ายบ้านมาทวีปนี้ไม่ได้ {ContinentLockWhy(chartC)}</color>"
                        : $"ย้ายทั้งเกาะ บ้าน ฟาร์ม และสัตว์ มาตั้งที่ทวีปนี้ เพื่อนๆ จะเห็นบ้านคุณที่นี่ ใช้เหรียญทอง {MoveHomeCost} (มี {Count(Item.GoldCoin)})";
            action = mine ? (atHome ? null : "กลับบ้าน (H)") : $"ย้ายมาที่นี่ ({MoveHomeCost} เหรียญ)";
            can = mine ? !atHome : Count(Item.GoldCoin) >= MoveHomeCost && !visiting && ContinentOpen(chartC);
        }
        else
        {
            var sst = StateOf(chartSel, chartC);
            title = sst switch
            {
                ChartState.Fog => $"เกาะที่ {chartSel}: ???",
                ChartState.Next or ChartState.Locked => $"เกาะที่ {chartSel}: เกาะที่ยังไม่มีใครรู้จัก",
                _ => $"เกาะที่ {chartSel}: {IslandName(chartSel + chartC * 31)}  <size=12><color=#cccccc>({BiomeNameOf(BiomeOf(chartSel, chartC))})</color></size>",
            };
            string cross = !atHome && chartC != continent ? " · ข้ามมหาสมุทรไปอีกทวีป ไกลกว่าปกติ" : "";
            text = sst switch
            {
                ChartState.Here => "คุณอยู่ที่เกาะนี้ เลือกเกาะอื่นบนแผนที่เพื่อออกเรือ",
                ChartState.Visited => "<color=#9dff9d>เคยไปแล้ว</color> · ทุกอย่างยังอยู่เหมือนตอนที่จากมา" + cross,
                ChartState.Next => "<color=#ffe27a>ยังไม่เคยไป</color> · ออกสำรวจเกาะใหม่ ไม่รู้ว่ามีอะไรรออยู่ เตรียมอาหารและน้ำให้พร้อม" + cross,
                ChartState.Locked when !ContinentOpen(chartC) => $"<color=#ff9a8a>{ContinentLockWhy(chartC)}</color>",
                ChartState.Locked => raftLevel <= 0 ? "<color=#ff9a8a>ยังไม่เคยไป</color> · ต้องมีแพหรือเรือก่อน"
                    : $"<color=#ff9a8a>ยังไม่เคยไป</color> · คลื่นแรงเกินไปสำหรับเรือ Lv {raftLevel} ให้ช่างต่อเรืออัปเกรดเป็น Lv {BoatNeedOn(chartC)} ก่อน",
                _ => $"<color=#e6ecf2>ยังไม่เคยไป</color> · อยู่ไกลเกินกว่าจะไปถึงตอนนี้ ต้องไปให้ถึงเกาะที่ {chartSel - 1} ก่อน",
            };
            if (chartSel == IslandsPerContinent) text += ContinentCleared(chartC)
                ? $"\n<color=#9dff9d>ปราบบอสทวีปแล้ว · {(chartC + 1 < Continents ? ContinentNames[chartC + 1] + "เปิดแล้ว" : "ผ่านครบทุกทวีปแล้ว!")}</color>"
                : "\n<color=#ff8a7a><b>เกาะของบอสทวีป</b> บอสมีพลังผลไม้ปลุกพลังขั้นสูงสุด ไปเป็นทีมลูกเรือ 5 คนจะดีที่สุด</color>";
            if (sst is ChartState.Here or ChartState.Visited) text += "\n" + IslandContents(chartSel, chartC); // IslandGame.ItemInfo.cs
            else if (sst is ChartState.Next or ChartState.Locked) text += "\n<color=#aaaaaa>ยังไม่รู้ว่ามีวัตถุดิบและมอนสเตอร์อะไร ไปสำรวจก่อนแล้วจะบันทึกลงแผนที่</color>";
            if (sst is ChartState.Visited or ChartState.Next)
            {
                can = CanSailTo(chartSel, chartC, out var why);
                action = "ล่องเรือไป (Enter)";
                if (!can && why != null) text += $"\n<color=#ffcc66>{why}</color>";
            }
        }
        ShadowLabel(new Rect(info.x, info.y, info.width - 190, 22), title, new GUIStyle(label) { richText = true }, new Color(1f, 0.9f, 0.6f));
        CrispGUI.Label(new Rect(info.x, info.y + 24, info.width - 190, infoH - 24), text, wrap);
        if (action != null)
        {
            GUI.enabled = can;
            if (CrispGUI.Button(new Rect(info.xMax - 180, info.y + 4, 180, 38), $"<b>{action}</b>", btn)) { GUI.enabled = true; ChartGo(); return; }
            GUI.enabled = true;
        }
        ShadowLabel(new Rect(info.xMax - 180, info.y + 46, 180, 18), "1-4 ทวีป · ← → เกาะ · ล้อเมาส์เลื่อน", smallCenter, new Color(0.8f, 0.8f, 0.8f));
    }

    static void Outline(Rect r, Color col)
    {
        Fill(new Rect(r.x, r.y, r.width, 2), col); Fill(new Rect(r.x, r.yMax - 2, r.width, 2), col);
        Fill(new Rect(r.x, r.y, 2, r.height), col); Fill(new Rect(r.xMax - 2, r.y, 2, r.height), col);
    }

    // ------------------------------------------------------------------ island pictures
    static readonly Dictionary<int, Texture2D> chartTex = new Dictionary<int, Texture2D>();

    /// A little island seen from above at an angle. key: a Biome, -1 = unknown (grey, in fog),
    /// -2 = the next island (a sandy outline you haven't seen up close yet), -3 = your private island.
    static Texture2D ChartIslandTex(int key)
    {
        if (chartTex.TryGetValue(key, out var t) && t != null) return t;
        const int w = 40, h = 25;
        t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        Color land = key switch
        {
            (int)Biome.Jungle => new Color(0.18f, 0.5f, 0.22f), (int)Biome.Volcanic => new Color(0.38f, 0.28f, 0.24f),
            (int)Biome.Tribal => new Color(0.6f, 0.72f, 0.3f), (int)Biome.Mushroom => new Color(0.55f, 0.36f, 0.75f),
            (int)Biome.Turtle => new Color(0.4f, 0.62f, 0.36f), (int)Biome.Sky => new Color(0.88f, 0.92f, 1f),
            (int)Biome.Bubble => new Color(0.42f, 0.8f, 0.88f), (int)Biome.Hourglass => new Color(0.86f, 0.72f, 0.42f),
            (int)Biome.Ice => new Color(0.85f, 0.94f, 1f), (int)Biome.Sunken => new Color(0.23f, 0.54f, 0.54f),
            (int)Biome.Pirate => new Color(0.74f, 0.6f, 0.38f), (int)Biome.Crystal => new Color(0.75f, 0.5f, 1f),
            (int)Biome.Storm => new Color(0.42f, 0.44f, 0.54f), (int)Biome.Insect => new Color(0.62f, 0.62f, 0.25f),
            -1 => new Color(0.55f, 0.6f, 0.66f, 0.75f), -2 => new Color(0.7f, 0.66f, 0.5f, 0.85f),
            _ => new Color(0.36f, 0.72f, 0.36f),
        };
        bool unknown = key is -1 or -2;
        Color sand = unknown ? land * 1.15f : new Color(0.94f, 0.86f, 0.62f);
        Color cliff = land * 0.6f; cliff.a = land.a;
        Color foam = new Color(1f, 1f, 1f, unknown ? 0.25f : 0.6f);
        var rng = new System.Random(key * 7919 + 13);
        float lobeA = (float)rng.NextDouble() * 6.28f, lobeB = (float)rng.NextDouble() * 6.28f;
        float Shape(float x, float y)
        {
            float dx = (x - w / 2f) / (w * 0.42f), dy = (y - h / 2f) / (h * 0.38f);
            float ang = Mathf.Atan2(dy, dx);
            float r = 1f + 0.14f * Mathf.Sin(ang * 2f + lobeA) + 0.08f * Mathf.Sin(ang * 3f + lobeB);
            return Mathf.Sqrt(dx * dx + dy * dy) / r;
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = Shape(x, y + 2.5f); // texture rows go bottom-up: +2.5 lifts the top surface
                float dc = Shape(x, y);       // the cliff face under it
                Color c = Color.clear;
                if (dc < 1.08f && dc >= 1f) c = foam;
                if (dc < 1f) c = cliff;
                if (d < 1f) c = d > 0.8f ? sand : land;
                if (!unknown && d < 0.78f && (x * 7 + y * 13) % 17 == 0) c = land * 0.72f; // trees / rocks
                c.a = c.a == 0f ? 0f : unknown ? land.a : 1f;
                px[y * w + x] = c;
            }
        if (key == -3) // your house: a red roof over a wooden wall in the middle
        {
            for (int y = 11; y <= 14; y++) for (int x = 17; x <= 23; x++) px[y * w + x] = new Color(0.62f, 0.42f, 0.25f);
            for (int y = 15; y <= 18; y++)
                for (int x = 15 + (y - 15); x <= 25 - (y - 15); x++) px[y * w + x] = new Color(0.85f, 0.25f, 0.2f);
            px[12 * w + 20] = new Color(0.3f, 0.18f, 0.1f); px[11 * w + 20] = new Color(0.3f, 0.18f, 0.1f);
        }
        t.SetPixels(px);
        t.Apply();
        chartTex[key] = t;
        return t;
    }
}
