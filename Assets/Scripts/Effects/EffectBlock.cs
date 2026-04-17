using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

/// <summary>
/// A world-space pickup block.  When the local player walks through it the block is
/// consumed, a random effect from <see cref="effectPool"/> is applied to that player,
/// and the block is destroyed for all clients.
///
/// To add a new effect type:
///  1. Add a value to <see cref="EffectType"/>.
///  2. Add the corresponding <c>case</c> in <see cref="CreateEffect"/>.
///  3. The new type is immediately available for the pool in the Inspector.
///
/// Unity setup:
///  - Attach to a prefab that has: BoxCollider (Is Trigger ✓), Rigidbody (Is Kinematic ✓),
///    PhotonView.
///  - Populate effectPool in the Inspector with desired effects, durations, and weights.
///  - Instantiate with PhotonNetwork.Instantiate so it can be destroyed across the network.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EffectBlock : MonoBehaviourPun
{
    // -------------------------------------------------------------------------
    // Effect registry
    // -------------------------------------------------------------------------

    /// <summary>Add new effect types here and in CreateEffect() below.</summary>
    public enum EffectType
    {
        DoubleJump,
        ShrinkFOV,
        Adrenaline,
        DropFrameRate,
        KeybindSwitch,
        CenterOfImpulse,
    }

    [System.Serializable]
    public class EffectEntry
    {
        public EffectType type;
        public float      duration = 60f;
        [Tooltip("Relative pick probability.  Higher = more likely.")]
        [Range(0.01f, 10f)]
        public float      weight   = 1f;
    }

    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    [Header("Effect Pool")]
    [SerializeField] private List<EffectEntry> effectPool = new()
    {
        new() { type = EffectType.DoubleJump, duration = 60f, weight = 1f },
        new() { type = EffectType.ShrinkFOV,  duration = 20f, weight = 1f },
        new() { type = EffectType.Adrenaline, duration = 15f, weight = 1f },
    };

    [Header("VFX / SFX")]
    [SerializeField] private GameObject consumeVFX;
    [SerializeField] private AudioClip  consumeSFX;

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private bool consumed = false;

    // -------------------------------------------------------------------------
    // Trigger
    // -------------------------------------------------------------------------

    private void OnTriggerEnter(Collider other)
    {
        if (consumed) return;
        if (!other.TryGetComponent(out Player player)) return;
        if (!player.IsLocalPlayer) return;

        consumed = true;

        EffectEntry entry = PickWeightedRandom();
        if (entry != null) ApplyEffect(entry, player);

        // Tell all clients to remove the block
        photonView.RPC(nameof(RPC_Consume), RpcTarget.All);
    }

    // -------------------------------------------------------------------------
    // Effect application
    // -------------------------------------------------------------------------

    private void ApplyEffect(EffectEntry entry, Player player)
    {
        System.Type effectType = GetEffectSystemType(entry.type);
        if (effectType == null)
        {
            Debug.LogWarning($"[EffectBlock] No type found for {entry.type}");
            return;
        }

        // If the player already has this effect, cancel it cleanly before reapplying
        var existing = player.GetComponent(effectType) as PlayerEffect;
        if (existing != null) existing.CancelEffect();

        PlayerEffect effect = player.gameObject.AddComponent(effectType) as PlayerEffect;
        effect.Initialize(player, entry.duration);
    }

    // Maps enum → concrete type.  Add a case here whenever a new EffectType is added.
    private static System.Type GetEffectSystemType(EffectType type) => type switch
    {
        EffectType.DoubleJump      => typeof(DoubleJumpEffect),
        EffectType.ShrinkFOV       => typeof(ShrinkFOVEffect),
        EffectType.Adrenaline      => typeof(AdrenalineEffect),
        EffectType.DropFrameRate   => typeof(DropFrameRateEffect),
        EffectType.KeybindSwitch   => typeof(KeybindSwitchEffect),
        EffectType.CenterOfImpulse => typeof(CenterOfImpulseEffect),
        _                          => null,
    };

    // -------------------------------------------------------------------------
    // RPC
    // -------------------------------------------------------------------------

    [PunRPC]
    private void RPC_Consume()
    {
        consumed = true;

        if (consumeVFX != null)
            Instantiate(consumeVFX, transform.position, Quaternion.identity);

        if (consumeSFX != null)
            AudioSource.PlayClipAtPoint(consumeSFX, transform.position);

        // Master client (or owner) destroys the networked object for all clients
        if (photonView.IsMine || PhotonNetwork.IsMasterClient)
            PhotonNetwork.Destroy(gameObject);
        else
            gameObject.SetActive(false);   // Fallback: hide until the destroy message arrives
    }

    // -------------------------------------------------------------------------
    // Weighted random selection
    // -------------------------------------------------------------------------

    private EffectEntry PickWeightedRandom()
    {
        if (effectPool == null || effectPool.Count == 0) return null;

        float total = 0f;
        foreach (var e in effectPool) total += e.weight;

        float roll = Random.Range(0f, total);
        float cumulative = 0f;
        foreach (var e in effectPool)
        {
            cumulative += e.weight;
            if (roll <= cumulative) return e;
        }

        return effectPool[effectPool.Count - 1]; // Fallback (floating-point edge case)
    }
}
