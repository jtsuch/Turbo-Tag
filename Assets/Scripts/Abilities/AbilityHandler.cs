using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Owns every Ability component on a player. Equips the owner's loadout (enabling only those
/// abilities, on every client so their visuals run remotely), routes input from InputHandler,
/// enforces "one aim/place ability at a time", and manages cheat-mode extra key bindings.
/// Also hosts the single set of ability RPC endpoints for the player, avoiding PUN's
/// "method found N times" error when several abilities share a base class.
/// Attach to: ThePlayer prefab root.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class AbilityHandler : MonoBehaviourPunCallbacks
{
    public event Action LoadoutChanged;

    private readonly Dictionary<string, Ability>  byId           = new();
    private readonly Ability[]                    equipped       = new Ability[4];
    private readonly Dictionary<string, KeyCode>  customBindings = new();
    private readonly HashSet<string>              cheatAbilities = new();
    private Ability active;

    public IEnumerable<Ability>                  All            => byId.Values;
    public IReadOnlyDictionary<string, KeyCode>  CustomBindings => customBindings;

    public Ability GetEquipped(AbilitySlot slot) => equipped[(int)slot];

    public Ability Find(string id) =>
        id != null && byId.TryGetValue(id, out var ability) ? ability : null;

    public bool IsEquipped(Ability ability) => Array.IndexOf(equipped, ability) >= 0;

    /// <summary>
    /// Every ability this player can use right now with its key: the starting abilities that have a key,
    /// then any extras bound in Cheats, in slot order. The HUD hotbar and timers show exactly this list.
    /// </summary>
    public List<(Ability ability, KeyCode key)> Usable()
    {
        var list = new List<(Ability, KeyCode)>();
        for (int i = 0; i < equipped.Length; i++)
        {
            KeyCode key = Keybinds.ForSlot((AbilitySlot)i);
            if (equipped[i] != null && key != KeyCode.None) list.Add((equipped[i], key)); // Unbound = unusable
        }

        var extras = new List<(Ability, KeyCode)>();
        foreach (var pair in customBindings)
        {
            var ability = Find(pair.Key);
            if (ability != null && !IsEquipped(ability)) extras.Add((ability, pair.Value));
        }
        extras.Sort((x, y) => x.Item1.Slot != y.Item1.Slot
            ? x.Item1.Slot.CompareTo(y.Item1.Slot)
            : string.CompareOrdinal(x.Item1.Id, y.Item1.Id));
        list.AddRange(extras);
        return list;
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        foreach (var ability in GetComponents<Ability>())
        {
            if (!byId.ContainsKey(ability.Id)) byId.Add(ability.Id, ability);
            else Debug.LogWarning($"[AbilityHandler] Duplicate ability component '{ability.Id}' ignored.");
        }
    }

    private void Start()
    {
        if (photonView.IsMine) Keybinds.ResetMatchBindings(); // Cheats key changes last one match
        if (photonView.IsMine && MatchRules.CheatsEnabled) RestoreCustomBindings();
        if (photonView.IsMine) Keybinds.AbilityKeyClaimed += ReleaseCustomBindingsUsing;
        ApplyLoadout();
    }

    private void OnDestroy()
    {
        Keybinds.AbilityKeyClaimed -= ReleaseCustomBindingsUsing;
    }

    // ─── Loadout ──────────────────────────────────────────────────────────────

    private void ApplyLoadout()
    {
        var loadout = AbilityLoadout.Of(photonView.Owner);
        for (int i = 0; i < equipped.Length; i++)
            equipped[i] = Find(loadout[(AbilitySlot)i]);

        cheatAbilities.Clear();
        foreach (var id in NetProps.Of(photonView.Owner, PlayerKeys.CheatAbilities, new string[0]))
            cheatAbilities.Add(id);

        RefreshEnabled();
        LoadoutChanged?.Invoke();
    }

    private void RefreshEnabled()
    {
        foreach (var ability in byId.Values)
        {
            bool shouldEnable = IsEquipped(ability) || cheatAbilities.Contains(ability.Id);
            if (ability.enabled != shouldEnable) ability.enabled = shouldEnable;
        }
        if (active != null && !active.enabled) active = null;
    }

    // ─── Input routing (local player) ─────────────────────────────────────────

    public void Use(Ability ability, AbilityInputEvent inputEvent)
    {
        if (ability == null || !ability.enabled) return;

        // Starting a new aim/place ability cancels the one already in progress
        if (inputEvent == AbilityInputEvent.Down && ability.UsesActionKey
            && active != null && active != ability && active.IsAwaitingAction)
        {
            active.Cancel();
            active = null;
        }

        ability.HandleInput(inputEvent);

        if (ability.IsAwaitingAction)  active = ability;
        else if (active == ability)    active = null;
    }

    public void ConfirmDown()
    {
        if (active != null && active.IsAwaitingAction) active.OnActionDown();
    }

    public void ConfirmUp()
    {
        if (active == null || !active.IsAwaitingAction) return;
        active.OnActionUp();
        if (!active.IsAwaitingAction) active = null;
    }

    /// <summary>Ends every in-progress ability (pause, frozen, caught, teleport).</summary>
    public void CancelAll()
    {
        foreach (var ability in byId.Values)
            if (ability.enabled) ability.Cancel();
        active = null;
    }

    // ─── Cheat bindings (local player) ────────────────────────────────────────

    /// <summary>Binds an extra ability to a key. Requires cheats; <see cref="KeyCode.None"/> unbinds.</summary>
    public void SetCustomBinding(string id, KeyCode key)
    {
        if (!photonView.IsMine || Find(id) == null) return;
        if (key != KeyCode.None && !MatchRules.CheatsEnabled) return;

        if (key == KeyCode.None)
        {
            customBindings.Remove(id);
        }
        else
        {
            // A key drives one ability: other extras and any starting slot on this key let go
            foreach (var pair in new List<KeyValuePair<string, KeyCode>>(customBindings))
            {
                if (pair.Value != key || pair.Key == id) continue;
                customBindings.Remove(pair.Key);
                Keybinds.SetCustom(pair.Key, KeyCode.None);
            }
            Keybinds.ReleaseSlotsUsing(key);
            customBindings[id] = key;
        }

        Keybinds.SetCustom(id, key);
        PublishCheatAbilities();
    }

    /// <summary>Binds (or with <see cref="KeyCode.None"/> unbinds) a starting slot's key. Other uses of the key let go.</summary>
    public void SetSlotBinding(AbilitySlot slot, KeyCode key)
    {
        if (!photonView.IsMine) return;
        Keybinds.Set(Keybinds.SlotAction(slot), key, forMatch: true);
    }

    // A starting slot just took this key: cheat extras on it are dropped
    private void ReleaseCustomBindingsUsing(KeyCode key)
    {
        bool changed = false;
        foreach (var pair in new List<KeyValuePair<string, KeyCode>>(customBindings))
        {
            if (pair.Value != key) continue;
            customBindings.Remove(pair.Key);
            Keybinds.SetCustom(pair.Key, KeyCode.None);
            changed = true;
        }
        if (changed) PublishCheatAbilities();
    }

    // Brings back this match's extra bindings (after cheats are switched back on). A key drives one
    // ability: an extra on a starting slot's key, or one an earlier extra already took, is dropped.
    private void RestoreCustomBindings()
    {
        customBindings.Clear();
        var used = new HashSet<KeyCode>();
        foreach (AbilitySlot slot in Enum.GetValues(typeof(AbilitySlot)))
            used.Add(Keybinds.ForSlot(slot));

        foreach (var id in byId.Keys)
        {
            KeyCode key = Keybinds.GetCustom(id);
            if (key == KeyCode.None) continue;
            if (!used.Add(key)) { Keybinds.SetCustom(id, KeyCode.None); continue; }
            customBindings[id] = key;
        }
        PublishCheatAbilities();
    }

    // Undo everything the Cheats tab changed for this player
    private void ClearCheats()
    {
        customBindings.Clear();
        foreach (var ability in byId.Values) ability.ResetTunables();
        GetComponent<Player>().Modifiers.Clear(Cheats.ModifierSource);
        PublishCheatAbilities();
    }

    // Shares which extra abilities are active so remote copies enable them for visuals
    private void PublishCheatAbilities()
    {
        var ids = new string[customBindings.Count];
        customBindings.Keys.CopyTo(ids, 0);
        NetProps.SetLocal(PlayerKeys.CheatAbilities, ids);
    }

    // ─── Photon Callbacks ─────────────────────────────────────────────────────

    public override void OnPlayerPropertiesUpdate(PhotonPlayer target, Hashtable changed)
    {
        if (target != photonView.Owner) return;

        foreach (var key in changed.Keys)
        {
            if (key is string s && (AbilityLoadout.IsLoadoutKey(s) || s == PlayerKeys.CheatAbilities))
            {
                ApplyLoadout();
                return;
            }
        }
    }

    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        if (!photonView.IsMine || !changed.ContainsKey(RuleKeys.CheatsEnabled)) return;

        if (MatchRules.CheatsEnabled) RestoreCustomBindings();
        else                          ClearCheats();
    }

    // ─── Ability RPC endpoints ────────────────────────────────────────────────

    /// <summary>Owner: shows or hides a throwable in hand on remote copies.</summary>
    public void BroadcastHeldVisual(string abilityId, bool visible) =>
        photonView.RPC(nameof(RPC_SetHeldVisual), RpcTarget.Others, abilityId, visible);

    [PunRPC]
    private void RPC_SetHeldVisual(string abilityId, bool visible)
    {
        if (Find(abilityId) is ThrowAbility throwAbility)
            throwAbility.SetHeldVisual(visible);
    }

    /// <summary>Owner: shows a Grow Ray shot (gun out, beam to <paramref name="end"/>) on remote copies.</summary>
    public void BroadcastBeam(Vector3 end) =>
        photonView.RPC(nameof(RPC_Beam), RpcTarget.Others, end);

    [PunRPC]
    private void RPC_Beam(Vector3 end)
    {
        if (Find(nameof(GrowRay)) is GrowRay ray) ray.ShowShot(end);
    }
}
