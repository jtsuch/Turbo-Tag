using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

/// <summary>
/// Placed object for the GravityWell TrapAbility: a gravity spike for floors, walls or ceilings. It reaches out along
/// its facing (the surface normal) in a column (<see cref="maxRange"/> long, <see cref="cylinderRadius"/> wide), shown
/// by a purple light along it: strong within 5 m, faint by 10 m, nothing beyond (Justin, Nov 14).
/// The first player (not the placer's team) or loose physics object to enter the column with a clear line to it springs
/// it; it then stays live for <see cref="activeSeconds"/>, reeling in everything that is in the column meanwhile
/// (players and objects, several at once), each drawn smoothly right up to the well (<see cref="pullSeconds"/>), and
/// then breaks.
///
/// Network behaviour: each client watches its own player and the physics objects it has authority over. Whoever
/// springs it tells everyone (RPC_Sprung); every client then reels in its own player and objects while it's live; the
/// placer's client removes it afterwards.
///
/// Unity setup: Resources/Object/GravityWell with Rigidbody (kinematic, no gravity), a trigger
/// collider and a PhotonView. Optionally assign humSFX.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GravityWellObject : MonoBehaviourPun
{
    [Header("Reach")]
    [Tooltip("How far the pull column reaches out from the well, along its facing (m).")]
    [TunableParam("Reach", 2f, 80f)]
    [SerializeField] private float maxRange       = 20f;
    [Tooltip("Width of the column, either side of its middle line (m).")]
    [TunableParam("Radius", 0.5f, 15f)]
    [SerializeField] private float cylinderRadius = 1.5f;   // Nov 14: half (the glow too)

    [Header("Pull")]
    [Tooltip("Time to reel something all the way in (s).")]
    [TunableParam("Pull Time", 0.1f, 2f)]
    [SerializeField] private float pullSeconds = 0.45f;
    [Tooltip("Once sprung, how long it keeps catching whatever is in the column (s).")]
    [TunableParam("Active Time", 0.1f, 5f)]
    [SerializeField] private float activeSeconds = 1f;

    private const float Behind = 1.5f; // The column starts a little behind the well (feet on the same floor count)
    private static readonly Color Purple = new(0.6f, 0.25f, 1f);

    [Header("Audio")]
    [SerializeField] private AudioClip humSFX;
    [SerializeField] private float humVolume = 0.5f;

    private bool sprung;
    private float liveUntil;
    private readonly HashSet<Rigidbody> reeled = new();
    private Light beamLight;
    private Material beamMaterial;

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake() => ObjectTunables.Apply(photonView); // Cheat values from the placer

    private void Start()
    {
        maxRange *= Mathf.Sqrt(SpawnData.Power(photonView));

        if (humSFX != null)
        {
            AudioSource hum = gameObject.AddComponent<AudioSource>();
            WorldFx.Configure3D(hum, 30f, humVolume);
            hum.clip = humSFX;
            hum.loop = true;
            hum.Play();
        }
        BuildBeam();
    }

    private void OnDestroy()
    {
        if (beamMaterial != null) Destroy(beamMaterial);
    }

    private AoeDebug aoe;

    private void FixedUpdate()
    {
        if (sprung && Time.time > liveUntil) { if (aoe != null) Destroy(aoe.gameObject); return; }
        if (AoeDebug.On && aoe == null) aoe = AoeDebug.Column(transform, maxRange, Behind, cylinderRadius, Purple, "Gravity Well (chest, line of sight)");

        // This client's own player (never the placer's team) and the physics objects it has authority over
        var local = Player.Local;
        if (local != null && local.Body != null && local.ActorNumber != photonView.OwnerActorNr && !local.IsCaught
            && local.AffectedByTrap(photonView.OwnerActorNr) && !reeled.Contains(local.Body))
        {
            Vector3 chest = AbilityDamage.Chest(local);
            if (InColumn(chest) && InSight(chest, local.transform))
            {
                Spring();
                reeled.Add(local.Body);
                StartCoroutine(ReelIn(local.Body, local, local.Height));
            }
        }
        foreach (var rb in LooseBodiesInColumn())
        {
            Spring();
            reeled.Add(rb);
            StartCoroutine(ReelIn(rb, null, 1f));
        }
    }

    // Loose physics objects (boxes, ladders, thrown things) this client moves, in the column with a clear line
    private IEnumerable<Rigidbody> LooseBodiesInColumn()
    {
        var found = new List<Rigidbody>();
        Vector3 mid = transform.position + transform.forward * (maxRange - Behind) * 0.5f;
        float half = (maxRange + Behind) * 0.5f;
        var cols = Physics.OverlapCapsule(mid - transform.forward * half, mid + transform.forward * half, cylinderRadius,
                                          Physics.AllLayers, QueryTriggerInteraction.Ignore);
        foreach (var c in cols)
        {
            var rb = c.attachedRigidbody;
            if (rb == null || rb.isKinematic || reeled.Contains(rb) || found.Contains(rb)) continue;
            if (rb.GetComponent<Player>() != null || rb.transform.IsChildOf(transform)) continue;
            if (!WorldFx.HasAuthority(rb)) continue;
            if (!InColumn(rb.worldCenterOfMass) || !InSight(rb.worldCenterOfMass, rb.transform)) continue;
            found.Add(rb);
        }
        return found;
    }

    // ─── The pull ────────────────────────────────────────────────────────────

    // First catch: tell everyone (each then reels its own things in while it's live)
    private void Spring()
    {
        if (sprung) return;
        sprung = true;
        liveUntil = Time.time + activeSeconds;
        photonView.RPC(nameof(RPC_Sprung), RpcTarget.Others);
        SprungFx();
    }

    // Drawn in on a smooth curve (slow off the mark, fast at the end) to just in front of the well, then let go
    private IEnumerator ReelIn(Rigidbody rb, Player victim, float height)
    {
        Vector3 from = rb.position;
        float radius = victim != null && victim.TryGetComponent(out CapsuleCollider capsule) ? capsule.radius : 0.5f;
        // In front of the well; on a wall or ceiling the feet end up level with it, on a floor right on top of it
        Vector3 to = transform.position + transform.forward * (radius + 0.25f);
        if (victim != null && transform.forward.y < 0.5f) to += Vector3.down * Mathf.Min(1f, height * 0.5f);

        for (float t = 0f; t < pullSeconds; t += Time.fixedDeltaTime)
        {
            if (rb == null || (victim != null && victim.IsCaught)) yield break;
            float k = t / pullSeconds;
            k = k * k * (3f - 2f * k); // Smooth in and out
            rb.linearVelocity = Vector3.zero;
            rb.MovePosition(Vector3.Lerp(from, to, k));
            if (victim != null) victim.NotePushed(photonView.OwnerActorNr, "GravityWell");
            yield return new WaitForFixedUpdate();
        }
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.MovePosition(to); }
    }

    [PunRPC]
    private void RPC_Sprung()
    {
        if (sprung) return;
        sprung = true;
        liveUntil = Time.time + activeSeconds;
        SprungFx();
    }

    private void SprungFx()
    {
        Sfx.At(SfxKind.ZapDown, transform.position, 1f, 50f, 0.7f);
        Vfx.Burst(transform.position, Purple, 24, 6f, 0.25f, 0.5f, direction: transform.forward, spread: 0.6f);
        Vfx.Ring(transform.position, Purple, 36, 4f, 0.3f, 0.4f);
        if (beamLight != null) beamLight.intensity *= 2.5f;
        if (photonView.IsMine) Invoke(nameof(Break), activeSeconds + pullSeconds + 0.25f); // Done once the last pull ends
    }

    private void Break()
    {
        if (this == null || !photonView.IsMine) return;
        Vfx.Burst(transform.position, Purple, 16, 4f, 0.2f, 0.4f);
        PhotonNetwork.Destroy(gameObject);
    }

    // ─── The reach, as purple light ───────────────────────────────────────────

    // A spot light along the facing (lights the floor and walls it reaches) and a see-through glow down the column,
    // bright at the well, faint by 10 m, gone by 12 m: noticeable up close, a hint further out, nothing beyond
    private void BuildBeam()
    {
        var lightGo = new GameObject("Well Light");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localRotation = Quaternion.identity;
        beamLight = lightGo.AddComponent<Light>();
        beamLight.type = LightType.Spot;
        beamLight.color = Purple;
        beamLight.range = 12f;
        beamLight.spotAngle = 2f * Mathf.Atan(cylinderRadius / 8f) * Mathf.Rad2Deg;
        beamLight.intensity = 6f;
        beamLight.shadows = LightShadows.None;

        var glow = new GameObject("Well Glow");
        glow.transform.SetParent(transform, false);
        glow.transform.localRotation = Quaternion.identity;
        Vector3 s = transform.lossyScale;
        glow.transform.localScale = new Vector3(1f / Mathf.Max(1e-4f, s.x), 1f / Mathf.Max(1e-4f, s.y), 1f / Mathf.Max(1e-4f, s.z));
        glow.AddComponent<MeshFilter>().sharedMesh = BeamMesh(cylinderRadius, 12f);
        var r = glow.AddComponent<MeshRenderer>();
        beamMaterial = new Material(Shader.Find("Sprites/Default"));
        r.sharedMaterial = beamMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private void Update()
    {
        if (beamMaterial == null) return;
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 3f);
        float fade = sprung ? Mathf.Clamp01((liveUntil - Time.time) / activeSeconds) : 1f;
        beamMaterial.color = new Color(1f, 1f, 1f, pulse * fade);
        if (beamLight != null) beamLight.enabled = fade > 0.01f;
    }

    // An open tube along +Z (radius r, length len) whose vertex alpha fades: 0.28 at the well, 0.07 at 5 m... 0 at 12 m
    private static Mesh BeamMesh(float r, float len)
    {
        const int sides = 24, rings = 13;
        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var tris = new List<int>();
        for (int j = 0; j < rings; j++)
        {
            float z = len * j / (rings - 1);
            float a = z < 5f ? Mathf.Lerp(0.28f, 0.12f, z / 5f) : z < 10f ? Mathf.Lerp(0.12f, 0.03f, (z - 5f) / 5f) : Mathf.Lerp(0.03f, 0f, (z - 10f) / 2f);
            for (int i = 0; i < sides; i++)
            {
                float ang = i / (float)sides * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, z));
                colors.Add(new Color(Purple.r, Purple.g, Purple.b, a));
            }
        }
        for (int j = 0; j < rings - 1; j++)
            for (int i = 0; i < sides; i++)
            {
                int a0 = j * sides + i, a1 = j * sides + (i + 1) % sides, b0 = a0 + sides, b1 = a1 + sides;
                // Both faces (Sprites/Default culls nothing, but keep the winding tidy)
                tris.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
            }
        var mesh = new Mesh { name = "Gravity Well Beam" };
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Inside the column: within the radius of the well's facing line, from just behind it out to its reach.</summary>
    private bool InColumn(Vector3 worldPos)
    {
        Vector3 to = worldPos - transform.position;
        float along = Vector3.Dot(to, transform.forward);
        if (along < -Behind || along > maxRange) return false;
        return (to - transform.forward * along).magnitude <= cylinderRadius;
    }

    // Nothing solid between the well and it (players and the thing itself don't block)
    private bool InSight(Vector3 point, Transform self)
    {
        Vector3 from = transform.position + transform.forward * 0.3f;
        Vector3 d = point - from;
        foreach (var hit in Physics.RaycastAll(from, d.normalized, d.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(self) || hit.collider.GetComponentInParent<Player>() != null) continue;
            return false;
        }
        return true;
    }

    // ─── Debug visualisation (editor only) ───────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0f, 1f, 0.4f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        const int segments = 16;
        Vector3 origin = Vector3.back * Behind, tip = Vector3.forward * maxRange;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            Vector3 p0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * cylinderRadius;
            Vector3 p1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * cylinderRadius;
            Gizmos.DrawLine(origin + p0, origin + p1);
            Gizmos.DrawLine(tip + p0, tip + p1);
            Gizmos.DrawLine(origin + p0, tip + p0);
        }
    }
#endif
}
