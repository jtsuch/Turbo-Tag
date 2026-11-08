using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Smoke canister (Resources/Object/Smoke): bounces, settles (or 1.5 s pass), then pours out a cloud of grey puffs
/// <see cref="radius"/> across that nobody sees through (inside it too: the puffs are double-sided), for
/// <see cref="seconds"/>. The cloud is built on every screen from one RPC; the thrower removes the canister after.
/// </summary>
public class SmokeObject : NetworkedProjectile
{
    [TunableParam("Radius", 1f, 15f)]
    [SerializeField] private float radius = 5f;
    [TunableParam("Lasts", 2f, 60f)]
    [SerializeField] private float seconds = 14f;

    private Transform cloud;
    private Material puffMaterial;
    private float deployedAt = -1f;

    protected override void OnLaunched()
    {
        if (IsAuthority) StartCoroutine(Deploy());
    }

    private IEnumerator Deploy()
    {
        float start = Time.time;
        yield return new WaitForSeconds(0.4f);
        while (Time.time - start < 1.5f && Body.linearVelocity.magnitude > 0.6f) yield return null;
        photonView.RPC(nameof(RPC_Deploy), RpcTarget.All, transform.position);
        yield return new WaitForSeconds(seconds + 1.5f);
        DestroyNetworked();
    }

    [PunRPC]
    private void RPC_Deploy(Vector3 at)
    {
        HideAndFreeze();
        transform.position = at;
        deployedAt = Time.time;
        Sfx.At(SfxKind.Blow, at, 1f, 45f, 0.6f);
        Sfx.At(SfxKind.Pop, at, 0.7f, 45f, 0.5f);

        cloud = new GameObject("Smoke Cloud").transform;
        cloud.SetParent(transform, false);
        cloud.position = at + Vector3.up * radius * 0.45f;
        puffMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.72f, 0.73f, 0.78f, 0f) };
        var rng = new System.Random(photonView.ViewID); // Same cloud shape on every screen
        for (int i = 0; i < 14; i++)
        {
            var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(puff.GetComponent<Collider>());
            puff.transform.SetParent(cloud, false);
            Vector3 offset = new((float)rng.NextDouble() - 0.5f, ((float)rng.NextDouble() - 0.5f) * 0.6f, (float)rng.NextDouble() - 0.5f);
            puff.transform.localPosition = offset * radius * 1.1f;
            puff.transform.localScale = Vector3.one * radius * (0.9f + (float)rng.NextDouble() * 0.5f);
            var r = puff.GetComponent<Renderer>();
            r.sharedMaterial = puffMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    // Fades in fast, holds, thins out at the end; puffs drift a little
    private void Update()
    {
        if (deployedAt < 0f || puffMaterial == null) return;
        float age = Time.time - deployedAt;
        float alpha = Mathf.Clamp01(age / 0.6f) * Mathf.Clamp01((seconds - age) / 1.5f) * 0.96f;
        var c = puffMaterial.color;
        c.a = alpha;
        puffMaterial.color = c;
        if (cloud != null) cloud.Rotate(0f, 6f * Time.deltaTime, 0f);
    }
}
