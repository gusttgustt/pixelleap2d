using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// The bounty (ค่าหัว): the price the government puts on your head. It only ever goes up:
///   - reaching islands you've never reached before (every continent counts, further ones pay more),
///   - building up your home (the house level, a bigger island, everything you've built),
///   - attacking government warships and sinking them (IslandGame.Navy.cs), beating the pirate captain.
/// The higher it is, the more the navy comes after you. It's kept in the save and survives a death
/// (with the private island), and online it goes to the server's board of wanted posters
/// (ป้ายประกาศค่าหัว, J): everyone's face and bounty, highest first. Offline the board shows just you.
public partial class IslandGame
{
    public int bounty;
    int bountyIsl;      // islands already paid for (all continents together)
    int bountyHome;     // home points already paid for
    int navyWins;       // warships sunk
    float bountyCheckT = 1f, bountySendT = 1f, bountyFlashT;
    int bountyFlashGain;
    bool bountyDirty = true;

    const int NavyHitBounty = 500;
    const int CaptainBounty = 30000;
    const int MaxBounty = 2000000000;
    int NavyBounty => 40000 + 20000 * tier + 5000 * Mathf.Min(navyWins, 20);

    public static string Baht(long n) => "฿" + n.ToString("N0");

    void LoadBounty(SaveData d)
    {
        bounty = Mathf.Clamp(d.bounty, 0, MaxBounty);
        bountyIsl = d.bountyIsl;
        bountyHome = d.bountyHome;
        navyWins = d.navyWins;
        bountyCheckT = 1f;
        bountyDirty = true;
    }

    void SaveBounty(SaveData d)
    {
        d.bounty = bounty; d.bountyIsl = bountyIsl; d.bountyHome = bountyHome; d.navyWins = navyWins;
    }

    /// Kept over a death (KeepHomeOnDeath -> NewRun).
    static void CarryBounty(SaveData from, SaveData to)
    {
        to.bounty = from.bounty; to.bountyIsl = from.bountyIsl; to.bountyHome = from.bountyHome; to.navyWins = from.navyWins;
    }

    /// Raise the bounty. `at`: a popup there; `why`: a hint line.
    void AddBounty(int amount, Vector3? at, string why)
    {
        if (amount <= 0) return;
        bounty = (int)System.Math.Min((long)bounty + amount, MaxBounty);
        bountyDirty = true;
        bountySendT = Mathf.Min(bountySendT, 1.5f);
        if (amount >= 5000) { bountyFlashT = 3.5f; bountyFlashGain = amount; Sfx.Play("pickup", 0.5f); }
        if (at != null) Popup($"ค่าหัว +{Baht(amount)}", at.Value, new Color(1f, 0.82f, 0.3f));
        if (why != null) Hint(why, 4.5f);
    }

    /// Islands: the k-th island ever reached is worth 5,000 + 1,000 k.
    static long IslandBountyUpTo(int n) { long s = 0; for (int k = 2; k <= n; k++) s += 5000 + 1000L * k; return s; }

    int IslandsReached { get { int s = 0; for (int c = 0; c < Continents; c++) s += reach[c]; return s; } }

    /// Home points: 10 per house level, 6 per time the island was made bigger, 1 per thing built
    /// outside, 1 per two pieces of furniture inside. -1 = can't tell right now (visiting a friend).
    int HomeScore()
    {
        if (visiting) return -1;
        int built = 0, furniture = 0;
        if (atHome && !inHouse) { foreach (var n in nodes) if (n != null && n.built) built++; }
        else foreach (var n in homeNodes) if (n.built) built++;
        if (inHouse) { foreach (var n in nodes) if (n != null && n.built) furniture++; }
        else foreach (var n in houseNodes) if (n.built) furniture++;
        return houseLevel * 10 + homeLand * 6 + Mathf.Min(built, 400) + Mathf.Min(furniture, 200) / 2;
    }

