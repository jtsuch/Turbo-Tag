using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Scene-level singleton that listens for room property updates and applies world-level rule
/// settings at runtime. Place one instance in every level scene.
///
/// Late-joining players get correct state because Start() re-applies all current room props.
/// Other systems can read the static properties (AllowKills, CheatsEnabled) without polling.
/// </summary>
public class GameModeApplicator : MonoBehaviourPunCallbacks
{
    // ─── Static state (readable by any system without a reference) ───────────
    public static bool AllowKills    { get; private set; }
    public static bool CheatsEnabled { get; private set; }
    public static bool FallDamage    { get; private set; }

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        if (PhotonNetwork.CurrentRoom != null)
            ApplyAll(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    // ─── Photon callbacks ─────────────────────────────────────────────────────

    public override void OnRoomPropertiesUpdate(Hashtable changedProps)
    {
        ApplyAll(changedProps);
    }

    // ─── Application logic ────────────────────────────────────────────────────

    private static void ApplyAll(Hashtable props)
    {
        if (props == null) return;

        // Gravity — stored as 0–200 percentage of standard gravity (100 = -9.81 m/s²)
        if (props.TryGetValue(RulesUI.KEY_GRAVITY, out object g) && g is float grav)
            Physics.gravity = Vector3.up * (-9.81f * grav / 100f);

        // Global cooldown multiplier — stored as 0–300 percentage (100 = 1×)
        if (props.TryGetValue(RulesUI.KEY_GLOBAL_COOLDOWN, out object cd) && cd is float cooldown
            && Player.Instance != null)
            Player.Instance.CooldownMultiplier = cooldown / 100f;

        // Kill rules
        if (props.TryGetValue(RulesUI.KEY_ALLOW_KILLS, out object ak) && ak is bool allowKills)
            AllowKills = allowKills;

        if (props.TryGetValue(RulesUI.KEY_FALL_DAMAGE, out object fd) && fd is bool fallDamage)
            FallDamage = fallDamage;

        // Player health — apply to local player if present
        if (props.TryGetValue(RulesUI.KEY_PLAYER_HEALTH, out object hp) && hp is float health
            && Player.Instance != null)
            Player.Instance.SetMaxHealth((int)health);

        // Cheats state — mirrored as a static for systems that don't want to poll room props
        if (props.TryGetValue(RulesUI.KEY_CHEATS, out object ch) && ch is bool cheats)
            CheatsEnabled = cheats;
    }
}
