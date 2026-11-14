using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns gun models (<c>Assets/Models/Gun-{Id}.fbx</c>) into gun prefabs (<c>Assets/Resources/Guns/{Id}.prefab</c>) that
/// PlayerGun can hold: the root faces along +Z (muzzle forward, grip down), is 1 m long (PlayerGun scales it per
/// view), its pivot sits on the barrel line above the magazine, and a <c>GunTip</c> child marks the muzzle.
/// The barrel axis is the model's longest direction; the muzzle is the thinner end. A separate child named
/// "Magazine" is animated on reload. Rebuilds every gun each run. Job "BuildGunPrefabs", menu Turbo Tag/Build Gun Prefabs.
/// </summary>
public static class GunPrefabBuilder
{
    private const string ModelFolder  = "Assets/Models";
    private const string OutputFolder = "Assets/Resources/Guns";

    [MenuItem("Turbo Tag/Build Gun Prefabs")]
    public static void Build()
    {
        Directory.CreateDirectory(OutputFolder);
        var log = new StringBuilder();
        foreach (string path in Directory.GetFiles(ModelFolder, "Gun-*.fbx", SearchOption.AllDirectories))
        {
            string id = Path.GetFileNameWithoutExtension(path).Substring(4);
            log.AppendLine(BuildOne(path.Replace('\\', '/'), id));
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[GunPrefabBuilder]\n" + log);
    }

    private static string BuildOne(string modelPath, string id)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (asset == null) return $"! {modelPath} did not load";

        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        model.transform.SetPositionAndRotation(Vector3.zero, asset.transform.rotation);

        // Every vertex in world space
        var points = new List<Vector3>();
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            foreach (var v in filter.sharedMesh.vertices) points.Add(filter.transform.TransformPoint(v));
        }
        if (points.Count < 3) { Object.DestroyImmediate(model); return $"! {id}: no mesh"; }

        Vector3 mean = Vector3.zero;
        foreach (var p in points) mean += p;
        mean /= points.Count;

        // A principal axis far off the model's own axes is the grip, not the barrel (the Flurry's long grip and
        // magazine tilted it ~42°, pointing the gun at the floor): snap those to the nearest axis. Smaller tilts
        // are left as they were (the AR's ~27° has been played with since the start)
        Vector3 axis = PrincipalAxis(points, mean);
        Vector3 snapped = SnapToAxis(axis);
        if (Vector3.Angle(axis, snapped) > 35f) axis = snapped;
        Vector3 up   = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
        if (up.sqrMagnitude < 0.5f) up = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;

        // The muzzle is the thinner end (barrel vs stock)
        Range(points, mean, axis, out float min, out float max);
        float length = max - min;
        if (EndSpread(points, mean, axis, max, length) > EndSpread(points, mean, axis, min, length))
        {
            axis = -axis;
            Range(points, mean, axis, out min, out max);
        }

        // Muzzle point: middle of the vertices at the very front
        Vector3 muzzle = Vector3.zero; int n = 0;
        foreach (var p in points)
            if (Vector3.Dot(p - mean, axis) >= max - length * 0.01f) { muzzle += p; n++; }
        muzzle /= Mathf.Max(1, n);

        // Pivot: on the barrel line above the magazine
        Transform magazine = Find(model.transform, "Magazine");
        // (no magazine, e.g. a pump shotgun: near the trigger, 38% of the length from the back)
        Vector3 pivot = magazine != null && magazine.TryGetComponent(out Renderer magRenderer)
            ? mean + axis * Vector3.Dot(magRenderer.bounds.center - mean, axis)
            : mean + axis * (min + length * 0.38f);

        // Root frame: forward = axis, up = grip-up; the model goes under it, scaled to 1 m
        Quaternion frame = Quaternion.LookRotation(axis, up);
        Quaternion toLocal = Quaternion.Inverse(frame);

        var root = new GameObject(id);
        model.transform.SetParent(root.transform, false);
        model.name = "Model";
        model.transform.localRotation = toLocal * asset.transform.rotation;
        model.transform.localPosition = toLocal * (Vector3.zero - pivot) / length;
        model.transform.localScale    = asset.transform.localScale / length;

        var tip = new GameObject("GunTip").transform;
        tip.SetParent(root.transform, false);
        tip.localPosition = toLocal * (muzzle - pivot) / length;

        foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        string outPath = $"{OutputFolder}/{id}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, outPath);
        string result = $"{id}: {outPath}, model length {length:0.###}, axis {axis}, magazine {(magazine != null ? "yes" : "no")}, tip {tip.localPosition}";
        Object.DestroyImmediate(root);
        return result;
    }

    private static Vector3 SnapToAxis(Vector3 v)
    {
        Vector3 a = new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
        if (a.y >= a.z) return new Vector3(0f, Mathf.Sign(v.y), 0f);
        return new Vector3(0f, 0f, Mathf.Sign(v.z));
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static void Range(List<Vector3> points, Vector3 mean, Vector3 axis, out float min, out float max)
    {
        min = float.MaxValue; max = float.MinValue;
        foreach (var p in points)
        {
            float d = Vector3.Dot(p - mean, axis);
            min = Mathf.Min(min, d); max = Mathf.Max(max, d);
        }
    }

    // How wide the model is within the last 10% of its length at one end
    private static float EndSpread(List<Vector3> points, Vector3 mean, Vector3 axis, float end, float length)
    {
        float spread = 0f;
        foreach (var p in points)
        {
            Vector3 rel = p - mean;
            float d = Vector3.Dot(rel, axis);
            if (Mathf.Abs(d - end) > length * 0.1f) continue;
            spread = Mathf.Max(spread, Vector3.ProjectOnPlane(rel, axis).magnitude);
        }
        return spread;
    }

    // Longest direction of the point cloud (power iteration on the covariance)
    private static Vector3 PrincipalAxis(List<Vector3> points, Vector3 mean)
    {
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in points)
        {
            Vector3 d = p - mean;
            xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
            yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
        }
        Vector3 v = new(0.3f, 0.5f, 1f);
        for (int i = 0; i < 50; i++)
        {
            v = new Vector3(xx * v.x + xy * v.y + xz * v.z,
                            xy * v.x + yy * v.y + yz * v.z,
                            xz * v.x + yz * v.y + zz * v.z).normalized;
        }
        return v;
    }
}
