using Photon.Pun;
using UnityEngine;

/// <summary>
/// Remote bomb, instant (no aiming): press once to toss it a short way forward, where it sticks to the first
/// wall, floor or prop it touches (players just bounce it off); press again to blow it up. The cooldown starts
/// at the blast. A bomb that is never set off goes off by itself after the bomb's Max Lifetime.
/// Stays in the Throw slot, so loadouts don't change; the object is still Resources/Object/BoomBomb (<see cref="Bomb"/>).
/// </summary>
[AbilityInfo("Tap to toss a sticky bomb. Tap again to blow it up.")]
public class BoomBomb : ThrowAbility
{
    [Header("Toss")]
    [TunableParam("Toss Speed", 2f, 60f)]
    [SerializeField] private float tossSpeed = 13f;
    [TunableParam("Toss Lift", 0f, 20f)]
    [SerializeField] private float tossLift  = 3f;

    private Bomb live;     // The bomb out in the world, until it goes off
    private bool tracking; // A bomb was out (so its blast, however it happens, starts the cooldown)

    public override bool UsesActionKey    => false;
    public override bool IsAwaitingAction => false;

    public override void HandleInput(AbilityInputEvent inputEvent)
    {
        if (inputEvent != AbilityInputEvent.Down) return;

        if (live != null)
        {
            live.Trigger();
            live = null;
            return; // The cooldown starts in LateUpdate once the bomb is gone
        }
        if (!tracking && IsReady) Toss();
    }

    public override void Cancel() { } // A bomb in the world stays there (Cancel runs on stuns, round resets…)

    private void Toss()
    {
        Transform origin = throwOrigin != null ? throwOrigin : transform;
        Vector3 velocity = origin.forward * tossSpeed + Vector3.up * tossLift + Owner.Velocity * 0.5f;

        var spawned = PhotonNetwork.Instantiate(ResourcePath, origin.position, origin.rotation, 0,
            SpawnData.Create(velocity, power, Owner.photonView.ViewID, ObjectTunableOverrides()));
        spawned.TryGetComponent(out live);
        tracking = true;
        Sfx.At(SfxKind.Toss, origin.position, 0.8f);
    }

    // Once the bomb is gone (set off, or its max lifetime ran out) the cooldown starts
    private void LateUpdate()
    {
        if (!IsLocal || !tracking || live != null && !live.HasExploded) return;
        tracking = false;
        live = null;
        StartCooldown();
    }
}
