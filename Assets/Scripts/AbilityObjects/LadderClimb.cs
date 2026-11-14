using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Climbing a placed Ladder (Justin, Nov 14): stand at it, face it and press Grab (F) to latch onto the side you're
/// facing; W/S climb up and down; Jump (a push off), F again or reaching either end (a hop onto the top) lets go.
///
/// While on it you're part of the ladder (Justin: "a physical part of the ladder that dictates where and how it
/// falls"):
///   - your body is held to its side, facing it and tilted with it (<see cref="Player.ExternalUp"/>: the view and body
///     turn to the ladder's up, like Wall Magnet), kinematic, with collisions between you and the ladder off;
///   - every screen gives the ladder a capsule where you are (a "rider" collider, with your weight added on the
///     ladder's simulating client), so the ladder falls the way your weight pulls it and you can't be pushed into
///     walls or floors: the capsule hits them, not you. The climber tells everyone where they are on it (RPC).
/// Added to the ladder by <see cref="TippingProp"/> (it shares the ladder's PhotonView).
/// </summary>
public class LadderClimb : MonoBehaviourPun
{
    private const float Reach = 1.8f;         // How close the chest must be to the ladder's middle line (m)
    private const float ClimbSpeed = 4.5f;    // m/s along the ladder
    private const float JumpOff = 6f;
    private const float RiderMass = 20f;      // Added to the ladder per climber (its simulating client)
    private const float StandIn = 0.15f;      // How far the body sits into the ladder's face (collisions between them are off)

    // ─── The ladder's shape (its own space), measured once from its colliders ──
    private Vector3 axisLocal, faceLocal, centreLocal;
    private float length, halfDepth;
    private bool measured;
    private Collider[] ladderColliders;
    private Rigidbody ladderBody;
    private float baseMass = -1f;

    // ─── The local climber ────────────────────────────────────────────────────
    private Player climber;
    private Rigidbody body;
    private Collider[] climberColliders;
    private float along, side, stand;
    private float lastSentAlong = float.NaN, nextSend;

    // ─── Riders on every screen: actor → their capsule on the ladder ──────────
    private readonly Dictionary<int, CapsuleCollider> riders = new();

