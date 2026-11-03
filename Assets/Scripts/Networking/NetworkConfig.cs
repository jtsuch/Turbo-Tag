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
