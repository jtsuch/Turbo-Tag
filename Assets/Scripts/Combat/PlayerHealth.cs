using System;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Health in gun modes. The owner keeps the number: shooters send damage to the owner (<see cref="RequestDamage"/>),
/// the owner subtracts it and, at zero, reports the death to the master (CombatManager). No regeneration:
/// health comes back on respawn or from a health drop. Idle (and ignored) in modes without guns.
/// Added by Player on every copy.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class PlayerHealth : MonoBehaviourPun
{
    /// <summary>Local player: (amount, attacker actor) after damage was taken.</summary>
    public event Action<float, int> Damaged;

    public float Current { get; private set; } = MatchRules.DefaultMaxHealth;
    public float Max     => MatchRules.MaxHealth;
    public float Fraction => Max > 0f ? Mathf.Clamp01(Current / Max) : 0f;

    /// <summary>Last player who hurt this one, and when (Time.time).</summary>
    public int   LastAttacker   { get; private set; } = -1;
    public float LastDamageTime { get; private set; } = float.NegativeInfinity;

    private Player player;
    private bool   dead;

    private void Awake() => player = GetComponent<Player>();

    // ─── Owner ────────────────────────────────────────────────────────────────

    /// <summary>Owner: back to full (respawn, health drop).</summary>
    public void Refill()
    {
        Current = Max;
        dead    = false;
    }

    // ─── Any client ───────────────────────────────────────────────────────────

    /// <summary>The local shooter hurts this player; the owner applies it.</summary>
    public void RequestDamage(float amount, string weapon, bool headshot) =>
        photonView.RPC(nameof(RPC_Damage), photonView.Owner, amount, PhotonNetwork.LocalPlayer.ActorNumber, weapon, headshot);

    [PunRPC]
    private void RPC_Damage(float amount, int attacker, string weapon, bool headshot)
    {
        if (!photonView.IsMine || dead || player.IsCaught) return;
        var combat  = CombatManager.Instance;
        var manager = GameModeManager.Instance;
        // Only while the match is live: not once the clock has run out (the winner screen follows)
        if (combat == null || manager == null || manager.Phase != MatchPhase.Active || manager.Clock.IsExpired) return;

        Current        = Mathf.Max(0f, Current - amount);
        LastAttacker   = attacker;
        LastDamageTime = Time.time;
        Damaged?.Invoke(amount, attacker);

        if (Current > 0f) return;
        dead = true;
        combat.ReportLocalDeath(attacker, weapon, headshot);
    }
}
