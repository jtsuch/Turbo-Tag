using Photon.Pun;
using UnityEngine;

/// <summary>
/// Taunts and pings for one player copy (added to every copy by Player, so the RPCs line up):
///   - Taunt (<see cref="InputAction.Taunt"/>, default G): a loud, silly call everyone hears from where you stand
///     (and the sound visualizer shows it). A few seconds' cooldown. No points (yet).
///   - Pings (the <see cref="PingWheel"/>): a marker at a spot ("Hide here", "Hunter there", "Let's meet there",
///     "Run that way") for your team (everyone, in modes without teams), shown by <see cref="PingMarkers"/>.
/// </summary>
public class PlayerCallouts : MonoBehaviourPun
{
    public const float TauntCooldown = 4f;

    private Player player;
    private float nextTaunt;

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

    private void Update()
    {
        if (!photonView.IsMine || player == null || player.IsCaught) return;
        if (PauseMenuManager.IsPaused || EmoteMenu.IsOpen || PingWheel.IsOpen) return;
        if (!Input.GetKeyDown(Keybinds.Get(InputAction.Taunt)) || Time.time < nextTaunt) return;
        nextTaunt = Time.time + TauntCooldown;
        photonView.RPC(nameof(RPC_Taunt), RpcTarget.All, (byte)Random.Range(0, 3));
    }

    [PunRPC]
    private void RPC_Taunt(byte variant)
    {
        Vector3 head = transform.position + Vector3.up * Mathf.Max(1.5f, player != null ? player.EyeHeight : 2f);
        Sfx.At(SfxKind.Taunt, head, 1f, 70f, 0.9f + variant * 0.12f);
        Vfx.Burst(head, new Color(1f, 0.85f, 0.3f), 10, 3f, 0.2f, 0.5f, direction: Vector3.up, spread: 0.6f);
        if (player != null) player.SetBounce(0.4f);
        if (!photonView.IsMine) SoundVisualizer.Notify(transform.position, 1f, photonView.OwnerActorNr);
    }

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
