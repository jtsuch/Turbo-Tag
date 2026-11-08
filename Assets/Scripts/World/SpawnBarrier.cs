using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Search &amp; Destroy: a pre-round wall. While a round's pre-round runs (the Countdown phase) it's solid and shows as
/// translucent blue, keeping each team in its spawn area; the rest of the time (and in every other mode) it's gone.
/// Put it on any object with a collider (a scaled cube is easiest: GameObject → 3D Object → Cube, then add this);
/// move, scale and rotate it like any other object. The level's walls should close off each spawn completely, with
/// barriers across every gap. A level with none falls back to an invisible soft wall round each team's spawn points
/// (SearchDestroyController).
/// </summary>
[DisallowMultipleComponent]
public class SpawnBarrier : MonoBehaviour
{
    [Tooltip("Which spawn this holds in (for your reference; every barrier blocks everyone while it's up).")]
    public SpawnPoint.Side side = SpawnPoint.Side.Any;

    private static readonly List<SpawnBarrier> all = new();
    private static Material material;

    /// <summary>The barriers in the loaded level.</summary>
    public static IReadOnlyList<SpawnBarrier> All => all;

    private Collider[] colliders;
    private Renderer[] renderers;
    private bool up = true;

    /// <summary>The see-through blue barriers wear (built once; the editor's look comes from the material on the object).</summary>
    public static Material Material
    {
        get
        {
            if (material == null)
                material = new Material(Shader.Find("Sprites/Default")) { name = "Spawn Barrier", color = new Color(0.25f, 0.62f, 1f, 0.28f) };
            return material;
        }
    }

    private void Awake()
    {
        gameObject.layer = 0; // Default: blocks players and shots, but nothing wall-runs, climbs or grapples on it
        colliders = GetComponentsInChildren<Collider>(true);
        renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers) r.sharedMaterial = Material;
        Set(false);
    }

    private void OnEnable()  => all.Add(this);
    private void OnDisable() => all.Remove(this);

    private void Update()
    {
        var manager = GameModeManager.Instance;
        Set(manager != null && manager.Mode is SearchDestroyMode && manager.Phase == MatchPhase.Countdown);
    }

    private void Set(bool on)
    {
        if (up == on) return;
        up = on;
        foreach (var c in colliders) if (c != null) c.enabled = on;
        foreach (var r in renderers) if (r != null) r.enabled = on;
    }

    private void OnDrawGizmos()
    {
        if (Application.isPlaying) return;
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.25f, 0.62f, 1f, 0.9f);
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
