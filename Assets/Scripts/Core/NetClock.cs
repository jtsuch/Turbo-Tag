using Photon.Pun;
using UnityEngine;

/// <summary>
/// The room's shared clock (<see cref="PhotonNetwork.Time"/>) as it applies to the moment being simulated, for anything
/// that places things by network time inside physics steps (remote players' playback, moving platforms).
///
/// Why not PhotonNetwork.Time itself: it only moves once per rendered frame. Physics steps don't line up with frames
/// (a frame can hold none, one or several), so code that read it in FixedUpdate saw time stand still for one step and
/// jump two steps' worth on the next. Remote players and platforms were placed in uneven hops (a fine judder, worst
/// when the frame rate and the 60 Hz physics rate drift past each other, and on platforms it shook their riders).
///
/// <see cref="Now"/> is game time plus a slowly corrected offset to the network clock, so in FixedUpdate it advances by
/// exactly one physics step per step, and in Update by the frame's own time. The offset follows real drift gently and
/// snaps after a hitch, a pause or a reconnect. While time is scaled (hit-stop, the final slow motion) it tracks the
/// network clock exactly, since game time is then running at a different speed on purpose.
/// </summary>
public static class NetClock
{
    private const double SnapSeconds = 0.25;   // Further off than this: jump to the right time
    private const double Follow      = 0.05;   // Share of the error corrected each frame otherwise

    private static double offset;
    private static bool   synced;

    /// <summary>Network time for the moment being simulated: this physics step's in FixedUpdate, this frame's elsewhere.</summary>
    public static double Now
    {
        get
        {
            if (driver == null) StartDriver();
            return (Time.inFixedTimeStep ? Time.fixedTimeAsDouble : Time.timeAsDouble) + offset;
        }
    }

    // The two clocks are compared once a frame, in Update (where Time.time is the frame's own time; inside a physics
    // step it reads the step's time instead, which would skew the comparison by up to a step)
    private static Driver driver;

    private static void StartDriver()
    {
        var go = new GameObject("NetClock") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(go);
        driver = go.AddComponent<Driver>();
        offset = PhotonNetwork.Time - (Time.inFixedTimeStep ? Time.fixedTimeAsDouble : Time.timeAsDouble);   // A first estimate until Update
    }

    private sealed class Driver : MonoBehaviour
    {
        private void Update() => Sync();
    }

    private static void Sync()
    {
        double target = PhotonNetwork.Time - Time.timeAsDouble;
        double error  = target - offset;
        bool   scaled = !Mathf.Approximately(Time.timeScale, 1f);
        if (!synced || scaled || error > SnapSeconds || error < -SnapSeconds) offset = target;
        else offset += error * Follow;
        synced = true;
    }
}
