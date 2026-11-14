using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// The local player's weapons and, in modes with an economy (<see cref="GameMode.UsesEconomy"/>: Search &amp; Destroy),
/// their credits, bought shield and ability upgrades. Added next to the GameModeManager in every gun mode.
///   - Slots: 1 the knife (always), 2 the secondary (the free Pebble unless another pistol was bought or picked up),
///     3 the primary (if any). Each gun keeps its ammo while put away.
///   - The shop (<see cref="ShopMenu"/>, the Shop key, B): with an economy, open only during the pre-round; left click
///     buys, right click sells back something bought this pre-round (a full refund). Without one, the guns are free
///     picks: it opens on every spawn for the Gun Pick Time and remembers the picks.
///   - Economy (owner-side, from synced state, like the rest of the mode's queries): +<see cref="Economy.Kill"/> a
///     kill, a round's win/loss money when it ends (losses grow with the streak), +<see cref="Economy.Plant"/> to every
///     attacker on a plant, capped at <see cref="Economy.Max"/>; every half starts at <see cref="Economy.Start"/> with
///     the starting kit and no upgrades. Survivors keep their guns and what's left of their shield; dying drops the
///     best gun (<see cref="GunDrops"/>) and loses the shield.
///   - Ability upgrades: up to two a half per ability; what each does is AbilityUpgrades' table (bar length,
///     strength or uses a round).
/// </summary>
public class Loadout : MonoBehaviour
{
    public static Loadout Instance { get; private set; }

    private const string PrimaryPref = "LastPrimary", SecondaryPref = "LastSecondary";
    /// <summary>Taking a gun out (a weapon switch, after a Teleport): no shot for this long, and the gun rises into view.
    /// The knife's; secondaries take 20% longer, primaries twice as long (Justin, Nov 14).</summary>
    public const float DrawTime = 0.35f;

    public static float DrawTimeFor(GunInfo gun) => gun == null ? DrawTime : gun.Slot switch
    {
        GunSlot.Primary   => DrawTime * 2f,
        GunSlot.Secondary => DrawTime * 1.2f,
        _                 => DrawTime,
    };

    public string  Primary   { get; private set; }                         // Null: none
    public string  Secondary { get; private set; } = GunCatalog.DefaultId;
    public GunSlot Slot      { get; private set; } = GunSlot.Secondary;
    public int     Credits   { get; private set; }

    private enum Kind { Gun, Shield, Upgrade }
    private sealed class Purchase { public Kind Kind; public string Id; public int Price; public float PrevArmor; }

    private readonly Dictionary<string, int> ammo = new();      // Rounds left in guns that are put away
    private readonly Dictionary<string, int> upgrades = new();  // Ability id → upgrades bought this half
    private readonly List<Purchase> bought = new();             // This pre-round's purchases (sellable)
    private GameModeManager manager;
    private CombatManager subscribedCombat;
    private int handledRound = -1, paidRound = -1, plantPaidRound = -1;
    private bool started;
    private float freePickUntil;

    /// <summary>Seconds left to pick free guns after a spawn (modes without an economy).</summary>
    public float FreePickLeft => freePickUntil - Time.time;

    /// <summary>Last credits change (amount, when), for the HUD's "+3000".</summary>
    public (int amount, string why, float at) LastIncome { get; private set; } = (0, "", -99f);

    private GameMode Mode => manager != null ? manager.Mode : null;
    public bool HasEconomy => Mode != null && Mode.UsesEconomy;
    private static Player Local => Player.Local;

    // A role with a set gun (Hunter mode) gets no slots and no shop
    private bool Fixed => Local != null && Mode != null && Mode.FixedGun(Local.Role) != null;

    /// <summary>Whether the shop may be open now.</summary>
    public bool CanShop =>
        Local != null && !Local.IsCaught && !Fixed && manager != null &&
        (HasEconomy ? manager.Phase == MatchPhase.Countdown : ShopAnytime || Time.time < freePickUntil);

