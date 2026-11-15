using UnityEngine;

/// <summary>
/// A decoy penguin (Decoys donut): a look-alike of a player (their colour, the same skeleton and Animator) that sprints
/// off in a direction for a few seconds, turning away from walls, then vanishes in a puff. Local only; every screen
/// makes the same three from the same RPC.
/// </summary>
public class Decoy : MonoBehaviour
{
    private const float Speed = 10f, Life = 4.5f;

    private Animator animator;
    private Vector3 direction;
    private float born;

    public static void Create(Player source, Vector3 at, Vector3 direction)
    {
        var root = PlayerCorpse.CreateCopy(source, $"Decoy ({source.DisplayName})", out _);
        if (root == null) return;
        root.position = at;
        var decoy = root.gameObject.AddComponent<Decoy>();
        decoy.direction = new Vector3(direction.x, 0f, direction.z).normalized;
        decoy.born = Time.time;

        var anim = source.Animation != null ? source.Animation.Animator : null;
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            decoy.animator = root.gameObject.AddComponent<Animator>();
            decoy.animator.avatar = anim.avatar;
            decoy.animator.runtimeAnimatorController = anim.runtimeAnimatorController;
            decoy.animator.applyRootMotion = false;
            decoy.animator.Rebind();
        }
    }

    private void Update()
    {
        if (animator != null)
        {
            animator.SetFloat("MovementSpeed", 1f); // Sprint
            animator.SetBool("onGround", true);
        }

        // Turn away from walls
        if (Physics.Raycast(transform.position + Vector3.up, direction, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            direction = Quaternion.Euler(0f, Random.value < 0.5f ? 110f : -110f, 0f) * direction;

        Vector3 next = transform.position + direction * Speed * Time.deltaTime;
        // Follow the ground (and run off edges)
        if (Physics.Raycast(next + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && ground.collider.GetComponentInParent<Player>() == null)
            next.y = ground.point.y;
        else next.y -= 9f * Time.deltaTime;
        transform.SetPositionAndRotation(next, Quaternion.LookRotation(direction));

        if (Time.time - born > Life)
        {
            Vfx.Burst(transform.position + Vector3.up, Color.white, 20, 3f, 0.35f, 0.5f);
            Sfx.At(SfxKind.Pop, transform.position + Vector3.up, 0.6f, 30f);
            Destroy(gameObject);
        }
    }
}
