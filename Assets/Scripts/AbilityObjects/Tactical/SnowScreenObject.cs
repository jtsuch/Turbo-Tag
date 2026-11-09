using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Snow Screen (Resources/Object/SnowScreen): a little snow cloud sent forward along the view (like the Drone), drifting
/// straight through walls and floors for <see cref="travelSeconds"/>. As it goes it leaves a long wall of packed snow
/// behind it, as tall as an Ice Wall: each step raises a segment on the first surface under the cloud (so it stands on
/// top of each level it passes over: on a roof above a room, not in the room under it). The wall blocks the view from
/// both sides and from inside; anyone can wade through it, but enemies of the placer are slowed while inside
/// (<see cref="SnowedEffect"/>). The segments stay where they were raised (a separate trail object, not children of
/// the moving cloud). Everything is worked out on every screen from the spawn data and the level (no sync); it all
/// melts after <see cref="lifetime"/>.
/// </summary>
public class SnowScreenObject : TacticalTrapObject
{
    [TunableParam("Speed", 2f, 30f)]
    [SerializeField] private float speed = 9f;
    [TunableParam("Travel Time", 0.5f, 10f)]
    [SerializeField] private float travelSeconds = 3.5f;
    [SerializeField] private float pileEvery = 1.1f;    // Metres between wall segments along the path
    [TunableParam("Wall Height", 1f, 8f)]
    [SerializeField] private float wallHeight = 3.64f;  // As tall as an Ice Wall
    [SerializeField] private float wallThickness = 1.3f;

    private struct Segment { public Vector3 ground; public Quaternion facing; public Transform wall; public float bornAt; }

    private Vector3 start, velocity;
    private float spawnedAt, travelled;
    private Transform cloud, trail;
    private Material snowMat;
    private readonly List<Segment> segments = new();
    private float nextFlake;

    protected override void Start()
    {
        base.Start();
        start = transform.position;
        velocity = SpawnData.Impulse(photonView);
        spawnedAt = Time.time;
        cloud = transform.Find("Cloud");
        snowMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        snowMat.SetColor("_BaseColor", new Color(0.95f, 0.97f, 1f));
        snowMat.SetFloat("_Smoothness", 0.15f);
        snowMat.SetFloat("_Cull", 0f); // Both faces: no seeing out from inside the wall either
        snowMat.doubleSidedGI = true;
        trail = new GameObject("Snow Screen Trail").transform; // Stays put while the cloud moves on
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (trail != null) Destroy(trail.gameObject);
    }

    private void Update()
    {
        float age = Time.time - spawnedAt;
        bool flying = age < travelSeconds;
        if (flying)
        {
            // Straight on, through anything; a pile under each step of the way
            Vector3 at = start + velocity * age;
            transform.position = at;
            float distance = velocity.magnitude * age;
            while (travelled + pileEvery <= distance)
            {
                travelled += pileEvery;
                RaiseSegment(start + velocity.normalized * travelled);
            }
            if (Time.time >= nextFlake)
            {
                nextFlake = Time.time + 0.06f;
                Vfx.Burst(at + Random.insideUnitSphere * 0.5f, new Color(0.95f, 0.97f, 1f), 3, 1f, 0.15f, 1.2f, gravity: true);
            }
        }
        else if (cloud != null && cloud.gameObject.activeSelf) cloud.gameObject.SetActive(false); // Snowed out

        // Each segment pushes up out of the ground; in the last moments the whole wall sinks away (melts)
        float melt = lifetime > 0f ? Mathf.Clamp01((lifetime - age) / 1.5f) : 1f;
        foreach (var s in segments)
        {
            if (s.wall == null) continue;
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - s.bornAt) / 0.3f)) * melt;
            float h = Mathf.Max(0.01f, wallHeight * rise);
            s.wall.localScale = new Vector3(s.wall.localScale.x, h, s.wall.localScale.z);
            s.wall.position = s.ground + Vector3.up * (h * 0.5f - 0.15f);
        }

        var enemy = LocalEnemy;
        bool inSnow = false;
        if (enemy != null && melt > 0.3f)
        {
            Vector3 feet = enemy.transform.position;
            foreach (var s in segments)
            {
                // In the segment's own frame: along the path, across it, and up from its foot
                Vector3 local = Quaternion.Inverse(s.facing) * (feet - s.ground);
                if (Mathf.Abs(local.z) <= pileEvery * 0.6f && Mathf.Abs(local.x) <= wallThickness * 0.5f + 0.5f
                    && local.y > -1f && local.y < wallHeight) { inSnow = true; break; }
            }
        }
        Hold(typeof(SnowedEffect), Player.Local, inSnow);
    }

    // The first surface straight below this point of the path (players and triggers don't count)
    private void RaiseSegment(Vector3 above)
    {
        float best = float.MaxValue;
        RaycastHit ground = default;
        foreach (var hit in Physics.RaycastAll(above, Vector3.down, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance >= best || hit.collider.GetComponentInParent<Player>() != null || hit.collider.transform.IsChildOf(transform)) continue;
            best = hit.distance;
            ground = hit;
        }
        if (best == float.MaxValue || ground.normal.y < 0.5f) return; // Nothing below, or only a steep face

        // Upright, running along the path (a little overlap and a slight wobble so it reads as packed snow)
        Vector3 along = Vector3.ProjectOnPlane(velocity, Vector3.up);
        var facing = Quaternion.LookRotation(along.sqrMagnitude > 0.01f ? along.normalized : Vector3.forward, Vector3.up);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(wall.GetComponent<Collider>()); // Blocks the view, not people
        wall.name = "Snow Wall";
        wall.transform.SetParent(trail, false);
        wall.transform.SetPositionAndRotation(ground.point, facing * Quaternion.Euler(0f, Random.Range(-4f, 4f), 0f));
        wall.transform.localScale = new Vector3(wallThickness * Random.Range(0.92f, 1.08f), 0.01f, pileEvery * 1.35f);
        var r = wall.GetComponent<Renderer>();
        r.sharedMaterial = snowMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        segments.Add(new Segment { ground = ground.point, facing = facing, wall = wall.transform, bornAt = Time.time });
        Vfx.Burst(ground.point + Vector3.up * 0.5f, new Color(0.95f, 0.97f, 1f), 6, 3f, 0.25f, 0.6f, direction: Vector3.up, spread: 0.7f);
    }
}
