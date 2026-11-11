using Photon.Pun;
using TMPro;
using UnityEngine;

/// <summary>
/// Placed nuke: a <see cref="countdownTime"/> countdown, then a blast that reaches <see cref="explosionRadius"/>
/// (70 m) and throws much harder the closer you are (a steep fifth-power falloff: <see cref="centreSpeed"/> at the bomb,
/// dropping fast to a small but noticeable <see cref="edgeSpeed"/> nudge over the whole outer half, as a velocity change
/// so every body flies the same). Warnings so people can
/// react: a slow beep everyone hears wherever they are (quicker for the last few seconds), a red digital countdown
/// floating on the bomb, and a faint see-through blue sphere marking its reach (up and down too). Anyone standing next to it can
/// disarm it with the Grab key.
/// The placing client owns the countdown; beeps (which carry the time left, so every screen's display agrees),
/// detonation and disarming are RPCs to all. Each client throws only what it has authority over: its own player
/// (checked directly, so a level full of colliders can't crowd them out of the overlap) and local props.
/// Attach to: Resources/Object/Nuke (with PhotonView).
/// </summary>
public class NukeSequence : MonoBehaviourPun
{
    [Header("Countdown")]
    [TunableParam("Countdown", 1f, 30f)]
    [SerializeField] private float countdownTime     = 10f;
    [Tooltip("Seconds left when the beeps double up.")]
    [SerializeField] private float fastBeepThreshold = 3f;

    [Header("Explosion")]
    [TunableParam("Blast Radius", 5f, 300f)]
    [SerializeField] private float     explosionRadius = 70f;
    [Tooltip("Speed (m/s) a body right at the bomb is thrown at.")]
    [TunableParam("Centre Speed", 5f, 300f)]
    [SerializeField] private float     centreSpeed     = 85f;
    [Tooltip("Speed (m/s) at the very edge of the radius.")]
    [TunableParam("Edge Speed", 0f, 50f)]
    [SerializeField] private float     edgeSpeed       = 4f;
    [SerializeField] private LayerMask affectedLayers;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip   beepSound;
    [SerializeField] private AudioClip   explosionSound;

    [Header("Disarm")]
    [TunableParam("Disarm Range", 0f, 10f)]
    [SerializeField] private float disarmRange = 3f;

    [Header("Visual Feedback")]
    [SerializeField] private GameObject explosionEffect;
    [SerializeField] private Light      warningLight;

    private static readonly Color DigitColor = new(1f, 0.12f, 0.1f);

    private static readonly Collider[] overlapBuffer = new Collider[512];

    private float timeRemaining;
    private float nextBeepTime;
    private float power = 1f;
    private bool  isActive = true;
    private TextMeshPro  display;
    private Renderer     ring; // The see-through blast sphere
    private Material     ringMaterial;

    private void Awake() => ObjectTunables.Apply(photonView); // Cheat values from the placer

    private void Start()
    {
        power         = SpawnData.Power(photonView);
        timeRemaining = countdownTime;
        nextBeepTime  = countdownTime;

        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        WorldFx.Configure3D(audioSource, 50f);
        if (warningLight != null) warningLight.enabled = false;

        BuildDisplay();
        BuildRing();
    }

    private AoeDebug aoe;

    private void Update()
    {
        if (!isActive) return;
        if (AoeDebug.On && aoe == null) aoe = AoeDebug.Sphere(transform.position, explosionRadius, new Color(1f, 0.35f, 0.25f), "Nuke (85 m/s centre, ~6.5 half way, 4 edge)", 0f, transform);

        // Every screen counts down (the owner's beeps keep it in step); the owner decides
        timeRemaining -= Time.deltaTime;
        if (photonView.IsMine) UpdateCountdown();
        UpdateDisplay();
        CheckDisarm();
    }

    private void UpdateCountdown()
    {
        if (timeRemaining <= nextBeepTime)
        {
            photonView.RPC(nameof(RPC_Beep), RpcTarget.All, Mathf.Max(0f, timeRemaining));
            nextBeepTime -= timeRemaining <= fastBeepThreshold ? 0.5f : 1f;
        }

        if (timeRemaining <= 0f)
            photonView.RPC(nameof(RPC_Detonate), RpcTarget.All);
    }

