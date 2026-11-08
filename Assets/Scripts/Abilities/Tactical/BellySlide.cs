using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// A fast belly slide along the ground: anyone you hit on the way takes 20 damage and is knocked aside (once each).
/// Hits teammates too. Health modes only.
/// </summary>
[AbilityInfo("Belly-slide forward fast: anyone you hit takes 20 damage and is knocked aside.", DisplayName = "Belly-Slide Bash", HealthModesOnly = true)]
public class BellySlide : QuickAbility
{
    [TunableParam("Speed", 5f, 60f)]
    [SerializeField] private float speed = 26f;
    [TunableParam("Slide Time", 0.1f, 2f)]
    [SerializeField] private float slideSeconds = 0.6f;
    [TunableParam("Damage", 0f, 100f)]
    [SerializeField] private float damage = 20f;

    private float until;
    private Vector3 direction;
    private readonly HashSet<Player> hitAlready = new();

    protected override void Activate()
    {
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : transform;
        direction = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
        until = Time.time + slideSeconds;
        hitAlready.Clear();
        Owner.photonView.RPC(nameof(RPC_SlideFx), RpcTarget.All, Owner.transform.position);
    }

    private void FixedUpdate()
    {
        if (!IsLocal || Time.time >= until) return;
        Owner.NotePushed(); // An outside force: movement mustn't brake it
        Vector3 v = Body.linearVelocity;
        Body.linearVelocity = direction * speed + Vector3.up * Mathf.Min(v.y, 0f);

        foreach (var p in Player.All)
        {
            if (!TacticalNet.IsTarget(Owner, p, enemiesOnly: false) || hitAlready.Contains(p)) continue;
            if (Vector3.Distance(p.transform.position, Owner.transform.position) > 1.9f) continue;
            hitAlready.Add(p);
            Vector3 side = Vector3.ProjectOnPlane(p.transform.position - Owner.transform.position, Vector3.up).normalized;
            AbilityDamage.Hit(p, damage, Id, direction, trap: false);
            TacticalNet.Send(p, TacticalNet.Status.Knock, 0f, (direction + side).normalized * 14f + Vector3.up * 6f);
            Sfx.Here(SfxKind.Hit, 0.8f, 0.8f);
        }
    }

    [PunRPC]
    private void RPC_SlideFx(Vector3 at)
    {
        Sfx.At(SfxKind.Whoosh, at, 1f, 35f, 0.8f);
        Vfx.Burst(at, new Color(0.9f, 0.95f, 1f), 18, 4f, 0.3f, 0.5f);
    }
}
