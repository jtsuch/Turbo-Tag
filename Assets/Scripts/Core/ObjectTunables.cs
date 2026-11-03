using System.Collections.Generic;
using System.Reflection;
using Photon.Pun;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Cheat tuning for the objects abilities spawn (bomb fuse, GravBall pull, trap hold time…).
/// Mark a float field on an ability object's script with [TunableParam] and it gets a Cheats slider
/// under its ability, with no UI code:
///   Ability (thrower's machine) keeps the slider values → only changed ones travel in SpawnData
///   → <see cref="Apply"/> writes them onto the spawned object on every client before it runs.
/// Keys are "{ComponentType}.{field}".
/// </summary>
public static class ObjectTunables
{
    /// <summary>One tunable field on a spawned object's prefab.</summary>
    public readonly struct Entry
    {
        public readonly string                Key;
        public readonly FieldInfo             Field;
        public readonly TunableParamAttribute Attribute;
        public readonly float                 Default;

        public Entry(string key, FieldInfo field, TunableParamAttribute attribute, float value)
        {
            Key = key; Field = field; Attribute = attribute; Default = value;
        }
    }

    /// <summary>Every [TunableParam] field on the scripts of <paramref name="prefab"/> (root and children).</summary>
    public static List<Entry> Discover(GameObject prefab)
    {
        var result = new List<Entry>();
        if (prefab == null) return result;

        foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            var type = component.GetType();
            foreach (var (field, attr) in TunableParamAttribute.FieldsOf(type))
                result.Add(new Entry($"{type.Name}.{field.Name}", field, attr, (float)field.GetValue(component)));
        }
        return result;
    }

    /// <summary>
    /// Writes the spawn's cheat values onto the object's scripts. Call from the object's Awake
    /// (PUN sets instantiation data before the object wakes up).
    /// </summary>
    public static void Apply(PhotonView view)
    {
        Hashtable values = SpawnData.Tunables(view);
        if (values == null || values.Count == 0) return;

        foreach (var component in view.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            var type = component.GetType();
            foreach (var (field, _) in TunableParamAttribute.FieldsOf(type))
                if (values.TryGetValue($"{type.Name}.{field.Name}", out object value) && value is float f)
                    field.SetValue(component, f);
        }
    }
}
