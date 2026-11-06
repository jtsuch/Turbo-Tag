using System.Collections.Generic;
using UnityEngine;

/// <summary>Every generated gameplay sound.</summary>
public enum SfxKind
{
    Pistol, Rifle, Shotgun, Sniper, Reload, Empty,
    Hit, Headshot, Kill, Hurt, Heal,
    Whoosh, Thwip, Warp, ZapUp, ZapDown, Heartbeat, Shimmer, Toss, Place, Flap, Blow, Pop,
    Explosion, Boing, Land, Jump, Tag, Respawn, Ice, Snap, Splat, Horn, Fanfare, Honk, Plop,
}

/// <summary>
/// Gameplay sounds made in code (no audio files), like the menu's UISound: gunshots, hit ticks, a headshot ding,
/// kill confirms, ability whooshes and zaps, explosions, boings, splats, a round horn and more. Clips are synthesised
/// once (noise bursts, filtered sweeps and tones) and played from a pool of AudioSources: <see cref="At"/> in 3D at a
/// spot (everyone nearby hears it), <see cref="Here"/> flat for the local player's own feedback. Master volume applies.
/// </summary>
public static class Sfx
{
    private const int Rate = 44100;
    private const int PoolSize = 24;

    private static readonly Dictionary<SfxKind, AudioClip> clips = new();
    private static readonly List<AudioSource> pool = new();
    private static int next;
    private static Transform root;

    /// <summary>Makes every clip now (a few ms, at level load) so none is built mid-fight.</summary>
    public static void Prewarm()
    {
        foreach (SfxKind kind in System.Enum.GetValues(typeof(SfxKind))) Clip(kind);
    }

    /// <summary>Plays <paramref name="kind"/> in 3D at <paramref name="position"/>.</summary>
    public static void At(SfxKind kind, Vector3 position, float volume = 1f, float maxDistance = 45f, float pitch = 1f)
    {
        var source = Source();
        source.transform.position = position;
        source.spatialBlend = 1f;
        source.maxDistance = maxDistance;
        Play(source, kind, volume, pitch);
    }

    /// <summary>Plays <paramref name="kind"/> flat (2D): the local player's own feedback.</summary>
    public static void Here(SfxKind kind, float volume = 1f, float pitch = 1f)
    {
        var source = Source();
        source.spatialBlend = 0f;
        Play(source, kind, volume, pitch);
    }

    private static void Play(AudioSource source, SfxKind kind, float volume, float pitch)
    {
        source.Stop();
        source.clip = Clip(kind);
        source.volume = Mathf.Clamp01(volume);
        source.pitch = pitch * Random.Range(0.95f, 1.05f); // A little variety on repeats
        source.Play();
    }

    private static AudioSource Source()
    {
        if (root == null)
        {
            pool.Clear();
            root = new GameObject("Sfx").transform;
            Object.DontDestroyOnLoad(root.gameObject);
            for (int i = 0; i < PoolSize; i++)
            {
                var s = new GameObject("Voice").AddComponent<AudioSource>();
                s.transform.SetParent(root, false);
                s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 2f;
                s.dopplerLevel = 0f;
                pool.Add(s);
            }
        }
        // The oldest voice that isn't playing, else the oldest of all
        for (int i = 0; i < pool.Count; i++)
        {
            var s = pool[(next + i) % pool.Count];
            if (!s.isPlaying) { next = (next + i + 1) % pool.Count; return s; }
        }
        var steal = pool[next];
        next = (next + 1) % pool.Count;
        return steal;
    }

    // ─── Synthesis ────────────────────────────────────────────────────────────

