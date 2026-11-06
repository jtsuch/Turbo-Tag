using UnityEngine;

/// <summary>
/// Draws the shield bubble around a player while <see cref="Player.IsShielded"/> (Shield ability or the Bubble
/// donut), on every screen. Your own bubble is fainter (seen from inside, it tints rather than blocks the view).
/// Added to every player copy by <see cref="Player"/>.
/// </summary>
[RequireComponent(typeof(Player))]
public class ShieldBubble : MonoBehaviour
{
    private static readonly Color BubbleColor = new(0.35f, 0.85f, 1f, 0.22f);

    private Player     player;
    private GameObject bubble;
    private Material   material;
    private float      shown; // 0–1 pop in/out

    private void Awake() => player = GetComponent<Player>();

    private void LateUpdate()
    {
        bool on = player.IsShielded && !player.IsDownAfterTag;
        shown = Mathf.MoveTowards(shown, on ? 1f : 0f, Time.deltaTime * 6f);
        if (shown <= 0f)
        {
            if (bubble != null && bubble.activeSelf) bubble.SetActive(false);
            return;
        }

        if (bubble == null) Build();
        bubble.SetActive(true);
        float pop  = shown < 1f ? Mathf.Sin(shown * Mathf.PI * 0.5f) : 1f + Mathf.Sin(Time.time * 4f) * 0.02f;
        float size = Mathf.Max(2.2f, player.Height * 1.7f) * pop;
        bubble.transform.position   = transform.position + transform.up * (player.Height * 0.8f);
        bubble.transform.localScale = Vector3.one * size;

        Color c = BubbleColor;
        c.a *= player.IsLocal ? 0.45f : 1f;
        material.color = c;
    }

    private void Build()
    {
        bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bubble.name = "Shield Bubble";
        Destroy(bubble.GetComponent<Collider>());
        material = new Material(Shader.Find("Sprites/Default")); // Two-sided, transparent, always in builds
        var r = bubble.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private void OnDestroy()
    {
        if (bubble != null) Destroy(bubble);
        if (material != null) Destroy(material);
    }
}
