using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Fire bottle (Resources/Object/Molly): bursts only on flat ground (Justin, Nov 14): walls, slopes and players bounce it
/// on until it lands on a floor (lying still on one after 3 s also bursts it; nothing after 8 s: it's gone), into a pool of flames
/// <see cref="radius"/> across for <see cref="burnSeconds"/>. The thrower's client burns everyone standing in it
/// (<see cref="damagePerSecond"/>, ticking 4 times a second; the thrower and teammates too).
/// </summary>
public class MollyObject : NetworkedProjectile
{
    [TunableParam("Radius", 1f, 12f)]
    [SerializeField] private float radius = 4.2f;
    [TunableParam("Burn Time", 1f, 20f)]
    [SerializeField] private float burnSeconds = 5f;
    [TunableParam("Damage / s", 0f, 200f)]
    [SerializeField] private float damagePerSecond = 30f;

    private bool lit;
    private float lastBounce = -1f;
    private float litAt = -1f;
    private Vector3 pool;
    private Light glow;
    private float nextFlame;

    protected override void OnLaunched()
    {
        launchedAt = Time.time;
        if (IsAuthority) StartCoroutine(Timeout());
    }

    // Settled on a floor without a clean hit (rolled to a stop): burst there; never found one: gone
    private IEnumerator Timeout()
    {
        yield return new WaitForSeconds(3f);
        while (!lit && Time.time - launchedAt < 8f)
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit floor, 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && floor.normal.y >= FlatFloor && floor.collider.GetComponentInParent<Player>() == null)
            {
                Ignite(floor.point);
                yield break;
            }
            yield return new WaitForSeconds(0.2f);
        }
        if (!lit) DestroyNetworked();
    }

    private const float FlatFloor = 0.8f;   // Surface normal's up part: about 37° of slope at most
    private float launchedAt;

    private void OnCollisionStay(Collision collision) => OnCollisionEnter(collision);

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsAuthority || lit || collision.contactCount == 0 || !RealContact(collision)) return;
        var contact = collision.GetContact(0);
        // Walls, slopes and players: it bounces off (a lively rebound, half the speed) and flies on to the floor
        if (contact.normal.y < FlatFloor || collision.collider.GetComponentInParent<Player>() != null)
        {
            if (collision.relativeVelocity.sqrMagnitude > 4f && Time.time - lastBounce > 0.1f)
            {
                lastBounce = Time.time;
                Body.linearVelocity = Vector3.Reflect(-collision.relativeVelocity, contact.normal) * 0.5f;
            }
            return;
        }
        Ignite(contact.point);
    }

    private void Ignite(Vector3 at)
    {
        if (lit) return;
        lit = true;
        // On the floor under it
        if (Physics.Raycast(at + Vector3.up, Vector3.down, out RaycastHit hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) at = hit.point;
        photonView.RPC(nameof(RPC_Ignite), RpcTarget.All, at);
        StartCoroutine(Burn(at));
    }

    private IEnumerator Burn(Vector3 at)
    {
        float end = Time.time + burnSeconds;
        while (Time.time < end)
        {
            foreach (var p in Player.All)
            {
                if (!TacticalNet.IsTarget(null, p, enemiesOnly: false)) continue;
                Vector3 off = p.transform.position - at;
                if (Mathf.Abs(off.y) > 2f || new Vector2(off.x, off.z).magnitude > radius) continue;
                AbilityDamage.Hit(p, damagePerSecond * 0.25f * Power, "Molly", Vector3.up, trap: false);
            }
            yield return new WaitForSeconds(0.25f);
        }
        yield return new WaitForSeconds(0.5f);
        DestroyNetworked();
    }

    [PunRPC]
    private void RPC_Ignite(Vector3 at)
    {
        lit = true;
        litAt = Time.time;
        pool = at;
        AoeDebug.Cylinder(at, radius, 2f, 2f, new Color(1f, 0.35f, 0.25f), "Molly fire", burnSeconds);
        HideAndFreeze();
        Sfx.At(SfxKind.Explosion, at, 0.6f, 40f, 1.6f);
        Vfx.Ring(at + Vector3.up * 0.1f, new Color(1f, 0.55f, 0.15f), 40, radius * 2f, 0.4f, 0.5f);

        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.name = "Fire Pool";
        disc.transform.SetParent(transform, false);
        disc.transform.SetPositionAndRotation(at + Vector3.up * 0.04f, Quaternion.identity);
        disc.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
        disc.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.4f, 0.08f, 0.45f) };

        glow = new GameObject("Fire Light").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.transform.position = at + Vector3.up;
        glow.color = new Color(1f, 0.5f, 0.15f);
        glow.range = radius * 3f;
        glow.shadows = LightShadows.None;
    }

    // Flames licking up across the pool while it burns
    private void Update()
    {
        if (litAt < 0f) return;
        float age = Time.time - litAt;
        if (glow != null) glow.intensity = age < burnSeconds ? 2.5f + Mathf.Sin(Time.time * 17f) * 0.6f : 0f;
        if (age > burnSeconds || Time.time < nextFlame) return;
        nextFlame = Time.time + 0.12f;
        Vector2 r = Random.insideUnitCircle * radius;
        Vfx.Burst(pool + new Vector3(r.x, 0.1f, r.y), new Color(1f, Random.Range(0.35f, 0.7f), 0.1f), 4, 3f, 0.35f, 0.6f, direction: Vector3.up, spread: 0.3f);
    }
}
