using UnityEngine;

/// <summary>
/// Touching this kills: the local player who lands on (or enters) it has fallen out of the level, exactly like
/// dropping below the Spawner's kill height (a self destruction in gun modes, a respawn otherwise).
/// Works with solid colliders (a death plane terrain) and triggers. Each client only checks its own player.
/// Attach to: a level's death plane (Improvement's DeathPlane).
/// </summary>
public class DeathZone : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision) => Touch(collision.collider);
    private void OnCollisionStay(Collision collision)  => Touch(collision.collider);
    private void OnTriggerEnter(Collider other)        => Touch(other);
    private void OnTriggerStay(Collider other)         => Touch(other);

    private static void Touch(Collider other)
    {
        var local = Player.Local;
        if (local == null || local.IsCaught || other.attachedRigidbody != local.Body) return;
        if (Spawner.Instance != null) Spawner.Instance.LocalPlayerFell();
    }
}
