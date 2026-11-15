using System.Collections.Generic;
using UnityEngine;

/// Very small pixel particle system: leaves, wood chips, splashes, sparks.
public static class Fx
{
    class Particle
    {
        public SpriteRenderer sr;
        public Vector3 vel;
        public float life, maxLife, gravity;
        public Color color;
    }

    static readonly List<Particle> live = new List<Particle>();
    static readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
    static Transform root;

    static SpriteRenderer Take()
    {
        if (root == null)
        {
            root = new GameObject("Fx").transform;
            pool.Clear();
            live.Clear();
        }
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

    /// Spray of single-pixel particles.
    public static void Burst(Vector3 pos, Color color, int count, float spread = 1.2f, float up = 1.6f, float life = 0.6f, float gravity = 5f)
    {
        for (int i = 0; i < count; i++)
        {
            var sr = Take();
            sr.transform.position = pos + new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.05f, 0.1f), 0f);
            var c = color;
            c.r *= Random.Range(0.85f, 1.1f); c.g *= Random.Range(0.85f, 1.1f);
            sr.color = c;
            live.Add(new Particle
            {
                sr = sr,
                vel = new Vector3(Random.Range(-1f, 1f) * spread, Random.Range(0.4f, 1f) * up, 0f),
                life = life * Random.Range(0.7f, 1.2f),
                maxLife = life,
                gravity = gravity,
                color = c,
            });
        }
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
                pool.Push(p.sr);
                live.RemoveAt(i);
                continue;
            }
            p.vel.y -= p.gravity * dt;
            p.vel.x *= 1f - 2f * dt;
            p.sr.transform.position += p.vel * dt;
            var c = p.color;
            c.a = Mathf.Clamp01(p.life / (p.maxLife * 0.5f));
            p.sr.color = c;
        }
    }
}
