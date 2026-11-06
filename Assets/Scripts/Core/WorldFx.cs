using Photon.Pun;
using UnityEngine;

/// <summary>
/// Shared helpers for one-shot world effects: positional audio, scaled VFX and explosions.
/// Every client calls these locally (usually from an RPC) — nothing here is networked.
/// </summary>
public static class WorldFx
{
    private static readonly Collider[] overlapBuffer = new Collider[64];

    /// <summary>Plays a clip at a position with linear falloff to silence at <paramref name="maxDistance"/>.</summary>
    public static void PlayClip(AudioClip clip, Vector3 position, float maxDistance, float volume = 1f)
    {
        if (clip == null) return;

        var go = new GameObject($"Audio ({clip.name})");
        go.transform.position = position;
        var source = go.AddComponent<AudioSource>();
        Configure3D(source, maxDistance, volume);
        source.clip = clip;
        source.Play();
        Object.Destroy(go, clip.length + 0.1f);
    }

    private static AudioClip beepClip;

    /// <summary>
    /// A short countdown beep at a position: <paramref name="clip"/> if one is given, otherwise a generated
    /// tone (so countdowns work before real sounds exist). Higher <paramref name="pitch"/> for the last beep.
    /// </summary>
    public static void Beep(Vector3 position, float maxDistance, AudioClip clip = null, float pitch = 1f, float volume = 0.8f)
    {
        // Unity's == (not ??) so an unassigned inspector field counts as empty
        if (clip == null)
        {
            if (beepClip == null) beepClip = MakeBeep();
            clip = beepClip;
        }
        var go = new GameObject("Audio (Beep)");
        go.transform.position = position;
        var source = go.AddComponent<AudioSource>();
        Configure3D(source, maxDistance, volume);
        source.clip  = clip;
        source.pitch = pitch;
        source.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
    }

    private static AudioClip MakeBeep()
    {
        const int   rate     = 44100;
        const float seconds  = 0.12f;
        const float frequency = 1400f;
        int samples = Mathf.CeilToInt(rate * seconds);
        var data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / rate;
            float envelope = Mathf.Min(1f, Mathf.Min(i, samples - i) / (rate * 0.005f)); // No clicks at the ends
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.6f;
        }
        var clip = AudioClip.Create("Beep", samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Applies standard 3D settings to an AudioSource.</summary>
    public static void Configure3D(AudioSource source, float maxDistance, float volume = 1f)
    {
        source.spatialBlend = 1f;
        source.rolloffMode  = AudioRolloffMode.Linear;
        source.minDistance  = 1f;
        source.maxDistance  = maxDistance;
        source.volume       = volume;
        source.playOnAwake  = false;
    }

    /// <summary>Spawns a local VFX prefab, scaling every particle system's start size.</summary>
    public static GameObject SpawnVfx(GameObject prefab, Vector3 position, float sizeScale = 1f)
    {
        if (prefab == null) return null;

        var vfx = Object.Instantiate(prefab, position, Quaternion.identity);
        if (!Mathf.Approximately(sizeScale, 1f))
        {
            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.startSizeMultiplier *= sizeScale;
            }
        }
        return vfx;
    }

    /// <summary>
    /// Applies an explosion impulse to every rigidbody this client has authority over:
    /// its own networked objects (including the local player) and un-networked scene props.
    /// Remote objects are moved by their owners, so pushing them here would only cause jitter.
    /// </summary>
    /// <param name="sourceActor">Who caused it (0 = the world): a shielded player ignores other players' blasts.</param>
    /// <param name="cause">The ability id behind it (knock-offs name it in the kill feed).</param>
    public static void Explode(Vector3 position, float radius, float force, LayerMask mask, float upwardsModifier = 1f, int sourceActor = 0,
                               string cause = null)
    {
        int layers = mask.value != 0 ? mask.value : Physics.AllLayers;
        int count  = Physics.OverlapSphereNonAlloc(position, radius, overlapBuffer, layers, QueryTriggerInteraction.Ignore);

        // A body with several colliders would otherwise be pushed once per collider
        var pushed = new System.Collections.Generic.HashSet<Rigidbody>();
        for (int i = 0; i < count; i++)
        {
            Rigidbody body = overlapBuffer[i].attachedRigidbody;
            if (body == null || body.isKinematic || !pushed.Add(body)) continue;
            if (!HasAuthority(body)) continue;
            var player = body.GetComponent<Player>();
            if (player != null && !player.AffectedBy(sourceActor)) continue;
            body.AddExplosionForce(force, position, radius, upwardsModifier, ForceMode.Impulse);
            if (player != null) player.NotePushed(sourceActor, cause);
        }
    }

    /// <summary>
    /// Applies an outside force to a body this client has authority over. For a player it also tells the
    /// movement code not to brake for a moment, so pulls and blasts actually move them.
    /// </summary>
    /// <param name="sourceActor">Who caused it (0 = the world, e.g. fans): a shielded player ignores other players' pushes.</param>
    /// <param name="cause">The ability id behind it (knock-offs name it in the kill feed).</param>
    public static void Push(Rigidbody body, Vector3 force, ForceMode mode, int sourceActor = 0, string cause = null)
    {
        var player = body.GetComponent<Player>();
        if (player != null && !player.AffectedBy(sourceActor)) return;
        body.AddForce(force, mode);
        if (player != null) player.NotePushed(sourceActor, cause);
    }

    /// <summary>True if this client should simulate forces on <paramref name="body"/>.</summary>
    public static bool HasAuthority(Rigidbody body)
    {
        var view = body.GetComponentInParent<PhotonView>();
        return view == null || view.IsMine;
    }
}
