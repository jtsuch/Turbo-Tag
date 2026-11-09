using Photon.Pun;
using UnityEngine;

/// <summary>
/// Trip Wire (Resources/Object/TripWire): a little anchor on a wall with a thin red wire running straight out to
/// whatever is across the gap (up to <see cref="maxLength"/>). Each screen checks its own player: the first enemy whose
/// body crosses it is held for <see cref="holdSeconds"/> and revealed to the placer's team, then the wire is used up.
/// </summary>
public class TripWireObject : TacticalTrapObject
{
    [TunableParam("Max Length", 2f, 30f)]
    [SerializeField] private float maxLength = 13.2f;
    [TunableParam("Hold Time", 0f, 6f)]
    [SerializeField] private float holdSeconds = 1.5f;
    [TunableParam("Reveal Time", 0f, 10f)]
    [SerializeField] private float revealSeconds = 3f;

    private Vector3 start, end;
    private LineRenderer line;
    private bool tripped;

    private void Reset() => lifetime = 0f;

    protected override void Start()
    {
        base.Start();
        start = transform.position + transform.forward * 0.1f;
        // To the first wall across the gap (never to a player standing in the way)
        end = start + transform.forward * maxLength;
        float nearest = float.MaxValue;
        foreach (var h in Physics.RaycastAll(start, transform.forward, maxLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (h.distance < nearest && !h.transform.IsChildOf(transform) && h.collider.GetComponentInParent<Player>() == null)
            { nearest = h.distance; end = h.point; }

        line = gameObject.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.widthMultiplier = 0.025f;
        line.material = Glass(new Color(1f, 0.2f, 0.25f, 0.55f));
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void Update()
    {
        if (tripped) return;
        var enemy = LocalEnemy;
        if (enemy == null) return;
        // The body as a line from the feet to the top: does it pass within reach of the wire?
        Vector3 feet = enemy.transform.position, top = new(feet.x, enemy.TopY, feet.z);
        if (SegmentDistance(start, end, feet, top) > 0.45f) return;

        tripped = true;
        enemy.HoldFor(holdSeconds);
        TacticalNet.Reveal(PlacerActor, enemy.ActorNumber, revealSeconds);
        photonView.RPC(nameof(RPC_Tripped), RpcTarget.All, enemy.transform.position + Vector3.up, enemy.ActorNumber);
    }

    [PunRPC]
    private void RPC_Tripped(Vector3 at, int caught)
    {
        // The placer hears about it: a line in the feed, a map pulse and a ping where it happened
        if (photonView.IsMine)
        {
            var who = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.GetPlayer(caught) : null;
            CombatHud.Post($"<color=#FF5A5A>Trip Wire</color> caught {(who != null ? who.NickName : "someone")}!", 4f);
            Minimap.Pulse(new Color(1f, 0.3f, 0.3f));
            PingMarkers.Add(at, PingKind.HunterThere, "TripWire");
        }
        tripped = true;
        if (line != null) line.enabled = false;
        Sfx.At(SfxKind.Snap, at, 1f, 40f, 1.3f);
        Vfx.Burst(at, new Color(1f, 0.25f, 0.3f), 18, 4f, 0.2f, 0.4f);
        if (photonView.IsMine) Invoke(nameof(Remove), 0.5f);
    }

    // Closest distance between two segments (p1-q1 and p2-q2)
    private static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float s, t;
        if (a <= 1e-6f && e <= 1e-6f) return r.magnitude;
        if (a <= 1e-6f) { s = 0f; t = Mathf.Clamp01(f / e); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= 1e-6f) { t = 0f; s = Mathf.Clamp01(-c / a); }
            else
            {
                float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                s = denom != 0f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
            }
        }
        return Vector3.Distance(p1 + d1 * s, p2 + d2 * t);
    }
}
