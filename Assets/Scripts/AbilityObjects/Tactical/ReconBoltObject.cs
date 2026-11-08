using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Recon Bolt (Resources/Object/ReconBolt): fired hard, sticks to the first surface it hits, then, after a beat, scans
/// twice: every enemy in its line of sight within <see cref="range"/> lights up through walls for the thrower and their
/// team (<see cref="HunterReveal"/>); the scanned player's map border pulses, and anyone near where it lands gets a
/// warning pulse. It blinks and chirps while it's up, so enemies can find it and shoot it down (<see cref="Shootable"/>).
/// Used by the Recon Bolt ability and by the Intel hunter's Recon Bolt tool (HunterKit), so it works in Tag too: the
/// hunters are a team there, so hunters light up hiders.
/// Each screen decides its own reveals from where the bolt stuck (no per-target messages).
/// </summary>
public class ReconBoltObject : NetworkedProjectile
{
    [TunableParam("Range", 5f, 100f)]
    [SerializeField] private float range = 40f;
    [TunableParam("Reveal Time", 0.5f, 10f)]
    [SerializeField] private float revealSeconds = 2f;
    [SerializeField] private int scans = 2;

    private static readonly Color Blue = new(0.35f, 0.7f, 1f);
    private bool stuck;
    private Light glow;

    public override float GravityScale => 0.6f;

    protected override void OnLaunched()
    {
        glow = new GameObject("Bolt Light").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.color = Blue;
        glow.range = 5f;
        glow.intensity = 2f;
        glow.shadows = LightShadows.None;
        if (IsAuthority) StartCoroutine(Lifetime());
    }

    private IEnumerator Lifetime()
    {
        yield return new WaitForSeconds(9f);
        DestroyNetworked();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsAuthority || stuck || collision.contactCount == 0) return;
        if (collision.collider.GetComponentInParent<Player>() != null) return;
        var contact = collision.GetContact(0);
        photonView.RPC(nameof(RPC_Stick), RpcTarget.All, contact.point + contact.normal * 0.05f, contact.normal);
    }

    [PunRPC]
    private void RPC_Stick(Vector3 at, Vector3 normal)
    {
        if (stuck) return;
        stuck = true;
        Body.isKinematic = true;
        transform.SetPositionAndRotation(at, Quaternion.LookRotation(-normal));
        Sfx.At(SfxKind.Place, at, 0.8f, 30f, 1.4f);

        // Anyone it's aimed at gets a moment's warning (a blue map pulse) before it scans
        var local = Player.Local;
        if (local != null && !IsViewer(local) && Vector3.Distance(local.transform.position, at) < 20f)
            Minimap.Pulse(Blue);
        StartCoroutine(Scan(at));
    }

    private IEnumerator Scan(Vector3 at)
    {
        yield return new WaitForSeconds(0.8f);
        for (int i = 0; i < Mathf.Max(1, scans); i++)
        {
            Vfx.Ring(at, Blue, 40, 12f, 0.35f, 0.7f);
            Sfx.At(SfxKind.Shimmer, at, 1f, 50f, 0.8f + i * 0.15f);
            if (glow != null) { glow.range = 16f; glow.intensity = 7f; }

            var local = Player.Local;
            var thrower = Player.Find(photonView.OwnerActorNr);
            foreach (var p in Player.All)
            {
                if (!TacticalNet.IsTarget(thrower, p, enemiesOnly: true)) continue;
                if (Vector3.Distance(AbilityDamage.Chest(p), at) > range * Mathf.Sqrt(Power)) continue;
                if (!AbilityDamage.InSight(at, p, transform)) continue;
                if (local != null && IsViewer(local)) HunterReveal.Reveal(p.ActorNumber, revealSeconds);
                else if (p == local) Minimap.Pulse(new Color(1f, 0.35f, 0.3f)); // You've been scanned
            }
            yield return new WaitForSeconds(1.2f);
        }
    }

    // The thrower and their team see what it finds
    private bool IsViewer(Player p) => p.ActorNumber == photonView.OwnerActorNr || p.IsTeammateOf(photonView.OwnerActorNr);

    // Blinks: slow in flight, quick once stuck
    private void Update()
    {
        if (glow == null) return;
        float rate = stuck ? 12f : 6f;
        float target = Mathf.Sin(Time.time * rate) > 0f ? 2.5f : 0.4f;
        glow.intensity = Mathf.MoveTowards(glow.intensity, target, 40f * Time.deltaTime);
        glow.range = Mathf.MoveTowards(glow.range, 5f, 30f * Time.deltaTime);
    }
}
