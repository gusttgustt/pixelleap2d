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

    public void Flash() { hurtT = 0.2f; PlayAction(PixelArt.HurtAnim, 14f); }

    // Action poses (ready-made characters only; the build-your-own look has none and skips them).
    int action = -1, actionFrames;
    float actionT, actionFps, idleT, deadT;

    /// Play a one-shot pose such as PixelArt.AttackAnim, then go back to standing / walking.
    public void PlayAction(int anim, float fps)
    {
        int n = PixelArt.ActionFrames(IslandGame.I.lookCode, anim);
        if (n == 0) return;
        action = anim; actionFrames = n; actionFps = fps; actionT = 0f;
    }

    const float SitDelay = 2.5f; // seconds standing still before sitting down to rest

    /// The frame to show: death, then a playing action, then idle breathing (or sitting), over walk/stand.
    int Pose(int frame)
    {
        var game = IslandGame.I;
        float dt = Time.deltaTime;
        if (game.dead)
        {
            deadT += dt;
            int n = PixelArt.ActionFrames(game.lookCode, PixelArt.DeathAnim);
            return n > 0 ? PixelArt.DeathAnim + Mathf.Min(n - 1, (int)(deadT * 8f)) : frame; // stays down on the last frame
        }
        deadT = 0f;
        if (action >= 0)
        {
            idleT = 0f; // gets up to swing, gather or flinch
            actionT += dt;
            int i = (int)(actionT * actionFps);
            if (i < actionFrames) return action + i;
            action = -1;
        }
        if (frame != 0) { idleT = 0f; return frame; }
        idleT += dt;
        // Characters with a sitting pose sit down for a rest after standing still a moment,
        // and stay seated (last frame) until they move. They sit facing the camera.
        int sit = PixelArt.ActionFrames(game.lookCode, PixelArt.SitAnim);
        if (sit > 0 && idleT > SitDelay)
        {
            dir = 0;
            return PixelArt.SitAnim + Mathf.Min(sit - 1, (int)((idleT - SitDelay) * 8f));
        }
        int idle = PixelArt.ActionFrames(game.lookCode, PixelArt.IdleAnim);
        if (idle <= 1) return idle > 0 ? PixelArt.IdleAnim : 0;
        int k = (int)(idleT * 5f) % (2 * idle - 2); // back and forth: 0 1 2 3 4 3 2 1 ...
        return PixelArt.IdleAnim + (k < idle ? k : 2 * idle - 2 - k);
    }

    // What other players need to draw this player (sent over the network).
    public int Dir => dir;
    public int Frame { get; private set; }
    public bool Flip => sr != null && sr.flipX;
    public SpriteRenderer Renderer => sr;

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
                frame = 1 + (int)animT % PixelArt.WalkFrames(IslandGame.I.lookCode);
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
        Apply(Pose(frame));
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

    /// Stuck inside something solid (spawned on a campfire, a tree regrew around you...): any step
    /// onto land that doesn't push deeper in is allowed, so you can always walk out.
    bool CanStand(Vector2 g)
    {
        var game = IslandGame.I;
        if (!game.CanClimb(grid, g)) return false;
        if (game.CanWalk(g, 0.18f)) return true;
        return game.IsLand(g) && !game.CanWalk(grid, 0.18f) && game.Overlap(g, 0.18f) <= game.Overlap(grid, 0.18f) + 0.0001f;
    }

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