    private void CheckDisarm()
    {
        var local = Player.Local;
        if (local == null || PauseMenuManager.IsPaused) return;
        if (!Input.GetKeyDown(Keybinds.Get(InputAction.Grab))) return;
        if (Vector3.Distance(local.transform.position, transform.position) > disarmRange) return;

        photonView.RPC(nameof(RPC_Disarm), RpcTarget.All);
    }

    // ─── Warnings ─────────────────────────────────────────────────────────────

    // A red LED-style "00:07" floating just above the bomb, turned to face this screen's camera
    private void BuildDisplay()
    {
        Bounds bounds = new(transform.position, Vector3.zero);
        foreach (var r in GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);

        var go = new GameObject("Countdown");
        display = go.AddComponent<TextMeshPro>(); // Swaps the Transform for a RectTransform: position it after
        go.transform.SetParent(transform, true);
        go.transform.position = new Vector3(bounds.center.x, bounds.max.y + 0.7f, bounds.center.z);
        display.font = UIKit.FontAsset(UIKit.Font.Bold);
        display.fontSize = 9f;
        display.color = DigitColor;
        display.alignment = TextAlignmentOptions.Center;
        display.textWrappingMode = TextWrappingModes.NoWrap;
        display.rectTransform.sizeDelta = new Vector2(6f, 2f);
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (display == null) return;
        int seconds = Mathf.CeilToInt(Mathf.Max(0f, timeRemaining));
        display.text = $"<mspace=0.62em>{seconds / 60:00}:{seconds % 60:00}</mspace>";
        // Blinks in the last seconds
        display.alpha = timeRemaining > fastBeepThreshold || Mathf.Repeat(timeRemaining, 0.5f) > 0.2f ? 1f : 0.35f;
        var cam = Camera.main;
        if (cam != null) display.transform.rotation = Quaternion.LookRotation(display.transform.position - cam.transform.position, Vector3.up);
    }

    // The blast's reach as a sphere (it reaches up and down too, not just along the floor): a faint see-through blue
    // shell, both faces drawn (Sprites/Default culls nothing) so it shows from inside as well
    private void BuildRing()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Blast Radius";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.position = transform.position;
        Vector3 lossy = transform.lossyScale;
        float d = explosionRadius * 2f;
        go.transform.localScale = new Vector3(d / lossy.x, d / lossy.y, d / lossy.z);
        ring = go.GetComponent<Renderer>();
        ringMaterial = new Material(Shader.Find("Sprites/Default")) { color = ShellColor }; // Always in builds
        ring.sharedMaterial = ringMaterial;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;

