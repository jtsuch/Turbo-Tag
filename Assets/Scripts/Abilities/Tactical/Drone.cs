using Photon.Pun;
using UnityEngine;

/// <summary>
/// Sends out a small recon drone (Resources/Object/Drone, <see cref="DroneObject"/>) that flies where you look for a few
/// seconds, scanning: enemies near it show for you and your team. Can be shot down. Health modes only.
/// </summary>
[AbilityInfo("Send out a little drone that flies where you look, revealing enemies near it to your team. Can be shot down.", HealthModesOnly = true)]
public class Drone : QuickAbility
{
    [TunableParam("Flight Speed", 2f, 40f)]
    [SerializeField] private float flightSpeed = 13f;

    protected override void Activate()
    {
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : transform;
        Vector3 start = cam.position + cam.forward * 1.2f;
        PhotonNetwork.Instantiate(ResourcePath, start, Quaternion.LookRotation(cam.forward), 0,
            SpawnData.Create(cam.forward * flightSpeed, UpgradePower, Owner.photonView.ViewID));   // S&D: the scan radius ×
        Sfx.Here(SfxKind.ZapUp, 0.6f, 1.4f);
    }
}
