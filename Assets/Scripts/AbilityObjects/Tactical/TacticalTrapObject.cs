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
    protected int PlacerActor => photonView.OwnerActorNr;
    protected float PlacedAt { get; private set; }

    protected virtual void Awake() => ObjectTunables.Apply(photonView); // Cheat values from the placer

    protected virtual void Start()
    {
        Power = SpawnData.Power(photonView);
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

    /// <summary>A see-through material in one colour (unlit; drawn from both sides).</summary>
    protected static Material Glass(Color color) => new(Shader.Find("Sprites/Default")) { color = color };
}