    private void Measure()
    {
        measured = true;
        ladderBody = GetComponent<Rigidbody>();
        ladderColliders = GetComponentsInChildren<Collider>();
        if (ladderColliders.Length == 0) return;
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        foreach (var c in ladderColliders)
        {
            var b = c.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z);
                Vector3 l = transform.InverseTransformPoint(corner);
                min = Vector3.Min(min, l); max = Vector3.Max(max, l);
            }
        }
        Vector3 size = max - min;
        centreLocal = (min + max) * 0.5f;
        Vector3 s = transform.lossyScale;
        Vector3 world = new(size.x * Mathf.Abs(s.x), size.y * Mathf.Abs(s.y), size.z * Mathf.Abs(s.z));
        int longest = world.x >= world.y && world.x >= world.z ? 0 : world.y >= world.z ? 1 : 2;
        int thinnest = world.x <= world.y && world.x <= world.z ? 0 : world.y <= world.z ? 1 : 2;
        if (thinnest == longest) thinnest = (longest + 1) % 3;
        axisLocal = longest == 0 ? Vector3.right : longest == 1 ? Vector3.up : Vector3.forward;
        faceLocal = thinnest == 0 ? Vector3.right : thinnest == 1 ? Vector3.up : Vector3.forward;
        length = world[longest];
        halfDepth = world[thinnest] * 0.5f;
    }

    // Up the ladder in the world (whichever end is higher is the top, decided when you get on), and its faces
    private float upSign = 1f;
    private Vector3 AxisWorld => transform.TransformDirection(axisLocal).normalized;
    private Vector3 Up => AxisWorld * upSign;
    private Vector3 Face => transform.TransformDirection(faceLocal).normalized;
    private Vector3 Bottom => transform.TransformPoint(centreLocal) - Up * (length * 0.5f);

    // ─── Getting on ───────────────────────────────────────────────────────────

    private void Update()
    {
        if (!measured) Measure();
        if (length < 0.5f) return;
        var local = Player.Local;
        if (local == null || PauseMenuManager.IsPaused) return;
        KeyCode grab = Keybinds.Get(InputAction.Grab);

        if (climber != null)
        {
            if (Input.GetKeyDown(Keybinds.Get(InputAction.Jump))) Detach(Face * side * JumpOff + Vector3.up * 4f);
            else if (Input.GetKeyDown(grab)) { local.ClaimGrabKey(); Detach(Vector3.zero); }
            return;
        }
        if (!local.CanMove || local.IsCaught || local.OnLadder || local.OnZipLine) return;

        upSign = AxisWorld.y >= 0f ? 1f : -1f;
        Vector3 chest = local.transform.position + Vector3.up * (local.Height * 0.5f);
        Vector3 bottom = Bottom;
        float at = Vector3.Dot(chest - bottom, Up);
        if (at < -0.5f || at > length + 0.5f) return;
        Vector3 offLine = chest - (bottom + Up * Mathf.Clamp(at, 0f, length));
        if (offLine.magnitude > Reach) return;
        Vector3 toLadder = -Vector3.ProjectOnPlane(offLine, Vector3.up);
        if (toLadder.sqrMagnitude > 0.01f && Vector3.Dot(local.transform.forward, toLadder.normalized) < 0.3f) return;

        local.ClaimGrabKey(); // F climbs the ladder here; it must not also grab a ledge
        HUDManager.Prompt(grab, "Climb the ladder");
        if (Input.GetKeyDown(grab)) Attach(local, offLine);
    }

    private void Attach(Player player, Vector3 offLine)
    {
        body = player.Body;
        if (body == null) return;
        climber = player;
        side = Vector3.Dot(offLine, Face) >= 0f ? 1f : -1f;
        float radius = player.TryGetComponent(out CapsuleCollider capsule) ? capsule.radius * player.transform.lossyScale.x : 0.7f;
        stand = halfDepth + radius - StandIn;
        along = Mathf.Clamp(Vector3.Dot(player.transform.position - Bottom, Up), 0f, Mathf.Max(0f, length - 0.3f));

        // Part of the ladder now: no collisions between the two, held in place by this script
        climberColliders = player.GetComponentsInChildren<Collider>();
        SetIgnored(climberColliders, true);
        body.isKinematic = true;
        player.OnLadder = true;
        player.SetFlag(PlayerFlag.OnLadder, true);
        player.ExternalUp = Up;
        if (PlayerCam.Local != null) PlayerCam.Local.FaceAlong(-Face * side);
        Hold();
        Send(true);
        Sfx.At(SfxKind.Snap, body.position, 0.5f, 20f, 1.2f);
    }

    // The body on the ladder's side at its height on it, its up along the ladder
    private void Hold()
    {
        Vector3 feet = Bottom + Up * along + Face * side * stand;
        body.MovePosition(feet);
        climber.ExternalUp = Up;
    }

    private void FixedUpdate()
    {
        if (climber == null) return;
        if (climber != Player.Local || climber.IsCaught || body == null) { Detach(Vector3.zero); return; }

        float input = climber.Input != null ? climber.Input.MoveInput.y : 0f;
        along += input * ClimbSpeed * Time.fixedDeltaTime;
        climber.SetFlag(PlayerFlag.LadderMoving, Mathf.Abs(input) > 0.1f);
        if (along >= length - 0.2f && input > 0.1f)
        {
            // The top: a hop up and over onto whatever it leans on
            along = length - 0.2f;
            Hold();
            Detach(Up * 7f - Face * side * 3f);
            return;
        }
        if (along <= 0f && input < -0.1f) { along = 0f; Hold(); Detach(Vector3.zero); return; }
        along = Mathf.Clamp(along, 0f, length);
        Hold();
        if (Time.time >= nextSend && Mathf.Abs(along - lastSentAlong) > 0.05f) Send(true);
    }

    private void Detach(Vector3 velocity)
    {
        if (climber == null) return;
        var player = climber;
        climber = null;
        player.OnLadder = false;
        player.SetFlag(PlayerFlag.OnLadder, false);
        player.SetFlag(PlayerFlag.LadderMoving, false);
        player.ExternalUp = null;
        SetIgnored(climberColliders, false);
        if (body != null)
        {
            body.isKinematic = false;
            body.linearVelocity = velocity;
            player.NotePushed();
        }
        body = null;
        Send(false);
    }

    private void OnDisable()
    {
        Detach(Vector3.zero);
        foreach (var r in riders.Values) if (r != null) Destroy(r.gameObject);
        riders.Clear();
    }

    private void SetIgnored(Collider[] theirs, bool ignore)
    {
        if (theirs == null || ladderColliders == null) return;
        foreach (var a in theirs)
        {
            if (a == null) continue;
            foreach (var b in ladderColliders) if (b != null) Physics.IgnoreCollision(a, b, ignore);
            foreach (var r in riders.Values) if (r != null) Physics.IgnoreCollision(a, r, ignore);
        }
    }

    // ─── Riders on every screen ───────────────────────────────────────────────

    private void Send(bool on)
    {
        lastSentAlong = along;
        nextSend = Time.time + 0.1f;
        if (PhotonNetwork.InRoom) photonView.RPC(nameof(RPC_LadderRider), RpcTarget.All, PhotonNetwork.LocalPlayer.ActorNumber, along, side, upSign, on);
        else RPC_LadderRider(0, along, side, upSign, on);
    }

    // A capsule on the ladder where the climber is, so the ladder carries their shape (and, where it's simulated,
    // their weight): it falls the way they pull it and they can't be pushed through walls
    [PunRPC]
    private void RPC_LadderRider(int actor, float at, float onSide, float up, bool on)
    {
        if (!measured) Measure();
        if (!on)
        {
            if (riders.TryGetValue(actor, out var gone) && gone != null) Destroy(gone.gameObject);
            riders.Remove(actor);
            UpdateMass();
            return;
        }
        var player = PhotonNetwork.InRoom ? Player.Find(actor) : Player.Local;
        if (!riders.TryGetValue(actor, out var capsule) || capsule == null)
        {
            var go = new GameObject($"Rider {actor}");
            go.transform.SetParent(transform, false);
            capsule = go.AddComponent<CapsuleCollider>();
            riders[actor] = capsule;
            // Nothing between the rider's own body and its capsule or the ladder
            if (player != null)
            {
                var theirs = player.GetComponentsInChildren<Collider>();
                foreach (var a in theirs) { Physics.IgnoreCollision(a, capsule, true); foreach (var b in ladderColliders) Physics.IgnoreCollision(a, b, true); }
            }
            foreach (var b in ladderColliders) Physics.IgnoreCollision(capsule, b, true);
        }
        float height = player != null ? player.Height : 2.9f;
        float radius = player != null && player.TryGetComponent(out CapsuleCollider own) ? own.radius * player.transform.lossyScale.x : 0.7f;
        float depth = halfDepth + radius - StandIn;
        // In the ladder's own space: along its axis from the bottom, out from the face on the rider's side
        Vector3 upWorld = AxisWorld * up;
        Vector3 bottom = transform.TransformPoint(centreLocal) - upWorld * (length * 0.5f);
        Vector3 middleWorld = bottom + upWorld * (at + height * 0.5f) + Face * onSide * depth;
        capsule.transform.position = middleWorld;
        capsule.transform.rotation = Quaternion.FromToRotation(Vector3.up, upWorld);
        // World size 1 whatever the ladder's scale, so radius and height are in metres
        capsule.transform.localScale = Vector3.one;
        capsule.transform.localScale = new Vector3(1f / Mathf.Max(1e-4f, capsule.transform.lossyScale.x),
                                                   1f / Mathf.Max(1e-4f, capsule.transform.lossyScale.y),
                                                   1f / Mathf.Max(1e-4f, capsule.transform.lossyScale.z));
        capsule.direction = 1;
        capsule.radius = radius;
        capsule.height = height;
        UpdateMass();
    }

    // The simulating client carries the riders' weight (the centre of mass follows the colliders, rider capsules too)
    private void UpdateMass()
    {
        if (ladderBody == null || !photonView.IsMine) return;
        if (baseMass < 0f) baseMass = ladderBody.mass;
        ladderBody.mass = baseMass + RiderMass * riders.Count;
        ladderBody.ResetCenterOfMass();
        ladderBody.ResetInertiaTensor();
        ladderBody.WakeUp();
    }
}
