using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A fan that blows along its forward axis: anything inside a column <see cref="radius"/> wide and
/// <see cref="range"/> long in front of it is pushed (an acceleration, so mass doesn't matter; above
/// gravity's 40 it lifts players). Pushes count as outside forces (WorldFx.Push), so movement doesn't
/// brake them. Only bodies this client owns are pushed (its own player and local props).
/// The blades spin around their visual centre (the mesh's pivot may sit off to one side), and a stream
/// of air particles rises through the column so players can see where it reaches.
///
/// Unity setup: on an object whose forward points the way the wind blows (the Fan prefab's "Wind"
/// child points up). Assign spinPart to the blades. Optional windMaterial for the air streaks.
/// </summary>
public class Fan : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform spinPart;
    [Tooltip("Material for the air streaks (a URP particle material). Falls back to a default.")]
    [SerializeField] private Material windMaterial;

    [Header("Blow Settings")]
    [Tooltip("Push inside the column (m/s²). Gravity is 40, so above that lifts players.")]
    [SerializeField] private float force  = 25f;
    [Tooltip("How far the wind reaches along the fan's forward (m).")]
    [SerializeField] private float range  = 6f;
    [Tooltip("Width of the wind column: its radius around the fan's centre line (m).")]
    [SerializeField] private float radius = 3f;
    [SerializeField] private LayerMask affectedLayers = ~0;

    [Header("Spin Settings")]
    [SerializeField] private float spinSpeed = 360f; // Degrees per second

    [Header("Air Effect")]
    [SerializeField] private bool  showWind    = true;
    [SerializeField] private float windDensity = 60f; // Streaks per second

    private Vector3 bladeCentreLocal; // The blades' visual centre, in their parent's space
    private readonly HashSet<Rigidbody> pushed = new();
    private static readonly Collider[] buffer = new Collider[32];

    // ─── Setup ────────────────────────────────────────────────────────────────

    private void Start()
    {
        if (spinPart != null)
        {
            // Spin around the middle of the blades, not the mesh's pivot (which made them wobble)
            var renderer = spinPart.GetComponentInChildren<Renderer>();
            Vector3 centre = renderer != null ? renderer.bounds.center : spinPart.position;
            bladeCentreLocal = spinPart.parent != null ? spinPart.parent.InverseTransformPoint(centre) : centre;
        }
        if (showWind) BuildWind();
    }

    // ─── Blade spin ───────────────────────────────────────────────────────────

    private void Update()
    {
        if (spinPart == null) return;
        Vector3 centre = spinPart.parent != null ? spinPart.parent.TransformPoint(bladeCentreLocal) : bladeCentreLocal;
        spinPart.RotateAround(centre, transform.forward, spinSpeed * Time.deltaTime);
    }

    // ─── Wind force ───────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        // A sphere around the column's middle covers it; then keep only what's really inside the column
        Vector3 axis   = transform.forward;
        Vector3 middle = transform.position + axis * (range * 0.5f);
        float   reach  = Mathf.Sqrt(range * range * 0.25f + radius * radius);
        int count = Physics.OverlapSphereNonAlloc(middle, reach, buffer, affectedLayers, QueryTriggerInteraction.Ignore);

        pushed.Clear();
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = buffer[i].attachedRigidbody;
            if (rb == null || rb.isKinematic || !pushed.Add(rb)) continue;
            if (!WorldFx.HasAuthority(rb)) continue; // Remote bodies are moved by their owners

            Vector3 toBody = rb.worldCenterOfMass - transform.position;
            float   along  = Vector3.Dot(toBody, axis);
            if (along < 0f || along > range) continue;
            if ((toBody - axis * along).sqrMagnitude > radius * radius) continue;

            WorldFx.Push(rb, axis * force, ForceMode.Acceleration);
        }
    }

    // ─── Air streaks ──────────────────────────────────────────────────────────

    private void BuildWind()
    {
        var go = new GameObject("Wind Streaks");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        const float lifetime = 1.4f;
        var main = ps.main;
        main.loop            = true;
        main.startLifetime   = lifetime;
        main.startSpeed      = range / lifetime;            // Streaks reach the top of the column
        main.startSize       = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startColor      = new Color(1f, 1f, 1f, 0.35f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 400;

        var emission = ps.emission;
        emission.rateOverTime = windDensity;

        var shape = ps.shape;
        // A near-flat cone emits from a disc straight along the emitter's forward (a Circle shape would spray sideways)
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = 2f;
        shape.radius    = radius * 0.85f;

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode   = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale  = 6f;
        renderer.velocityScale = 0.05f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.sharedMaterial = windMaterial != null
            ? windMaterial
            : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));

        ps.Play();
    }

    // ─── Scene-view gizmos ────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color  = new Color(0f, 1f, 1f, 0.6f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position + transform.forward * (range * 0.5f), transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(radius * 2f, radius * 2f, range));
    }
}
