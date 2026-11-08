using Photon.Pun;
using UnityEngine;

/// <summary>
/// Taunts and pings for one player copy (added to every copy by Player, so the RPCs line up):
///   - Taunt (<see cref="InputAction.Taunt"/>, default G): a loud, silly call everyone hears from where you stand
///     (and the sound visualizer shows it). Hold the key and let go to throw it instead: the call comes from where
///     you're looking (up to 25 m away), a decoy nobody can tell from the real thing. A few seconds' cooldown. No points.
///   - Pings (the <see cref="PingWheel"/>): a marker at a spot ("Hide here", "Hunter there", "Let's meet there",
///     "Run that way") for your team (everyone, in modes without teams), shown by <see cref="PingMarkers"/>.
/// </summary>
public class PlayerCallouts : MonoBehaviourPun
{
    public const float TauntCooldown = 4f;

    private const float ThrowHold = 0.35f, ThrowRange = 25f;

    private Player player;
    private float nextTaunt, tauntDownAt = -1f;

    /// <summary>The local player's copy.</summary>
    public static PlayerCallouts Local { get; private set; }

    private void Awake() => player = GetComponent<Player>();

    private void Start()
    {
        if (photonView.IsMine) Local = this;
    }

    private void OnDestroy()
    {
        if (Local == this) Local = null;
    }

    // Tap: taunt where you stand. Hold and let go: throw it where you're looking (a decoy)
    private void Update()
    {
        if (!photonView.IsMine || player == null || player.IsCaught) { tauntDownAt = -1f; return; }
        if (PauseMenuManager.IsPaused || EmoteMenu.IsOpen || PingWheel.IsOpen) { tauntDownAt = -1f; return; }
        KeyCode key = Keybinds.Get(InputAction.Taunt);
        if (Input.GetKeyDown(key)) tauntDownAt = Time.time;
        if (tauntDownAt < 0f) return;
        bool held = Time.time - tauntDownAt >= ThrowHold;
        if (held && Input.GetKey(key)) HUDManager.Prompt(key, "Let go to throw your taunt");
        if (!Input.GetKeyUp(key)) return;
        tauntDownAt = -1f;
        if (Time.time < nextTaunt) return;
        nextTaunt = Time.time + TauntCooldown;

        Vector3 head = transform.position + Vector3.up * Mathf.Max(1.5f, player.EyeHeight);
        Vector3 from = head;
        bool decoy = held && PlayerCam.Local != null;
        if (decoy)
        {
            var cam = PlayerCam.Local.Camera.transform;
            from = cam.position + cam.forward * ThrowRange;
            foreach (var hit in Physics.RaycastAll(cam.position, cam.forward, ThrowRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(transform) && Vector3.Distance(cam.position, hit.point) < Vector3.Distance(cam.position, from))
                    from = hit.point + hit.normal * 0.3f;
        }
        photonView.RPC(nameof(RPC_Taunt), RpcTarget.All, (byte)Random.Range(0, 3), from, decoy);
    }

    [PunRPC]
    private void RPC_Taunt(byte variant, Vector3 at, bool decoy)
    {
        Sfx.At(SfxKind.Taunt, at, 1f, 70f, 0.9f + variant * 0.12f);
        // The thrower sees where their decoy went; to everyone else it's just a taunt from over there
        if (!decoy || photonView.IsMine) Vfx.Burst(at, new Color(1f, 0.85f, 0.3f), 10, 3f, 0.2f, 0.5f, direction: Vector3.up, spread: 0.6f);
        if (!decoy && player != null) player.SetBounce(0.4f);
        if (!photonView.IsMine) SoundVisualizer.Notify(at, 1f, photonView.OwnerActorNr);
    }

    // ─── Being watched (Last Hider Cam) ───────────────────────────────────────

    /// <summary>A hunter watching this player (the last hider) tells them, quietly.</summary>
    public void NotifyWatched() => photonView.RPC(nameof(RPC_Watched), photonView.Owner);

    [PunRPC]
    private void RPC_Watched() => Minimap.Pulse(new Color(0.75f, 0.45f, 1f));

    // ─── Pings ────────────────────────────────────────────────────────────────

    /// <summary>Local: marks <paramref name="point"/> for the team.</summary>
    public void SendPing(Vector3 point, PingKind kind) => photonView.RPC(nameof(RPC_Ping), RpcTarget.All, point, (byte)kind);

    [PunRPC]
    private void RPC_Ping(Vector3 point, byte kind)
    {
        // Your own pings, and your team's (everyone's, in modes without teams)
        var local = Player.Local;
        var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
        bool mine = photonView.IsMine;
        bool team = local != null && player != null
                    && (mode == null || !mode.UsesRoles || mode.SameTeam(local.Role, player.Role));
        if (!mine && !team) return;
        PingMarkers.Add(point, (PingKind)kind, player != null ? player.DisplayName : "");
    }
}

/// <summary>What a ping says. Wheel directions: up, right, down, left.</summary>
public enum PingKind : byte { HideHere, HunterThere, MeetThere, RunThatWay }