    void CheckBountyMilestones()
    {
        int isl = IslandsReached;
        if (isl > bountyIsl)
        {
            long gain = IslandBountyUpTo(isl) - IslandBountyUpTo(Mathf.Max(1, bountyIsl));
            bool first = bounty == 0;
            bountyIsl = isl;
            if (gain > 0) AddBounty((int)System.Math.Min(gain, int.MaxValue), player != null ? player.transform.position + Vector3.up * 1.2f : (Vector3?)null,
                first ? $"รัฐบาลประกาศค่าหัวคุณแล้ว {Baht(bounty + gain)}! กด J ดูป้ายประกาศ" : $"ไปถึงเกาะใหม่ ข่าวไปถึงหูรัฐบาลแล้ว ค่าหัวเพิ่มเป็น {Baht(bounty + gain)}");
        }
        int home = HomeScore();
        if (home > bountyHome)
        {
            int gain = (home - bountyHome) * 1000;
            bountyHome = home;
            AddBounty(gain, player != null ? player.transform.position + Vector3.up * 1.2f : (Vector3?)null, null);
        }
    }

    void TickBounty(float dt)
    {
        bountyFlashT -= dt;
        bountyCheckT -= dt;
        if (bountyCheckT <= 0f)
        {
            bountyCheckT = 2f;
            if (!dead && player != null && voyage == null && sailT <= 0f) CheckBountyMilestones();
        }
        bountySendT -= dt;
        if (Online && bountyDirty && bountySendT <= 0f)
        {
            bountySendT = 4f;
            bountyDirty = false;
            Send(new NetMsg { t = "bounty", hits = bounty, look = lookCode });
        }
        if (wantedOpen && Online && Time.time - wantedAskedAt > 15f) AskWanted(); // keep the board fresh while it's open
    }

    // ------------------------------------------------------------------ the board of wanted posters
    public bool wantedOpen;
    float wantedAskedAt = -99f;
    bool serverWanted;              // this server keeps a board (an older one stays quiet)
    readonly List<NetPeer> wanted = new List<NetPeer>();
    Vector2 wantedScroll;

    void ToggleWanted()
    {
        wantedOpen = !wantedOpen;
        if (!wantedOpen) return;
        craftOpen = false; skillsOpen = false; bagOpen = false;
        wantedScroll = Vector2.zero;
        AskWanted();
        Sfx.Play("pickup", 0.3f);
    }

    void AskWanted()
    {
        wantedAskedAt = Time.time;
        if (!Online) return;
        Send(new NetMsg { t = "bounty", hits = bounty, look = lookCode }); // mine first, so it's on the board I get back
        bountyDirty = false;
        Send(new NetMsg { t = "wanted" });
    }

    void OnWanted(NetPeer[] players)
    {
        serverWanted = true;
        wanted.Clear();
        if (players != null) wanted.AddRange(players);
    }

    /// The posters to show, highest bounty first. Mine (c = 1 from the server) shows my bounty as it is now.
    List<NetPeer> WantedList()
    {
        var list = new List<NetPeer>();
        bool mine = false;
        if (Online && serverWanted)
            foreach (var p in wanted)
            {
                if (p == null) continue;
                if (p.c == 1) { if (mine) continue; mine = true; list.Add(new NetPeer { name = playerName, b = bounty, lk = lookCode, c = 1 }); }
                else list.Add(p);
            }
        if (!mine) list.Add(new NetPeer { name = playerName, b = bounty, lk = lookCode, c = 1 });
        list.Sort((a, b) => b.b.CompareTo(a.b));
        return list;
    }

