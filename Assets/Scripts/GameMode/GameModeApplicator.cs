using Photon.Pun;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Applies world-level rules (currently gravity) whenever room properties change, and restores
/// the original values when the level unloads so rules never leak into menus or other levels.
/// Late joiners get the correct state because Start re-applies the current room properties.
/// Attach to: one GameObject per level scene.
/// </summary>
public class GameModeApplicator : MonoBehaviourPunCallbacks
{
    private Vector3 defaultGravity;

    private void Awake() => defaultGravity = Physics.gravity;

    private void Start() => Apply();

    private void OnDestroy() => Physics.gravity = defaultGravity;

    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        if (changed.ContainsKey(RuleKeys.Gravity)) Apply();
    }

    private void Apply()
    {
        Physics.gravity = defaultGravity * MatchRules.GravityMultiplier;
    }
}
