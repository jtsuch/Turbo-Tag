using Photon.Pun;
using UnityEngine;

/// <summary>
/// Slow Field (Resources/Object/SlowField): a trap. It sits armed (just the small emitter; the field disc hidden) until
/// an enemy of the placer comes within <see cref="radius"/>, then deploys: the purple disc spreads out and lasts
/// <see cref="activeSeconds"/>. Enemies standing in it are slowed to 20% (<see cref="SlowFieldEffect"/>, held while
/// they're inside: one steady card, no repeats). The placer's client is told by the triggering client (RPC to all).
/// </summary>
public class SlowFieldObject : TacticalTrapObject
{
    [TunableParam("Radius", 1f, 15f)]
    [SerializeField] private float radius = 5f;
    [TunableParam("Active Time", 1f, 30f)]
    [SerializeField] private float activeSeconds = 10f;

    private bool active;
    private float activeAt;
    private Transform field;
    private Vector3 fieldScale;
    private float nextPulse;

    protected override void Start()
    {
        base.Start();
        field = transform.Find("Field");
        if (field != null) { fieldScale = field.localScale; field.gameObject.SetActive(false); }
    }

    private AoeDebug aoe;

    private void Update()
    {
        if (AoeDebug.On && aoe == null) aoe = AoeDebug.Cylinder(transform.position, radius * Power, 3f, 3f, new Color(0.75f, 0.45f, 1f), active ? "Slow Field" : "Slow Field (armed: springs here)", 0f, transform);
        var enemy = LocalEnemy;
        bool inside = false;
        if (enemy != null)
        {
            Vector3 off = enemy.transform.position - transform.position;
            inside = Mathf.Abs(off.y) < 3f && new Vector2(off.x, off.z).magnitude <= radius * Power;
        }

        if (!active)
        {
            if (inside) photonView.RPC(nameof(RPC_Deploy), RpcTarget.All); // Sprung: everyone sees it open
            return;
        }

        float age = Time.time - activeAt;
        float grow = Mathf.Clamp01(age / 0.25f); // Spreads out from the emitter
        if (field != null) field.localScale = new Vector3(fieldScale.x * grow, fieldScale.y, fieldScale.z * grow);
        if (Time.time >= nextPulse)
        {
            nextPulse = Time.time + 1f;
            Vfx.Ring(transform.position + Vector3.up * 0.1f, new Color(0.7f, 0.45f, 1f), 36, radius * Power * 1.6f, 0.25f, 0.8f);
        }
        Hold(typeof(SlowFieldEffect), Player.Local, inside && age < activeSeconds);
        if (age >= activeSeconds && photonView.IsMine) Remove();
    }

    [PunRPC]
    private void RPC_Deploy()
    {
        if (active) return;
        active = true;
        activeAt = Time.time;
        if (field != null) field.gameObject.SetActive(true);
        if (aoe != null) Destroy(aoe.gameObject); // Relabelled
        Sfx.At(SfxKind.ZapDown, transform.position, 1f, 40f, 0.6f);
        Vfx.Ring(transform.position + Vector3.up * 0.1f, new Color(0.7f, 0.45f, 1f), 48, radius * Power * 2f, 0.4f, 0.5f);
    }
}
