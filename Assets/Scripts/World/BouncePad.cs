using TMPro;
using UnityEngine;

/// <summary>
/// Launches any rigidbody that enters its trigger along the pad's forward axis, cancelling
/// vertical velocity first so the bounce height is consistent.
/// Attach to: BouncePad prefab (trigger collider).
/// </summary>
public class BouncePad : MonoBehaviour
{
    [SerializeField] private float       bounceForce = 40f;
    [Tooltip("Optional label showing the bounce strength.")]
    [SerializeField] private TextMeshPro strengthText;

    private float nextFxAt;

    private void Start()
    {
        if (strengthText != null) strengthText.text = bounceForce.ToString("F0");
    }

    private void OnTriggerEnter(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        // Every screen sees the bounce (remote copies hit the trigger too); only the owner pushes
        if (Time.time >= nextFxAt && rb.GetComponent<Player>() != null)
        {
            nextFxAt = Time.time + 0.3f;
            Fx.Bounce(transform.position);
        }
        if (!WorldFx.HasAuthority(rb)) return;

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(transform.forward * bounceForce, ForceMode.Impulse);
    }
}
