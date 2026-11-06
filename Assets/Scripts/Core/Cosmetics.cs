using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Cosmetics: one item per slot (a Hat on the head, something on the Back), worn by the player's penguin on every
/// screen, in the menu preview and on the scoreboard podium. Each item is a prefab at Resources/Cosmetics/{Id}
/// (made from Justin's FBX by the BuildCosmetics job: pivot at the hat's base / the plate's centre, facing +Z,
/// real size). A player's picks are the <see cref="PlayerKeys.CosmeticHat"/> / <see cref="PlayerKeys.CosmeticBack"/>
/// properties (synced), remembered locally. Chosen on the main menu player card and in the Lobby.
/// <b>Add one:</b> an FBX named Cosmetic-{Name}.fbx in Assets/Models/Cosmetics, an entry in BuildCosmetics' list,
/// run the job, and an entry in <see cref="All"/>.
/// </summary>
public static class Cosmetics
{
    public enum Slot { Hat, Back }

    public readonly struct Item
    {
        public readonly string Id, Name;
        public readonly Slot   Slot;
        public Item(string id, string name, Slot slot) { Id = id; Name = name; Slot = slot; }
    }

    public const string None = "";

    public static readonly Item[] All =
    {
        new("Tophat",       "Top Hat",       Slot.Hat),
        new("PartyHat",     "Party Hat",     Slot.Hat),
        new("PropellerCap", "Propeller Cap", Slot.Hat),
        new("VikingHelmet", "Viking Helmet", Slot.Hat),
        new("ChefHat",      "Chef Hat",      Slot.Hat),
        new("LicensePlate", "LAGGING Plate", Slot.Back),
        new("Backpack",     "Backpack",      Slot.Back),
        new("Jetpack",      "Jetpack",       Slot.Back),
    };

    // Placement on a penguin (metres, × body size)
    private const float HatSink   = 0.2f;  // Below the top of the body: the hat sits down over the head
    private const float BackDepth = 0.72f; // Behind the chest bone
    private const float BackDrop  = 0.2f;  // Below the chest bone: the back anchor is the penguin's rear

    public static string PropertyKey(Slot slot) => slot == Slot.Hat ? PlayerKeys.CosmeticHat : PlayerKeys.CosmeticBack;

    private static string PrefsKey(Slot slot) => "Cosmetic_" + slot;

    /// <summary>Items for a slot, in picker order.</summary>
    public static System.Collections.Generic.List<Item> InSlot(Slot slot)
    {
        var list = new System.Collections.Generic.List<Item>();
        foreach (var item in All) if (item.Slot == slot) list.Add(item);
        return list;
    }

    public static GameObject Prefab(string id) => string.IsNullOrEmpty(id) ? null : Resources.Load<GameObject>("Cosmetics/" + id);

    public static string Of(PhotonPlayer player, Slot slot) => player == null ? None : NetProps.Of(player, PropertyKey(slot), None);

    public static string Saved(Slot slot) => PlayerPrefs.GetString(PrefsKey(slot), None);

    /// <summary>Local player: wears <paramref name="id"/> ("" for nothing) and remembers it.</summary>
    public static void SetLocal(Slot slot, string id)
    {
        PlayerPrefs.SetString(PrefsKey(slot), id ?? None);
        if (PhotonNetwork.InRoom) NetProps.SetLocal(PropertyKey(slot), id ?? None);
    }

    /// <summary>Local player: sends the remembered picks if this room doesn't have them yet (call on joining).</summary>
    public static void EnsureLocal()
    {
        if (!PhotonNetwork.InRoom) return;
        var props = PhotonNetwork.LocalPlayer?.CustomProperties;
        foreach (Slot slot in System.Enum.GetValues(typeof(Slot)))
            if (props != null && !props.ContainsKey(PropertyKey(slot))) NetProps.SetLocal(PropertyKey(slot), Saved(slot));
    }

    /// <summary>
    /// Puts a worn item in place for this frame: the hat on top of the head (level with <paramref name="topY"/>,
    /// centred on the head bone, facing the body's forward), the back item behind the chest facing backward.
    /// </summary>
    public static void Place(Transform item, Slot slot, Transform body, Transform head, Transform chest, float topY, float size)
    {
        Vector3 forward = Vector3.ProjectOnPlane(body.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        forward.Normalize();
        if (slot == Slot.Hat)
        {
            Vector3 h = head != null ? head.position : body.position;
            item.SetPositionAndRotation(new Vector3(h.x, topY - HatSink * size, h.z), Quaternion.LookRotation(forward, Vector3.up));
        }
        else
        {
            Vector3 c = chest != null ? chest.position : body.position + Vector3.up * 1.4f * size;
            item.SetPositionAndRotation(c - forward * BackDepth * size - Vector3.up * BackDrop * size, Quaternion.LookRotation(-forward, Vector3.up));
        }
        item.localScale = Vector3.one * size;
    }

    /// <summary>The Head and Chest bones under <paramref name="root"/> (either may be null).</summary>
    public static void FindBones(Transform root, out Transform head, out Transform chest)
    {
        head = chest = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (head == null && t.name == "Head") head = t;
            if (chest == null && t.name == "Chest") chest = t;
        }
    }
}
