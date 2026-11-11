using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Bulldozer (was Belly-Slide Bash): a committed 2 s charge along the ground that can't be stopped or steered (it
/// sends you a long way). Enemies run into take damage, are knocked aside and stunned for 1.5 s
/// (<see cref="StunnedEffect"/>: barely moving, slow to shoot); every piece of placed utility (anything a trap
/// ability put down, either team's) within reach of the path is smashed (<see cref="TacticalNet.DestroyUtility"/>).
/// Health modes only.
/// </summary>
[AbilityInfo("Charge forward for 2 s, unstoppable: enemies you hit are stunned and knocked aside, and any traps in your way are smashed.", HealthModesOnly = true)]
public class Bulldozer : QuickAbility
{
    [TunableParam("Speed", 5f, 60f)]
    [SerializeField] private float speed = 20f;
    [TunableParam("Charge Time", 0.2f, 5f)]
    [SerializeField] private float chargeSeconds = 2f;
    [TunableParam("Damage", 0f, 100f)]
    [SerializeField] private float damage = 20f;
    [TunableParam("Stun Time", 0f, 6f)]
    [SerializeField] private float stunSeconds = 1.5f;
    [TunableParam("Reach", 0.5f, 5f)]
    [SerializeField] private float reach = 1.9f;

    private float until;
    private Vector3 direction;
    private float nextDust;
    private readonly HashSet<Player> hitAlready = new();
    private readonly HashSet<PhotonView> smashed = new();

    /// <summary>True while the charge is running (nothing cancels it).</summary>
    public bool Charging => IsLocal && Time.time < until;

    protected override void Activate()
    {
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : transform;
        direction = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
        until = Time.time + chargeSeconds;
        hitAlready.Clear();
        smashed.Clear();
        Owner.photonView.RPC(nameof(RPC_DozerFx), RpcTarget.All, Owner.transform.position);
    }

    // Can't be cancelled: Cancel() (pause, other abilities, being caught) leaves the charge running
    public override void Cancel() { }

    private void FixedUpdate()
    {
        if (!Charging) return;
        if (Owner.IsCaught) { until = 0f; return; } // Down: the charge ends with you
        Owner.NotePushed(); // An outside force: movement mustn't brake or steer it
        Vector3 v = Body.linearVelocity;
        Body.linearVelocity = direction * speed + Vector3.up * Mathf.Min(v.y, 0f);

        Vector3 at = Owner.transform.position;
        foreach (var p in Player.All)
        {
            if (!TacticalNet.IsTarget(Owner, p, enemiesOnly: true) || hitAlready.Contains(p)) continue;
            if (Vector3.Distance(p.transform.position, at) > reach) continue;
            hitAlready.Add(p);
            Vector3 side = Vector3.ProjectOnPlane(p.transform.position - at, Vector3.up).normalized;
            AbilityDamage.Hit(p, damage, Id, direction, trap: false);
            TacticalNet.Send(p, TacticalNet.Status.Stun, stunSeconds);
            TacticalNet.Send(p, TacticalNet.Status.Knock, 0f, (direction + side).normalized * 12f + Vector3.up * 6f);
            Sfx.Here(SfxKind.Hit, 0.8f, 0.7f);
            if (PlayerCam.Local != null) PlayerCam.Local.AddShake(0.25f);
        }

        // Utility in the way: smashed
        var found = new HashSet<PhotonView>();
        TacticalNet.FindUtility(at + Vector3.up, reach + 0.6f, found);
        foreach (var view in found)
            if (smashed.Add(view)) TacticalNet.DestroyUtility(view);

        if (Time.time >= nextDust)
        {
            nextDust = Time.time + 0.12f;
            Owner.photonView.RPC(nameof(RPC_DozerDust), RpcTarget.All, at);
        }
    }

    [PunRPC]
    private void RPC_DozerFx(Vector3 at)
    {
        AoeDebug.Sphere(transform.position, reach, new Color(1f, 0.35f, 0.25f), "Bulldozer hit (players)", chargeSeconds, transform);
        AoeDebug.Sphere(transform.position + Vector3.up, reach + 0.6f, new Color(1f, 0.6f, 0.15f), "Bulldozer smash (utility)", chargeSeconds, transform);
        Sfx.At(SfxKind.Whoosh, at, 1f, 40f, 0.6f);
        Sfx.At(SfxKind.Land, at, 0.8f, 40f, 0.6f);
        Vfx.Burst(at, new Color(0.9f, 0.95f, 1f), 24, 5f, 0.35f, 0.5f);
    }

    [PunRPC]
    private void RPC_DozerDust(Vector3 at) =>
        Vfx.Burst(at + Vector3.up * 0.2f, new Color(0.85f, 0.85f, 0.88f), 5, 3f, 0.3f, 0.45f, direction: Vector3.up, spread: 0.7f);
}