    /// <summary>The shop opens with the Shop key at any time (Freeplay), not by itself on spawning.</summary>
    public bool ShopAnytime => Mode != null && Mode.ShopAnytime && !HasEconomy;

    private void Awake()
    {
        Instance = this;
        manager = GetComponent<GameModeManager>();
        ShopMenu.Create();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (subscribedCombat != null) subscribedCombat.Killed -= HandleKilled;
    }

    // ─── Spawns, deaths, rounds ───────────────────────────────────────────────

    /// <summary>Local player (re)spawned or a new round began: the kit in hand (a set gun, or the slots).</summary>
    public void OnSpawn()
    {
        var local = Local;
        if (local == null || local.IsCaught) return;
        string fixedGun = Mode != null ? Mode.FixedGun(local.Role) : null;
        if (fixedGun != null)
        {
            local.Gun.Equip(fixedGun == "" ? null : fixedGun);
            ShopMenu.Close();
            return;
        }
        ammo.Clear();   // Every life and round starts with full magazines
        local.Gun.Equip(null);   // ...including the gun in hand (Select would otherwise save its rounds back)
        if (!HasEconomy)
        {
            Primary   = Remembered(PrimaryPref, "AR", GunSlot.Primary);
            Secondary = Remembered(SecondaryPref, GunCatalog.DefaultId, GunSlot.Secondary);
            if (!ShopAnytime)
            {
                freePickUntil = Time.time + MatchRules.GunPickTime;
                ShopMenu.Open();
            }
        }
        Select(Primary != null ? GunSlot.Primary : GunSlot.Secondary, 0f);
        ApplyAbilityLevels();
    }

    /// <summary>Local player died: with an economy the best gun drops where they fell, and the kit and shield are lost.</summary>
    public void OnCaught()
    {
        var local = Local;
        ShopMenu.Close();
        if (local == null) return;
        if (HasEconomy)
        {
            string drop = Primary ?? (Secondary != GunCatalog.DefaultId ? Secondary : null);
            if (drop != null && GunDrops.Instance != null) GunDrops.Instance.RequestDrop(drop, local.transform.position);
            Primary = null;
            Secondary = GunCatalog.DefaultId;
            local.Health.Armor = 0f;
        }
        local.Gun.Equip(null);
    }

    private void Update()
    {
        if (subscribedCombat == null && CombatManager.Instance != null)
        {
            subscribedCombat = CombatManager.Instance;
            subscribedCombat.Killed += HandleKilled;
        }
        var local = Local;
        if (local == null || manager == null) return;

        if (HasEconomy) UpdateEconomy(local);
        UpdateKeys(local);
    }

    private void UpdateEconomy(Player local)
    {
        int round = NetProps.Room(RoomKeys.Round, 0);
        var phase = manager.Phase;
        // A new round (read from the room, which carries the round number with its phase)
        if (phase == MatchPhase.Countdown && round != handledRound && NetProps.Room(RoomKeys.Phase, 0) == (int)MatchPhase.Countdown)
        {
            handledRound = round;
            bool newHalf = round <= 1 || round == MatchRules.RoundsToWin;
            if (newHalf || !started)
            {
                started = true;
                SetCredits(Economy.Start);
                upgrades.Clear();
                Primary = null;
                Secondary = GunCatalog.DefaultId;
                local.Health.Armor = 0f;
            }
            bought.Clear();
            if (!local.IsCaught) OnSpawn();
        }
        // The round's money once it's decided (the winner and history arrive with the RoundEnd phase)
        if (phase == MatchPhase.RoundEnd && round != paidRound && SearchDestroyMode.RoundWinner != ""
            && SearchDestroyMode.History.Length >= round && PlayerRoles.IsTeam(local.Role))
        {
            paidRound = round;
            string mine = local.Role == PlayerRole.TeamRed ? "R" : "B";
            string history = SearchDestroyMode.History;
            bool won = history.Length > 0 && history[^1].ToString() == mine;
            if (won) Earn(Economy.Win, "ROUND WON");
            else
            {
                // Losses in a row this half (the streak starts over at half time, like the credits)
                int halfStart = round > SearchDestroyMode.HalfTime ? SearchDestroyMode.HalfTime : 0;
                int streak = 0;
                for (int i = history.Length - 1; i >= halfStart && history[i].ToString() != mine; i--) streak++;
                Earn(Economy.Loss[Mathf.Clamp(streak, 1, Economy.Loss.Length) - 1], "ROUND LOST");
            }
        }
        // A plant pays every attacker
        if (phase == MatchPhase.Active && round != plantPaidRound && SearchDestroyMode.Bomb == SearchDestroyMode.BombState.Planted
            && SearchDestroyMode.IsAttacker(local.Role))
        {
            plantPaidRound = round;
            Earn(Economy.Plant, "BOMB PLANTED");
        }
    }

