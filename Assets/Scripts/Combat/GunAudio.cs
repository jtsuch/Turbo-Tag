using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Recorded gun sounds. Each folder <c>Resources/Sfx/Guns/{Name}/</c> holds Justin's recordings chopped by Claude:
/// <c>Shot*</c> (a single shot with its echo: taps and the first shot of a spray), <c>Auto*</c> (one shot's body,
/// about a fire interval long: every later shot of a held spray, so it runs together), <c>Tail</c> (the echo after a
/// spray), <c>Cycle</c> (pumping or working the bolt, a moment after each shot) and <c>ReloadOut</c>/<c>ReloadIn</c>/
/// <c>ReloadBolt</c> (timed to the reload animation).
/// <see cref="Recipes"/> says which folder each gun sounds like and at what pitch (Justin: lighter guns higher), and
/// where its reload comes from: the AR's recordings stand in for most guns. A gun without a recipe keeps the
/// code-made sounds (<see cref="Fx.Shot"/>, <see cref="SfxKind.Reload"/>). PlayerGun decides which clip to play.
/// </summary>
public static class GunAudio
{
    private readonly struct Recipe
    {
        public readonly string Sounds, Reload;
        public readonly float Pitch, ReloadPitch, CycleDelay;
        public Recipe(string sounds, float pitch, string reload, float reloadPitch, float cycleDelay = 0f)
        { Sounds = sounds; Pitch = pitch; Reload = reload; ReloadPitch = reloadPitch; CycleDelay = cycleDelay; }
    }

    // Gun id → (shots from, pitch, reload from, its pitch, seconds from a shot to its pump/bolt (0: none))
    private static readonly Dictionary<string, Recipe> Recipes = new()
    {
        ["AR"]          = new("AR", 1f, "AR", 1f),
        ["SMG"]         = new("AR", 1.2f, "AR", 1.15f),          // Lighter: higher
        ["AutoPistol"]  = new("AR", 1.35f, "AR", 1.3f),
        ["LMG"]         = new("AR", 0.82f, "AR", 0.85f),         // Heavier: lower
        ["LongRifle"]   = new("AR", 0.9f, "AR", 0.95f),          // Semi-auto: always the full shot
        ["Pistol"]      = new("AR", 1.5f, "AR", 1.35f),
        ["DesertEagle"] = new("Sniper", 1.3f, "AR", 1.2f),       // The hand cannon borrows the sniper's crack
        ["Shotgun"]     = new("Shotgun", 1f, "Shotgun", 1f, 0.25f),
        ["Sniper"]      = new("Sniper", 1f, "Sniper", 1f, 0.45f),
    };

    public sealed class Bank
    {
        public readonly List<AudioClip> Shots = new(), Autos = new();
        public AudioClip Tail, Cycle, ReloadOut, ReloadIn, ReloadBolt;
        public float Pitch = 1f, ReloadPitch = 1f, CycleDelay;
        public bool HasReload => ReloadOut != null || ReloadIn != null || ReloadBolt != null;
        private int lastShot = -1, lastAuto = -1;

        public AudioClip Shot() => Pick(Shots, ref lastShot);
        public AudioClip Auto() => Autos.Count > 0 ? Pick(Autos, ref lastAuto) : Shot();

        // A random clip, never the same one twice running
        private static AudioClip Pick(List<AudioClip> list, ref int last)
        {
            if (list.Count == 0) return null;
            int i = Random.Range(0, list.Count);
            if (list.Count > 1 && i == last) i = (i + 1) % list.Count;
            last = i;
            return list[i];
        }
    }

    private static readonly Dictionary<string, Bank> banks = new();
    private static readonly Dictionary<string, AudioClip[]> folders = new();

    private static AudioClip[] Folder(string name)
    {
        if (!folders.TryGetValue(name, out var clips)) folders[name] = clips = Resources.LoadAll<AudioClip>($"Sfx/Guns/{name}");
        return clips;
    }

    /// <summary>The recorded sounds for <paramref name="gunId"/>, or null (code-made sounds).</summary>
    public static Bank For(string gunId)
    {
        if (string.IsNullOrEmpty(gunId)) return null;
        if (banks.TryGetValue(gunId, out var bank)) return bank;
        bank = null;
        if (Recipes.TryGetValue(gunId, out var recipe))
        {
            bank = new Bank { Pitch = recipe.Pitch, ReloadPitch = recipe.ReloadPitch, CycleDelay = recipe.CycleDelay };
            foreach (var clip in Folder(recipe.Sounds))
            {
                string n = clip.name;
                if (n.StartsWith("Shot")) bank.Shots.Add(clip);
                else if (n.StartsWith("Auto")) bank.Autos.Add(clip);
                else if (n == "Tail") bank.Tail = clip;
                else if (n == "Cycle" && recipe.CycleDelay > 0f) bank.Cycle = clip;
            }
            foreach (var clip in Folder(recipe.Reload))
            {
                if (clip.name == "ReloadOut") bank.ReloadOut = clip;
                else if (clip.name == "ReloadIn") bank.ReloadIn = clip;
                else if (clip.name == "ReloadBolt") bank.ReloadBolt = clip;
            }
            bank.Shots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            bank.Autos.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (bank.Shots.Count == 0) bank = null;
        }
        banks[gunId] = bank;
        return bank;
    }

    /// <summary>Plays a gun clip at <paramref name="pitch"/> (± a touch): flat for your own gun, in 3D at
    /// <paramref name="at"/> for others'.</summary>
    public static void Play(AudioClip clip, bool local, Vector3 at, float volume, float range, float pitch = 1f)
    {
        if (clip == null) return;
        pitch *= Random.Range(0.97f, 1.03f);
        if (local) Sfx.ClipHere(clip, volume, pitch);
        else Sfx.ClipAt(clip, at, volume, range, pitch);
    }
}
