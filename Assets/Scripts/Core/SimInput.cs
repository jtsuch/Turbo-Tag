using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simulated input for automated recordings (the ability clip recorder): while <see cref="Active"/>, InputHandler
/// and PlayerCam read keys, movement and look from here instead of the keyboard and mouse. A driver calls
/// <see cref="BeginFrame"/> once per frame (before InputHandler runs) with the keys held that frame; presses and
/// releases are worked out from the previous frame. Off in normal play.
/// </summary>
public static class SimInput
{
    public static bool Active { get; private set; }

    /// <summary>Movement keys (x strafe, y forward), like the raw axes.</summary>
    public static Vector2 Move;

    /// <summary>Absolute view: yaw (degrees, world) and pitch (degrees, down positive).</summary>
    public static float Yaw, Pitch;

    private static readonly HashSet<KeyCode> held = new(), previous = new();

    public static void Start(float yaw, float pitch)
    {
        Active = true;
        Yaw = yaw;
        Pitch = pitch;
        Move = Vector2.zero;
        held.Clear();
        previous.Clear();
    }

    public static void Stop()
    {
        Active = false;
        held.Clear();
        previous.Clear();
        Move = Vector2.zero;
    }

    /// <summary>Sets this frame's held keys (call before the player's input is read).</summary>
    public static void BeginFrame(IEnumerable<KeyCode> keys)
    {
        previous.Clear();
        previous.UnionWith(held);
        held.Clear();
        foreach (var k in keys) if (k != KeyCode.None) held.Add(k);
    }

    public static bool Key(KeyCode k)     => Active ? held.Contains(k) : Input.GetKey(k);
    public static bool KeyDown(KeyCode k) => Active ? held.Contains(k) && !previous.Contains(k) : Input.GetKeyDown(k);
    public static bool KeyUp(KeyCode k)   => Active ? !held.Contains(k) && previous.Contains(k) : Input.GetKeyUp(k);

    /// <summary>Raw movement axes (simulated or real).</summary>
    public static Vector2 MoveAxes => Active ? Move : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
}
