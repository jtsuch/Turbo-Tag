using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Frag grenade (Resources/Object/Frag): bounces, goes off after <see cref="fuse"/> s. The thrower's client deals the
/// damage (up to <see cref="maxDamage"/> at the middle, nothing at <see cref="radius"/>, only with a clear line from the
/// blast; teammates and the thrower too); every screen gets the blast and pushes its own bodies. From the throw on, a
/// red circle on the ground under it shows the damage radius on every screen.
/// </summary>
public class FragObject : NetworkedProjectile
{
    [TunableParam("Fuse", 0.2f, 10f)]
    [SerializeField] private float fuse = 3.2f;
    [TunableParam("Radius", 1f, 30f)]
    [SerializeField] private float radius = 10.8f;
    [TunableParam("Max Damage", 0f, 400f)]
    [SerializeField] private float maxDamage = 170f;

    private LineRenderer ring;
    private const int RingPoints = 72;

    protected override void OnLaunched()
    {
        // The danger circle (world space, so it stays flat on the ground however the grenade tumbles)
        ring = new GameObject("Frag Radius").AddComponent<LineRenderer>();
        ring.transform.SetParent(transform, false);
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = RingPoints;
        ring.widthMultiplier = 0.12f;
        ring.material = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.15f, 0.15f, 0.9f) };
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (IsAuthority) StartCoroutine(Fuse());
    }

    private void LateUpdate()
    {
        if (ring == null) return;
        // On the real floor: where the blast sphere meets whatever is under each point (down off ledges, not hanging
        // in the air at the grenade's height)
        GroundRing.Drape(transform.position, radius, ringPoints, transform);
        for (int i = 0; i < RingPoints; i++) ringPoints[i].y += 0.06f;
        ring.SetPositions(ringPoints);
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 10f);
        ring.material.color = new Color(1f, 0.15f, 0.15f, 0.9f * pulse);
    }

    private readonly Vector3[] ringPoints = new Vector3[RingPoints];

    private IEnumerator Fuse()
    {
        yield return new WaitForSeconds(fuse);
        Vector3 at = transform.position;
        foreach (var p in Player.All)
        {
            if (!TacticalNet.IsTarget(null, p, enemiesOnly: false)) continue;
            float d = Vector3.Distance(AbilityDamage.Chest(p), at);
            if (d > radius || !AbilityDamage.InSight(at + Vector3.up * 0.2f, p, transform)) continue;
            float t = 1f - d / radius;
            AbilityDamage.Hit(p, maxDamage * Mathf.Lerp(0.25f, 1f, t * t) * Power, "Frag", AbilityDamage.Chest(p) - at, trap: false);
        }
        HideAndFreeze();
        photonView.RPC(nameof(RPC_Blast), RpcTarget.All, at);
        yield return new WaitForSeconds(0.3f);
        DestroyNetworked();
    }

    [PunRPC]
    private void RPC_Blast(Vector3 at)
    {
        AoeDebug.Sphere(at, radius, new Color(1f, 0.35f, 0.25f), "Frag damage (chest, line of sight)");
        HideAndFreeze();
        if (ring != null) ring.enabled = false;
        Fx.Explosion(at, radius * 0.5f);
        WorldFx.Explode(at, radius, 18f, default, 1f, sourceActor: photonView.OwnerActorNr, cause: "Frag");
    }
}
