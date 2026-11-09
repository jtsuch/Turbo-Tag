using Photon.Pun;
using UnityEngine;

/// <summary>
/// Snow Screen (Trap slot): press to send a little snow cloud forward along your view (no placing). It drifts through
/// walls, leaving a tall wall of snow along its path on whatever is below it (<see cref="SnowScreenObject"/>): nobody
/// sees through it, and enemies wading through it are slowed.
/// Health modes only.
/// </summary>
[AbilityInfo("Send a little snow cloud forward: it drifts through walls, leaving a long, tall wall of snow along its path that nobody can see through. Enemies wading through it are slowed.", DisplayName = "Snow Screen", HealthModesOnly = true)]
public class SnowScreen : TrapAbility
{
    [TunableParam("Cloud Speed", 2f, 30f)]
    [SerializeField] private float cloudSpeed = 9f;

    // A press sends the cloud: no hologram and no Action key
    public override bool UsesActionKey    => false;
    public override bool IsAwaitingAction => false;

    public override void HandleInput(AbilityInputEvent inputEvent)
    {
        if (inputEvent != AbilityInputEvent.Down || !IsReady) return;
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : transform;
        PhotonNetwork.Instantiate(ResourcePath, cam.position + cam.forward * 1.5f, Quaternion.LookRotation(cam.forward), 0,
            SpawnData.Create(cam.forward * cloudSpeed, Owner.AbilityPower, Owner.photonView.ViewID));
        Sfx.Here(SfxKind.Blow, 0.6f, 1.3f);
        StartCooldown();
    }

    public override void Cancel() { }
}
