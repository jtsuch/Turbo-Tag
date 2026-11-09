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
        spawnedAt = Time.time;
        nextScan = Time.time + 0.3f;
        if (photonView.IsMine) Invoke(nameof(Remove), lifetime);
        var list = new System.Collections.Generic.List<Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>()) if (t.name.StartsWith("Rotor")) list.Add(t);
        rotors = list.ToArray();
    }

    private GroundRing reach;

    // The reach on the floor below: where the scan sphere meets the ground
    private void UpdateReach()
    {
        var hits = Physics.RaycastAll(transform.position, Vector3.down, scanRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 floor = Vector3.zero;
        foreach (var h in hits)
            if (h.distance < best && !h.transform.IsChildOf(transform) && h.collider.GetComponentInParent<Player>() == null) { best = h.distance; floor = h.point; }
        if (best == float.MaxValue) { if (reach != null) reach.gameObject.SetActive(false); return; }
        float r = Mathf.Sqrt(Mathf.Max(0f, scanRadius * scanRadius - best * best));
        if (reach == null) reach = GroundRing.Held(floor, r, new Color(0.35f, 1f, 0.6f));
        reach.gameObject.SetActive(true);
        reach.Place(floor, r);
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

        // Straight ahead until something is in the way; then hover, bobbing
        if (!blocked)
        {
            Vector3 step = velocity * Time.deltaTime;
            if (Physics.SphereCast(transform.position, 0.3f, velocity.normalized, out _, step.magnitude + 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                blocked = true;
            else transform.position += step;
        }
        transform.position += Vector3.up * Mathf.Sin((Time.time - spawnedAt) * 5f) * 0.15f * Time.deltaTime;
        UpdateReach();

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