    void DrawWanted(float W, float H)
    {
        float pw = Mathf.Min(W - 20f, 720f), ph = Mathf.Min(H - 80f, 460f);
        var panel = new Rect(W / 2f - pw / 2f, Mathf.Max(40f, H / 2f - ph / 2f - 10f), pw, ph);
        // a wooden notice board
        Fill(new Rect(panel.x - 4, panel.y - 4, panel.width + 8, panel.height + 8), new Color(0.16f, 0.09f, 0.04f, 0.97f));
        for (float y = panel.y; y < panel.yMax; y += 22f)
            Fill(new Rect(panel.x, y, panel.width, Mathf.Min(21f, panel.yMax - y)), ((int)((y - panel.y) / 22f) & 1) == 0 ? new Color(0.42f, 0.27f, 0.14f) : new Color(0.38f, 0.24f, 0.12f));
        Fill(new Rect(panel.x, panel.y, panel.width, 40), new Color(0.22f, 0.13f, 0.06f, 0.9f));
        var head = new GUIStyle(label) { fontSize = 18, fontStyle = FontStyle.Bold };
        ShadowLabel(new Rect(panel.x + 14, panel.y + 6, 300, 26), "ป้ายประกาศค่าหัว", head, new Color(1f, 0.85f, 0.35f));
        var list = WantedList();
        string note = !Online ? "ออฟไลน์: เห็นแค่ป้ายของคุณ (เล่นออนไลน์เพื่อดูค่าหัวของทุกคน)"
                    : serverWanted ? $"ผู้ถูกประกาศจับ {list.Count} คน เรียงจากค่าหัวสูงสุด"
                    : Time.time - wantedAskedAt < 4f ? "กำลังโหลดป้ายจากเซิร์ฟเวอร์..." : "เซิร์ฟเวอร์นี้ยังไม่มีป้ายรวม: เห็นแค่ป้ายของคุณ";
        ShadowLabel(new Rect(panel.x + 180, panel.y + 12, pw - 230, 20), note, small, new Color(1f, 0.92f, 0.75f, 0.85f));
        if (CrispGUI.Button(new Rect(panel.xMax - 34, panel.y + 8, 26, 24), "X")) { wantedOpen = false; return; }

        // posters in rows
        var view = new Rect(panel.x + 8, panel.y + 46, pw - 16, ph - 54);
        const float postW = 128f, postH = 186f, gap = 10f;
        int cols = Mathf.Max(1, Mathf.FloorToInt((view.width - 16f + gap) / (postW + gap)));
        int rows = Mathf.CeilToInt(list.Count / (float)cols);
        float rowW = cols * postW + (cols - 1) * gap;
        var content = new Rect(0, 0, view.width - 16f, Mathf.Max(view.height, rows * (postH + gap) + 4f));
        wantedScroll = CrispGUI.BeginScrollView(view, wantedScroll, content, false, false);
        float ox = Mathf.Max(0f, (content.width - rowW) / 2f);
        for (int i = 0; i < list.Count; i++)
        {
            float x = ox + (i % cols) * (postW + gap), y = 4f + (i / cols) * (postH + gap);
            if (y + postH < wantedScroll.y || y > wantedScroll.y + view.height) continue; // out of sight
            DrawPoster(new Rect(x, y, postW, postH), list[i], i + 1);
        }
        CrispGUI.EndScrollView();
    }

