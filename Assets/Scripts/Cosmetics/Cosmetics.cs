using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// The player's cosmetic loadout (see Docs/CosmeticsPipeline.md): one id per <see cref="CosmeticSlot"/>, saved in
/// PlayerPrefs and synced as player properties ("Cos_{Slot}", the player's own write); every screen then wears them
/// locally with a <see cref="CosmeticWearer"/>. Definitions live in the <see cref="CosmeticRegistry"/>. Also holds
/// where rigid items sit on a penguin (<see cref="Place"/>).
/// </summary>
public static class Cosmetics
{
    public const string None = "";

    public static readonly CosmeticSlot[] Slots = (CosmeticSlot[])System.Enum.GetValues(typeof(CosmeticSlot));

    // Rigid placement on a penguin (metres, × body size)
    private const float HatSink   = 0.2f;  // Below the top of the body: the hat sits down over the head
    private const float BackDepth = 0.72f; // Behind the chest bone
    private const float BackDrop  = 0.2f;  // Below the chest bone: the back anchor is the penguin's rear
    private const float StandingTop = 2.9f; // Top of a standing size-1 penguin above its feet (Player.TopY)

    /// <summary>The bone a rigid item rides when its definition names none: the head for Head gear, else the chest.</summary>
    public static string AnchorBone(CosmeticSlot slot) => slot == CosmeticSlot.Head ? "Head" : "Chest";

    /// <summary>
    /// Where a rigid item sits on a standing, size-1 penguin, relative to the player root, given its anchor bone's rest
    /// position there: the same spots as <see cref="Place"/> (the wearer turns this into an offset from the bone, so
    /// the item then follows the bone through tilts, prone and every animation).
    /// </summary>
    public static Matrix4x4 RestPlacement(CosmeticSlot slot, Vector3 anchor)
    {
        if (slot == CosmeticSlot.Head)
            return Matrix4x4.TRS(new Vector3(anchor.x, StandingTop - HatSink, anchor.z), Quaternion.identity, Vector3.one);
        return Matrix4x4.TRS(anchor - Vector3.forward * BackDepth - Vector3.up * BackDrop, Quaternion.LookRotation(Vector3.back), Vector3.one);
    }

    public static string PropertyKey(CosmeticSlot slot) => "Cos_" + slot;

    private static string PrefsKey(CosmeticSlot slot) => "Cosmetic_" + slot;

    /// <summary>True for this game's cosmetic property keys.</summary>
    public static bool IsPropertyKey(object key) => key is string s && s.StartsWith("Cos_");

    /// <summary>What <paramref name="player"/> wears in a slot ("" for nothing).</summary>
    public static string Of(PhotonPlayer player, CosmeticSlot slot) => player == null ? None : NetProps.Of(player, PropertyKey(slot), None);

    /// <summary>This client's own pick for a slot (saved locally).</summary>
    public static string Saved(CosmeticSlot slot)
    {
        MigrateSaves();
        string id = PlayerPrefs.GetString(PrefsKey(slot), None);
        var def = CosmeticRegistry.Find(id);
        return def != null && def.slot == slot ? id : None;
    }

    // Older saves: "Hat" became Head; the onesie's old "Body" became Outfit and "Back" became Body
    private static void MigrateSaves()
    {
        if (PlayerPrefs.GetInt("Cosmetic_SlotsV3", 0) == 1) return;
        if (!PlayerPrefs.HasKey("Cosmetic_Head") && PlayerPrefs.HasKey("Cosmetic_Hat"))
            PlayerPrefs.SetString("Cosmetic_Head", PlayerPrefs.GetString("Cosmetic_Hat"));
        string oldBody = PlayerPrefs.GetString("Cosmetic_Body", None), oldBack = PlayerPrefs.GetString("Cosmetic_Back", None);
        PlayerPrefs.SetString("Cosmetic_Outfit", oldBody);
        PlayerPrefs.SetString("Cosmetic_Body", oldBack);
        PlayerPrefs.DeleteKey("Cosmetic_Back");
        PlayerPrefs.DeleteKey("Cosmetic_Hat");
        PlayerPrefs.SetInt("Cosmetic_SlotsV3", 1);
    }

    /// <summary>The local loadout as slot → id (only the slots with something in them).</summary>
    public static Dictionary<CosmeticSlot, string> SavedLoadout()
    {
        var loadout = new Dictionary<CosmeticSlot, string>();
        foreach (var slot in Slots)
        {
            string id = Saved(slot);
            if (id != None) loadout[slot] = id;
        }
        return loadout;
    }

    /// <summary>Local player: wears <paramref name="id"/> ("" for nothing) in its slot and remembers it.</summary>
    public static void SetLocal(CosmeticSlot slot, string id)
    {
        id ??= None;
        PlayerPrefs.SetString(PrefsKey(slot), id);
        if (PhotonNetwork.InRoom) NetProps.SetLocal(PropertyKey(slot), id);
    }

    /// <summary>Local player: sends the remembered loadout if this room doesn't have it yet (call on joining).</summary>
    public static void EnsureLocal()
    {
        if (!PhotonNetwork.InRoom) return;
        var props = PhotonNetwork.LocalPlayer?.CustomProperties;
        var send = new ExitGames.Client.Photon.Hashtable();
        foreach (var slot in Slots)
            if (props == null || !props.ContainsKey(PropertyKey(slot))) send[PropertyKey(slot)] = Saved(slot);
        if (send.Count > 0) NetProps.SetLocal(send);
    }

    /// <summary>
    /// Puts a rigid item in place for this frame: Head on top of the head (level with <paramref name="topY"/>) facing
    /// forward, Body on the back (behind the rear) facing backward; or on <paramref name="socket"/> when the
    /// definition names a bone. (Skinned items need none of this: they ride Jimmy's bones.)
    /// </summary>
    public static void Place(Transform item, CosmeticSlot slot, Transform body, Transform head, Transform chest, Transform socket,
                             float topY, float size)
    {
        item.localScale = Vector3.one * size;
        if (socket != null)
        {
            item.SetPositionAndRotation(socket.position, socket.rotation);
            return;
        }
        Vector3 forward = Vector3.ProjectOnPlane(body.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 h = head != null ? head.position : body.position + Vector3.up * 2.2f * size;
        switch (slot)
        {
            case CosmeticSlot.Head:
                item.SetPositionAndRotation(new Vector3(h.x, topY - HatSink * size, h.z), Quaternion.LookRotation(forward, Vector3.up));
                break;
            default: // Body gear (and a rigid outfit, if there ever is one): on the back
                Vector3 c = chest != null ? chest.position : body.position + Vector3.up * 1.4f * size;
                item.SetPositionAndRotation(c - forward * BackDepth * size - Vector3.up * BackDrop * size, Quaternion.LookRotation(-forward, Vector3.up));
                break;
        }
    }
}
