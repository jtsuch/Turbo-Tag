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
    public enum Status : byte { Ink, Suppress, Shock, Heal, Knock, Hold, Stun }

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
            case Status.Ink:
                PlayerEffect.Apply(typeof(InkedEffect), player, seconds);
                photonView.RPC(nameof(RPC_TInkFace), RpcTarget.Others, seconds);   // Everyone else sees the splats
                break;
            case Status.Suppress: PlayerEffect.Apply(typeof(SuppressedEffect), player, seconds); break;
            case Status.Shock:    PlayerEffect.Apply(typeof(ShockedEffect), player, seconds); break;
            case Status.Heal:     PlayerEffect.Apply(typeof(HealingEffect), player, seconds); break;
            case Status.Hold:     player.HoldFor(seconds); break;
            case Status.Stun:     PlayerEffect.Apply(typeof(StunnedEffect), player, seconds); break;
            case Status.Knock:
                player.Body.linearVelocity += vector;
                player.NotePushed(source, "BellySlide");
                break;
        }
    }

    // Ink splats on this player's face for everyone else (the inked player sees the ink over their screen instead):
    // dark glossy blobs on the front of the head, following the Head bone, gone when the ink wears off
    [PunRPC]
    private void RPC_TInkFace(float seconds)
    {
        if (player == null) return;
        Transform head = player.HeadBone;
        if (head == null) return;
        var old = head.Find("Ink Splats");
        if (old != null) Destroy(old.gameObject);
        var root = new GameObject("Ink Splats").transform;
        root.SetParent(head, false);
        if (inkMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            inkMaterial = new Material(shader) { color = new Color(0.05f, 0.03f, 0.09f) };
            if (inkMaterial.HasProperty("_Smoothness")) inkMaterial.SetFloat("_Smoothness", 0.9f);
        }
        // Placed in world terms round the face (the head's own axes vary by model), then kept on the bone
        Vector3 centre = player.HeadCentre;
        float r = player.HeadRadius;
        Vector3 fwd = player.transform.forward, right = player.transform.right, up = player.transform.up;
        var rng = new System.Random(player.ActorNumber * 31 + Time.frameCount);
        for (int i = 0; i < 7; i++)
        {
            float x = (float)(rng.NextDouble() * 2 - 1) * 0.7f, y = (float)(rng.NextDouble() * 2 - 1) * 0.55f;
            Vector3 dir = (fwd * 0.9f + right * x + up * y).normalized;
            var blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(blob.GetComponent<Collider>());
            blob.name = "Splat";
            blob.transform.SetParent(root, true);
            blob.transform.position = centre + dir * r * 0.92f;
            blob.transform.rotation = Quaternion.LookRotation(dir);
            float size = r * (0.35f + (float)rng.NextDouble() * 0.35f);
            blob.transform.localScale = Vector3.one;   // Set in world terms below
            blob.transform.localScale = WorldToLocalScale(blob.transform, new Vector3(size, size * 0.8f, size * 0.25f));
            var rend = blob.GetComponent<Renderer>();
            rend.sharedMaterial = inkMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Destroy(root.gameObject, seconds);
    }

    private static Material inkMaterial;   // Shared by every player's splats

    private static Vector3 WorldToLocalScale(Transform t, Vector3 world)
    {
        Vector3 parent = t.parent != null ? t.parent.lossyScale : Vector3.one;
        return new Vector3(world.x / Mathf.Max(1e-4f, parent.x), world.y / Mathf.Max(1e-4f, parent.y), world.z / Mathf.Max(1e-4f, parent.z));
    }

    /// <summary>
    /// Any client: removes a placed piece of utility (a trap ability's object), asking its owner (only they can destroy
    /// it). The Bulldozer runs them over.
    /// </summary>
    public static void DestroyUtility(PhotonView utility)
    {
        if (utility == null) return;
        var owner = Player.Find(utility.OwnerActorNr);
        if (owner == null || !owner.TryGetComponent(out TacticalNet net)) return;
        net.photonView.RPC(nameof(RPC_TDestroy), utility.Owner, utility.ViewID);
    }

    [PunRPC]
    private void RPC_TDestroy(int viewId)
    {
        var view = PhotonView.Find(viewId);
        if (view == null || !view.IsMine) return;
        if (view.TryGetComponent(out TripWireObject wire)) wire.NotifyBroken("smashed");
        photonView.RPC(nameof(RPC_TSmashed), RpcTarget.All, view.transform.position);
        PhotonNetwork.Destroy(view.gameObject);
    }

    [PunRPC]
    private void RPC_TSmashed(Vector3 at)
    {
        Sfx.At(SfxKind.Snap, at, 1f, 40f, 0.7f);
        Vfx.Burst(at + Vector3.up * 0.3f, new Color(0.85f, 0.85f, 0.9f), 26, 6f, 0.25f, 0.6f);
    }

    /// <summary>
    /// A placed trap object near <paramref name="at"/> (within <paramref name="radius"/>): any networked ability object
    /// spawned by a Trap-slot ability.
    /// </summary>
    public static void FindUtility(Vector3 at, float radius, System.Collections.Generic.HashSet<PhotonView> into)
    {
        foreach (var col in Physics.OverlapSphere(at, radius, Physics.AllLayers, QueryTriggerInteraction.Collide))
        {
            var view = col.GetComponentInParent<PhotonView>();
            if (view == null || view.GetComponent<Player>() != null || into.Contains(view)) continue;
            var data = view.InstantiationData;
            if (data == null || data.Length < 3 || data[2] is not int) continue; // Not an ability object
            string id = view.gameObject.name.Replace("(Clone)", "").Trim();
            if (AbilityCatalog.SlotOf(id) == AbilitySlot.Trap) into.Add(view);
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
