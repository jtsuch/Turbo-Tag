using UnityEngine;

/// <summary>
/// Where a cosmetic is worn, one per slot at a time: Head gear (hats, glasses, ears, masks), Body gear (on the chest
/// or back: wings, scarves, backpacks) and full Outfits (onesies). Wardrobe order is top to bottom.
/// </summary>
public enum CosmeticSlot { Head, Body, Outfit }

/// <summary>
/// Rigid: a prefab placed on the body (hats, backpacks). Skinned: a mesh weighted to Jimmy's skeleton (outfits),
/// rebound to his bones by name so it follows every animation.
/// </summary>
public enum CosmeticKind { Rigid, Skinned }

/// <summary>
/// One cosmetic (see Docs/CosmeticsPipeline.md): id (synced over Photon), display name, slot, kind, the prefab or
/// outfit FBX, the Wardrobe picture, and for rigid items an optional bone to ride on (empty: the slot's own spot,
/// e.g. the top of the head for Head). Listed in the <see cref="CosmeticRegistry"/>.
/// </summary>
[CreateAssetMenu(menuName = "Turbo Tag/Cosmetic Definition", fileName = "Cosmetic")]
public class CosmeticDefinition : ScriptableObject
{
    [Tooltip("Unique id, synced in player properties. Never rename once players own it.")]
    public string id;
    public string displayName;
    public CosmeticSlot slot;
    public CosmeticKind kind;
    [Tooltip("Rigid: the prefab. Skinned: the outfit FBX (same armature as Jimmy).")]
    public GameObject prefab;
    [Tooltip("Picture for the Wardrobe.")]
    public Sprite icon;
    [Tooltip("Rigid only: a bone of Jimmy's to follow (e.g. \"Head\"). Empty: the slot's usual spot.")]
    public string socket;
    [Tooltip("Worn on the back: the Wardrobe turns Jimmy round to show it.")]
    public bool backFacing;
}