    private void HandleKilled(KillInfo info)
    {
        if (!HasEconomy || Local == null || manager.Phase != MatchPhase.Active) return;
        int me = PhotonNetwork.LocalPlayer.ActorNumber;
        if (info.Killer != me || info.Victim == me) return;
        var victimRole = PlayerRoles.Get(info.Victim);
        if (Mode.SameTeam(Local.Role, victimRole)) return;
        Earn(Economy.Kill, "KILL");
    }

    private void Earn(int amount, string why)
    {
        SetCredits(Credits + amount);
        LastIncome = (amount, why, Time.time);
    }

    private void SetCredits(int value)
    {
        Credits = Mathf.Clamp(value, 0, Economy.Max);
        if (PhotonNetwork.InRoom) NetProps.SetLocal(PlayerKeys.Credits, Credits);
    }

    // ─── Slots ────────────────────────────────────────────────────────────────

    private void UpdateKeys(Player local)
    {
        if (local.IsCaught || Fixed || local.Gun == null) return;
        if (Input.GetKeyDown(Keybinds.Get(InputAction.Shop)) && !PauseMenuManager.IsPaused && !EmoteMenu.IsOpen && !PingWheel.IsOpen)
        {
            if (ShopMenu.IsOpen) ShopMenu.Close();
            else if (CanShop) ShopMenu.Open();
            else if (HasEconomy && HUDManager.Instance != null) HUDManager.Instance.ShowBanner("The shop is open in the pre-round", 1.2f);
        }
        if (PauseMenuManager.IsPaused || EmoteMenu.IsOpen || PingWheel.IsOpen) return;
        if (Input.GetKeyDown(Keybinds.Get(InputAction.WeaponKnife)))     Switch(GunSlot.Knife);
        if (Input.GetKeyDown(Keybinds.Get(InputAction.WeaponSecondary))) Switch(GunSlot.Secondary);
        if (Input.GetKeyDown(Keybinds.Get(InputAction.WeaponPrimary)) && Primary != null) Switch(GunSlot.Primary);
    }

    private void Switch(GunSlot slot)
    {
        SearchDestroyController.PutBombAway();   // A weapon key takes the gun back out instead of the bomb
        if (slot == Slot && Local.Gun.Current != null) return;
        Select(slot, DrawTime);
        Sfx.Here(SfxKind.Snap, 0.3f, slot == GunSlot.Knife ? 1.6f : 1.1f);
    }

    public string GunIn(GunSlot slot) => slot switch
    {
        GunSlot.Knife     => GunCatalog.KnifeId,
        GunSlot.Secondary => Secondary,
        _                 => Primary,
    };

    // Takes a slot's gun in hand with the rounds it had when put away
    private void Select(GunSlot slot, float draw)
    {
        var local = Local;
        if (local == null) return;
        var held = local.Gun.Current;
        if (held != null && !held.Melee) ammo[held.Id] = local.Gun.Ammo;
        string id = GunIn(slot);
        if (id == null) { slot = GunSlot.Secondary; id = Secondary; }
        Slot = slot;
        if (draw > 0f) draw = DrawTimeFor(GunCatalog.Find(id));
        local.Gun.Equip(id, ammo.TryGetValue(id, out int left) ? left : -1, draw);
    }

