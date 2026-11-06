using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// The gun in gun modes. The owner picks one per life (gun picker), shoots with the Action key (hitscan from
/// the crosshair) and reloads with the Reload key (or automatically when empty). Hits are decided on the
/// shooter's screen and sent to the target's owner as damage (PlayerHealth). The held gun is the "Gun" player
/// property, so every copy shows it: the built-in pistols for the Pistol, otherwise the gun's own prefab
/// (Resources/Guns) under the camera (owner) or in the right hand (others). Reloads are announced so other
/// screens animate them too (<see cref="GunViewModel"/>).
/// Added by Player on every copy; idle in modes without guns.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class PlayerGun : MonoBehaviourPun
{
    /// <summary>Local: (headshot) after a shot hit a player, for the hit marker.</summary>
    public static event System.Action<bool> LocalHit;

    /// <summary>The gun in hand (owner: set by Equip; other copies: the synced property). Null when none.</summary>
    public GunInfo Current { get; private set; }

    public int  Ammo        { get; private set; }
    public bool IsReloading => Current != null && Time.time < reloadEndsAt;
    /// <summary>Reload progress 0–1, or -1 when not reloading.</summary>
    public float ReloadProgress => IsReloading ? 1f - (reloadEndsAt - Time.time) / Mathf.Max(0.01f, Current.ReloadTime) : -1f;

    private const float HeadFraction = 0.25f; // Top part of the body that counts as the head

    private Player player;
    private float  nextShotAt;
    private float  reloadEndsAt;
    private float  bloom;          // Extra spread from spraying (degrees), shrinking back between shots
    private bool   reloadPending;
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private bool fireBlockedUntilRelease; // An ability used the Action key: no shot until it is let go
    private bool lastTraceHit; // The last Trace stopped on something (for impact sparks)
    private readonly Dictionary<Player, (float damage, bool head)> pending = new();

    // Visuals: first-person (owner) and third-person (others) models per gun id
    private readonly Dictionary<string, GameObject> viewModels  = new();
    private readonly Dictionary<string, GameObject> worldModels = new();
    private GunViewModel pistolView;   // On the built-in first-person pistol
    private GunViewModel pistolWorld;  // On the built-in third-person pistol
    private string killcamGun;         // Owner: gun shown on the killcam view (the killer's)

    private static bool GunsActive => CombatManager.Instance != null;

    private void Awake() => player = GetComponent<Player>();

    private void OnDestroy()
    {
        foreach (var model in worldModels.Values) if (model != null) Destroy(model);
    }

    // ─── Owner API ────────────────────────────────────────────────────────────

    /// <summary>Owner: takes <paramref name="gunId"/> in hand, fully loaded (null or "" puts the gun away).</summary>
    public void Equip(string gunId)
    {
        if (!photonView.IsMine) return;
        Current       = GunCatalog.Find(gunId);
        Ammo          = Current != null ? Current.Magazine : 0;
        reloadEndsAt  = 0f;
        reloadPending = false;
        nextShotAt    = 0f;
        bloom         = 0f;
        if (PhotonNetwork.InRoom) NetProps.SetLocal(PlayerKeys.Gun, Current != null ? Current.Id : "");
    }

    /// <summary>Owner: shows <paramref name="gunId"/> on the killcam view (null hides it).</summary>
    public void ShowKillcamGun(string gunId) => killcamGun = GunCatalog.Find(gunId) != null ? gunId : null;

    /// <summary>Owner: the killcam replayed a shot.</summary>
    public void KillcamKick()
    {
        var model = ViewModel(killcamGun);
        if (model != null) model.Kick();
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Update()
    {
        if (!photonView.IsMine)
            Current = GunsActive ? GunCatalog.Find(NetProps.Of(photonView.Owner, PlayerKeys.Gun, "")) : null;

        bool shown = GunsActive && Current != null && !player.IsCaught && !player.IsDownAfterTag;
        if (player.Animation != null)
        {
            player.Animation.HoldingGun    = shown;
            player.Animation.HoldingPistol = shown && Current.Model == null;
        }

        if (photonView.IsMine && Current != null) bloom = Mathf.MoveTowards(bloom, 0f, Current.BloomRecovery * Time.deltaTime);
        if (!photonView.IsMine || !shown) return;
        if (reloadPending && !IsReloading) { Ammo = Current.Magazine; reloadPending = false; }

        // A click that places a trap or throws something never fires too: while an ability waits for the
        // Action key, firing is blocked until that key is let go (the place/throw can land before this runs)
        KeyCode action = Keybinds.Get(InputAction.Action);
        if (player.Abilities != null && player.Abilities.IsAwaitingAction) fireBlockedUntilRelease = true;
        else if (!Input.GetKey(action)) fireBlockedUntilRelease = false;
        if (fireBlockedUntilRelease) return;

        if (!CanShoot) return;

        if (Input.GetKeyDown(Keybinds.Get(InputAction.Reload)) && Ammo < Current.Magazine && !IsReloading)
            StartReload();

        KeyCode fire = Keybinds.Get(InputAction.Action);
        bool wantsShot = Current.Automatic ? Input.GetKey(fire) : Input.GetKeyDown(fire);
        if (wantsShot && !IsReloading && Time.time >= nextShotAt && GunPicker.ClosedFrame != Time.frameCount)
        {
            if (Ammo > 0) Shoot();
            else StartReload();
        }
    }

    private void LateUpdate()
    {
        if (photonView.IsMine) UpdateFirstPerson();
        else UpdateThirdPerson();
    }

    private bool CanShoot
    {
        get
        {
            var manager = GameModeManager.Instance;
            return manager != null && manager.Phase == MatchPhase.Active && !manager.Clock.IsExpired
                && !PauseMenuManager.IsPaused && !GunPicker.IsOpen && !Killcam.IsPlaying
                && !player.IsHeld && !player.MovementLocked
                && !player.Abilities.IsAwaitingAction
                && PlayerCam.Local != null && !PlayerCam.Local.HasViewOverride;
        }
    }

    // ─── Reload ───────────────────────────────────────────────────────────────

    private void StartReload()
    {
        reloadEndsAt  = Time.time + Current.ReloadTime;
        reloadPending = true;
        Sfx.Here(SfxKind.Reload, 0.6f);
        photonView.RPC(nameof(RPC_Reload), RpcTarget.Others);
    }

    [PunRPC]
    private void RPC_Reload()
    {
        var gun = GunCatalog.Find(NetProps.Of(photonView.Owner, PlayerKeys.Gun, ""));
        if (gun != null) reloadEndsAt = Time.time + gun.ReloadTime;
        Sfx.At(SfxKind.Reload, transform.position + Vector3.up, 0.6f, 20f);
    }

    // ─── Shooting (owner) ─────────────────────────────────────────────────────

    private void Shoot()
    {
        var gun = Current;
        Ammo--;
        nextShotAt = Time.time + gun.FireInterval;

        Transform view   = PlayerCam.Local.Camera.transform;
        // Perfect on the first shot; spraying blooms; air/moving error only with the Movement Inaccuracy rule
        float     spread = gun.Spread + bloom + (MatchRules.MovementSpread && !player.IsGrounded ? gun.AirSpread : 0f);
        bloom = Mathf.Min(gun.MaxBloom, bloom + gun.Bloom);
        float     damage = gun.Damage * MatchRules.DamageScale;

        pending.Clear();
        bool anyHit = false, anyHead = false;
        Vector3 firstEnd = view.position + view.forward * gun.Range;
        Vector3 muzzle   = Muzzle(true).position;

        for (int p = 0; p < Mathf.Max(1, gun.Pellets); p++)
        {
            Vector3 dir = Spread(view, spread);
            Vector3 end = Trace(view.position, dir, gun.Range, out Player target, out bool head);
            if (p == 0) firstEnd = end;
            GunFx.Tracer(muzzle, end);
            if (lastTraceHit) Fx.Impact(end, view.position, target != null);

            if (target == null) continue;
            anyHit = true;
            anyHead |= head;
            float amount = damage * (head ? gun.HeadshotMultiplier : 1f);
            pending.TryGetValue(target, out var sum);
            pending[target] = (sum.damage + amount, sum.head || head);
        }

        foreach (var pair in pending)
            pair.Key.Health.RequestDamage(pair.Value.damage, gun.Id, pair.Value.head);

        GunFx.Flash(muzzle);
        Fx.Shot(gun.Id, muzzle, local: true);
        var model = ViewModel(gun.Id);
        if (model != null) model.Kick();
        PlayerCam.Local.AddRecoil(gun.Recoil);
        if (Killcam.Instance != null) Killcam.Instance.RecordShot(player.ActorNumber, firstEnd);
        photonView.RPC(nameof(RPC_ShotFx), RpcTarget.Others, firstEnd, gun.Pellets);
        if (anyHit) LocalHit?.Invoke(anyHead);

        if (Ammo <= 0) StartReload();
    }

    private static Vector3 Spread(Transform view, float degrees)
    {
        if (degrees <= 0f) return view.forward;
        Vector2 offset = Random.insideUnitCircle * Mathf.Tan(degrees * Mathf.Deg2Rad);
        return (view.forward + view.right * offset.x + view.up * offset.y).normalized;
    }

    // The nearest thing the bullet meets, skipping the shooter's own colliders
    private Vector3 Trace(Vector3 origin, Vector3 dir, float range, out Player target, out bool head)
    {
        target = null;
        head   = false;
        int count = Physics.RaycastNonAlloc(origin, dir, hits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        int   hitIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].distance >= best) continue;
            if (hits[i].collider.GetComponentInParent<Player>() == player) continue;
            best = hits[i].distance;
            hitIndex = i;
        }
        lastTraceHit = hitIndex >= 0;
        if (hitIndex < 0) return origin + dir * range;

        var hit = hits[hitIndex];
        var other = hit.collider.GetComponentInParent<Player>();
        if (other != null && !other.IsDownAfterTag)
        {
            target = other;
            float feet = other.transform.position.y;
            head = hit.point.y >= other.TopY - (other.TopY - feet) * HeadFraction;
        }
        return hit.point;
    }

    // ─── Effects on other screens ─────────────────────────────────────────────

    [PunRPC]
    private void RPC_ShotFx(Vector3 end, int pellets)
    {
        if (Killcam.Instance != null) Killcam.Instance.RecordShot(player.ActorNumber, end);
        if (Killcam.IsPlaying) return; // The killcam replays the killer's shots itself

        var model = WorldModel(Current != null ? Current.Id : null);
        if (model != null) model.Kick();

        Vector3 from = Muzzle(false).position;
        Fx.Shot(Current != null ? Current.Id : null, from, local: false);
        if (Vector3.Distance(from, end) < (Current != null ? Current.Range : 150f) - 1f) Fx.Impact(end, from, false);
        GunFx.Tracer(from, end);
        for (int i = 1; i < pellets; i++)
            GunFx.Tracer(from, end + Random.insideUnitSphere * Vector3.Distance(from, end) * 0.05f);
        GunFx.Flash(from);
    }

    // ─── Models ───────────────────────────────────────────────────────────────

    /// <summary>Muzzle of the gun in hand: the first-person model (owner's view) or the third-person one.</summary>
    public Transform Muzzle(bool firstPerson)
    {
        string id = firstPerson && killcamGun != null ? killcamGun : Current != null ? Current.Id : null;
        var gun = GunCatalog.Find(id);
        if (gun != null && gun.Model != null)
        {
            var model = firstPerson ? ViewModel(id) : WorldModel(id);
            if (model != null)
            {
                var tip = model.transform.Find("GunTip");
                return tip != null ? tip : model.transform;
            }
        }
        var pistolTip = player.Animation != null ? player.Animation.GunTip(firstPerson) : null;
        return pistolTip != null ? pistolTip : transform;
    }

    // Owner: the gun under the camera (the killer's gun during their killcam)
    private void UpdateFirstPerson()
    {
        bool alive = GunsActive && Current != null && !player.IsCaught;
        string id  = killcamGun ?? (alive ? Current.Id : null);
        var gun    = GunCatalog.Find(id);

        if (player.Animation != null) player.Animation.ForceFirstPersonGun = killcamGun != null && gun != null && gun.Model == null;

        foreach (var pair in viewModels)
            if (pair.Value != null) pair.Value.SetActive(gun != null && pair.Key == gun.Id);

        var model = ViewModel(id);
        if (model != null) model.Apply(killcamGun != null ? -1f : ReloadProgress);
    }

    // Others: the gun in the right hand
    private void UpdateThirdPerson()
    {
        bool shown = GunsActive && Current != null && !player.IsDownAfterTag && !player.IsCaught;
        string id  = shown ? Current.Id : null;
        foreach (var pair in worldModels)
            if (pair.Value != null) pair.Value.SetActive(pair.Key == id && !Killcam.IsPlaying);

        var model = WorldModel(id);
        if (model == null) return;

        // Custom guns sit in the hand (where the built-in pistol would be), pointing where the player looks
        var gun = Current;
        if (gun.Model != null)
        {
            var hand = player.Animation != null ? player.Animation.ThirdPersonPistol : null;
            float pitch = player.Animation != null ? player.Animation.LookPitch : 0f;
            Quaternion aim = transform.rotation * Quaternion.Euler(pitch, 0f, 0f); // Body rotation: follows Free Spin flips
            Vector3 grip = hand != null ? hand.transform.position : transform.position + transform.rotation * (Vector3.up * player.EyeHeight * 0.75f);
            model.SetBase(grip + aim * Vector3.forward * (gun.WorldLength * 0.15f), aim);
        }
        model.Apply(ReloadProgress);
    }

    private GunViewModel ViewModel(string id)
    {
        var gun = GunCatalog.Find(id);
        if (gun == null) return null;
        if (gun.Model == null)
        {
            var pistol = player.Animation != null ? player.Animation.FirstPersonPistol : null;
            if (pistol == null) return null;
            if (pistolView == null && !pistol.TryGetComponent(out pistolView)) pistolView = pistol.AddComponent<GunViewModel>();
            return pistolView;
        }

        if (!viewModels.TryGetValue(id, out var go) || go == null)
        {
            var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera : null;
            var prefab = Resources.Load<GameObject>(gun.Model);
            if (cam == null || prefab == null) return null;
            go = Instantiate(prefab, cam.transform);
            go.name = $"{gun.Id} (view)";
            go.transform.SetLocalPositionAndRotation(gun.ViewOffset, Quaternion.identity);
            go.transform.localScale = Vector3.one * gun.ViewLength;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<GunViewModel>().Capture();
            viewModels[id] = go;
        }
        return go.GetComponent<GunViewModel>();
    }

    private GunViewModel WorldModel(string id)
    {
        var gun = GunCatalog.Find(id);
        if (gun == null) return null;
        if (gun.Model == null)
        {
            var pistol = player.Animation != null ? player.Animation.ThirdPersonPistol : null;
            if (pistol == null) return null;
            if (pistolWorld == null)
            {
                if (!pistol.TryGetComponent(out pistolWorld)) pistolWorld = pistol.AddComponent<GunViewModel>();
                pistolWorld.Capture();
            }
            return pistolWorld;
        }

        if (!worldModels.TryGetValue(id, out var go) || go == null)
        {
            var prefab = Resources.Load<GameObject>(gun.Model);
            if (prefab == null) return null;
            go = Instantiate(prefab);
            go.name = $"{gun.Id} ({player.DisplayName})";
            go.transform.localScale = Vector3.one * gun.WorldLength;
            go.AddComponent<GunViewModel>().Capture();
            worldModels[id] = go;
        }
        return go.GetComponent<GunViewModel>();
    }
}
