using Photon.Pun;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Packs and reads the instantiation data passed to PhotonNetwork.Instantiate for ability
/// objects, so every client receives the launch impulse, power multiplier and owner without
/// a follow-up RPC.
/// Layout: [0] Vector3 impulse, [1] float power, [2] int owner player's ViewID,
/// [3] optional Hashtable of cheat values for the object's [TunableParam] fields (see ObjectTunables).
/// </summary>
public static class SpawnData
{
    public static object[] Create(Vector3 impulse, float power, int ownerViewId, Hashtable tunables = null) =>
        tunables != null && tunables.Count > 0
            ? new object[] { impulse, power, ownerViewId, tunables }
            : new object[] { impulse, power, ownerViewId };

    /// <summary>Cheat values for the object's tunable fields (null when none were changed).</summary>
    public static Hashtable Tunables(PhotonView view) =>
        Read<Hashtable>(view, 3, null);

    public static Vector3 Impulse(PhotonView view) =>
        Read(view, 0, Vector3.zero);

    /// <summary>Power multiplier from cheats (1 when absent).</summary>
    public static float Power(PhotonView view) =>
        Read(view, 1, 1f);

    /// <summary>The player object that spawned this one, if it still exists.</summary>
    public static PhotonView Owner(PhotonView view)
    {
        int id = Read(view, 2, 0);
        return id != 0 ? PhotonView.Find(id) : null;
    }

    private static T Read<T>(PhotonView view, int index, T fallback)
    {
        object[] data = view != null ? view.InstantiationData : null;
        return data != null && data.Length > index && data[index] is T value ? value : fallback;
    }
}