    private static string Remembered(string pref, string fallback, GunSlot slot)
    {
        var gun = GunCatalog.Find(PlayerPrefs.GetString(pref, fallback));
        return gun != null && gun.Slot == slot ? gun.Id : fallback;
    }

    // ─── Shop: guns ───────────────────────────────────────────────────────────

    public bool Owns(GunInfo gun) => gun.Id == Primary || gun.Id == Secondary || gun.Slot == GunSlot.Knife;
    public bool BoughtThisRound(GunInfo gun) => bought.Exists(p => p.Kind == Kind.Gun && p.Id == gun.Id);
    public int PriceOf(GunInfo gun) => HasEconomy ? gun.Price : 0;

    /// <summary>Shop: left click on a gun. Free modes: pick it. Economy: buy it (a gun it replaces that was bought this
    /// pre-round is refunded; one owned from before drops at your feet).</summary>
    public void Buy(GunInfo gun)
    {
        var local = Local;
        if (local == null || gun.Slot == GunSlot.Knife || Owns(gun)) return;
        if (!HasEconomy)
        {
            SetSlot(gun.Slot, gun.Id);
            PlayerPrefs.SetString(gun.Slot == GunSlot.Primary ? PrimaryPref : SecondaryPref, gun.Id);
            Select(gun.Slot, 0f);
            UISound.Play(UISound.Kind.Confirm);
            return;
        }
        string previous = GunIn(gun.Slot);
        var refund = previous != null ? bought.Find(p => p.Kind == Kind.Gun && p.Id == previous) : null;
        int cost = gun.Price - (refund != null ? refund.Price : 0);
        if (cost > Credits) { UISound.Play(UISound.Kind.Back); return; }
        if (refund != null) bought.Remove(refund);
        else if (previous != null && previous != GunCatalog.DefaultId && GunDrops.Instance != null)
            GunDrops.Instance.RequestDrop(previous, local.transform.position + local.transform.forward * 0.8f);
        SetCredits(Credits - cost);
        bought.Add(new Purchase { Kind = Kind.Gun, Id = gun.Id, Price = gun.Price });
        SetSlot(gun.Slot, gun.Id);
        ammo.Remove(gun.Id);
        Select(gun.Slot, 0f);
        UISound.Play(UISound.Kind.Confirm);
    }

    /// <summary>Shop: right click on a gun bought this pre-round: a full refund (the slot goes back to the starting kit).</summary>
    public void Sell(GunInfo gun)
    {
        var p = bought.Find(b => b.Kind == Kind.Gun && b.Id == gun.Id);
        if (p == null) return;
        bought.Remove(p);
        SetCredits(Credits + p.Price);
        SetSlot(gun.Slot, gun.Slot == GunSlot.Primary ? null : GunCatalog.DefaultId);
        if (Slot == gun.Slot) Select(gun.Slot == GunSlot.Primary ? GunSlot.Secondary : gun.Slot, 0f);
        UISound.Play(UISound.Kind.Back);
    }

    private void SetSlot(GunSlot slot, string id)
    {
        if (slot == GunSlot.Primary) Primary = id;
        else if (slot == GunSlot.Secondary) Secondary = id ?? GunCatalog.DefaultId;
    }

    /// <summary>A gun picked up off the floor (GunDrops): into its slot and in hand; what was there drops.</summary>
    public void TakeDrop(string gunId)
    {
        var gun = GunCatalog.Find(gunId);
        var local = Local;
        if (gun == null || local == null || local.IsCaught) return;
        string previous = GunIn(gun.Slot);
        if (previous != null && previous != GunCatalog.DefaultId && GunDrops.Instance != null)
            GunDrops.Instance.RequestDrop(previous, local.transform.position + local.transform.forward * 0.8f);
        SetSlot(gun.Slot, gun.Id);
        ammo.Remove(gun.Id);
        Select(gun.Slot, DrawTime);
        Sfx.Here(SfxKind.Snap, 0.5f, 1.3f);
    }

