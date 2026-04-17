using UnityEngine;

/// <summary>
/// Inverts the player's movement axes so W moves backward, S moves forward,
/// A moves right, and D moves left, for the effect duration.
///
/// Requires Player.MovementScale (Vector2) and JimmyMove to multiply
/// Input.GetAxis values by player.MovementScale.x/y respectively.
/// </summary>
public class KeybindSwitchEffect : PlayerEffect
{
    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.MovementScale = new Vector2(-1f, -1f);
    }

    protected override void OnEffectEnd()
    {
        if (player != null)
            player.MovementScale = Vector2.one;
    }
}