    private static AudioClip Clip(SfxKind kind)
    {
        if (clips.TryGetValue(kind, out var clip) && clip != null) return clip;
        float[] d = kind switch
        {
            SfxKind.Pistol    => Gun(0.22f, 0.9f, 140f, 0.55f),
            SfxKind.Rifle     => Gun(0.14f, 0.8f, 160f, 0.45f),
            SfxKind.Shotgun   => Gun(0.42f, 0.35f, 90f, 0.9f),
            SfxKind.Sniper    => Gun(0.75f, 0.45f, 60f, 1f, crack: true),
            SfxKind.Reload    => Concat(Click(0.04f, 2400f), Silence(0.12f), Click(0.05f, 1600f), Silence(0.18f), Click(0.06f, 2000f)),
            SfxKind.Empty     => Click(0.04f, 3200f),
            SfxKind.Hit       => Tone(0.05f, 2300f, 2300f, 0.5f, decay: 60f),
            SfxKind.Headshot  => Mix(Tone(0.35f, 1760f, 1760f, 0.4f, decay: 9f), Tone(0.35f, 2640f, 2640f, 0.25f, decay: 12f)),
            SfxKind.Kill      => Concat(Tone(0.09f, 880f, 880f, 0.4f, decay: 14f), Tone(0.22f, 1320f, 1320f, 0.45f, decay: 9f)),
            SfxKind.Hurt      => Mix(Tone(0.18f, 140f, 70f, 0.8f, decay: 14f), Filtered(Noise(0.12f), 0.15f, 0.35f, decay: 30f)),
            SfxKind.Heal      => Concat(Tone(0.08f, 660f, 660f, 0.3f, 12f), Tone(0.08f, 880f, 880f, 0.3f, 12f), Tone(0.2f, 1320f, 1320f, 0.3f, 8f)),
            SfxKind.Whoosh    => Swell(0.35f, 0.08f, 0.45f, 0.7f),
            SfxKind.Thwip     => Mix(Swell(0.12f, 0.3f, 0.5f, 0.5f), Tone(0.12f, 500f, 1400f, 0.25f, decay: 18f)),
            SfxKind.Warp      => Vibrato(0.45f, 1200f, 300f, 0.35f),
            SfxKind.ZapUp     => Vibrato(0.3f, 400f, 1600f, 0.3f),
            SfxKind.ZapDown   => Vibrato(0.3f, 1600f, 400f, 0.3f),
            SfxKind.Heartbeat => Concat(Tone(0.1f, 70f, 55f, 1f, 22f), Silence(0.08f), Tone(0.14f, 65f, 50f, 0.9f, 18f)),
            SfxKind.Shimmer   => Sparkle(0.5f, 2000f, 4200f, 0.18f),
            SfxKind.Toss      => Swell(0.22f, 0.15f, 0.5f, 0.45f),
            SfxKind.Place     => Mix(Tone(0.12f, 220f, 110f, 0.8f, 26f), Click(0.03f, 900f)),
            SfxKind.Flap      => Concat(Swell(0.09f, 0.05f, 0.4f, 0.5f), Swell(0.09f, 0.05f, 0.4f, 0.4f)),
            SfxKind.Blow      => Swell(0.6f, 0.25f, 0.2f, 0.5f),
            SfxKind.Pop       => Tone(0.08f, 700f, 250f, 0.6f, decay: 30f),
            SfxKind.Explosion => Boom(1.4f),
            SfxKind.Boing     => Boing(0.45f),
            SfxKind.Land      => Mix(Tone(0.12f, 110f, 60f, 0.7f, 28f), Filtered(Noise(0.1f), 0.08f, 0.3f, decay: 35f)),
            SfxKind.Jump      => Swell(0.12f, 0.2f, 0.6f, 0.35f),
            SfxKind.Tag       => Mix(Filtered(Noise(0.06f), 0.6f, 0.9f, decay: 50f), Tone(0.25f, 520f, 780f, 0.35f, decay: 10f)),
            SfxKind.Respawn   => Sparkle(0.6f, 800f, 2400f, 0.25f),
            SfxKind.Ice       => Mix(Sparkle(0.7f, 2500f, 5000f, 0.25f), Filtered(Noise(0.25f), 0.7f, 0.25f, decay: 12f)),
            SfxKind.Snap      => Mix(Click(0.03f, 3000f), Tone(0.08f, 300f, 150f, 0.6f, 40f)),
            SfxKind.Splat     => Filtered(Noise(0.2f), 0.12f, 0.7f, decay: 18f),
            SfxKind.Horn      => Chord(0.7f, new[] { 262f, 330f, 392f }, 0.22f),
            SfxKind.Fanfare   => Concat(Chord(0.15f, new[] { 392f, 494f }, 0.2f), Chord(0.15f, new[] { 440f, 554f }, 0.2f), Chord(0.6f, new[] { 523f, 659f, 784f }, 0.22f)),
            SfxKind.Honk      => Honk(0.42f),
            SfxKind.Plop      => Mix(Tone(0.12f, 900f, 250f, 0.6f, 25f), Filtered(Noise(0.1f), 0.3f, 0.4f, decay: 30f)),
            _                 => Silence(0.05f),
        };
        clip = AudioClip.Create("Sfx" + kind, d.Length, 1, Rate, false);
        clip.SetData(d, 0);
        clips[kind] = clip;
        return clip;
    }

