using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Pulls out a horn and HONKS: a short-range blast that shoves every player and loose object in front of you.
///   owner key → RPC_Honk (all: horn pops out, honk, air puff) → each client pushes the bodies it has authority
///   over (its own player, its props), so every push happens on the right machine.
/// Shielded players ignore it. The horn is built in code (a cone and a squeeze bulb); a real model can replace it.
/// </summary>
[AbilityInfo("HONK! Blasts players and objects in front of you away.")]
public class Honk : QuickAbility
{
    [Header("Honk")]
    [TunableParam("Range", 1f, 20f)]
    [SerializeField] private float range = 7f;
    [Tooltip("Half-angle of the cone in front of you that gets blasted (degrees).")]
    [TunableParam("Cone Angle", 10f, 90f)]
    [SerializeField] private float coneAngle = 50f;
    [Tooltip("Speed given to players at point-blank range (m/s); fades to half at the edge of the range.")]
    [TunableParam("Push", 1f, 150f)]
    [SerializeField] private float push = 44f;
    [TunableParam("Lift", 0f, 30f)]
    [SerializeField] private float lift = 12f;
    [Tooltip("Impulse for loose objects (boxes, ladders), per kg-ish.")]
    [TunableParam("Object Push", 1f, 100f)]
    [SerializeField] private float objectPush = 28f;

    [Header("References")]
    [Tooltip("The camera (aim). Defaults to the player's camera.")]
    [SerializeField] private Transform cam;

    private static readonly Collider[] overlap = new Collider[64];
    private GameObject horn;
    private float      hornUntil;

    protected override void Activate()
    {
        Transform view = cam != null ? cam : PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : transform;
        Owner.photonView.RPC(nameof(RPC_Honk), RpcTarget.All, view.position, view.forward);
    }

    [PunRPC]
    private void RPC_Honk(Vector3 origin, Vector3 direction)
    {
        direction.Normalize();
        AoeDebug.Cone(origin, direction, range, coneAngle, new Color(1f, 0.9f, 0.3f), "Honk");
        ShowHorn(origin, direction);
        Sfx.At(SfxKind.Honk, origin, 1f, 45f);
        Vfx.Burst(origin + direction * 1.2f, new Color(1f, 1f, 1f, 0.7f), 22, 9f, 0.35f, 0.4f, direction: direction, spread: 0.35f);
        Vfx.Burst(origin + direction * 1.2f, new Color(1f, 0.79f, 0.24f), 10, 7f, 0.18f, 0.35f, direction: direction, spread: 0.45f);
        if (IsLocal) Fx.Shake(0.12f);

        PushInCone(origin, direction);
    }

    // Each client pushes only what it simulates: its own player and un-networked or owned props
    private void PushInCone(Vector3 origin, Vector3 direction)
    {
        int count = Physics.OverlapSphereNonAlloc(origin, range, overlap, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        var done = new HashSet<Rigidbody>();
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = overlap[i].attachedRigidbody;
            if (rb == null || rb == Body || rb.isKinematic || !done.Add(rb)) continue;
            if (!WorldFx.HasAuthority(rb)) continue;

            Vector3 to = rb.worldCenterOfMass - origin;
            float distance = to.magnitude;
            if (distance > range || Vector3.Angle(direction, to) > coneAngle) continue;
            float falloff = Mathf.Lerp(1f, 0.5f, distance / range);

            if (rb.TryGetComponent(out Player target))
            {
                if (target.IsCaught || !target.AffectedBy(Owner.ActorNumber)) continue;
                Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
                Vector3 kick = (Vector3.Lerp(direction, flat, 0.5f).normalized * push + Vector3.up * lift) * falloff * PowerOf(Owner);
                WorldFx.Push(rb, kick, ForceMode.VelocityChange, Owner.ActorNumber, "Honk");
                Fx.Shake(0.25f);
            }
            else
            {
                WorldFx.Push(rb, (direction * objectPush + Vector3.up * lift * 0.5f) * falloff, ForceMode.Impulse, Owner.ActorNumber, "Honk");
            }
        }
    }

    // The honker's Ability Overdrive is known only on their own screen; others honk at normal strength
    private float PowerOf(Player p) => p != null && p.IsLocal ? Mathf.Sqrt(p.AbilityPower) * UpgradePower : 1f;   // S&D: 75–125%

    // ─── Horn (every client) ──────────────────────────────────────────────────

    private void ShowHorn(Vector3 origin, Vector3 direction)
    {
        if (horn == null) horn = BuildHorn();
        horn.SetActive(true);
        hornUntil = Time.time + 0.55f;
        // In front of the face (your own screen sees it at the bottom right of the view)
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        horn.transform.SetPositionAndRotation(origin + direction * 0.7f + right * 0.25f - Vector3.up * 0.25f,
            Quaternion.LookRotation(direction));
    }

    private void LateUpdate()
    {
        if (horn == null || !horn.activeSelf) return;
        float left = hornUntil - Time.time;
        if (left <= 0f) { horn.SetActive(false); return; }
        float age = 0.55f - left;
        float pop = age < 0.08f ? age / 0.08f : left < 0.12f ? left / 0.12f : 1f + Mathf.Sin(age * 60f) * 0.06f;
        horn.transform.localScale = Vector3.one * pop;
    }

    private static GameObject BuildHorn()
    {
        var root = new GameObject("Honk Horn");
        // Justin's air horn (Resources/Object/AirHorn, 0.52 tall standing on its base, the bell facing −Z): turned to
        // blow forward, centred on the root
        var model = Resources.Load<GameObject>("Object/AirHorn");
        if (model != null)
        {
            var horn = Object.Instantiate(model, root.transform, false);
            foreach (var c in horn.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (var r in horn.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            horn.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            horn.transform.localPosition = new Vector3(0f, -0.26f, 0f);
            root.SetActive(false);
            return root;
        }
        var brass = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(1f, 0.78f, 0.2f) };
        var rubber = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.95f, 0.2f, 0.3f) };

        // Bell: a short wide cylinder at the front, the tube behind it, the squeeze bulb at the back
        Part(root.transform, PrimitiveType.Cylinder, brass, new Vector3(0f, 0f, 0.32f), new Vector3(0.34f, 0.05f, 0.34f));
        Part(root.transform, PrimitiveType.Cylinder, brass, new Vector3(0f, 0f, 0.18f), new Vector3(0.2f, 0.1f, 0.2f));
        Part(root.transform, PrimitiveType.Cylinder, brass, new Vector3(0f, 0f, 0.02f), new Vector3(0.07f, 0.14f, 0.07f));
        Part(root.transform, PrimitiveType.Sphere, rubber, new Vector3(0f, 0f, -0.2f), new Vector3(0.2f, 0.2f, 0.24f));
        root.SetActive(false);
        return root;
    }

    private static void Part(Transform parent, PrimitiveType type, Material material, Vector3 position, Vector3 scale)
    {
        var part = GameObject.CreatePrimitive(type);
        Object.Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        if (type == PrimitiveType.Cylinder) part.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Point along +Z
        part.transform.localScale = scale;
        var r = part.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void OnDestroy()
    {
        if (horn != null) Destroy(horn);
    }
}
