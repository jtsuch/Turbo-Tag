using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Discovers every concrete Ability class by reflection, so new abilities appear in Pregame and
/// the Cheats lists automatically. Also checks that an ability is usable (its component is on the
/// player prefab and any object prefab it spawns exists), so unfinished abilities are hidden
/// instead of failing at runtime. Names and descriptions come from [AbilityInfo].
/// </summary>
public static class AbilityCatalog
{
    private static Dictionary<string, Type> types;
    private static readonly Dictionary<string, bool> availability = new();
    private const string PlayerPrefabPath = "Player/ThePlayer";

    private static Dictionary<string, Type> Types
    {
        get
        {
            if (types != null) return types;

            types = new Dictionary<string, Type>();
            foreach (var type in typeof(Ability).Assembly.GetTypes())
                if (!type.IsAbstract && typeof(Ability).IsAssignableFrom(type))
                    types[type.Name] = type;
            return types;
        }
    }

    public static bool Exists(string id) => !string.IsNullOrEmpty(id) && Types.ContainsKey(id);

    /// <summary>Player-facing name: [AbilityInfo] DisplayName, else the class name spaced out ("ZipGrapple" → "Zip Grapple").</summary>
    public static string DisplayName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var info = Info(id);
        if (!string.IsNullOrEmpty(info?.DisplayName)) return info.DisplayName;

        var sb = new System.Text.StringBuilder(id.Length + 4);
        for (int i = 0; i < id.Length; i++)
        {
            if (i > 0 && char.IsUpper(id[i]) && !char.IsUpper(id[i - 1])) sb.Append(' ');
            sb.Append(id[i]);
        }
        return sb.ToString();
    }

    /// <summary>Player-facing description from [AbilityInfo] ("" when none).</summary>
    public static string Description(string id) => Info(id)?.Description ?? "";

    private static AbilityInfoAttribute Info(string id) =>
        Exists(id) ? (AbilityInfoAttribute)Attribute.GetCustomAttribute(Types[id], typeof(AbilityInfoAttribute)) : null;

    public static AbilitySlot? SlotOf(string id)
    {
        if (!Exists(id)) return null;
        var type = Types[id];
        if (typeof(BasicAbility).IsAssignableFrom(type)) return AbilitySlot.Basic;
        if (typeof(QuickAbility).IsAssignableFrom(type)) return AbilitySlot.Quick;
        if (typeof(ThrowAbility).IsAssignableFrom(type)) return AbilitySlot.Throw;
        if (typeof(TrapAbility).IsAssignableFrom(type))  return AbilitySlot.Trap;
        return null;
    }

    /// <summary>Ability ids for a slot, alphabetically.</summary>
    public static List<string> InSlot(AbilitySlot slot)
    {
        var ids = new List<string>();
        foreach (var id in Types.Keys)
            if (SlotOf(id) == slot) ids.Add(id);
        ids.Sort(string.CompareOrdinal);
        return ids;
    }

    /// <summary>
    /// False when the ability's component is missing from the player prefab, or a throw/trap ability
    /// has no usable networked prefab at Resources/Object/{id}. Throwables additionally need a
    /// Rigidbody and a NetworkedProjectile component to launch on every client.
    /// </summary>
    public static bool IsAvailable(string id)
    {
        if (!Exists(id)) return false;
        if (availability.TryGetValue(id, out bool cached)) return cached;

        var playerPrefab = Resources.Load<GameObject>(PlayerPrefabPath);
        bool available = playerPrefab == null || playerPrefab.GetComponent(Types[id]) != null;
        if (!available)
            Debug.LogWarning($"[AbilityCatalog] '{id}' is hidden: it is not a component on Resources/{PlayerPrefabPath}.");

        var slot = SlotOf(id);
        if (available && (slot == AbilitySlot.Throw || slot == AbilitySlot.Trap))
        {
            var prefab = Resources.Load<GameObject>(Ability.ObjectResourceFolder + id);
            available = prefab != null && prefab.GetComponent<PhotonView>() != null
                        && (slot != AbilitySlot.Throw
                            || (prefab.GetComponent<NetworkedProjectile>() != null && prefab.GetComponent<Rigidbody>() != null));

            if (!available)
                Debug.LogWarning($"[AbilityCatalog] '{id}' is hidden: Resources/{Ability.ObjectResourceFolder}{id} " +
                                 "is missing or lacks a PhotonView" + (slot == AbilitySlot.Throw ? ", Rigidbody or NetworkedProjectile." : "."));
        }

        availability[id] = available;
        return available;
    }
}