    /// One wanted poster: "ประกาศจับ", the face, the name and the bounty, with the rank pinned on.
    void DrawPoster(Rect r, NetPeer p, int rank)
    {
        bool me = p.c == 1;
        Color paper = new Color(0.91f, 0.83f, 0.64f), paperD = new Color(0.72f, 0.6f, 0.38f), ink = new Color(0.27f, 0.16f, 0.07f);
        Fill(new Rect(r.x + 3, r.y + 4, r.width, r.height), new Color(0f, 0f, 0f, 0.35f)); // shadow on the board
        if (me) Fill(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(1f, 0.82f, 0.3f));
        Fill(r, paperD);
        Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), paper);
        // worn edges and a few stains
        for (int k = 0; k < 7; k++)
        {
            float sx = r.x + 4 + (k * 37 + rank * 13) % (r.width - 12), sy = r.y + 4 + (k * 53 + rank * 7) % (r.height - 12);
            Fill(new Rect(sx, sy, 3 + k % 3, 2 + k % 2), new Color(0.75f, 0.63f, 0.42f, 0.6f));
        }
        Fill(new Rect(r.center.x - 3, r.y - 3, 6, 6), new Color(0.6f, 0.15f, 0.12f)); // the pin
        Fill(new Rect(r.center.x - 2, r.y - 2, 2, 2), new Color(1f, 0.5f, 0.45f));
        var title = new GUIStyle(labelCenter) { fontSize = 17, fontStyle = FontStyle.Bold };
        title.normal.textColor = ink;
        CrispGUI.Label(new Rect(r.x, r.y + 4, r.width, 24), "ประกาศจับ", title);
        // the face
        var face = new Rect(r.x + 12, r.y + 30, r.width - 24, 84);
        Fill(face, ink);
        Fill(new Rect(face.x + 2, face.y + 2, face.width - 4, face.height - 4), new Color(0.82f, 0.74f, 0.55f));
        DrawPortrait(new Rect(face.x + 4, face.y + 4, face.width - 8, face.height - 6), p.lk);
        var tiny = new GUIStyle(smallCenter) { fontSize = 10 };
        tiny.normal.textColor = new Color(ink.r, ink.g, ink.b, 0.8f);
        CrispGUI.Label(new Rect(r.x, face.yMax + 1, r.width, 16), "จับเป็นหรือตาย", tiny);
        var nameSt = new GUIStyle(labelCenter) { fontSize = 13, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
        nameSt.normal.textColor = ink;
        CrispGUI.Label(new Rect(r.x + 4, face.yMax + 15, r.width - 8, 20), string.IsNullOrEmpty(p.name) ? "ไม่ทราบชื่อ" : p.name, nameSt);
        Fill(new Rect(r.x + 10, face.yMax + 37, r.width - 20, 1), new Color(ink.r, ink.g, ink.b, 0.5f));
        var amt = new GUIStyle(labelCenter) { fontSize = p.b >= 100000000 ? 12 : 14, fontStyle = FontStyle.Bold };
        amt.normal.textColor = new Color(0.55f, 0.12f, 0.06f);
        CrispGUI.Label(new Rect(r.x, face.yMax + 39, r.width, 22), Baht(p.b), amt);
        // the rank, pinned on the corner
        Color badge = rank == 1 ? new Color(0.95f, 0.75f, 0.2f) : rank == 2 ? new Color(0.78f, 0.8f, 0.84f) : rank == 3 ? new Color(0.8f, 0.5f, 0.28f) : new Color(0.35f, 0.22f, 0.12f);
        var br = new Rect(r.x - 5, r.y - 5, 30, 20);
        Fill(br, Color.Lerp(badge, Color.black, 0.35f));
        Fill(new Rect(br.x + 1, br.y + 1, br.width - 2, br.height - 2), badge);
        var rs = new GUIStyle(smallCenter) { fontSize = 12, fontStyle = FontStyle.Bold };
        rs.normal.textColor = rank <= 3 ? new Color(0.2f, 0.12f, 0.05f) : new Color(1f, 0.92f, 0.75f);
        CrispGUI.Label(br, "#" + rank, rs);
        if (me)
        {
            var yb = new Rect(r.xMax - 30, r.yMax - 12, 36, 18); // a green tag on the bottom corner
            Fill(yb, new Color(0.18f, 0.35f, 0.12f));
            Fill(new Rect(yb.x + 1, yb.y + 1, yb.width - 2, yb.height - 2), new Color(0.35f, 0.62f, 0.25f));
            var youSt = new GUIStyle(smallCenter) { fontSize = 11, fontStyle = FontStyle.Bold };
            youSt.normal.textColor = Color.white;
            CrispGUI.Label(yb, "คุณ", youSt);
        }
    }

    /// The character's head and shoulders from their standing picture (the top part of the sprite).
    static void DrawPortrait(Rect r, int look)
    {
        var s = PixelArt.Player(0, 0, null, null, look);
        if (s == null || s.texture == null) return;
        var tex = s.texture;
        Rect tr = s.textureRect;
        float keep = 0.56f; // the top of the picture: hair, face, shoulders
        float srcW = tr.width, srcH = tr.height * keep;
        var uv = new Rect(tr.x / tex.width, (tr.y + tr.height - srcH) / tex.height, srcW / tex.width, srcH / tex.height);
        float k = Mathf.Min(r.width / srcW, r.height / srcH);
        float w = srcW * k, h = srcH * k;
        GUI.DrawTextureWithTexCoords(new Rect(r.center.x - w / 2f, r.yMax - h, w, h), tex, uv);
    }

    /// HUD: your bounty under the status block, and a short "bounty up" banner after a big jump.
    float DrawBountyHud(float y)
    {
        if (bounty <= 0) return y;
        float glow = bountyFlashT > 0f ? 0.5f + 0.5f * Mathf.Sin(Time.time * 10f) : 0f;
        ShadowLabel(new Rect(14, y, 360, 18), $"ค่าหัว {Baht(bounty)}   (J ป้ายประกาศ)", small, Color.Lerp(new Color(1f, 0.8f, 0.35f), Color.white, glow));
        return y + 18f;
    }

    void DrawBountyFlash(float W)
    {
        if (bountyFlashT <= 0f || wantedOpen) return;
        float a = Mathf.Clamp01(bountyFlashT / 0.6f) * Mathf.Clamp01((3.5f - bountyFlashT) / 0.3f);
        var r = new Rect(W / 2f - 120f, 92f, 240f, 46f);
        Fill(r, new Color(0.25f, 0.15f, 0.06f, 0.85f * a));
        Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), new Color(0.91f, 0.83f, 0.64f, 0.95f * a));
        var st = new GUIStyle(labelCenter) { fontSize = 13, fontStyle = FontStyle.Bold };
        st.normal.textColor = new Color(0.27f, 0.16f, 0.07f, a);
        CrispGUI.Label(new Rect(r.x, r.y + 3, r.width, 20), $"ค่าหัวเพิ่มขึ้น +{Baht(bountyFlashGain)}", st);
        var st2 = new GUIStyle(st) { fontSize = 15 };
        st2.normal.textColor = new Color(0.55f, 0.12f, 0.06f, a);
        CrispGUI.Label(new Rect(r.x, r.y + 21, r.width, 22), Baht(bounty), st2);
    }

    /// The warships' health, boss style, under the voyage bar.
    void DrawNavyBars(float W, float y)
    {
        if (voyage == null) return;
        int n = 0;
        float step = NavyAlive > 2 ? 24f : 30f; // a whole fleet: pack the bars tighter
        foreach (var c in voyage.creatures)
        {
            if (c == null || c.kind != SeaKind.Navy) continue;
            var hb = new Rect(W / 2f - 140f, y + n * step, 280f, NavyAlive > 2 ? 6f : 9f);
            ShadowLabel(new Rect(hb.x - 60, hb.y - 17, hb.width + 120, 18), c.DisplayName + (c.shieldHits > 0 ? $" [โล่ฟอง {c.shieldHits}]" : ""), smallCenter, c.flagship ? new Color(1f, 0.85f, 0.4f) : c.officer != Power.None ? new Color(1f, 0.75f, 1f) : new Color(0.7f, 0.85f, 1f));
            Fill(new Rect(hb.x - 2, hb.y - 2, hb.width + 4, hb.height + 4), new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(hb.x, hb.y, hb.width * Mathf.Clamp01((float)c.hp / c.maxHp), hb.height), new Color(0.3f, 0.5f, 0.95f));
            n++;
        }
    }

    void HandleWantedKeys(Keyboard kb)
    {
        if (kb.jKey.wasPressedThisFrame) ToggleWanted();
        if (kb.escapeKey.wasPressedThisFrame) wantedOpen = false;
    }
}
