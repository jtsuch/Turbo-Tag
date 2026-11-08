using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A place to (re)spawn in modes that use spawn points (Free for All). Position is the feet, forward is
/// the facing. The Spawner picks one away from other players; at the start of a match everyone gets a
/// different one. Place several around a level, on safe ground (not on risky outer jumps).
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    /// <summary>Search &amp; Destroy: which end this point belongs to (Any: used by every mode, sides picked by distance from the sites).</summary>
    public enum Side { Any, Attack, Defend }

    [Tooltip("Search & Destroy: the attackers' or defenders' spawn. Other modes ignore it.")]
    public Side side = Side.Any;

    private static readonly List<SpawnPoint> all = new();
    public static IReadOnlyList<SpawnPoint> All => all;

    private void OnEnable()  => all.Add(this);
    private void OnDisable() => all.Remove(this);

    private void OnDrawGizmos()
    {
        Gizmos.color = side == Side.Attack ? new Color(1f, 0.3f, 0.3f, 0.9f)
                     : side == Side.Defend ? new Color(0.3f, 0.6f, 1f, 0.9f) : new Color(1f, 0.55f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.8f);
        Gizmos.DrawLine(transform.position + Vector3.up * 0.9f, transform.position + Vector3.up * 0.9f + transform.forward);
    }
}