        // Three great circles so the sphere reads as a sphere from inside and out
        foreach (var axis in new[] { Vector3.up, Vector3.right, Vector3.forward })
        {
            var circle = new GameObject("Blast Circle").AddComponent<LineRenderer>();
            circle.transform.SetParent(go.transform, true);
            circle.useWorldSpace = true;
            circle.loop = true;
            circle.positionCount = 96;
            circle.widthMultiplier = 0.35f;
            circle.sharedMaterial = ringMaterial;
            circle.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Vector3 a = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, b = Vector3.Cross(axis, a);
            for (int i = 0; i < 96; i++)
            {
                float t = i * Mathf.PI * 2f / 96;
                circle.SetPosition(i, transform.position + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * explosionRadius);
            }
            circle.startColor = circle.endColor = new Color(1f, 1f, 1f, 4f); // ×4 the shell's alpha (vertex colour × material)
        }
    }

    private static readonly Color ShellColor = new(0.3f, 0.65f, 1f, 0.12f);

    // The shell brightens with each beep, then fades back to faint
    private System.Collections.IEnumerator PulseRing()
    {
        for (float t = 0f; t < 0.4f && ring != null; t += Time.deltaTime)
        {
            var c = ShellColor;
            c.a = Mathf.Lerp(0.3f, ShellColor.a, t / 0.4f);
            ringMaterial.color = c;
            yield return null;
        }
    }

    // ─── RPCs ─────────────────────────────────────────────────────────────────

    [PunRPC]
    private void RPC_Beep(float remaining)
    {
        timeRemaining = remaining;
        if (beepSound != null) audioSource.PlayOneShot(beepSound);
        // Heard everywhere on the map (flat), and from the bomb itself up close
        bool fast = remaining <= fastBeepThreshold;
        Sfx.Here(SfxKind.Tick, fast ? 0.55f : 0.4f, fast ? 1.25f : 0.8f);
        Sfx.At(SfxKind.Tick, transform.position, 1f, 40f, fast ? 1.25f : 0.8f);
        if (ring != null) StartCoroutine(PulseRing());

        if (warningLight == null) return;
        warningLight.enabled = true;
        CancelInvoke(nameof(LightOff));
        Invoke(nameof(LightOff), 0.1f);
    }

    private void LightOff() => warningLight.enabled = false;

    [PunRPC]
    private void RPC_Detonate()
    {
        if (!isActive) return;
        isActive = false;
        if (display != null) display.gameObject.SetActive(false);
        if (ring != null) ring.gameObject.SetActive(false);

        WorldFx.PlayClip(explosionSound, transform.position, explosionRadius);
        WorldFx.SpawnVfx(explosionEffect, transform.position);
        Fx.Explosion(transform.position, Mathf.Min(explosionRadius, 40f));
        Sfx.Here(SfxKind.Explosion, 0.6f, 0.6f); // A distant boom for everyone
        Blast(transform.position);

        if (photonView.IsMine) PhotonNetwork.Destroy(gameObject);
    }

    // Throws this client's own player and props away from the bomb: hardest right at it, falling off with the cube
    private void Blast(Vector3 centre)
    {
        var local = Player.Local;
        // The placer is thrown too; their teammates aren't (traps spare the placer's team)
        if (local != null && local.Body != null && !local.IsCaught && !local.IsTeammateOf(photonView.OwnerActorNr)) Throw(local.Body, centre);

        int layers = affectedLayers.value != 0 ? affectedLayers.value : Physics.AllLayers;
        int count = Physics.OverlapSphereNonAlloc(centre, explosionRadius, overlapBuffer, layers, QueryTriggerInteraction.Ignore);
        var thrown = new System.Collections.Generic.HashSet<Rigidbody>();
        for (int i = 0; i < count; i++)
        {
            Rigidbody body = overlapBuffer[i].attachedRigidbody;
            if (body == null || body.isKinematic || !thrown.Add(body) || body.GetComponent<Player>() != null) continue;
            if (WorldFx.HasAuthority(body)) Throw(body, centre);
        }
    }

    private void Throw(Rigidbody body, Vector3 centre)
    {
        Vector3 away = body.worldCenterOfMass - centre;
        float distance = away.magnitude;
        if (distance > explosionRadius) return;
        float t = 1f - distance / explosionRadius;
        // Fifth power: half way out is barely above the edge nudge (~6.5 m/s), a quarter of the way in ~23 m/s
        float speed = Mathf.Lerp(edgeSpeed, centreSpeed, t * t * t * t * t) * Mathf.Sqrt(power);
        Vector3 direction = distance > 0.5f ? away / distance : Vector3.up;
        direction = (direction + Vector3.up * 0.5f).normalized; // Up and away
        WorldFx.Push(body, direction * speed, ForceMode.VelocityChange, photonView.OwnerActorNr, "Nuke");
    }

    [PunRPC]
    private void RPC_Disarm()
    {
        if (!isActive) return;
        isActive = false;
        Sfx.At(SfxKind.Pop, transform.position, 1f, 30f);
        Vfx.Burst(transform.position, new Color(0.5f, 1f, 0.5f), 20, 3f, 0.25f, 0.6f);
        if (photonView.IsMine) PhotonNetwork.Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (ringMaterial != null) Destroy(ringMaterial);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, disarmRange);
    }
}
