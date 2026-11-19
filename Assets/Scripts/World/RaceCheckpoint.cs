using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A gate across a race course (Race mode, Justin Nov 18): numbered from 1 in course order, the last one the finish.
/// Passing them in order (on your own screen: <see cref="RaceProgress"/>) moves your respawn point up to the last one
/// passed and, at the finish, records your time. A box trigger the course's width (added if the object has none);
/// the builder puts a visible arch on it.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class RaceCheckpoint : MonoBehaviour
{
    public static readonly List<RaceCheckpoint> All = new();

    [Tooltip("Order along the course, from 1. The highest is the finish.")]
    [Min(1)] public int index = 1;

    [Tooltip("Where a player who falls after passing this gate comes back (its own position if empty).")]
    public Transform respawn;

    public bool IsFinish => index >= Count;

    /// <summary>How many gates the course has (the finish's index).</summary>
    public static int Count
    {
        get
        {
            int highest = 0;
            foreach (var c in All) if (c != null) highest = Mathf.Max(highest, c.index);
            return highest;
        }
    }

    public static RaceCheckpoint Find(int index)
    {
        foreach (var c in All) if (c != null && c.index == index) return c;
        return null;
    }

    /// <summary>Where a respawn after this gate puts you (just past it, facing along the course).</summary>
    public void RespawnPose(out Vector3 position, out Quaternion rotation)
    {
        var t = respawn != null ? respawn : transform;
        position = t.position;
        rotation = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
    }

    private void Awake()
    {
        var box = GetComponent<BoxCollider>();
        box.isTrigger = true;
        gameObject.layer = 0;   // Default: never a wall to run or a ledge to grab
    }

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponentInParent<Player>();
        if (player != null && player.IsLocal) RaceProgress.Reached(this);
    }
}

/// <summary>
/// The local player's progress through the race on their own screen: the next gate due, the last passed (the respawn
/// point) and the finishing time. Passing a gate writes it to their own properties (<see cref="PlayerKeys.RaceCheckpoint"/>,
/// <see cref="PlayerKeys.RaceFinish"/>), which is how the host and everyone else know where they are.
/// </summary>
public static class RaceProgress
{
    public static int   Passed      { get; private set; }   // The last gate passed (0: none)
    public static float FinishTime  { get; private set; } = -1f;
    public static RaceCheckpoint Last => RaceCheckpoint.Find(Passed);

    public static bool Finished => FinishTime >= 0f;

    /// <summary>A new race: nothing passed, no time (and everyone else hears so).</summary>
    public static void Reset()
    {
        Passed = 0;
        FinishTime = -1f;
        if (Photon.Pun.PhotonNetwork.InRoom)
            NetProps.SetLocal(new ExitGames.Client.Photon.Hashtable { [PlayerKeys.RaceCheckpoint] = 0, [PlayerKeys.RaceFinish] = -1f });
    }

    public static void Reached(RaceCheckpoint gate)
    {
        var manager = GameModeManager.Instance;
        if (manager == null || manager.Mode is not RaceMode || manager.Phase != MatchPhase.Active || Finished) return;
        if (gate.index != Passed + 1) return;   // In order only (no skipping a gate by a shortcut over it)

        Passed = gate.index;
        var props = new ExitGames.Client.Photon.Hashtable { [PlayerKeys.RaceCheckpoint] = Passed };
        if (gate.IsFinish)
        {
            FinishTime = manager.Clock.Elapsed;
            props[PlayerKeys.RaceFinish] = FinishTime;
            Sfx.Here(SfxKind.Fanfare, 0.7f);
            if (HUDManager.Instance != null) HUDManager.Instance.ShowBanner($"FINISHED!\n<size=55%>{RaceMode.FormatTime(FinishTime)}</size>", 3f);
        }
        else
        {
            Sfx.Here(SfxKind.Snap, 0.7f, 1.4f);
            if (HUDManager.Instance != null) HUDManager.Instance.ShowBanner($"CHECKPOINT {Passed} / {RaceCheckpoint.Count - 1}", 1.2f);
        }
        NetProps.SetLocal(props);
    }
}
