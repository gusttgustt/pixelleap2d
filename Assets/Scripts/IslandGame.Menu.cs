using System.Collections.Generic;
using UnityEngine;

/// The hamburger menu, bottom right: a round ☰ button that opens a list of the menu buttons
/// that used to sit in the top right corner (private island H, build G, visit friends V,
/// pets P, wanted posters J, crew L). The keys still work as before. On phones it's one more
/// round touch button (IslandGame.Touch.cs); with a mouse it's drawn here.
public partial class IslandGame
{
    bool menuOpen;
    static Texture2D burgerTex;

    /// Where the ☰ button is (HUD units): above the CH button.
    Vector2 MenuCenter(float W, float H) => ShowTouch ? new Vector2(W - 36f, H - 320f) : new Vector2(W - 40f, H - 318f);
    const float MenuR = 24f;

    bool MenuShown => !title && player != null && voyage == null && !dead && homeT <= 0f && sailT <= 0f && sleepT <= 0f && dialog == null;

    void ToggleMenu()
    {
        menuOpen = !menuOpen;
        if (menuOpen) { craftOpen = false; skillsOpen = false; bagOpen = false; whoOpen = false; }
    }

    /// The items, in order: label, colour, what it does.
    List<(string label, Color col, System.Action act)> MenuItems()
    {
        var items = new List<(string, Color, System.Action)>();
        items.Add((visiting ? "กลับบ้านตัวเอง (H)" : atHome ? "กลับไปผจญภัย (H)" : "ไปเกาะส่วนตัว (H)",
            atHome ? new Color(0.6f, 0.85f, 1f) : new Color(1f, 0.85f, 0.45f), ToggleHome));
        if (atHome && !visiting && !buildOpen)
            items.Add((inHouse ? "จัดบ้าน (G)" : "สร้าง / รื้อ (G)", new Color(0.75f, 1f, 0.7f), ToggleBuild));
        if (!buildOpen)
        {
            var pink = new Color(1f, 0.72f, 0.82f);
            if (Online && serverHomes) items.Add(("เยี่ยมบ้านเพื่อน (V)", pink, AskHomes));
            items.Add((pets.Count > 0 ? $"สัตว์เลี้ยง {pets.Count} ตัว (P)" : "สัตว์เลี้ยง (P)", pink, OpenPetMenu));
            items.Add(("ป้ายประกาศค่าหัว (J)", new Color(1f, 0.8f, 0.35f), ToggleWanted));
            if (Online)
            {
                string crew = InCrew ? (aboard ? $"บนเรือ {crewCaptainName} (L)" : $"ลูกเรือของ {crewCaptainName} (L)")
                            : IsCaptain ? $"ลูกเรือ {crewAboard.Count}/{crewMates.Count} บนเรือ (L)" : "ลูกเรือ (L)";
                items.Add((crew, new Color(0.7f, 0.9f, 1f), () => crewOpen = !crewOpen));
                items.Add(($"ใครออนไลน์ / ย้าย CH{channel} (O)", new Color(0.6f, 1f, 0.65f), ToggleWho));
            }
        }
        return items;
    }

    /// Late in OnGUI (on top of the HUD): the ☰ button for the mouse, and the open menu either way.
    void DrawMenu(float W, float H)
    {
        if (!MenuShown) { menuOpen = false; return; }
        var c = MenuCenter(W, H);
        var e = Event.current;
        if (!ShowTouch) // phones: the touch button (IslandGame.Touch.cs)
        {
            bool over = (e.mousePosition - c).magnitude < MenuR;
            if (over && e.type == EventType.MouseDown && e.button == 0) { e.Use(); ToggleMenu(); }
            DrawBurger(c, menuOpen || over);
            // bag and skills, in a row to the left of ☰ (phones have their own, by the stick)
            QuickButton(c + new Vector2(-56f, 0f), "สกิล", "K", new Color(0.8f, 0.7f, 1f), skillsOpen,
                () => { skillsOpen = !skillsOpen; craftOpen = false; bagOpen = false; menuOpen = false; });
            QuickButton(c + new Vector2(-112f, 0f), "กระเป๋า", "B", new Color(0.95f, 0.85f, 0.6f), bagOpen,
                () => { ToggleBag(); menuOpen = false; });
        }
        if (!menuOpen) return;

        var items = MenuItems();
        const float bw = 196f, bh = 26f, gap = 4f;
        float h = items.Count * (bh + gap) + 8f;
        var box = new Rect(Mathf.Max(6f, c.x + MenuR - bw - 8f), Mathf.Max(6f, c.y - MenuR - 8f - h), bw + 8f, h);
        Fill(box, new Color(0.06f, 0.07f, 0.1f, 0.92f));
        Fill(new Rect(box.x, box.yMax - 3f, box.width, 3f), new Color(1f, 0.85f, 0.45f, 0.9f));
        var st = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
        st.hover.textColor = Color.white;
        float y = box.y + 4f;
        foreach (var (label, col, act) in items)
        {
            var bst = new GUIStyle(st); bst.normal.textColor = col;
            if (CrispGUI.Button(new Rect(box.x + 4f, y, bw, bh), label, bst)) { menuOpen = false; act(); }
            y += bh + gap;
        }
        // a click on the panel itself isn't a click on the world; a click anywhere else closes it
        if (e.type == EventType.MouseDown)
        {
            if (box.Contains(e.mousePosition)) e.Use();
            else if ((e.mousePosition - c).magnitude >= MenuR) menuOpen = false;
        }
    }

    /// A round mouse button the size of ☰: a name and its key under it.
    void QuickButton(Vector2 c, string text, string key, Color col, bool on, System.Action act)
    {
        var e = Event.current;
        bool over = (e.mousePosition - c).magnitude < MenuR;
        if (over && e.type == EventType.MouseDown && e.button == 0) { e.Use(); if (!chatOpen) act(); }
        TouchCircle(c, MenuR, null, col, on || over, 0);
        var st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11, fontStyle = FontStyle.Bold };
        ShadowLabel(new Rect(c.x - MenuR, c.y - MenuR, MenuR * 2f, MenuR * 2f - 12f), text, st, col);
        ShadowLabel(new Rect(c.x - MenuR, c.y - MenuR + 15f, MenuR * 2f, MenuR * 2f - 12f), $"[{key}]", new GUIStyle(st) { fontSize = 10 }, new Color(1f, 1f, 1f, 0.85f));
    }

    void DrawBurger(Vector2 c, bool lit)
    {
        var col = new Color(1f, 0.85f, 0.45f);
        TouchCircle(c, MenuR, null, col, lit, 0);
        if (burgerTex == null) { burgerTex = new Texture2D(1, 1); burgerTex.SetPixel(0, 0, Color.white); burgerTex.Apply(); }
        GUI.color = col;
        for (int i = -1; i <= 1; i++) GUI.DrawTexture(new Rect(c.x - 10f, c.y - 1.5f + i * 6f, 20f, 3f), burgerTex);
        GUI.color = Color.white;
    }
}
