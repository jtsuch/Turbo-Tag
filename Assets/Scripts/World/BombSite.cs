using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Search &amp; Destroy: a bomb site, the box where attackers may plant (centred on this object, <see cref="size"/>
/// across, from the floor at the object up). Name it with <see cref="letter"/> (A, B…). A level without any gets two
/// made up from its spawn points (<see cref="Zones"/>), so every map plays the mode.
/// </summary>
public class BombSite : MonoBehaviour
{
    [Tooltip("The site's name on the HUD (A, B, ...).")]
    public string letter = "A";

    [Tooltip("Plant zone size in metres (x, height, z), centred on this object; the bottom is this object's height.")]
    public Vector3 size = new(20f, 8f, 20f);

    private static readonly List<BombSite> all = new();

    private void OnEnable()  => all.Add(this);
    private void OnDisable() => all.Remove(this);

    /// <summary>A plant zone (a scene site, or one made up for a level without any).</summary>
    public readonly struct Zone
    {
        public readonly string     Letter;
        public readonly Vector3    Centre; // Floor middle
        public readonly Vector3    Size;
        public readonly Quaternion Rotation;

        public Zone(string letter, Vector3 centre, Vector3 size, Quaternion rotation)
        {
            Letter = letter; Centre = centre; Size = size; Rotation = rotation;
        }

        public bool Contains(Vector3 point)
        {
            Vector3 local = Quaternion.Inverse(Rotation) * (point - Centre);
            return Mathf.Abs(local.x) <= Size.x * 0.5f && Mathf.Abs(local.z) <= Size.z * 0.5f
                   && local.y >= -1f && local.y <= Size.y;
        }

        /// <summary>Where the site's marker floats.</summary>
        public Vector3 Marker => Centre + Vector3.up * 2.5f;
    }

    private static List<Zone> cached;
    private static int cachedFrame = -1;

    /// <summary>
    /// The level's bomb sites. Without any BombSite in the scene: two made up from the spawn points on one end of the
    /// level (the two furthest apart, 14 m across), or one at the Spawner.
    /// </summary>
    public static IReadOnlyList<Zone> Zones
    {
        get
        {
            if (cached != null && cachedFrame == Time.frameCount) return cached;
            cachedFrame = Time.frameCount;
            cached = new List<Zone>();
            foreach (var site in all)
                if (site != null)
                    cached.Add(new Zone(site.letter, site.transform.position, site.size, Quaternion.Euler(0f, site.transform.eulerAngles.y, 0f)));
            if (cached.Count > 0)
            {
                cached.Sort((a, b) => string.CompareOrdinal(a.Letter, b.Letter));
                return cached;
            }

            var points = new List<SpawnPoint>(SpawnPoint.All);
            var fallbackSize = new Vector3(14f, 8f, 14f);
            if (points.Count >= 2)
            {
                points.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                var end = Spawner.TeamHalf(points, PlayerRole.TeamRed);
                SpawnPoint first = end[0], second = end[0];
                float best = -1f;
                foreach (var a in end)
                    foreach (var b in end)
                    {
                        float d = Vector3.Distance(a.transform.position, b.transform.position);
                        if (d > best) { best = d; first = a; second = b; }
                    }
                cached.Add(new Zone("A", first.transform.position, fallbackSize, Quaternion.identity));
                if (second != first && best > 10f) cached.Add(new Zone("B", second.transform.position, fallbackSize, Quaternion.identity));
            }
            else if (Spawner.Instance != null)
                cached.Add(new Zone("A", Spawner.Instance.transform.position, fallbackSize, Quaternion.identity));
            return cached;
        }
    }

    /// <summary>The site containing <paramref name="point"/>, if any.</summary>
    public static bool TryFind(Vector3 point, out Zone zone)
    {
        foreach (var z in Zones)
            if (z.Contains(point)) { zone = z; return true; }
        zone = default;
        return false;
    }

    /// <summary>The middle of all the sites (the defenders' end of the map).</summary>
    public static Vector3 Middle()
    {
        var zones = Zones;
        if (zones.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        foreach (var z in zones) sum += z.Centre;
        return sum / zones.Count;
    }

    private void OnDrawGizmos()
    {
        Gizmos.matrix = Matrix4x4.TRS(transform.position + Vector3.up * size.y * 0.5f, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Vector3.one);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.12f);
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
}
