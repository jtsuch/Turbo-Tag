using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A named place on the map ("A Main", "Mid", "B Site"): the minimap writes the name over it so players learn the
/// callouts. Placed by level builders (FacilityBuilder) over each room; the box is the area it names.
/// </summary>
public class MapCallout : MonoBehaviour
{
    public string label = "Callout";
    [Tooltip("Write the name over the map (off: the area still counts as this place, e.g. a main's other rooms).")]
    public bool showLabel = true;
    [Tooltip("The area this name covers (centred on the object).")]
    public Vector3 size = new(16f, 10f, 16f);

    private static readonly List<MapCallout> all = new();
    public static IReadOnlyList<MapCallout> All => all;

    private void OnEnable() => all.Add(this);
    private void OnDisable() => all.Remove(this);

    /// <summary>Whether <paramref name="point"/> is inside this callout's area.</summary>
    public bool Contains(Vector3 point)
    {
        Vector3 d = point - transform.position;
        return Mathf.Abs(d.x) <= size.x * 0.5f && Mathf.Abs(d.z) <= size.z * 0.5f && d.y >= -1f && d.y <= size.y;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * size.y * 0.5f, size);
    }
}
