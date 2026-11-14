using Photon.Pun;
using UnityEngine;

/// <summary>
/// The recon drone (Resources/Object/Drone, spawned by the Drone ability): flies straight along the launch direction
/// at the launch speed, stopping to hover where something blocks it, for <see cref="lifetime"/> s. Every screen flies
/// it the same way from the spawn data (no sync needed). Every <see cref="scanEvery"/> s it scans: enemies of its owner
/// within <see cref="scanRadius"/> and in its sight light up for the owner and their team; the scanned get a map
/// pulse. Breakable (<see cref="Shootable"/> on the prefab).
/// </summary>
public class DroneObject : MonoBehaviourPun, IPunInstantiateMagicCallback
{
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private float scanEvery = 0.5f;
    [SerializeField] private float scanRadius = 15f;
    [SerializeField] private float revealSeconds = 2f;

    private Vector3 velocity;
    private float spawnedAt, nextScan;
    private bool blocked;
    private Transform[] rotors;

    public void OnPhotonInstantiate(PhotonMessageInfo info)
    {
        velocity = SpawnData.Impulse(photonView);
        scanRadius *= Mathf.Clamp(SpawnData.Power(photonView), 0.25f, 4f);   // S&D upgrades (the Drone ability's UpgradePower)
        spawnedAt = Time.time;
        nextScan = Time.time + 0.3f;
        if (photonView.IsMine) Invoke(nameof(Remove), lifetime);
        var list = new System.Collections.Generic.List<Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>()) if (t.name.StartsWith("Rotor")) list.Add(t);
        rotors = list.ToArray();
    }

    private GroundRing reach;
    private AoeDebug aoe;

    // The reach on the floor: where the scan sphere meets the ground, each point on whatever is under it (a platform
    // under the drone, the floor beyond its edges)
    private void UpdateReach()
    {
        if (reach == null) reach = GroundRing.Held(transform.position, scanRadius, new Color(0.35f, 1f, 0.6f));
        reach.Place(transform.position, scanRadius);
    }

    private void OnDestroy()
    {
        if (reach != null) Destroy(reach.gameObject);
    }

    private void Remove()
    {
        if (this != null && photonView.IsMine) PhotonNetwork.Destroy(gameObject);
    }

    private void Update()
    {
        if (rotors != null) foreach (var r in rotors) if (r != null) r.Rotate(0f, 1440f * Time.deltaTime, 0f, Space.Self);

        // Straight ahead; an angled surface in the way turns it to slide along it at the same speed (Justin, Nov 14:
        // it stuck on slopes and slanted walls); head-on (or a player): it stops and hovers, bobbing
        if (!blocked)
        {
            Vector3 step = velocity * Time.deltaTime;
            if (Physics.SphereCast(transform.position, 0.3f, velocity.normalized, out RaycastHit hit, step.magnitude + 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Vector3 slide = Vector3.ProjectOnPlane(velocity, hit.normal);
                if (hit.collider.GetComponentInParent<Player>() == null && slide.magnitude > velocity.magnitude * 0.3f)
                {
                    velocity = slide.normalized * velocity.magnitude;
                    transform.position += hit.normal * Mathf.Max(0f, 0.3f - hit.distance) + velocity * Time.deltaTime;
                }
                else blocked = true;
            }
            else transform.position += step;
        }
        transform.position += Vector3.up * Mathf.Sin((Time.time - spawnedAt) * 5f) * 0.15f * Time.deltaTime;
        UpdateReach();
        if (AoeDebug.On && aoe == null) aoe = AoeDebug.Sphere(transform.position, scanRadius, new Color(0.4f, 1f, 0.55f), "Drone scan (line of sight)", 0f, transform);

        if (Time.time < nextScan) return;
        nextScan = Time.time + scanEvery;
        Scan();
    }

    private void Scan()
    {
        Vector3 at = transform.position;
        Vfx.Ring(at, new Color(0.35f, 1f, 0.6f), 20, scanRadius * 1.2f, 0.12f, 0.4f);
        Sfx.At(SfxKind.Tick, at, 0.5f, 25f, 1.8f);
        var local = Player.Local;
        var owner = Player.Find(photonView.OwnerActorNr);
        bool viewer = local != null && (local.ActorNumber == photonView.OwnerActorNr || local.IsTeammateOf(photonView.OwnerActorNr));
        foreach (var p in Player.All)
        {
            if (!TacticalNet.IsTarget(owner, p, enemiesOnly: true)) continue;
            if (Vector3.Distance(AbilityDamage.Chest(p), at) > scanRadius || !AbilityDamage.InSight(at, p, transform)) continue;
            if (viewer) HunterReveal.Reveal(p.ActorNumber, revealSeconds);
            else if (p == local) Minimap.Pulse(new Color(0.35f, 1f, 0.6f));
        }
    }
}
