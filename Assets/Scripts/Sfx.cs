using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tiny synthesizer: every sound effect is generated in code, so no audio files are needed.
/// Replace with real sounds later: put clips in Assets/Resources/Sounds/<name>.wav (or .ogg)
/// and they are used instead (names: pickup, chop, drink, eat, hurt, hit, craft, splash, win, waves).
/// </summary>
public static class Sfx
{
    const int Rate = 44100;
    static AudioSource src, ambience;
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    static readonly System.Random rnd = new System.Random(1234);

    public static void Init(GameObject host)
    {
        if (src != null) return;
        if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null)
            (Camera.main != null ? Camera.main.gameObject : host).AddComponent<AudioListener>();
        src = host.AddComponent<AudioSource>();
        src.playOnAwake = false;
        ambience = host.AddComponent<AudioSource>();
        ambience.clip = Get("waves");
        ambience.loop = true;
        ambience.volume = 0.22f;
        ambience.Play();
    }

    public static void Play(string name, float volume = 0.6f)
    {
        if (src == null) return;
        src.pitch = 1f + (float)(rnd.NextDouble() - 0.5) * 0.15f;
        src.PlayOneShot(Get(name), volume);
    }

    static AudioClip Get(string name)
    {
        if (clips.TryGetValue(name, out var c) && c != null) return c;
        c = Resources.Load<AudioClip>("Sounds/" + name) ?? Make(name);
        clips[name] = c;
        return c;
    }

    static float Noise() => (float)(rnd.NextDouble() * 2.0 - 1.0);

    static AudioClip Make(string name)
    {
        switch (name)
        {
            case "pickup": // short rising blip
                return Synth(name, 0.09f, (t, i) => Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(600, 1300, t / 0.09f) * t) * Mathf.Exp(-t * 25));
            case "chop": // woody thunk
                return Synth(name, 0.14f, (t, i) => (Noise() * 0.6f * Mathf.Exp(-t * 45)) + Mathf.Sin(2 * Mathf.PI * 120 * t) * Mathf.Exp(-t * 28));
            case "drink": // three bubbles
                return Synth(name, 0.27f, (t, i) =>
                {
                    float lt = t % 0.09f;
                    return Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(520, 260, lt / 0.09f) * lt) * Mathf.Exp(-lt * 30) * 0.8f;
                });
            case "eat": // crunch
                return Synth(name, 0.22f, (t, i) => Noise() * (Mathf.Sin(2 * Mathf.PI * 22 * t) > 0 ? 1f : 0.15f) * Mathf.Exp(-t * 10) * 0.6f);
            case "hurt":
                return Synth(name, 0.22f, (t, i) => Mathf.Sign(Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(170, 80, t / 0.22f) * t)) * Mathf.Exp(-t * 12) * 0.35f);
            case "hit":
                return Synth(name, 0.1f, (t, i) => (Noise() * 0.5f + Mathf.Sin(2 * Mathf.PI * 320 * t)) * Mathf.Exp(-t * 40));
            case "craft": // two-note chime
                return Synth(name, 0.32f, (t, i) => Mathf.Sin(2 * Mathf.PI * (t < 0.12f ? 660 : 990) * t) * Mathf.Exp(-(t % 0.12f) * 14) * 0.6f);
            case "splash":
                {
                    float lp = 0f;
                    return Synth(name, 0.35f, (t, i) => { lp += (Noise() - lp) * 0.25f; return lp * Mathf.Exp(-t * 9) * 1.4f; });
                }
            case "win": // little arpeggio
                return Synth(name, 0.9f, (t, i) =>
                {
                    float[] notes = { 523, 659, 784, 1047 };
                    int n = Mathf.Min(3, (int)(t / 0.18f));
                    float lt = t - n * 0.18f;
                    return Mathf.Sin(2 * Mathf.PI * notes[n] * t) * Mathf.Exp(-lt * 5) * 0.5f;
                });
            case "levelup": // quick rising sparkle
                return Synth(name, 0.5f, (t, i) =>
                {
                    float[] notes = { 784, 988, 1175, 1568 };
                    int n = Mathf.Min(3, (int)(t / 0.08f));
                    return (Mathf.Sin(2 * Mathf.PI * notes[n] * t) + 0.3f * Mathf.Sin(4 * Mathf.PI * notes[n] * t)) * Mathf.Exp(-t * 5) * 0.4f;
                });
            case "waves": // looping surf: brown noise with a slow swell
                {
                    float y = 0f;
                    const float len = 6f;
                    return Synth(name, len, (t, i) =>
                    {
                        y += (Noise() - y) * 0.03f;
                        float swell = 0.55f + 0.45f * Mathf.Sin(2 * Mathf.PI * t / len);
                        float edge = Mathf.Min(1f, Mathf.Min(t, len - t) / 0.05f); // avoid a click at the loop point
                        return y * 2.2f * swell * edge;
                    });
                }
            default:
                return Synth(name, 0.05f, (t, i) => 0f);
        }
    }

    static AudioClip Synth(string name, float seconds, Func<float, int, float> wave)
    {
        int n = Mathf.CeilToInt(seconds * Rate);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave(i / (float)Rate, i), -1f, 1f);
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