    private static float[] Silence(float s) => new float[Mathf.CeilToInt(s * Rate)];

    private static float[] Noise(float s)
    {
        var d = Silence(s);
        for (int i = 0; i < d.Length; i++) d[i] = Random.Range(-1f, 1f);
        return d;
    }

    // One-pole low-pass (0–1: lower is darker), then an exponential decay
    private static float[] Filtered(float[] d, float cutoff, float gain, float decay)
    {
        float y = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            y += cutoff * (d[i] - y);
            float t = i / (float)Rate;
            d[i] = y * gain * Mathf.Exp(-t * decay) * Mathf.Min(1f, i / (Rate * 0.002f));
        }
        return d;
    }

    private static float[] Tone(float s, float from, float to, float gain, float decay)
    {
        var d = Silence(s);
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float k = i / (float)d.Length;
            phase += 2f * Mathf.PI * Mathf.Lerp(from, to, k) / Rate;
            float t = i / (float)Rate;
            d[i] = Mathf.Sin(phase) * gain * Mathf.Exp(-t * decay) * Mathf.Min(1f, i / (Rate * 0.003f));
        }
        return d;
    }

    private static float[] Click(float s, float freq) => Mix(Tone(s, freq, freq * 0.7f, 0.5f, 120f), Filtered(Noise(s), 0.9f, 0.4f, 150f));

    // Gunshot: a bright crack of noise, a darker body and a low thump
    private static float[] Gun(float s, float bright, float thump, float gain, bool crack = false)
    {
        var body = Filtered(Noise(s), bright, gain, decay: 9f / s);
        var low = Tone(s, thump, thump * 0.5f, gain * 0.8f, decay: 6f / s);
        var mix = Mix(body, low);
        if (crack) mix = Mix(mix, Filtered(Noise(0.05f), 0.95f, 0.9f, decay: 70f));
        return Clamp(mix);
    }

    private static float[] Boom(float s)
    {
        var rumble = Filtered(Noise(s), 0.03f, 2.5f, decay: 2.6f);
        var low = Tone(s, 70f, 30f, 0.9f, decay: 3f);
        var crack = Filtered(Noise(0.15f), 0.5f, 0.8f, decay: 25f);
        return Clamp(Mix(Mix(rumble, low), crack));
    }

    // Noise that swells and fades (wind, swooshes)
    private static float[] Swell(float s, float cutoff, float peakAt, float gain)
    {
        var d = Noise(s);
        float y = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float k = i / (float)d.Length;
            float c = cutoff * (0.6f + 0.8f * Mathf.Sin(k * Mathf.PI));
            y += c * (d[i] - y);
            float env = k < peakAt ? k / peakAt : 1f - (k - peakAt) / (1f - peakAt);
            d[i] = y * gain * 2.2f * env * env;
        }
        return d;
    }

    private static float[] Vibrato(float s, float from, float to, float gain)
    {
        var d = Silence(s);
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float k = i / (float)d.Length;
            float f = Mathf.Lerp(from, to, k) * (1f + 0.06f * Mathf.Sin(i / (float)Rate * 2f * Mathf.PI * 22f));
            phase += 2f * Mathf.PI * f / Rate;
            float env = Mathf.Min(1f, k * 12f) * (1f - k);
            d[i] = (Mathf.Sin(phase) + 0.3f * Mathf.Sign(Mathf.Sin(phase))) * gain * env;
        }
        return d;
    }

    // Random little bell tones scattered across the clip
    private static float[] Sparkle(float s, float low, float high, float gain)
    {
        var d = Silence(s);
        for (int n = 0; n < 9; n++)
        {
            var bell = Tone(0.18f, Random.Range(low, high), Random.Range(low, high), gain, decay: 18f);
            int start = Random.Range(0, Mathf.Max(1, d.Length - bell.Length));
            for (int i = 0; i < bell.Length && start + i < d.Length; i++) d[start + i] += bell[i];
        }
        return Clamp(d);
    }

    private static float[] Boing(float s)
    {
        var d = Silence(s);
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float f = 180f + 260f * (1f - Mathf.Exp(-t * 6f)) + 40f * Mathf.Sin(t * 2f * Mathf.PI * 14f) * Mathf.Exp(-t * 4f);
            phase += 2f * Mathf.PI * f / Rate;
            d[i] = Mathf.Sin(phase) * 0.55f * Mathf.Exp(-t * 5f) * Mathf.Min(1f, i / (Rate * 0.003f));
        }
        return d;
    }

    // A clown-horn honk: two reedy square-ish tones a little apart, with a squeeze dip in pitch
    private static float[] Honk(float s)
    {
        var d = Silence(s);
        float a = 0f, b = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float k = t / s;
            float bend = 1f - 0.12f * Mathf.Sin(k * Mathf.PI);
            a += 2f * Mathf.PI * 370f * bend / Rate;
            b += 2f * Mathf.PI * 466f * bend / Rate;
            float reed = Mathf.Clamp(Mathf.Sin(a) * 3f, -1f, 1f) + 0.7f * Mathf.Clamp(Mathf.Sin(b) * 3f, -1f, 1f);
            float env = Mathf.Min(1f, t / 0.015f) * Mathf.Min(1f, (s - t) / 0.06f);
            d[i] = reed * 0.28f * env;
        }
        return d;
    }

    private static float[] Chord(float s, float[] freqs, float gain)
    {
        var d = Silence(s);
        foreach (float f in freqs)
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float saw = 2f * (t * f - Mathf.Floor(0.5f + t * f));
                float env = Mathf.Min(1f, t / 0.02f) * Mathf.Min(1f, (s - t) / 0.12f);
                d[i] += (0.6f * Mathf.Sin(2f * Mathf.PI * f * t) + 0.25f * saw) * gain * env;
            }
        return Clamp(d);
    }

    private static float[] Mix(float[] a, float[] b)
    {
        var d = new float[Mathf.Max(a.Length, b.Length)];
        for (int i = 0; i < d.Length; i++) d[i] = (i < a.Length ? a[i] : 0f) + (i < b.Length ? b[i] : 0f);
        return d;
    }

    private static float[] Concat(params float[][] parts)
    {
        int n = 0;
        foreach (var p in parts) n += p.Length;
        var d = new float[n];
        int at = 0;
        foreach (var p in parts) { System.Array.Copy(p, 0, d, at, p.Length); at += p.Length; }
        return d;
    }

    private static float[] Clamp(float[] d)
    {
        for (int i = 0; i < d.Length; i++) d[i] = Mathf.Clamp(d[i], -1f, 1f);
        return d;
    }
}
