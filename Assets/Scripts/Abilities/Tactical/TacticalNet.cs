using Photon.Pun;
using UnityEngine;

/// <summary>
/// The tactical abilities' messages to and about players (added to every player copy by Player, so the RPCs line up;
/// every RPC is prefixed RPC_T). Statuses go to the target's owner, who applies them to their own player:
/// ink, suppression, shock (slow), healing, a knock and a hold. Reveals go to everyone: each screen whose player is
/// the source or on the source's team lights the target up through walls (<see cref="HunterReveal"/>, the same as a
/// hunter's tools), and the revealed player's map border pulses so they know.
/// Damage doesn't come through here: the attacker's client sends it straight to the target (<see cref="AbilityDamage"/>).
/// </summary>
public class TacticalNet : MonoBehaviourPun
{
    public enum Status : byte { Ink, Suppress, Shock, Heal, Knock, Hold }

    private Player player;

    private void Awake() => player = GetComponent<Player>();

    /// <summary>Any client: gives <paramref name="target"/> a status (applied by its owner; shields block the harmful ones).</summary>
    public static void Send(Player target, Status status, float seconds, Vector3 vector = default)
    {
        if (target == null || !target.TryGetComponent(out TacticalNet net)) return;
        net.photonView.RPC(nameof(RPC_TStatus), target.photonView.Owner, (byte)status, seconds, vector, PhotonNetwork.LocalPlayer.ActorNumber);
    }

    [PunRPC]
    private void RPC_TStatus(byte status, float seconds, Vector3 vector, int source)
    {
        if (!photonView.IsMine || player == null || player.IsCaught) return;
        var kind = (Status)status;
        bool harmful = kind != Status.Heal;
        if (harmful && !player.AffectedBy(source)) return; // Shielded / a ghost
        switch (kind)
        {
            case Status.Ink:      PlayerEffect.Apply(typeof(InkedEffect), player, seconds); break;
            case Status.Suppress: PlayerEffect.Apply(typeof(SuppressedEffect), player, seconds); break;
            case Status.Shock:    PlayerEffect.Apply(typeof(ShockedEffect), player, seconds); break;
            case Status.Heal:     PlayerEffect.Apply(typeof(HealingEffect), player, seconds); break;
            case Status.Hold:     player.HoldFor(seconds); break;
            case Status.Knock:
                player.Body.linearVelocity += vector;
                player.NotePushed(source, "BellySlide");
                break;
        }
    }

    /// <summary>
    /// Any client: <paramref name="targetActor"/> is revealed for <paramref name="seconds"/> to <paramref name="sourceActor"/>
    /// and their team (everyone's screens decide for themselves).
    /// </summary>
    public static void Reveal(int sourceActor, int targetActor, float seconds)
    {
        var local = Player.Local;
        if (local == null || !local.TryGetComponent(out TacticalNet net)) return;
        net.photonView.RPC(nameof(RPC_TReveal), RpcTarget.All, sourceActor, targetActor, seconds);
    }

    [PunRPC]
    private void RPC_TReveal(int sourceActor, int targetActor, float seconds)
    {
        var local = Player.Local;
        if (local == null) return;
        if (local.ActorNumber == sourceActor || local.IsTeammateOf(sourceActor)) HunterReveal.Reveal(targetActor, seconds);
        else if (local.ActorNumber == targetActor) Minimap.Pulse(new Color(1f, 0.35f, 0.3f)); // You've been spotted
    }

    /// <summary>
    /// Who an ability's reveal, damage or status can be aimed at from <paramref name="source"/>'s point of view: someone
    /// else, alive and not a ghost. <paramref name="enemiesOnly"/> also skips the source's team (reveals, traps).
    /// </summary>
    public static bool IsTarget(Player source, Player p, bool enemiesOnly)
    {
        if (p == null || p == source || p.IsCaught || p.IsDownAfterTag || p.Role == PlayerRole.Ghost) return false;
        return !enemiesOnly || source == null || !p.IsTeammateOf(source.ActorNumber);
    }
}

/// <summary>
/// Damage from tactical abilities (health modes only: nothing happens without a CombatManager). Called on the
/// attacker's client (the thrower or placer owns the object), which sends it to the target's owner like a bullet.
/// Non-trap abilities hurt everyone in reach, teammates and the user included (Justin's starting rule); traps
/// (<paramref name="trap"/>) spare the placer's team.
/// </summary>
public static class AbilityDamage
{
    public static bool Active => CombatManager.Instance != null;

    public static void Hit(Player target, float amount, string ability, Vector3 direction, bool trap)
    {
        if (!Active || target == null || target.IsCaught || target.IsDownAfterTag || target.Role == PlayerRole.Ghost || amount <= 0f) return;
        if (trap && target.IsTeammateOf(PhotonNetwork.LocalPlayer.ActorNumber)) return;
        float scaled = amount * MatchRules.DamageScale;
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
        target.Health.RequestDamage(scaled, ability, false, dir);
        if (!target.IsLocal) DamageNumber.Show(Chest(target), scaled, false);
    }

    /// <summary>Middle of a player's body (aim point for sight checks).</summary>
    public static Vector3 Chest(Player p) => p.transform.position + Vector3.up * Mathf.Max(0.9f, p.EyeHeight * 0.6f);

    /// <summary>Nothing solid between <paramref name="from"/> and the player (other players don't block).</summary>
    public static bool InSight(Vector3 from, Player p, Transform ignore = null)
    {
        Vector3 to = Chest(p);
        Vector3 d = to - from;
        foreach (var hit in Physics.RaycastAll(from, d.normalized, d.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<Player>() != null) continue;
            if (ignore != null && hit.collider.transform.IsChildOf(ignore)) continue;
            return false;
        }
        return true;
    }
}
