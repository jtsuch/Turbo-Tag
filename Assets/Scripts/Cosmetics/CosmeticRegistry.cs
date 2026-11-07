using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every cosmetic in the game, in Wardrobe order (Resources/Cosmetics/CosmeticRegistry.asset). Kept up to date by
/// the BuildCosmetics job (it makes a <see cref="CosmeticDefinition"/> per item); hand-made definitions can be
/// added to the list too.
/// </summary>
[CreateAssetMenu(menuName = "Turbo Tag/Cosmetic Registry", fileName = "CosmeticRegistry")]
public class CosmeticRegistry : ScriptableObject
{
    public const string ResourcePath = "Cosmetics/CosmeticRegistry";

    public List<CosmeticDefinition> items = new();

    private static CosmeticRegistry instance;

    public static CosmeticRegistry Instance => instance != null ? instance : instance = Resources.Load<CosmeticRegistry>(ResourcePath);

    /// <summary>The definition with this id (null for "" or unknown).</summary>
    public static CosmeticDefinition Find(string id)
    {
        if (string.IsNullOrEmpty(id) || Instance == null) return null;
        foreach (var item in Instance.items) if (item != null && item.id == id) return item;
        return null;
    }

    /// <summary>Items for a slot, in registry order.</summary>
    public static List<CosmeticDefinition> InSlot(CosmeticSlot slot)
    {
        var list = new List<CosmeticDefinition>();
        if (Instance == null) return list;
        foreach (var item in Instance.items) if (item != null && item.slot == slot) list.Add(item);
        return list;
    }
}
