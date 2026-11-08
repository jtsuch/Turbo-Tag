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

    /// <summary>Owner: damage soaked up before health (Shield Bubble). 0 = none.</summary>
    public float Shield { get; set; }

    private void Awake() => player = GetComponent<Player>();

    // ─── Owner ────────────────────────────────────────────────────────────────

    /// <summary>Owner: back to full (respawn, health drop).</summary>
    public void Refill()
    {
        Shield  = 0f;
        Current = Max;
        dead    = false;
    }

    /// <summary>Owner: health to double the maximum, draining back down (Overheal donut).</summary>
    public void Overheal()
    {
        if (dead) return;
        Current = Max * 2f;
    }

    private void Update()
    {
        if (photonView.IsMine && Current > Max) Current = Mathf.Max(Max, Current - 6f * Time.deltaTime);
    }

    /// <summary>Owner: adds health, up to the maximum (the Vampire donut). Nothing once dead.</summary>
    public void Heal(float amount)
    {
        if (dead || amount <= 0f) return;
        Current = Mathf.Min(Max, Current + amount);
    }

    // ─── Any client ───────────────────────────────────────────────────────────

    /// <summary>The local shooter hurts this player (shot travelling along <paramref name="direction"/>); the owner applies it.</summary>
    public void RequestDamage(float amount, string weapon, bool headshot, Vector3 direction) =>
        photonView.RPC(nameof(RPC_Damage), photonView.Owner, amount, PhotonNetwork.LocalPlayer.ActorNumber, weapon, headshot, direction);

    [PunRPC]
    private void RPC_Damage(float amount, int attacker, string weapon, bool headshot, Vector3 direction)
    {
        if (!photonView.IsMine || dead || player.IsCaught || player.AdminGod || player.Role == PlayerRole.Ghost) return;
        if (player.Has(PlayerFlag.Golden)) { Fx.Shake(0.05f); return; } // Invulnerable donut
        var combat  = CombatManager.Instance;
        var manager = GameModeManager.Instance;
        // Only while the match is live: not once the clock has run out (the winner screen follows)
        if (combat == null || manager == null || manager.Phase != MatchPhase.Active || manager.Clock.IsExpired) return;

        if (Shield > 0f)
        {
            float soaked = Mathf.Min(Shield, amount);
            Shield -= soaked;
            amount -= soaked;
            Sfx.Here(SfxKind.Snap, 0.4f, 1.5f);
            if (amount <= 0f) return; // All of it into the shield
        }
        Current          = Mathf.Max(0f, Current - amount);
        LastAttacker     = attacker;
        LastDamageTime   = Time.time;
        LastHitDirection = direction; // Before Damaged: the HUD's hit-direction arcs read it
        LastHitTime      = Time.time;
        Damaged?.Invoke(amount, attacker);

        bool fatal = Current <= 0f;
        if (fatal && player.SecondLife)
        {
            // Second Life: back up on 50 instead of dying
            player.SecondLife = false;
            Current = Mathf.Min(Max, 50f);
            fatal = false;
            Fx.Heal(player.transform.position + Vector3.up, true);
            if (HUDManager.Instance != null) HUDManager.Instance.ShowBanner("Second Life!", 1.5f);
        }
        // Everyone sees the hit land: a flinch, feathers, a thud; the corpse is thrown along the shot
        photonView.RPC(nameof(RPC_HitReact), RpcTarget.All, direction, amount, headshot, fatal);
        KnockBack(direction, amount, fatal);

        if (!fatal) return;
        dead = true;
        combat.ReportLocalDeath(attacker, weapon, headshot);
    }

    // Owner: the shot shoves the body a little (more for big hits) and punches the view
    private void KnockBack(Vector3 direction, float amount, bool fatal)
    {
        if (fatal) return; // The ragdoll takes the hit instead
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        float push = Mathf.Clamp(amount * 0.08f, 1f, 6f);
        player.Body.linearVelocity += flat * push + Vector3.up * push * 0.25f;
        player.NotePushed();
        if (PlayerCam.Local != null) PlayerCam.Local.Punch(direction, amount);
    }

    /// <summary>Last shot that hit this player (every screen), for the flinch and for throwing the ragdoll.</summary>
    public Vector3 LastHitDirection { get; private set; }
    public float   LastHitTime      { get; private set; } = float.NegativeInfinity;
    public bool    LastHitHeadshot  { get; private set; }

    [PunRPC]
    private void RPC_HitReact(Vector3 direction, float amount, bool headshot, bool fatal)
    {
        LastHitDirection = direction;
        LastHitTime      = Time.time;
        LastHitHeadshot  = headshot;

        if (player.TryGetComponent(out HitFlinch flinch)) flinch.Flinch(direction, amount, headshot);
        if (photonView.IsMine) return; // Your own hit: the HUD flash, hurt sound and view punch cover it

        Vector3 at = player.transform.position + Vector3.up * (headshot ? player.EyeHeight : player.EyeHeight * 0.6f);
        Fx.Feathers(at, -direction, headshot ? 18 : 10);
        Sfx.At(SfxKind.Hurt, at, headshot ? 1f : 0.7f, 40f, headshot ? 1.25f : 1f);
    }
}
