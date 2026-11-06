using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hunter mode: a ghost (<see cref="PlayerRole.Ghost"/>) has no physical hitbox for other players: on every screen
/// its colliders ignore every other player's (no bumping, no contact tags, no standing on each other), while it
/// still walks on the level. Bullets skip ghosts (PlayerGun) and no push or blast moves them
/// (<see cref="Player.AffectedBy"/>). Turning back into anything else restores the collisions.
/// Added to every player copy by Player.
/// </summary>
[RequireComponent(typeof(Player))]
public class GhostBody : MonoBehaviour
{
    private Player player;
    private Collider[] own;
    private bool ghosted;
    private float nextRefresh;
    private readonly HashSet<Collider> ignored = new();

    private void Awake()
    {
        player = GetComponent<Player>();
        own = GetComponentsInChildren<Collider>(true);
    }

    private void Update()
    {
        bool ghost = player.Role == PlayerRole.Ghost || player.Has(PlayerFlag.Phantom);
        if (ghost == ghosted && (!ghost || Time.time < nextRefresh)) return;
        nextRefresh = Time.time + 0.5f; // New players join, bodies respawn: keep the list fresh
        ghosted = ghost;

        if (!ghost)
        {
            foreach (var other in ignored)
                if (other != null) foreach (var mine in own) if (mine != null) Physics.IgnoreCollision(mine, other, false);
            ignored.Clear();
            return;
        }

        foreach (var p in Player.All)
        {
            if (p == null || p == player) continue;
            foreach (var other in p.GetComponentsInChildren<Collider>(true))
            {
                if (!ignored.Add(other)) continue;
                foreach (var mine in own) if (mine != null) Physics.IgnoreCollision(mine, other, true);
            }
        }
    }
}
