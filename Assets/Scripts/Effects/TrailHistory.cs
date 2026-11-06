using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where every player has been over the last <see cref="Keep"/> seconds (a point every quarter second), recorded
/// locally all match long so Bloodhound can show recent footprints the moment it's eaten. Lives on the
/// GameModeManager object (added with the DonutManager).
/// </summary>
public class TrailHistory : MonoBehaviour
{
    public const float Keep = 15f;
    private const float Every = 0.25f;

    private static TrailHistory instance;
    private readonly Dictionary<int, List<(float time, Vector3 at)>> trails = new();
    private float next;

    public static TrailHistory Instance => instance;

    private void Awake() => instance = this;

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        if (Time.time < next) return;
        next = Time.time + Every;
        foreach (var p in Player.All)
        {
            if (p == null || p.IsDownAfterTag) continue;
            if (!trails.TryGetValue(p.ActorNumber, out var list)) trails[p.ActorNumber] = list = new List<(float, Vector3)>();
            list.Add((Time.time, p.transform.position));
            while (list.Count > 0 && Time.time - list[0].time > Keep) list.RemoveAt(0);
        }
    }

    /// <summary>The recent points of <paramref name="actor"/>, oldest first (empty if none).</summary>
    public IReadOnlyList<(float time, Vector3 at)> Of(int actor) =>
        trails.TryGetValue(actor, out var list) ? list : (IReadOnlyList<(float, Vector3)>)System.Array.Empty<(float, Vector3)>();
}