    /// <summary>Whether a dropped gun would go straight into an empty slot (picked up by walking over it).</summary>
    public bool SlotFree(GunInfo gun) => gun.Slot == GunSlot.Primary ? Primary == null : Secondary == GunCatalog.DefaultId;

    // ─── Shop: shields ────────────────────────────────────────────────────────

    public float Armor => Local != null ? Local.Health.Armor : 0f;
    public bool ShieldBoughtThisRound(int index) => bought.Exists(p => p.Kind == Kind.Shield && p.Id == index.ToString());

    public void BuyShield(int index)
    {
        var local = Local;
        if (local == null || !HasEconomy) return;
        var (_, amount, price) = Economy.Shields[index];
        if (Armor >= amount) { UISound.Play(UISound.Kind.Back); return; }
        var refund = bought.Find(p => p.Kind == Kind.Shield);
        int cost = price - (refund != null ? refund.Price : 0);
        if (cost > Credits) { UISound.Play(UISound.Kind.Back); return; }
        float before = refund != null ? refund.PrevArmor : Armor;
        if (refund != null) bought.Remove(refund);
        SetCredits(Credits - cost);
        bought.Add(new Purchase { Kind = Kind.Shield, Id = index.ToString(), Price = price, PrevArmor = before });
        local.Health.Armor = amount;
        UISound.Play(UISound.Kind.Confirm);
    }

    public void SellShield(int index)
    {
        var p = bought.Find(b => b.Kind == Kind.Shield && b.Id == index.ToString());
        if (p == null || Local == null) return;
        bought.Remove(p);
        SetCredits(Credits + p.Price);
        Local.Health.Armor = p.PrevArmor;
        UISound.Play(UISound.Kind.Back);
    }

    // ─── Shop: ability upgrades ───────────────────────────────────────────────

    public int UpgradeLevel(string abilityId) => upgrades.TryGetValue(abilityId, out int n) ? n : 0;
    public bool UpgradeBoughtThisRound(string abilityId) => bought.Exists(p => p.Kind == Kind.Upgrade && p.Id == abilityId);

    /// <summary>What the next upgrade of <paramref name="ability"/> does, for the shop ("bar 6 s → 10.5 s").</summary>
    public string UpgradeEffect(Ability ability) => AbilityUpgrades.Describe(ability, UpgradeLevel(ability.Id));

    public void BuyUpgrade(Ability ability)
    {
        if (!HasEconomy || ability == null) return;
        int level = UpgradeLevel(ability.Id);
        if (level >= Economy.MaxUpgrades) return;
        int price = Economy.UpgradePrice[level];
        if (price > Credits) { UISound.Play(UISound.Kind.Back); return; }
        SetCredits(Credits - price);
        upgrades[ability.Id] = level + 1;
        bought.Add(new Purchase { Kind = Kind.Upgrade, Id = ability.Id, Price = price });
        ApplyAbilityLevels();
        ability.RefillNow();
        UISound.Play(UISound.Kind.Confirm);
    }

    public void SellUpgrade(Ability ability)
    {
        if (ability == null) return;
        var p = bought.FindLast(b => b.Kind == Kind.Upgrade && b.Id == ability.Id);
        if (p == null) return;
        bought.Remove(p);
        SetCredits(Credits + p.Price);
        upgrades[ability.Id] = Mathf.Max(0, UpgradeLevel(ability.Id) - 1);
        ApplyAbilityLevels();
        UISound.Play(UISound.Kind.Back);
    }

    /// <summary>
    /// The abilities' sizes for this mode: with an economy every Quick/Throw/Trap ability has 1 + its upgrades uses a
    /// round and every Basic bar is 1 + 40% per upgrade of the prefab's; without one, the prefabs' own numbers.
    /// </summary>
    public void ApplyAbilityLevels()
    {
        var local = Local;
        if (local == null || local.Abilities == null) return;
        foreach (var (ability, _) in local.Abilities.Usable())
            AbilityUpgrades.Apply(ability, UpgradeLevel(ability.Id), HasEconomy);
    }
}
