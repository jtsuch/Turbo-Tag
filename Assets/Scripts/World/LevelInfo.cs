using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Facts about the loaded level, worked out once per scene: <see cref="Bounds"/> is everything walkable (the
/// structure layer), minus death planes and terrain-sized ground. Used by donut placement and the countdown fly-over.
/// </summary>
public static class LevelInfo
{
    public const int StructureLayer = 6;

    private static Bounds? bounds;
    private static int     sceneHandle = -1;

    /// <summary>The walkable level's bounds, or null if it has no structure-layer colliders.</summary>
    public static Bounds? Bounds
    {
        get
        {
            int handle = SceneManager.GetActiveScene().handle;
            if (handle != sceneHandle) { sceneHandle = handle; bounds = Compute(); }
            return bounds;
        }
    }

    private static Bounds? Compute()
    {
        Bounds? total = null;
        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (col.gameObject.layer != StructureLayer || col.isTrigger || !col.enabled) continue;
            if (col.GetComponentInParent<DeathZone>() != null) continue;
            var size = col.bounds.size;
            if (size.x * size.z > 150000f) continue; // Terrain-sized ground (Quads' 1 km terrain) isn't the play area
            if (total.HasValue) { var t = total.Value; t.Encapsulate(col.bounds); total = t; }
            else total = col.bounds;
        }
        return total;
    }
}
