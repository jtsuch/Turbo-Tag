using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Single entry point for connecting to Photon. Every client is pinned to the same region:
/// Photon rooms only exist in the region they were created in, and "best region" picks by ping,
/// so friends (or two editor copies) can otherwise end up unable to see each other's rooms.
/// If the Photon settings asset sets a Fixed Region, that wins.
/// </summary>
public static class NetworkConfig
{
    /// <summary>Photon region code ("us" = US East, "usw" = US West, "eu" = Europe...).</summary>
    public const string DefaultRegion = "us";

    /// <summary>
    /// Snapshots per second for observed components (player movement). 20 keeps remote players smooth
    /// with ~100 ms of playback delay; PUN's default of 10 needs twice that.
    /// </summary>
    public const int SerializationRate = 20;

    /// <summary>
    /// Every player sends its snapshots to every other one, so a room's messages grow with the square of its size
    /// (12 players at 20/s is ~2,900 a second; Photon's guideline is ~500 per room). Fewer snapshots in bigger
    /// rooms: 20/s up to 6 players, 15/s up to 9, 10/s beyond; the playback delay follows the rate
    /// (<see cref="PlayerNetworkSync"/>). Called every frame by the GameModeManager; only acts when the count changes.
    /// </summary>
    public static void FitToRoom()
    {
        int players = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 1;
        if (players == fittedFor) return;
        fittedFor = players;
        int rate = players <= 6 ? SerializationRate : players <= 9 ? 15 : 10;
        PhotonNetwork.SerializationRate = rate;
        PhotonNetwork.SendRate = rate * 2;
    }

    private static int fittedFor = -1;

    public static bool Connect()
    {
        PhotonNetwork.SendRate          = SerializationRate * 2;
        PhotonNetwork.SerializationRate = SerializationRate;

        var settings = new AppSettings();
        PhotonNetwork.PhotonServerSettings.AppSettings.CopyTo(settings); // Never mutate the asset
        if (string.IsNullOrEmpty(settings.FixedRegion)) settings.FixedRegion = DefaultRegion;
        return PhotonNetwork.ConnectUsingSettings(settings);
    }
}
