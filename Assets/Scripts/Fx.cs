using System.Collections.Generic;
using UnityEngine;

/// Very small pixel particle system: leaves, wood chips, splashes, sparks - plus a few bigger
/// pieces for the power effects: radial bursts, lightning bolts, shockwave rings on the ground
/// and fading after-images.
public static class Fx
{
    class Particle
    {
        public SpriteRenderer sr;
        public Vector3 vel;
        public float life, maxLife, gravity, size, drag;
        public bool dragY;
        public Color color;
    }

    /// A sprite that grows / fades away on its own (shockwave rings, after-images).
    class Decal
    {
        public SpriteRenderer sr;
        public float t, dur, delay, from, to, alpha;
        public Color color;
    }

    static readonly List<Particle> live = new List<Particle>();
    static readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
    static readonly List<Decal> decals = new List<Decal>();
    static Transform root;

    static void EnsureRoot()
    {
        if (root != null) return;
        root = new GameObject("Fx").transform;
        pool.Clear();
        live.Clear();
        decals.Clear();
    }

    static SpriteRenderer Take()
    {
        EnsureRoot();
        while (pool.Count > 0)
        {
            var s = pool.Pop();
            if (s != null) { s.gameObject.SetActive(true); return s; }
        }
        var sr = new GameObject("p").AddComponent<SpriteRenderer>();
        sr.transform.SetParent(root);
        sr.sprite = PixelArt.Dot();
        sr.sortingOrder = 30000; // in front of everything in the world
        return sr;
    }

    static void Add(Vector3 pos, Vector3 vel, Color c, float life, float gravity, float size, float drag, bool dragY)
    {
        var sr = Take();
        sr.transform.position = pos;
        sr.transform.localScale = Vector3.one * size;
        sr.color = c;
        live.Add(new Particle { sr = sr, vel = vel, life = life, maxLife = life, gravity = gravity, color = c, size = size, drag = drag, dragY = dragY });
    }

