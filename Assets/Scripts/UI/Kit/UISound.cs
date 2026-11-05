using UnityEngine;

/// <summary>
/// Menu sounds made in code (no audio files): a soft tick on hover, a pop on click, a lower pop for
/// back/close and a bright chime for confirming. One persistent AudioSource; master volume applies.
/// </summary>
public static class UISound
{
    public enum Kind { Hover, Click, Back, Confirm, Error }

    private static AudioSource source;
    private static AudioClip hover, click, back, confirm, error;
    private static float lastHover;

    public static void Play(Kind kind)
    {
        if (kind == Kind.Hover)
        {
            if (Time.unscaledTime - lastHover < 0.05f) return; // Sweeping across a list shouldn't buzz
            lastHover = Time.unscaledTime;
        }
        EnsureSource();
        var clip = kind switch
        {
            Kind.Hover   => hover   ??= Tone("UIHover",   1500f, 1800f, 0.035f, 0.18f),
            Kind.Back    => back    ??= Tone("UIBack",     520f,  360f, 0.09f,  0.45f),
            Kind.Confirm => confirm ??= Chime(),
            Kind.Error   => error   ??= Tone("UIError",    220f,  180f, 0.16f,  0.45f),
            _            => click   ??= Tone("UIClick",    760f, 1100f, 0.07f,  0.5f),
        };
        source.PlayOneShot(clip);
    }

    private static void EnsureSource()
    {
        if (source != null) return;
        var go = new GameObject("UISound");
        Object.DontDestroyOnLoad(go);
        source = go.AddComponent<AudioSource>();
        source.playOnAwake  = false;
        source.spatialBlend = 0f;
        source.volume       = 0.6f;
    }

    // A short sine sweep with a fast attack and an exponential tail
    private static AudioClip Tone(string name, float from, float to, float seconds, float gain)
    {
        const int rate = 44100;
        int n = Mathf.CeilToInt(rate * seconds);
        var data = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float f = Mathf.Lerp(from, to, t);
            phase += 2f * Mathf.PI * f / rate;
            float env = Mathf.Min(1f, i / (rate * 0.003f)) * Mathf.Exp(-t * 5f);
            data[i] = Mathf.Sin(phase) * env * gain;
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Two quick rising notes
    private static AudioClip Chime()
    {
        const int rate = 44100;
        int n = Mathf.CeilToInt(rate * 0.22f);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float time = i / (float)rate;
            float f = time < 0.07f ? 880f : 1320f;
            float local = time < 0.07f ? time : time - 0.07f;
            float env = Mathf.Min(1f, local / 0.003f) * Mathf.Exp(-local * 14f);
            data[i] = Mathf.Sin(2f * Mathf.PI * f * time) * env * 0.4f;
        }
        var clip = AudioClip.Create("UIConfirm", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
