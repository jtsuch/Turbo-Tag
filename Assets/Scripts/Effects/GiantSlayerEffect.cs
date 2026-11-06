using UnityEngine;

/// <summary>Boon: twice the size, and anyone you run into is flung away.</summary>
public class GiantSlayerEffect : PlayerEffect
{
    public override string DisplayName => "Giant Slayer";
    public override string Description => "Huge, and you fling whoever you hit";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        player.Modifiers.Set(Stat.Size, this, 2f);
    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;
        player.Modifiers.Remove(Stat.Size, this);
    }

    private readonly System.Collections.Generic.Dictionary<int, float> flung = new();

    private void Update()
    {
        if (!IsLocalEffect || player.IsCaught || !player.TryGetComponent(out DonutNet net)) return;
        foreach (var p in Player.All)
        {
            if (p == null || p == player || p.IsDownAfterTag) continue;
            Vector3 to = p.transform.position - player.transform.position;
            if (to.magnitude > 2.4f) continue;
            if (flung.TryGetValue(p.ActorNumber, out float at) && Time.time - at < 1f) continue;
            flung[p.ActorNumber] = Time.time;
            Vector3 dir = new Vector3(to.x, 0f, to.z).normalized;
            net.Fling(p, dir * 22f + Vector3.up * 12f);
            Sfx.At(SfxKind.Boing, p.transform.position + Vector3.up, 1f, 30f, 0.7f);
        }
    }
}