    /// Spray of single-pixel particles (size > 1: bigger square pixels that shrink as they fade).
    public static void Burst(Vector3 pos, Color color, int count, float spread = 1.2f, float up = 1.6f, float life = 0.6f, float gravity = 5f, float size = 1f)
    {
        for (int i = 0; i < count; i++)
        {
            var c = color;
            c.r *= Random.Range(0.85f, 1.1f); c.g *= Random.Range(0.85f, 1.1f);
            Add(pos + new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.05f, 0.1f), 0f),
                new Vector3(Random.Range(-1f, 1f) * spread, Random.Range(0.4f, 1f) * up, 0f),
                c, life * Random.Range(0.7f, 1.2f), gravity, size, 2f, false);
        }
    }

    /// One particle with its own velocity.
    public static void Spark(Vector3 pos, Vector3 vel, Color c, float life, float size = 1f, float gravity = 0f, float drag = 0f) =>
        Add(pos, vel, c, life, gravity, size, drag, true);

    /// A ring of particles flying out flat along the ground (speed < 0: rushing in from that far out).
    public static void Radial(Vector3 pos, Color c, int count, float speed, float life, float size = 1f, float squash = 0.5f)
    {
        for (int i = 0; i < count; i++)
        {
            float a = (i + Random.value * 0.6f) * Mathf.PI * 2f / count;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * squash, 0f);
            float s = Mathf.Abs(speed) * Random.Range(0.75f, 1.2f);
            if (speed >= 0f) Add(pos, dir * s, c, life * Random.Range(0.8f, 1.15f), 0f, size, 1.5f, true);
            else Add(pos + dir * s * life, -dir * s, c, life, 0f, size, 0f, true); // ends right at the centre
        }
    }

    /// Particles rising from an area of the ground (smoke, sparkles, bubbles, thorns).
    public static void Rise(Vector3 pos, Color c, int count, float rx, float speed, float life, float size = 1f, float gravity = -0.5f)
    {
        for (int i = 0; i < count; i++)
        {
            var off = Random.insideUnitCircle;
            Add(pos + new Vector3(off.x * rx, off.y * rx * 0.5f, 0f), new Vector3(Random.Range(-0.15f, 0.15f), speed * Random.Range(0.6f, 1.2f), 0f),
                c, life * Random.Range(0.7f, 1.2f), gravity, size, 1f, false);
        }
    }

    /// A jagged lightning bolt from a to b, with a white-hot core and optional side branches.
    public static void Bolt(Vector3 a, Vector3 b, Color c, float thickness = 1f, float jitter = 0.15f, int branches = 0, float life = 0.3f)
    {
        float len = Vector3.Distance(a, b);
        if (len < 0.01f) return;
        int nodes = Mathf.Max(4, Mathf.RoundToInt(len * 6f));
        Vector3 side = Vector3.Cross(b - a, Vector3.forward).normalized;
        var pts = new Vector3[nodes + 1];
        for (int i = 0; i <= nodes; i++)
        {
            float k = i / (float)nodes;
            float j = i == 0 || i == nodes ? 0f : Random.Range(-1f, 1f) * jitter * Mathf.Sin(k * Mathf.PI);
            pts[i] = Vector3.Lerp(a, b, k) + side * j;
        }
        var core = Color.Lerp(c, Color.white, 0.75f);
        for (int i = 0; i < nodes; i++)
        {
            float seg = Vector3.Distance(pts[i], pts[i + 1]);
            int steps = Mathf.Max(1, Mathf.CeilToInt(seg / 0.035f));
            for (int s = 0; s < steps; s++)
            {
                var p = Vector3.Lerp(pts[i], pts[i + 1], s / (float)steps);
                Add(p, Vector3.zero, c, life * Random.Range(0.8f, 1.1f), 0f, thickness * 1.6f, 0f, true);
                Add(p, Vector3.zero, core, life * 0.7f, 0f, Mathf.Max(1f, thickness * 0.7f), 0f, true);
            }
        }
        for (int k = 0; k < branches; k++)
        {
            var from = pts[Random.Range(1, nodes)];
            var dir = (b - a).normalized + side * Random.Range(-1.2f, 1.2f);
            Bolt(from, from + dir.normalized * len * Random.Range(0.2f, 0.35f), c, Mathf.Max(1f, thickness * 0.6f), jitter * 0.6f, 0, life * 0.8f);
        }
    }

    /// A sprite on the ground that grows from `from` to `to` scale and fades (after `delay`).
    public static void Wave(Sprite sprite, Vector3 pos, Color c, float from, float to, float dur, int order, float delay = 0f, bool flip = false)
    {
        EnsureRoot();
        var sr = new GameObject("wave").AddComponent<SpriteRenderer>();
        sr.transform.SetParent(root);
        sr.transform.position = pos;
        sr.transform.localScale = Vector3.one * from;
        sr.sprite = sprite;
        sr.flipX = flip;
        sr.sortingOrder = order;
        sr.color = delay > 0f ? Color.clear : c;
        decals.Add(new Decal { sr = sr, t = 0f, dur = dur, delay = delay, from = from, to = to, alpha = c.a, color = c });
    }

    public static void Tick(float dt)
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var p = live[i];
            if (p.sr == null) { live.RemoveAt(i); continue; }
            p.life -= dt;
            if (p.life <= 0f)
            {
                p.sr.gameObject.SetActive(false);
                p.sr.transform.localScale = Vector3.one;
                pool.Push(p.sr);
                live.RemoveAt(i);
                continue;
            }
            p.vel.y -= p.gravity * dt;
            float keep = Mathf.Max(0f, 1f - p.drag * dt);
            p.vel.x *= keep;
            if (p.dragY) p.vel.y *= keep;
            p.sr.transform.position += p.vel * dt;
            var c = p.color;
            c.a = Mathf.Clamp01(p.life / (p.maxLife * 0.5f)) * p.color.a;
            p.sr.color = c;
            if (p.size > 1f) p.sr.transform.localScale = Vector3.one * Mathf.Max(1f, p.size * (0.35f + 0.65f * p.life / p.maxLife));
        }
        for (int i = decals.Count - 1; i >= 0; i--)
        {
            var d = decals[i];
            if (d.sr == null) { decals.RemoveAt(i); continue; }
            if (d.delay > 0f) { d.delay -= dt; continue; }
            d.t += dt;
            float k = Mathf.Clamp01(d.t / d.dur);
            if (k >= 1f) { Object.Destroy(d.sr.gameObject); decals.RemoveAt(i); continue; }
            float e = 1f - (1f - k) * (1f - k); // ease out
            d.sr.transform.localScale = Vector3.one * Mathf.Lerp(d.from, d.to, e);
            var c = d.color;
            c.a = d.alpha * (1f - k);
            d.sr.color = c;
        }
    }
}
