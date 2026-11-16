using UnityEngine;
using UnityEngine.InputSystem;

/// WASD / arrows / gamepad stick movement on the isometric grid, with collision and terrain height.
public class PlayerController : MonoBehaviour
{
    public Vector2 grid;
    public Vector2 facing = new Vector2(0.7071f, 0.7071f);

    const float Speed = 1.8f; // world units per second (screen space)
    SpriteRenderer sr;
    float animT, height, hurtT;
    int dir;
    float dashT;
    Vector2 iceVel; // momentum while sliding on ice (grid units / second)
    Vector2 dashVel;

    /// Dash power: zip forward along `facing`.
    public void Dash(float distance) { dashT = 0.18f; dashVel = facing.normalized * distance / dashT; }
    public bool Dashing => dashT > 0f;

    public void Init(Vector2 g)
    {
        grid = g;
        sr = gameObject.AddComponent<SpriteRenderer>();
        height = IslandGame.I.HeightAt(g);
        Apply(0);
    }

    public void Flash() => hurtT = 0.2f;

    // What other players need to draw this player (sent over the network).
    public int Dir => dir;
    public int Frame { get; private set; }
    public bool Flip => sr != null && sr.flipX;

    void Update()
    {
        var game = IslandGame.I;
        int frame = 0;
        hurtT -= Time.deltaTime;
        if (dashT > 0f)
        {
            dashT -= Time.deltaTime;
            Vector2 step = dashVel * Time.deltaTime;
            if (CanStand(grid + step)) grid += step; else dashT = 0f; // stop at walls and water
            Apply(1);
            return;
        }
        if (!game.dead && !game.InputBlocked)
        {
            Vector2 input = ReadInput();
            if (input.sqrMagnitude > 0.01f)
            {
                input = Vector2.ClampMagnitude(input, 1f);
                // Move in screen space; vertical is halved so speed feels equal in every iso direction.
                Vector2 world = new Vector2(input.x, input.y * 0.5f) * Speed * game.SpeedMultiplier * Time.deltaTime;
                Vector2 d = IslandGame.WorldDeltaToGrid(world);
                if (game.Slippery) // on ice you build up speed slowly and keep sliding
                {
                    iceVel = Vector2.Lerp(iceVel, d / Mathf.Max(Time.deltaTime, 0.0001f), Time.deltaTime * 1.6f);
                    d = iceVel * Time.deltaTime;
                }
                else iceVel = d / Mathf.Max(Time.deltaTime, 0.0001f);

                if (CanStand(grid + d)) grid += d;
                else if (CanStand(grid + new Vector2(d.x, 0f))) grid.x += d.x; // slide along obstacles
                else if (CanStand(grid + new Vector2(0f, d.y))) grid.y += d.y;

                facing = d.normalized;
                if (Mathf.Abs(input.y) > 0.2f) dir = input.y > 0 ? 1 : 0;
                if (Mathf.Abs(input.x) > 0.2f) sr.flipX = input.x < 0;
                animT += Time.deltaTime * 8f;
                frame = 1 + ((int)animT & 1);
            }
            else
            {
                animT = 0f;
                if (game.Slippery && iceVel.sqrMagnitude > 0.01f) // no input: still gliding
                {
                    iceVel = Vector2.Lerp(iceVel, Vector2.zero, Time.deltaTime * 1.2f);
                    Vector2 d = iceVel * Time.deltaTime;
                    if (CanStand(grid + d)) grid += d; else iceVel = Vector2.zero;
                }
                else iceVel = Vector2.zero;
            }
        }
        Apply(frame);
    }

    public static Vector2 ReadInput()
    {
        Vector2 v = Vector2.zero;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1;
        }
        var gp = Gamepad.current;
        if (gp != null) v += gp.leftStick.ReadValue();
        v += IslandGame.TouchMove; // on-screen joystick (phones, tablets)
        return v;
    }

    bool CanStand(Vector2 g) => IslandGame.I.CanWalk(g, 0.18f) && IslandGame.I.CanClimb(grid, g);

    void Apply(int frame)
    {
        Frame = frame;
        // Smoothly step up/down terrain levels.
        height = Mathf.Lerp(height, IslandGame.I.HeightAt(grid), 1f - Mathf.Exp(-14f * Time.deltaTime));
        Vector3 flat = IslandGame.GridToWorld(grid);
        transform.position = flat + Vector3.up * height;
        sr.sortingOrder = IslandGame.SortOrder(flat.y);
        sr.sprite = PixelArt.Player(dir, frame, IslandGame.I.hat, IslandGame.I.outfit, IslandGame.I.lookCode);
        var col = hurtT > 0f ? new Color(1f, 0.45f, 0.45f) : IslandGame.I.Bubbled ? new Color(0.75f, 0.95f, 1f) : Color.white;
        if (IslandGame.I.Invisible) col.a = 0.35f + 0.1f * Mathf.Sin(Time.time * 8f);
        sr.color = col;
    }
}
