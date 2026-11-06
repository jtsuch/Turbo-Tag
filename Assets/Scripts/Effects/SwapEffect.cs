using System.Collections.Generic;
using UnityEngine;

/// <summary>One-off chaos: you trade places with a random other player (they are moved to where you were).</summary>
public class SwapEffect : PlayerEffect
{
    private string partner = "";

    public override string DisplayName => "Swap";
    public override string Description => string.IsNullOrEmpty(partner) ? "Nobody to swap with" : $"Swapped with {partner}";
    public override bool   IsChaos     => true;

    protected override void OnEffectStart()
    {
        if (!IsLocalEffect) return;
        var others = new List<Player>();
        foreach (var p in Player.All)
            if (p != null && p != player && !p.IsCaught && !p.IsDownAfterTag) others.Add(p);
        if (others.Count == 0 || DonutManager.Instance == null) return;

        var other = others[Random.Range(0, others.Count)];
        partner = other.Owner != null ? other.Owner.NickName : "someone";
        Vector3 mine = player.transform.position, theirs = other.transform.position;
        DonutManager.Instance.SendSwap(other, mine);
        player.Teleport(theirs, other.transform.rotation);
        if (PlayerCam.Local != null) PlayerCam.Local.SnapTo(other.transform.eulerAngles.y);
        Fx.Respawn(theirs);
    }

    protected override void OnEffectEnd() { }
}
