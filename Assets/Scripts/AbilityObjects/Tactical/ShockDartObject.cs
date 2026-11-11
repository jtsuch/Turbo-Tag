using Photon.Pun;
using UnityEngine;

/// <summary>
/// Shock Dart (Resources/Object/ShockDart): flies fast and flat, and on its first real touch (a player or anything
/// else) bursts into a shock sphere <see cref="radius"/> across: everyone inside takes damage falling off from
/// <see cref="centreDamage"/> at the middle to <see cref="edgeDamage"/> at the edge (a direct hit counts as the middle)
/// and is slowed. Every screen sees the sphere (a translucent blue ball with bright blue outline rings). The thrower's
/// client decides and deals it.
/// </summary>
public class ShockDartObject : NetworkedProjectile
{
    [TunableParam("Radius", 0.5f, 10f)]
    [SerializeField] private float radius = 3f;
    [TunableParam("Centre Damage", 0f, 200f)]
    [SerializeField] private float centreDamage = 45f;
    [TunableParam("Edge Damage", 0f, 200f)]
    [SerializeField] private float edgeDamage = 10f;
    [TunableParam("Slow Time", 0f, 10f)]
    [SerializeField] private float slowSeconds = 2.5f;

    private static readonly Color Blue = new(0.35f, 0.85f, 1f);
    private bool done;

    public override float GravityScale => 0.3f;
    public override Vector3 LaunchVelocity(Vector3 thrown) => thrown * 1.6f;

    private void OnCollisionStay(Collision collision) => OnCollisionEnter(collision);

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsAuthority || done || !RealContact(collision)) return;
        done = true;
        Vector3 at = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        var direct = collision.collider.GetComponentInParent<Player>();
        float r = radius * Mathf.Sqrt(Power);

        foreach (var p in Player.All)
        {
            if (!TacticalNet.IsTarget(null, p, enemiesOnly: false)) continue;
            float d = p == direct ? 0f : DistanceToBody(p, at);
            if (d > r) continue;
            float t = Mathf.Clamp01(d / r);
            AbilityDamage.Hit(p, Mathf.Lerp(centreDamage, edgeDamage, t) * Power, "ShockDart", AbilityDamage.Chest(p) - at, trap: false);
            TacticalNet.Send(p, TacticalNet.Status.Shock, slowSeconds * Mathf.Lerp(1f, 0.5f, t));
        }
        HideAndFreeze();
        photonView.RPC(nameof(RPC_Zap), RpcTarget.All, at, r);
        Invoke(nameof(Remove), 0.8f);
    }

    // The nearest point of the body (a direct graze counts), not its middle
    private static float DistanceToBody(Player p, Vector3 at)
    {
        var col = p.GetComponent<Collider>();
        Vector3 nearest = col != null && col.enabled ? col.ClosestPoint(at) : AbilityDamage.Chest(p);
        return Vector3.Distance(nearest, at);
    }

    private void Remove() => DestroyNetworked();

    [PunRPC]
    private void RPC_Zap(Vector3 at, float r)
    {
        AoeDebug.Sphere(at, r, new Color(0.4f, 0.95f, 1f), "Shock Dart (to the body)");
        HideAndFreeze();
        Sfx.At(SfxKind.ZapDown, at, 1f, 40f, 1.4f);
        Vfx.Burst(at, Blue, 30, 7f, 0.18f, 0.35f);

        // The shock sphere: a see-through ball with outline rings round it, fading over 0.6 s
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(sphere.GetComponent<Collider>());
        sphere.name = "Shock Sphere";
        sphere.transform.position = at;
        sphere.transform.localScale = Vector3.one * r * 2f;
        var mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(Blue.r, Blue.g, Blue.b, 0.18f) };
        sphere.GetComponent<Renderer>().sharedMaterial = mat;
        sphere.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var lineMat = new Material(Shader.Find("Sprites/Default")) { color = Blue };
        Ring(sphere.transform, r, Quaternion.identity, lineMat);
        Ring(sphere.transform, r, Quaternion.Euler(90f, 0f, 0f), lineMat);
        Ring(sphere.transform, r, Quaternion.Euler(0f, 0f, 90f), lineMat);
        sphere.AddComponent<FadeAway>().Begin(0.6f, mat, lineMat);
    }

    private static void Ring(Transform parent, float r, Quaternion turn, Material mat)
    {
        var line = new GameObject("Outline").AddComponent<LineRenderer>();
        line.transform.SetParent(parent, true);
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = 48;
        line.widthMultiplier = 0.06f;
        line.sharedMaterial = mat;
        for (int i = 0; i < 48; i++)
        {
            float a = i * Mathf.PI * 2f / 48;
            line.SetPosition(i, parent.position + turn * new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }
    }

    /// <summary>Fades a short-lived visual's materials out, then removes it.</summary>
    private class FadeAway : MonoBehaviour
    {
        private float born, life;
        private Material[] mats;
        private float[] alphas;

        public void Begin(float seconds, params Material[] materials)
        {
            born = Time.time;
            life = seconds;
            mats = materials;
            alphas = new float[mats.Length];
            for (int i = 0; i < mats.Length; i++) alphas[i] = mats[i].color.a;
        }

        private void Update()
        {
            float t = (Time.time - born) / life;
            if (t >= 1f) { Destroy(gameObject); return; }
            for (int i = 0; i < mats.Length; i++) { var c = mats[i].color; c.a = alphas[i] * (1f - t); mats[i].color = c; }
        }
    }
}
