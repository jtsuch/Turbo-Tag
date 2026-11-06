using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Round Modifiers (the "Round Modifiers" rule, any mode): at the start of every round's countdown the host picks a
/// random modifier (<see cref="RoomKeys.Modifier"/>, never the same twice in a row), it's announced with the
/// countdown and shown under the clock, and every screen applies it to its own player until the round ends.
/// Lives on the GameModeManager object. <b>Add one:</b> an entry in <see cref="All"/> (stat multipliers, and/or
/// an <c>OnStart</c>/<c>OnEnd</c>/<c>Tick</c> for anything else).
/// </summary>
public class RoundModifiers : MonoBehaviour
{
    public sealed class Modifier
    {
        public string Id, Name, Description;
        public (Stat stat, float multiplier)[] Stats = Array.Empty<(Stat, float)>();
        public Action OnStart, OnEnd;   // This screen, when the modifier begins / ends
        public Action<Player> Tick;     // Every frame on the local player while it's live
    }

    private static readonly object Source = new(); // StatModifiers source

    public static readonly Modifier[] All =
    {
        new() { Id = "LowGravity", Name = "Low Gravity", Description = "Everyone floats. Jumps go much higher.",
                Stats = new[] { (Stat.Gravity, 0.45f) } },
        new() { Id = "SpeedDemons", Name = "Speed Demons", Description = "Everyone runs 45% faster.",
                Stats = new[] { (Stat.Speed, 1.45f), (Stat.Acceleration, 1.3f) } },
        new() { Id = "Tiny", Name = "Tiny Penguins", Description = "Everyone is half size. Hide anywhere.",
                Stats = new[] { (Stat.Size, 0.55f) } },
        new() { Id = "Giants", Name = "Giants", Description = "Everyone is huge. Nowhere to hide.",
                Stats = new[] { (Stat.Size, 1.6f) } },
        new() { Id = "SuperJump", Name = "Super Jump", Description = "Jumps are 70% stronger.",
                Stats = new[] { (Stat.Jump, 1.7f) } },
        new() { Id = "AbilityFrenzy", Name = "Ability Frenzy", Description = "Cooldowns are a third as long.",
                Stats = new[] { (Stat.Cooldown, 0.35f) } },
        new() { Id = "Slippery", Name = "Slippery", Description = "The whole map is ice.",
                Tick = p => { if (Time.time >= nextIce) { nextIce = Time.time + 0.4f; p.IceFor(0.7f); } } },
        new() { Id = "Fog", Name = "Thick Fog", Description = "You can barely see past your beak.",
                OnStart = FogOn, OnEnd = FogOff },
        new() { Id = "DonutParty", Name = "Donut Party", Description = "Donuts everywhere, four times as often." },
    };

    private static float nextIce;
    private static RoundModifiers instance;
    private Modifier applied;
    private float nextSync;

    /// <summary>The modifier this round (null when none or the rule is off).</summary>
    public static Modifier Current => instance != null ? Find(NetProps.Room(RoomKeys.Modifier, "")) : null;

    public static Modifier Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var m in All) if (m.Id == id) return m;
        return null;
    }

    /// <summary>Master: a random modifier different from <paramref name="last"/>.</summary>
    public static string PickNext(string last)
    {
        var options = new List<Modifier>(All);
        options.RemoveAll(m => m.Id == last);
        return options[UnityEngine.Random.Range(0, options.Count)].Id;
    }

    private void Awake() => instance = this;

    private void OnDestroy()
    {
        Apply(null);
        if (instance == this) instance = null;
    }

    private void Update()
    {
        var manager = GameModeManager.Instance;
        var phase = manager != null ? manager.Phase : MatchPhase.Waiting;
        bool live = phase == MatchPhase.Countdown || phase == MatchPhase.Hiding || phase == MatchPhase.Active;
        var want = live ? Current : null;
        if (want != applied)
        {
            Apply(want);
            if (want != null && phase == MatchPhase.Countdown && HUDManager.Instance != null)
                HUDManager.Instance.ShowBanner($"<size=70%>MODIFIER</size>\n{want.Name.ToUpperInvariant()}\n<size=55%>{want.Description}</size>", 4f);
        }

        var local = Player.Local;
        if (applied == null || local == null) return;
        // Respawns and size changes rebuild the player's state: keep the multipliers set
        if (Time.time >= nextSync)
        {
            nextSync = Time.time + 0.25f;
            foreach (var (stat, mult) in applied.Stats) local.Modifiers.Set(stat, Source, mult);
        }
        if (phase != MatchPhase.Countdown) applied.Tick?.Invoke(local);
    }

    private void Apply(Modifier next)
    {
        var local = Player.Local;
        if (applied != null)
        {
            if (local != null) local.Modifiers.Clear(Source);
            applied.OnEnd?.Invoke();
        }
        applied = next;
        if (applied == null) return;
        if (local != null) foreach (var (stat, mult) in applied.Stats) local.Modifiers.Set(stat, Source, mult);
        applied.OnStart?.Invoke();
    }

    // ─── Fog ──────────────────────────────────────────────────────────────────

    private static bool fogWas;
    private static FogMode fogModeWas;
    private static Color fogColorWas;
    private static float fogDensityWas;

    private static void FogOn()
    {
        fogWas = RenderSettings.fog;
        fogModeWas = RenderSettings.fogMode;
        fogColorWas = RenderSettings.fogColor;
        fogDensityWas = RenderSettings.fogDensity;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.72f, 0.74f, 0.8f);
        RenderSettings.fogDensity = 0.045f;
    }

    private static void FogOff()
    {
        RenderSettings.fog = fogWas;
        RenderSettings.fogMode = fogModeWas;
        RenderSettings.fogColor = fogColorWas;
        RenderSettings.fogDensity = fogDensityWas;
    }
}
