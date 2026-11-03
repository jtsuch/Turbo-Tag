using UnityEngine;

/// <summary>
/// Creates purely visual, local-only copies of prefabs (held throwables, trap holograms).
/// The prefab is instantiated under an inactive parent so no Awake/Start runs, then every
/// script, rigidbody and audio source is stripped before it becomes active. Colliders are
/// removed, or kept disabled when the caller needs their shape (e.g. placement checks).
/// </summary>
public static class VisualProxy
{
    private static Transform stagingRoot;

    public static GameObject Create(GameObject prefab, Transform parent, bool keepDisabledColliders = false)
    {
        if (prefab == null) return null;

        var copy = Object.Instantiate(prefab, StagingRoot);
        copy.name = prefab.name + " (Visual)";

        // Behaviours first. Scripts may [RequireComponent] others (e.g. PhotonView), which blocks
        // removal, so strip in reverse order and repeat until nothing is left.
        for (int pass = 0; pass < 4; pass++)
        {
            var behaviours = copy.GetComponentsInChildren<MonoBehaviour>(true);
            if (behaviours.Length == 0) break;
            for (int i = behaviours.Length - 1; i >= 0; i--)
                if (CanRemove(behaviours[i], behaviours)) Object.DestroyImmediate(behaviours[i]);
        }
        foreach (var joint in copy.GetComponentsInChildren<Joint>(true))
            Object.DestroyImmediate(joint);
        foreach (var col in copy.GetComponentsInChildren<Collider>(true))
        {
            if (keepDisabledColliders) col.enabled = false;
            else                       Object.DestroyImmediate(col);
        }
        foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true))
            Object.DestroyImmediate(body);
        foreach (var audio in copy.GetComponentsInChildren<AudioSource>(true))
            Object.DestroyImmediate(audio);

        copy.transform.SetParent(parent, false);
        return copy;
    }

    // A component can be removed once no other remaining behaviour on its GameObject requires it
    private static bool CanRemove(MonoBehaviour candidate, MonoBehaviour[] all)
    {
        if (candidate == null) return false;
        var type = candidate.GetType();
        foreach (var other in all)
        {
            if (other == null || other == candidate || other.gameObject != candidate.gameObject) continue;
            foreach (RequireComponent req in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                if ((req.m_Type0 != null && req.m_Type0.IsAssignableFrom(type))
                    || (req.m_Type1 != null && req.m_Type1.IsAssignableFrom(type))
                    || (req.m_Type2 != null && req.m_Type2.IsAssignableFrom(type)))
                    return false;
            }
        }
        return true;
    }

    /// <summary>Replaces every material on the proxy with <paramref name="material"/> (instanced).</summary>
    public static void ApplyMaterial(GameObject proxy, Material material)
    {
        if (proxy == null || material == null) return;
        foreach (var renderer in proxy.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            renderer.materials = mats;
        }
    }

    public static void SetColor(GameObject proxy, Color color)
    {
        if (proxy == null) return;
        foreach (var renderer in proxy.GetComponentsInChildren<Renderer>())
            foreach (var mat in renderer.materials)
                mat.color = color;
    }

    private static Transform StagingRoot
    {
        get
        {
            if (stagingRoot != null) return stagingRoot;
            var go = new GameObject("[VisualProxyStaging]");
            go.SetActive(false);
            go.hideFlags = HideFlags.HideAndDontSave;
            stagingRoot = go.transform;
            return stagingRoot;
        }
    }
}
