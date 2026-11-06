using UnityEngine;

/// <summary>Spins a cosmetic part around its local up axis (the propeller cap's blades).</summary>
public class CosmeticSpin : MonoBehaviour
{
    [SerializeField] private float degreesPerSecond = 540f;

    private void Update() => transform.Rotate(0f, degreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
}