/// <summary>
/// A player's four chosen abilities, stored in their custom properties so every client can
/// read anyone's loadout. The last choices are remembered locally for the next Pregame.
/// </summary>
public readonly struct AbilityLoadout
{
    private static readonly string[] defaults = { "SwingGrapple", "Dash", "BoomStick", "Box" };
    private const string LastChoicePrefsPrefix = "LastAbility_";

    private readonly string[] ids;

    private AbilityLoadout(string[] ids) => this.ids = ids;

    public string this[AbilitySlot slot] => ids?[(int)slot] ?? Default(slot);

    /// <summary>The slot's default pick, or the top ability in the slot's list if the default can't be used.</summary>
    public static string Default(AbilitySlot slot)
    {
        string preferred = defaults[(int)slot];
        if (AbilityCatalog.IsAvailable(preferred)) return preferred;
        foreach (string id in AbilityCatalog.InSlot(slot))
            if (AbilityCatalog.IsAvailable(id)) return id;
        return preferred;
    }

    /// <summary>
    /// Local player: fills any slot without a usable pick from the last choices (then the default).
    /// Pregame sets every slot itself, but modes that skip it (Freeplay) rely on this.
    /// </summary>
    public static void EnsureLocal()
    {
        var props = PhotonNetwork.LocalPlayer?.CustomProperties;
        foreach (AbilitySlot slot in Enum.GetValues(typeof(AbilitySlot)))
        {
            string current = props != null && props.TryGetValue(PropertyKey(slot), out object value) ? value as string : null;
            if (AbilityCatalog.SlotOf(current) == slot && AbilityCatalog.IsAvailable(current)) continue;
            SetLocal(slot, LastChoice(slot));
        }
    }

    public static string PropertyKey(AbilitySlot slot) => slot switch
    {
        AbilitySlot.Basic => PlayerKeys.AbilityBasic,
        AbilitySlot.Quick => PlayerKeys.AbilityQuick,
        AbilitySlot.Throw => PlayerKeys.AbilityThrow,
        _                 => PlayerKeys.AbilityTrap,
    };

    public static bool IsLoadoutKey(string key) =>
        key == PlayerKeys.AbilityBasic || key == PlayerKeys.AbilityQuick
        || key == PlayerKeys.AbilityThrow || key == PlayerKeys.AbilityTrap;

    /// <summary>Reads a player's loadout, substituting defaults for missing or unknown entries.</summary>
    public static AbilityLoadout Of(PhotonPlayer player)
    {
        var ids = new string[4];
        for (int i = 0; i < ids.Length; i++)
        {
            var slot = (AbilitySlot)i;
            string id = NetProps.Of(player, PropertyKey(slot), Default(slot));
            ids[i] = AbilityCatalog.SlotOf(id) == slot ? id : Default(slot);
        }
        return new AbilityLoadout(ids);
    }

    /// <summary>Equips <paramref name="id"/> for the local player and remembers the choice.</summary>
    public static void SetLocal(AbilitySlot slot, string id)
    {
        if (AbilityCatalog.SlotOf(id) != slot) return;
        NetProps.SetLocal(PropertyKey(slot), id);
        PlayerPrefs.SetString(LastChoicePrefsPrefix + slot, id);
    }

    /// <summary>The ability this client picked for <paramref name="slot"/> last time.</summary>
    public static string LastChoice(AbilitySlot slot)
    {
        string id = Renamed(PlayerPrefs.GetString(LastChoicePrefsPrefix + slot, Default(slot)));
        return AbilityCatalog.SlotOf(id) == slot && AbilityCatalog.IsAvailable(id) ? id : Default(slot);
    }

    // Abilities that were renamed: a saved choice of the old name picks the new one
    private static string Renamed(string id) => id switch
    {
        "BasicGrapple" => "SwingGrapple",
        "StiffGrapple" => "ZipGrapple",
        _              => id,
    };
}
