using UnityEngine;

/// <summary>Boon (chasing): anyone within 3 m counts as touched (tags, Keep Away steals).</summary>
public class LongArmsEffect : PlayerEffect
{
    public override string DisplayName => "Long Arms";
    public override string Description => "Tag (or steal) from 3 m away";
    public override bool   IsGood      => true;
    public override bool   IsChaos     => false;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;

    }

    protected override void OnEffectEnd()
    {
        if (!IsLocalEffect || player == null) return;

    }

    private float next;

    private void Update()
    {
        var mode = GameModeManager.Instance != null ? GameModeManager.Instance.Mode : null;
        if (!IsLocalEffect || mode == null || Time.time < next || player.IsCaught) return;
        next = Time.time + 0.15f;
        foreach (var p in Player.All)
            if (p != null && p != player && !p.IsDownAfterTag && Vector3.Distance(p.transform.position, player.transform.position) <= 3f)
                mode.OnLocalPlayerContact(player, p);
    }
}
