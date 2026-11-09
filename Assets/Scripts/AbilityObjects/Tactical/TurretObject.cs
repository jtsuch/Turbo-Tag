using Photon.Pun;
using UnityEngine;

/// <summary>
/// Turret (Resources/Object/Turret): sits where it's placed facing the placer's view, and on the placer's client
/// shoots the nearest enemy in front of it (within <see cref="arc"/>° of its facing, <see cref="range"/> m, in sight)
/// every <see cref="interval"/> s for <see cref="damage"/>. A trap: never targets the placer's team. Breakable
/// (<see cref="Shootable"/> on the prefab). Every screen sees the head turn and the shots (RPC per shot).
/// </summary>
public class TurretObject : TacticalTrapObject
{
    [TunableParam("Range", 5f, 80f)]
    [SerializeField] private float range = 30f;
    [TunableParam("Arc", 10f, 180f)]
    [SerializeField] private float arc = 70f;
    [TunableParam("Interval", 0.05f, 3f)]
    [SerializeField] private float interval = 0.35f;
    [TunableParam("Damage", 0f, 100f)]
    [SerializeField] private float damage = 4f;
    [Tooltip("The part that turns to aim (the prefab's 'Head').")]
    [SerializeField] private Transform head;

    private float nextShot;
    private Quaternion aim;

    private void Reset() => lifetime = 30f;

    protected override void Start()
    {
        base.Start();
        if (head == null) head = transform.Find("Head");
        aim = transform.rotation;
        nextShot = Time.time + 1f; // A moment to set up
        BuildFacingMarker();
    }

    // A flat red triangle on the floor just in front, pointing the way it covers
    private void BuildFacingMarker()
    {
        var go = new GameObject("Facing");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.03f / transform.lossyScale.y, 0f);
        var mesh = new Mesh { name = "Turret Facing" };
        Vector3 Local(Vector3 world) => new(world.x / transform.lossyScale.x, world.y / transform.lossyScale.y, world.z / transform.lossyScale.z);
        mesh.vertices = new[] { Local(new Vector3(-0.35f, 0f, 0.75f)), Local(new Vector3(0f, 0f, 1.35f)), Local(new Vector3(0.35f, 0f, 0.75f)) };
        mesh.triangles = new[] { 0, 1, 2, 2, 1, 0 }; // Both sides
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Glass(new Color(1f, 0.25f, 0.2f, 0.75f));
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void Update()
    {
        if (head != null) head.rotation = Quaternion.Slerp(head.rotation, aim, 1f - Mathf.Exp(-12f * Time.deltaTime));
        if (!photonView.IsMine || Time.time < nextShot || !AbilityDamage.Active) return;
        nextShot = Time.time + interval;

        Vector3 muzzle = Muzzle;
        Player best = null;
        float bestDistance = float.MaxValue;
        var placer = Player.Find(PlacerActor);
        foreach (var p in Player.All)
        {
            if (!TacticalNet.IsTarget(placer, p, enemiesOnly: true)) continue;
            Vector3 to = AbilityDamage.Chest(p) - muzzle;
            float d = to.magnitude;
            if (d > range || d >= bestDistance || Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(to, Vector3.up)) > arc) continue;
            if (!AbilityDamage.InSight(muzzle, p, transform)) continue;
            best = p;
            bestDistance = d;
        }
        if (best == null) return;
        Vector3 target = AbilityDamage.Chest(best);
        AbilityDamage.Hit(best, damage * Power, "Turret", target - muzzle, trap: true);
        photonView.RPC(nameof(RPC_TurretShot), RpcTarget.All, muzzle, target);
    }

    private Vector3 Muzzle => (head != null ? head.position : transform.position + Vector3.up) + (head != null ? head.forward : transform.forward) * 0.5f;

    [PunRPC]
    private void RPC_TurretShot(Vector3 from, Vector3 to)
    {
        aim = Quaternion.LookRotation(to - from);
        GunFx.Tracer(from, to);
        GunFx.Flash(from);
        Sfx.At(SfxKind.Pistol, from, 0.7f, 45f, 1.3f);
    }
}
