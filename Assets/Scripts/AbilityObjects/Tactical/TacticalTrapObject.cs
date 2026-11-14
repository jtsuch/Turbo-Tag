using System;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Base for the tactical placed objects (Snow Screen, Trip Wire, Turret, Ice Wall, Slow Field, Suppression, Updraft
/// Vent): reads the placer's power, removes itself after <see cref="lifetime"/> (placer's client), and helps each screen
/// check its own player: <see cref="LocalEnemy"/> is the local player when they're on the other side from the placer
/// (traps spare the placer's team) and not shielded.
/// </summary>
public abstract class TacticalTrapObject : MonoBehaviourPun
{
    [Tooltip("Removes itself after this many seconds (0 = stays until the round ends).")]
    [TunableParam("Lasts (0 = round)", 0f, 120f)]
    [SerializeField] protected float lifetime = 15f;

    protected float Power { get; private set; } = 1f;

    /// <summary>The S&amp;D upgrade multiplier from the placer (1 elsewhere): each trap decides what it scales.</summary>
    protected float Upgrade { get; private set; } = 1f;

    /// <summary>Upgrades lengthen its lifetime (Suppression).</summary>
    protected virtual bool UpgradeScalesLifetime => false;
    protected int PlacerActor => photonView.OwnerActorNr;
    protected float PlacedAt { get; private set; }

    protected virtual void Awake() => ObjectTunables.Apply(photonView); // Cheat values from the placer

    protected virtual void Start()
    {
        Power = SpawnData.Power(photonView);
        Upgrade = SpawnData.Upgrade(photonView);
        if (UpgradeScalesLifetime) lifetime *= Upgrade;
        PlacedAt = Time.time;
        if (lifetime > 0f && photonView.IsMine) Invoke(nameof(Remove), lifetime);
        Sfx.At(SfxKind.Place, transform.position, 0.8f, 30f);
    }

    protected void Remove()
    {
        if (this != null && photonView.IsMine) PhotonNetwork.Destroy(gameObject);
    }

    /// <summary>The local player, if they're someone this trap works on (not the placer or their team, not shielded).</summary>
    protected Player LocalEnemy
    {
        get
        {
            var local = Player.Local;
            if (local == null || local.IsCaught || local.ActorNumber == PlacerActor) return null;
            return local.AffectedByTrap(PlacerActor) ? local : null;
        }
    }

    /// <summary>Keeps a short effect running on the local player while they stay in a zone (no new card every frame).</summary>
    protected static void Keep(Type effect, Player player, float seconds)
    {
        if (player.GetComponent(effect) is PlayerEffect running && running.Remaining > 0.25f) return;
        PlayerEffect.Apply(effect, player, seconds);
    }

    // Which zones currently hold each zone effect on the local player (two overlapping zones: leaving one keeps it)
    private static readonly System.Collections.Generic.Dictionary<Type, System.Collections.Generic.HashSet<object>> holders = new();

    /// <summary>
    /// A zone effect held on the local player while they're inside (no timer: the card stays up, "ON", and goes when
    /// they leave every zone holding it). Call every frame with whether they're inside this zone.
    /// </summary>
    protected void Hold(Type effect, Player player, bool inside)
    {
        if (!holders.TryGetValue(effect, out var set)) holders[effect] = set = new System.Collections.Generic.HashSet<object>();
        bool changed = inside ? set.Add(this) : set.Remove(this);
        if (!changed || player == null) return;
        var running = player.GetComponent(effect) as PlayerEffect;
        if (set.Count > 0 && running == null) PlayerEffect.Apply(effect, player, 0f);
        else if (set.Count == 0 && running != null) { running.CancelEffect(); Destroy(running); }
    }

    // A zone that goes (or the round ends) lets go of whatever it held
    protected virtual void OnDestroy()
    {
        foreach (var pair in holders)
        {
            if (!pair.Value.Remove(this) || pair.Value.Count > 0) continue;
            var local = Player.Local;
            if (local != null && local.GetComponent(pair.Key) is PlayerEffect running) { running.CancelEffect(); Destroy(running); }
        }
    }

    /// <summary>A see-through material in one colour (unlit; drawn from both sides).</summary>
    protected static Material Glass(Color color) => new(Shader.Find("Sprites/Default")) { color = color };
}
