using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BombState = SearchDestroyMode.BombState;

/// <summary>
/// Search &amp; Destroy on every client (added next to the GameModeManager when the mode is on; shares its scene
/// PhotonView for RPCs, so every RPC name starts with SnD):
///   - the bomb in the world: lying where it dropped (it starts each round on the floor in front of the attackers'
///     spawn; attackers walk over it to pick it up, the carrier drops it with <see cref="InputAction.DropBomb"/>, G;
///     only the one who picks it up is told), or planted, blinking and
///     beeping faster as its clock runs down; the explosion (everyone within <see cref="SearchDestroyMode.ExplosionRadius"/> dies);
///   - the Bomb key (<see cref="InputAction.Bomb"/>, 4, like Valorant): the carrier taps it to take the bomb out in
///     the right hand instead of the gun (tap again for the gun back; the gun can't fire meanwhile; synced as
///     <see cref="PlayerFlag.HoldingBomb"/>, so everyone sees it in hand), and holds it inside a site to plant; a
///     defender holds it at the planted bomb to defuse. Planting/defusing: the player stands still and a bar fills;
///     letting go or getting knocked away cancels it; the master checks and decides
///     (<see cref="SearchDestroyMode.MasterPlant"/>, MasterDefuse). Drop Bomb (G) tosses it, only while it's in hand;
///   - the HUD (own canvas): alive counts under the clock ("3 v 2"), the plant/defuse bar, markers on the sites and on
///     the bomb (the dropped/carried bomb for attackers, the planted one for everyone), banners for plants, defuses,
///     round wins and side swaps;
///   - spectating: dead players watch a living teammate from a chase camera (Action or Jump for the next one) until
///     the next round;
///   - the pre-round: everyone moves and plans but is held inside their spawn area by the level's SpawnBarriers
///     (translucent blue walls, up only then); a level without any gets an invisible soft wall round each team's spawn
///     points instead;
///   - the round timeline while the Scores key (Tab) is held: one box per possible round, ATK/DEF for your side in it,
///     won/lost for those played, the current one outlined, and where the teams switch sides.
/// </summary>
public class SearchDestroyController : MonoBehaviourPun
{
    public static SearchDestroyController Instance { get; private set; }

    /// <summary>True while the local player is planting or defusing (standing still).</summary>
    public static bool Busy => Instance != null && Instance.busy != Task.None;

    private enum Task { None, Plant, Defuse }

    private GameModeManager manager;
    private Task busy;
    private float progress;
    private bool abilityUsed, sentHalf;   // An ability used this frame (cancels a plant/defuse); this defuse reported half
    private bool lockedMovement;
    private float nextPickupAsk, nextBeep, nextBusyTick;

    // The Bomb key: a tap takes the bomb out / puts it away, a hold plants or defuses
    private const float TapTime = 0.2f;
    private float bombKeyDownAt = -1f;
    private bool bombOut;
    private Transform heldFirstPerson;
    private readonly Dictionary<int, Transform> heldThirdPerson = new();

    /// <summary>The local player carries the bomb (the controls hint shows the Bomb key).</summary>
    public static bool LocalCarrying => Instance != null && Instance.carryingNow;
    /// <summary>The local player has the bomb out in hand (the controls hint shows Drop).</summary>
    public static bool LocalBombOut => Instance != null && Instance.bombOut;
    private bool carryingNow;
    private int lastCarrier = -1;
    private bool droppedHere; // Dropped it: no picking it back up until you've stepped away from it

    // A tossed bomb flying on every screen (from the thrower's hand to where it lands)
    private Vector3 tossFrom, tossTo;
    private float tossStart = -10f, tossTime;
    private const float TossSpeed = 13f, TossLift = 3f; // Like the Boom Bomb's toss

    // Pre-round spawn area
    private Vector3 areaCentre;
    private float areaRadius;
    private (int round, PlayerRole role, string attack) areaKey;
    private bool areaReady;

    // Round timeline (Tab)
    private Canvas timelineCanvas;
    private RectTransform timelineCard, timelineRow;
    private TMP_Text timelineTitle;
    private readonly List<(Image box, TMP_Text label, Outline ring)> segments = new();

    // World
    private Transform bombModel;
    private Renderer bombLight;
    private Light bombGlow;
    private Material lightMaterial;

    // HUD
    private Canvas canvas;
    private TMP_Text aliveText, spectateText, carrierText;
    private RectTransform barCard;
    private Image barFill;
    private TMP_Text barLabel;
    private readonly List<TMP_Text> markers = new();

    // Change detection for banners
    private BombState lastBomb = BombState.Carried;
    private MatchPhase lastPhase = MatchPhase.Waiting;
    private string lastAttack = "";

    // Spectating
    private Transform spectateAnchor;
    private int spectateIndex;
    private Player spectating;
    private bool spectateOn;
    private float deadSince = -1f;

    private void Awake()
    {
        Instance = this;
        manager = GetComponent<GameModeManager>();
        BuildBomb();
        BuildHud();
        spectateAnchor = new GameObject("SnD Spectate Cam").transform;
        spectateAnchor.SetParent(transform, false);
        AbilityHandler.LocalUsed += NoteAbilityUsed;
    }

    private void NoteAbilityUsed() => abilityUsed = true;

    private void OnDestroy()
    {
        AbilityHandler.LocalUsed -= NoteAbilityUsed;
        if (Instance == this) Instance = null;
        StopSpectating();
        if (lockedMovement && Player.Local != null) Player.Local.MovementLocked = false;
    }

    private SearchDestroyMode Mode => manager != null ? manager.Mode as SearchDestroyMode : null;

    // ─── Every frame ──────────────────────────────────────────────────────────

    private void Update()
    {
        if (Mode == null) { canvas.gameObject.SetActive(false); bombModel.gameObject.SetActive(false); return; }
        canvas.gameObject.SetActive(true);
        var phase = manager.Phase;
        var local = Player.Local;

        Announce((MatchPhase)NetProps.Room(RoomKeys.Phase, (int)MatchPhase.Waiting)); // The room's phase: its round and winner arrive with it
        UpdateBomb(phase);
        UpdateBombInHand(local, phase);
        UpdateTask(local, phase);
        UpdatePickup(local, phase);
        UpdateDrop(local, phase);
        UpdateSpawnArea(local, phase);
        UpdateHud(local, phase);
        UpdateSpectate(local, phase);
        UpdateTimeline(local);
    }

    // ─── Banners ──────────────────────────────────────────────────────────────

    private void Announce(MatchPhase phase)
    {
        var bomb = SearchDestroyMode.Bomb;
        var hud = HUDManager.Instance;
        PlayerRole mine = PlayerRoles.Get(PhotonNetwork.LocalPlayer);

        if (phase != lastPhase)
        {
            if (phase == MatchPhase.Countdown)
            {
                string attack = SearchDestroyMode.AttackTeam;
                bool swapped = lastAttack != "" && attack != lastAttack;
                lastAttack = attack;
                bool attacking = SearchDestroyMode.IsAttacker(mine);
                string side = attacking ? "<color=#FF8A3D>ATTACK</color>" : "<color=#22D3EE>DEFEND</color>";
                if (hud != null)
                    hud.ShowBanner(swapped ? $"<size=70%>SWITCHING SIDES</size>\nYOU {side}" : $"<size=70%>ROUND {manager.Round}</size>\nYOU {side}", 3f);
            }
            else if (phase == MatchPhase.RoundEnd)
            {
                string winner = SearchDestroyMode.RoundWinner;
                string how = SearchDestroyMode.RoundReason switch
                {
                    SearchDestroyMode.ReasonDefused   => "Bomb defused",
                    SearchDestroyMode.ReasonDetonated => "The bomb went off",
                    SearchDestroyMode.ReasonTime      => "Time ran out",
                    _                                 => "Team eliminated",
                };
                bool won = (winner == TeamMode.Red && mine == PlayerRole.TeamRed) || (winner == TeamMode.Blue && mine == PlayerRole.TeamBlue);
                if (hud != null)
                    hud.ShowBanner($"<color={SearchDestroyMode.TeamColor(winner)}>{SearchDestroyMode.TeamName(winner).ToUpperInvariant()} WINS THE ROUND</color>\n<size=60%>{how}</size>", 4f);
                Sfx.Here(won ? SfxKind.Fanfare : SfxKind.Horn, 0.7f, won ? 1f : 0.8f);
            }
            lastPhase = phase;
        }

        // Only the one who picks the bomb up is told
        int carrier = bomb == BombState.Carried ? SearchDestroyMode.Carrier : -1;
        if (carrier != lastCarrier)
        {
            if (carrier == PhotonNetwork.LocalPlayer.ActorNumber && carrier >= 0)
            {
                if (hud != null) hud.ShowBanner($"<size=55%>YOU PICKED UP</size>\n<color=#FFC93C>THE BOMB</color>\n<size=40%>{Keybinds.Label(Keybinds.Get(InputAction.DropBomb))} to drop it</size>", 2.2f);
                Sfx.Here(SfxKind.Pop, 0.8f, 0.9f);
            }
            lastCarrier = carrier;
        }

        if (bomb != lastBomb)
        {
            if (bomb == BombState.Planted && phase == MatchPhase.Active)
            {
                if (hud != null) hud.ShowBanner($"<color=#FF4D6D>BOMB PLANTED</color>\n<size=60%>Site {SearchDestroyMode.Site}</size>", 3f);
                Sfx.Here(SfxKind.Horn, 0.8f, 1.2f);
                if (PlayerCam.Local != null) PlayerCam.Local.AddShake(0.25f);
            }
            lastBomb = bomb;
        }
    }

    // ─── The bomb in the world ────────────────────────────────────────────────

    private void BuildBomb()
    {
        bombModel = BombMesh("SnD Bomb", transform, out bombLight);
        lightMaterial = new Material(Shader.Find("Sprites/Default"));
        bombLight.sharedMaterial = lightMaterial;

        var glow = new GameObject("Glow");
        glow.transform.SetParent(bombModel, false);
        glow.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        bombGlow = glow.AddComponent<Light>();
        bombGlow.type = LightType.Point;
        bombGlow.range = 5f;
        bombGlow.intensity = 0f;
        bombGlow.color = new Color(1f, 0.2f, 0.2f);
        bombModel.gameObject.SetActive(false);
    }

    private static Material bodyMat, panelMat, heldLightMat;

    // The bomb's shape: Justin's model, or (without it) primitives: a dark case, a yellow panel and a red bulb on top
    private static Transform BombMesh(string name, Transform parent, out Renderer bulbRenderer)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (bodyMat == null) { bodyMat = new Material(lit); bodyMat.SetColor("_BaseColor", new Color(0.18f, 0.18f, 0.2f)); }
        if (panelMat == null) { panelMat = new Material(lit); panelMat.SetColor("_BaseColor", new Color(0.9f, 0.75f, 0.2f)); }
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);

        // Justin's bomb (Resources/Object/SearchDestroyBomb: 0.8 × 0.43 × 0.54, standing on its base), with the blinking
        // bulb on top of it
        var model = Resources.Load<GameObject>("Object/SearchDestroyBomb");
        if (model != null)
        {
            var bomb = Instantiate(model, root, false);
            foreach (var c in bomb.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in bomb.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(bulb.GetComponent<Collider>());
            bulb.name = "Light";
            bulb.transform.SetParent(root, false);
            bulb.transform.localPosition = new Vector3(0f, 0.47f, 0f);
            bulb.transform.localScale = Vector3.one * 0.09f;
            bulbRenderer = bulb.GetComponent<Renderer>();
            bulbRenderer.sharedMaterial = panelMat;
            bulbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        GameObject Part(PrimitiveType type, string partName, Vector3 at, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.name = partName;
            go.transform.SetParent(root, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
        Part(PrimitiveType.Cube, "Body", new Vector3(0f, 0.15f, 0f), new Vector3(0.6f, 0.3f, 0.4f), bodyMat);
        Part(PrimitiveType.Cube, "Panel", new Vector3(-0.08f, 0.31f, 0f), new Vector3(0.3f, 0.03f, 0.28f), panelMat);
        bulbRenderer = Part(PrimitiveType.Sphere, "Light", new Vector3(0.18f, 0.34f, 0f), Vector3.one * 0.12f, panelMat).GetComponent<Renderer>();
        return root;
    }

    // ─── The bomb in hand (Bomb key) ──────────────────────────────────────────

    private void UpdateBombInHand(Player local, MatchPhase phase)
    {
        int me = PhotonNetwork.LocalPlayer.ActorNumber;
        bool live = phase == MatchPhase.Countdown || phase == MatchPhase.Active;
        carryingNow = local != null && !local.IsCaught && live && SearchDestroyMode.Bomb == BombState.Carried && SearchDestroyMode.Carrier == me;
        if (!carryingNow) bombOut = false;
        if (local != null) local.SetFlag(PlayerFlag.HoldingBomb, bombOut);
    }

    // Where held bombs sit: in front of your view (bottom right), or in another player's right hand
    private void PlaceHeldBombs()
    {
        if (heldLightMat == null) heldLightMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.2f, 0.15f) };
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : null;
        bool fp = bombOut && cam != null && !PlayerCam.Local.HasViewOverride;
        if (fp && heldFirstPerson == null)
        {
            heldFirstPerson = BombMesh("Held Bomb (first person)", cam, out var bulb);
            bulb.sharedMaterial = heldLightMat;
            heldFirstPerson.localScale = Vector3.one * 0.4f;
            heldFirstPerson.localPosition = new Vector3(0.36f, -0.36f, 0.68f);
            heldFirstPerson.localRotation = Quaternion.Euler(-15f, -20f, 0f);
        }
        if (heldFirstPerson != null && heldFirstPerson.gameObject.activeSelf != fp) heldFirstPerson.gameObject.SetActive(fp);

        foreach (var p in Player.All)
        {
            if (p == null || p.IsLocal) continue;
            int actor = p.photonView.OwnerActorNr;
            bool show = p.Has(PlayerFlag.HoldingBomb) && !p.IsDownAfterTag;
            heldThirdPerson.TryGetValue(actor, out var model);
            if (show && model == null)
            {
                model = BombMesh("Held Bomb", transform, out var bulb);
                bulb.sharedMaterial = heldLightMat;
                model.localScale = Vector3.one * 0.6f;
                heldThirdPerson[actor] = model;
            }
            if (model == null) continue;
            if (model.gameObject.activeSelf != show) model.gameObject.SetActive(show);
            if (!show) continue;
            var hand = RightHand(p);
            Vector3 at = hand != null ? hand.position : p.transform.position + Vector3.up * (p.EyeHeight * 0.55f) + p.transform.right * 0.45f;
            model.SetPositionAndRotation(at - p.transform.up * 0.1f, p.transform.rotation);
        }
    }

    private readonly Dictionary<int, Transform> hands = new();

    private Transform RightHand(Player p)
    {
        int actor = p.photonView.OwnerActorNr;
        if (hands.TryGetValue(actor, out var hand) && hand != null) return hand;
        Transform best = null;
        foreach (var t in p.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "RightArm4_end") { best = t; break; }
            if (t.name == "RightArm3") best = t;
        }
        hands[actor] = best;
        return best;
    }

    private void UpdateBomb(MatchPhase phase)
    {
        var bomb = SearchDestroyMode.Bomb;
        bool flying = Time.time - tossStart < tossTime;
        bool shown = (phase == MatchPhase.Countdown || phase == MatchPhase.Active || phase == MatchPhase.RoundEnd)
                     && (flying || bomb == BombState.Dropped || bomb == BombState.Planted || bomb == BombState.Defused);
        if (bombModel.gameObject.activeSelf != shown) bombModel.gameObject.SetActive(shown);
        if (!shown) return;

        if (flying)
        {
            // Along its arc: straight line plus a parabola's rise, tumbling
            float t = (Time.time - tossStart) / tossTime;
            float rise = Mathf.Max(0.5f, TossLift * tossTime * 0.5f);
            bombModel.position = Vector3.Lerp(tossFrom, tossTo, t) + Vector3.up * (4f * rise * t * (1f - t));
            bombModel.rotation = Quaternion.Euler(t * 540f, t * 200f, 0f);
            return;
        }
        bombModel.rotation = Quaternion.identity;
        bombModel.position = SearchDestroyMode.BombPos;
        Color off = new(0.35f, 0.08f, 0.08f);
        if (bomb == BombState.Planted && phase == MatchPhase.Active)
        {
            // Beeps faster as it runs down: every second at the start, five a second at the end
            float left = manager.Clock.Remaining;
            float t = Mathf.Clamp01(left / MatchRules.BombTimer);
            float interval = Mathf.Lerp(0.18f, 1f, t);
            if (Time.time >= nextBeep)
            {
                nextBeep = Time.time + interval;
                Sfx.At(SfxKind.Tick, bombModel.position, 1f, 55f, Mathf.Lerp(2.2f, 1.5f, t));
            }
            bool on = nextBeep - Time.time > interval * 0.6f;
            lightMaterial.color = on ? new Color(1f, 0.15f, 0.1f) : off;
            bombGlow.intensity = on ? 3f : 0f;
            bombGlow.color = new Color(1f, 0.2f, 0.2f);
        }
        else if (bomb == BombState.Defused)
        {
            lightMaterial.color = new Color(0.3f, 1f, 0.4f);
            bombGlow.intensity = 1.5f;
            bombGlow.color = new Color(0.3f, 1f, 0.4f);
        }
        else
        {
            lightMaterial.color = Time.time % 1.2f < 0.15f ? new Color(1f, 0.85f, 0.3f) : off; // Dropped: a slow amber blink
            bombGlow.intensity = 0f;
        }
    }

    /// <summary>Master: the bomb went off at <paramref name="at"/>.</summary>
    public void MasterExplode(Vector3 at) => photonView.RPC(nameof(RPC_SnDExplode), RpcTarget.All, at);

    [PunRPC]
    private void RPC_SnDExplode(Vector3 at)
    {
        Fx.Explosion(at, 8f);
        Vfx.Explosion(at + Vector3.up * 2f, 12f);
        Vfx.Ring(at + Vector3.up * 0.3f, new Color(1f, 0.6f, 0.2f), 60, 30f, 1.2f, 0.9f);
        if (PlayerCam.Local != null) PlayerCam.Local.AddShake(0.8f);
        var local = Player.Local;
        if (local != null && !local.IsCaught && Vector3.Distance(local.transform.position, at) <= SearchDestroyMode.ExplosionRadius
            && CombatManager.Instance != null)
            CombatManager.Instance.ReportLocalDeath(-1, "", false);
    }

    // ─── Planting and defusing ────────────────────────────────────────────────

    private void UpdateTask(Player local, MatchPhase phase)
    {
        Task can = Task.None;
        string letter = "";
        KeyCode key = Keybinds.Get(InputAction.Bomb);
        // Menus that use the number keys own them while open (the gun picker's 4 is the Sniper)
        bool menus = GunPicker.IsOpen || EmoteMenu.IsOpen || PingWheel.IsOpen || PauseMenuManager.IsPaused;
        if (Input.GetKeyDown(key)) bombKeyDownAt = menus ? -1f : Time.time;
        bool keyHeld = bombKeyDownAt >= 0f && Input.GetKey(key);
        bool longHold = keyHeld && Time.time - bombKeyDownAt >= TapTime;
        if (Input.GetKeyUp(key))
        {
            // A tap (not the end of a plant or defuse): the bomb in or out of hand
            if (bombKeyDownAt >= 0f && Time.time - bombKeyDownAt < TapTime && busy == Task.None && carryingNow)
            {
                bombOut = !bombOut;
                Sfx.Here(SfxKind.Place, 0.4f, bombOut ? 1.1f : 0.9f);
            }
            bombKeyDownAt = -1f;
        }
        if (local != null && phase == MatchPhase.Active && !local.IsCaught && !PauseMenuManager.IsPaused && !manager.Clock.IsExpired)
        {
            var bomb = SearchDestroyMode.Bomb;
            int me = PhotonNetwork.LocalPlayer.ActorNumber;
            if (bomb == BombState.Carried && SearchDestroyMode.Carrier == me && SearchDestroyMode.IsAttacker(local.Role)
                && BombSite.TryFind(local.transform.position, out var zone))
            {
                can = Task.Plant;
                letter = zone.Letter;
            }
            else if (bomb == BombState.Planted && PlayerRoles.IsTeam(local.Role) && !SearchDestroyMode.IsAttacker(local.Role)
                     && Vector3.Distance(local.transform.position, SearchDestroyMode.BombPos) <= SearchDestroyMode.DefuseRange)
                can = Task.Defuse;
        }

        bool interrupted = abilityUsed;
        abilityUsed = false;
        if (interrupted && busy != Task.None)
        {
            // Using an ability breaks a plant or defuse: let go of the key and start again
            EndTask(local);
            bombKeyDownAt = -1f;
            return;
        }

        bool holding = can != Task.None && longHold && (can != Task.Plant || local.Movement.OnGround);
        if (can != Task.None && !holding)
            HUDManager.Prompt(key, can == Task.Plant ? $"Hold to plant the bomb at {letter}"
                                   : SearchDestroyMode.DefuseHalf ? "Hold to defuse the bomb (half done)" : "Hold to defuse the bomb");

        if (!holding || (busy != Task.None && busy != can))
        {
            EndTask(local);
            return;
        }

        if (busy == Task.None)
        {
            busy = can;
            local.SetFlag(can == Task.Plant ? PlayerFlag.Planting : PlayerFlag.Defusing, true); // The animation on every screen
            // Valorant's checkpoint: once anyone has defused this bomb half way, a defuse starts from half
            progress = can == Task.Defuse && SearchDestroyMode.DefuseHalf ? MatchRules.DefuseTime * 0.5f : 0f;
            sentHalf = false;
            if (can == Task.Plant) bombOut = true; // Planting takes it out
            if (!local.MovementLocked) { local.MovementLocked = true; lockedMovement = true; }
            photonView.RPC(nameof(RPC_SnDBusy), RpcTarget.All, local.transform.position, can == Task.Defuse);
        }

        float needed = busy == Task.Plant ? MatchRules.PlantTime : MatchRules.DefuseTime;
        progress += Time.deltaTime;
        if (busy == Task.Defuse && !sentHalf && progress >= needed * 0.5f)
        {
            sentHalf = true;
            if (!SearchDestroyMode.DefuseHalf) photonView.RPC(nameof(RPC_SnDDefuseHalf), RpcTarget.MasterClient);
        }
        if (Time.time >= nextBusyTick)
        {
            nextBusyTick = Time.time + 0.5f;
            Sfx.Here(SfxKind.Tick, 0.5f, busy == Task.Plant ? 1.2f : 0.9f);
        }
        if (progress < needed) return;

        if (busy == Task.Plant)
        {
            Vector3 at = local.transform.position;
            if (Physics.Raycast(at + Vector3.up, Vector3.down, out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                at = hit.point;
            photonView.RPC(nameof(RPC_SnDPlant), RpcTarget.MasterClient, at, letter);
        }
        else photonView.RPC(nameof(RPC_SnDDefuse), RpcTarget.MasterClient);
        EndTask(local);
    }

    private void EndTask(Player local)
    {
        if (local != null) { local.SetFlag(PlayerFlag.Planting, false); local.SetFlag(PlayerFlag.Defusing, false); }
        busy = Task.None;
        progress = 0f;
        if (lockedMovement && local != null) local.MovementLocked = false;
        lockedMovement = false;
    }

    // Everyone hears a plant or defuse start where it happens
    [PunRPC]
    private void RPC_SnDBusy(Vector3 at, bool defuse) =>
        Sfx.At(defuse ? SfxKind.ZapDown : SfxKind.Place, at, 1f, 40f, defuse ? 0.8f : 1f);

    [PunRPC]
    private void RPC_SnDPlant(Vector3 at, string letter, PhotonMessageInfo info)
    {
        if (PhotonNetwork.IsMasterClient) Mode?.MasterPlant(info.Sender, at, letter);
    }

    [PunRPC]
    private void RPC_SnDDefuseHalf(PhotonMessageInfo info)
    {
        if (PhotonNetwork.IsMasterClient) Mode?.MasterDefuseHalf(info.Sender);
    }

    [PunRPC]
    private void RPC_SnDDefuse(PhotonMessageInfo info)
    {
        if (PhotonNetwork.IsMasterClient) Mode?.MasterDefuse(info.Sender);
    }

    // ─── Picking up a dropped bomb ────────────────────────────────────────────

    private void UpdatePickup(Player local, MatchPhase phase)
    {
        if (local == null || local.IsCaught || (phase != MatchPhase.Active && phase != MatchPhase.Countdown) || SearchDestroyMode.Bomb != BombState.Dropped) return;
        if (!SearchDestroyMode.IsAttacker(local.Role) || Time.time < nextPickupAsk) return;
        float distance = Vector3.Distance(local.transform.position, SearchDestroyMode.BombPos);
        if (droppedHere) { if (distance > SearchDestroyMode.PickupRange + 0.5f) droppedHere = false; return; }
        if (distance > SearchDestroyMode.PickupRange) return;
        nextPickupAsk = Time.time + 0.5f;
        photonView.RPC(nameof(RPC_SnDPickup), RpcTarget.MasterClient);
    }

    [PunRPC]
    private void RPC_SnDPickup(PhotonMessageInfo info)
    {
        if (PhotonNetwork.IsMasterClient) Mode?.MasterPickup(info.Sender);
    }

    // ─── Dropping it (G) ──────────────────────────────────────────────────────

    private void UpdateDrop(Player local, MatchPhase phase)
    {
        if (local == null || local.IsCaught || PauseMenuManager.IsPaused || busy != Task.None) return;
        if (phase != MatchPhase.Active && phase != MatchPhase.Countdown) return;
        if (SearchDestroyMode.Bomb != BombState.Carried || SearchDestroyMode.Carrier != PhotonNetwork.LocalPlayer.ActorNumber) return;
        if (!bombOut || !Input.GetKeyDown(Keybinds.Get(InputAction.DropBomb))) return; // Only a bomb in hand drops

        // Tossed a short way where you look, like the Boom Bomb: worked out here, flown on every screen
        var view = PlayerCam.Local != null ? PlayerCam.Local.Camera.transform : local.transform;
        Vector3 from = view.position + view.forward * 0.5f - Vector3.up * 0.3f;
        Vector3 velocity = view.forward * TossSpeed + Vector3.up * TossLift + Vector3.ProjectOnPlane(local.Velocity, Vector3.up) * 0.5f;
        Vector3 at = Landing(local, from, velocity, out float flight);
        nextPickupAsk = Time.time + 0.5f;
        droppedHere = true;
        photonView.RPC(nameof(RPC_SnDDrop), RpcTarget.MasterClient, at);
        photonView.RPC(nameof(RPC_SnDToss), RpcTarget.All, from, at, flight);
    }

    // Steps the toss until it lands on something you could stand on (walls bounce it back, softer)
    private static Vector3 Landing(Player thrower, Vector3 from, Vector3 velocity, out float flight)
    {
        const float dt = 0.02f;
        Vector3 pos = from;
        flight = 0f;
        for (int i = 0; i < 200; i++)
        {
            Vector3 next = pos + velocity * dt;
            velocity += Physics.gravity * dt;
            flight += dt;
            RaycastHit? best = null;
            foreach (var hit in Physics.RaycastAll(pos, next - pos, Vector3.Distance(pos, next), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(thrower.transform) && hit.collider.GetComponentInParent<Player>() == null
                    && (best == null || hit.distance < best.Value.distance)) best = hit;
            if (best.HasValue)
            {
                var hit = best.Value;
                if (hit.normal.y > 0.5f) return hit.point;
                velocity = Vector3.Reflect(velocity, hit.normal) * 0.35f; // Off a wall
                pos = hit.point + hit.normal * 0.1f;
                continue;
            }
            pos = next;
        }
        return pos; // Never landed (over the void): the host hands it to a living attacker
    }

    [PunRPC]
    private void RPC_SnDToss(Vector3 from, Vector3 to, float flight)
    {
        tossFrom = from;
        tossTo = to;
        tossTime = Mathf.Clamp(flight, 0.2f, 1.5f);
        tossStart = Time.time;
        Sfx.At(SfxKind.Toss, from, 0.8f);
    }

    [PunRPC]
    private void RPC_SnDDrop(Vector3 at, PhotonMessageInfo info)
    {
        if (PhotonNetwork.IsMasterClient) Mode?.MasterDrop(info.Sender, at);
    }

    // ─── Pre-round: stay in your spawn area ───────────────────────────────────

    private void UpdateSpawnArea(Player local, MatchPhase phase)
    {
        // Levels with real barriers (SpawnBarrier) don't need this invisible fallback
        if (phase != MatchPhase.Countdown || local == null || local.IsCaught || !PlayerRoles.IsTeam(local.Role) || SpawnBarrier.All.Count > 0)
        { areaReady = false; return; }
        var key = (manager.Round, local.Role, SearchDestroyMode.AttackTeam); // Recomputed when any of these arrive or change
        if (!areaReady || key != areaKey)
        {
            areaKey = key;
            var points = new List<SpawnPoint>(SpawnPoint.All);
            if (points.Count == 0) return;
            points.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var mine = points.Count >= 2 ? SearchDestroyMode.SpawnsFor(points, SearchDestroyMode.IsAttacker(local.Role)) : points;
            areaCentre = Vector3.zero;
            foreach (var s in mine) areaCentre += s.transform.position;
            areaCentre /= mine.Count;
            areaRadius = 0f;
            foreach (var s in mine) areaRadius = Mathf.Max(areaRadius, Vector3.Distance(Flat(s.transform.position), Flat(areaCentre)));
            areaRadius = Mathf.Max(12f, areaRadius + 7f);
            areaReady = true;
        }

        Vector3 offset = Flat(local.transform.position) - Flat(areaCentre);
        if (offset.magnitude <= areaRadius) return;
        if (offset.magnitude > areaRadius + 20f) // Nowhere near (sides just changed, or teleported): back to your spawn
        {
            if (Spawner.Instance != null) Spawner.Instance.RespawnLocalPlayer();
            return;
        }
        // Held at the edge: back inside, moving outward no further
        Vector3 outward = offset.normalized;
        Vector3 inside = areaCentre + outward * (areaRadius - 0.05f);
        local.Body.position = new Vector3(inside.x, local.Body.position.y, inside.z);
        Vector3 v = local.Body.linearVelocity;
        float away = Vector3.Dot(v, outward);
        if (away > 0f) local.Body.linearVelocity = v - outward * away;
    }

    private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    // ─── Round timeline (hold Tab) ────────────────────────────────────────────

    private void BuildTimeline(int count)
    {
        var theme = UITheme.Current;
        if (timelineCanvas == null)
        {
            timelineCanvas = UIKit.Canvas("SnD Timeline Canvas", 6); // Over the Tab standings (5)
            timelineCanvas.transform.SetParent(transform, false);
            timelineCard = UIKit.Card(timelineCanvas.transform, UIKit.WithAlpha(theme.panel, 0.96f), 24f, 0.5f, "Timeline");
            timelineCard.anchorMin = timelineCard.anchorMax = timelineCard.pivot = new Vector2(0.5f, 0f);
            timelineCard.anchoredPosition = new Vector2(0f, 300f); // Well above the ability bar, clear of the clock and the standings
            timelineTitle = UIKit.OneLine(UIKit.Text(timelineCard, "", 26f, UIKit.Font.Display, theme.text, TextAlignmentOptions.Center, "Title"));
            timelineTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            timelineTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            timelineTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
            timelineTitle.rectTransform.anchoredPosition = new Vector2(0f, -14f);
            timelineTitle.rectTransform.sizeDelta = new Vector2(0f, 34f);
            timelineRow = UIKit.Rect("Row", timelineCard);
            timelineRow.anchorMin = timelineRow.anchorMax = timelineRow.pivot = new Vector2(0.5f, 0f);
            timelineRow.anchoredPosition = new Vector2(0f, 18f);
        }
        foreach (Transform child in timelineRow) Destroy(child.gameObject);
        segments.Clear();

        const float box = 84f, gap = 10f, switchGap = 74f;
        int half = SearchDestroyMode.HalfTime;
        float width = count * box + (count - 1) * gap + (half > 0 && half < count ? switchGap - gap : 0f);
        timelineRow.sizeDelta = new Vector2(width, 70f);
        timelineCard.sizeDelta = new Vector2(Mathf.Max(520f, width + 48f), 136f);
        float x = -width * 0.5f;
        for (int i = 0; i < count; i++)
        {
            var image = UIKit.Panel(timelineRow, theme.field, 12f, $"Round {i + 1}");
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(box, 64f);
            rect.anchoredPosition = new Vector2(x, 0f);
            var label = UIKit.Text(image.transform, "", 20f, UIKit.Font.Display, theme.text, TextAlignmentOptions.Center);
            UIKit.Stretch(label.rectTransform);
            var ring = image.gameObject.AddComponent<Outline>();
            ring.effectColor = theme.primary;
            ring.effectDistance = new Vector2(3f, -3f);
            ring.enabled = false;
            segments.Add((image, label, ring));
            x += box + gap;
            if (i + 1 == half && half < count)
            {
                var swap = UIKit.Text(timelineRow, "SWITCH\nSIDES", 14f, UIKit.Font.Display, theme.textMuted, TextAlignmentOptions.Center, "Switch");
                swap.rectTransform.anchorMin = swap.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                swap.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                swap.rectTransform.sizeDelta = new Vector2(switchGap, 60f);
                swap.rectTransform.anchoredPosition = new Vector2(x - gap + switchGap * 0.5f, 0f);
                x += switchGap - gap;
            }
        }
        foreach (var g in timelineCanvas.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
    }

    private void UpdateTimeline(Player local)
    {
        bool show = !PauseMenuManager.IsPaused && Input.GetKey(Keybinds.Get(InputAction.Scores));
        if (!show) { if (timelineCanvas != null) timelineCanvas.gameObject.SetActive(false); return; }

        int count = SearchDestroyMode.MaxRounds;
        if (timelineCanvas == null || segments.Count != count) BuildTimeline(count);
        timelineCanvas.gameObject.SetActive(true);

        var theme = UITheme.Current;
        PlayerRole mine = local != null && PlayerRoles.IsTeam(local.Role) ? local.Role : PlayerRole.TeamRed;
        string myTeam = mine == PlayerRole.TeamBlue ? TeamMode.Blue : TeamMode.Red;
        char myMark = mine == PlayerRole.TeamBlue ? 'B' : 'R';
        string history = SearchDestroyMode.History;
        int round = Mathf.Max(1, manager.Round);
        bool roundOver = manager.Phase == MatchPhase.RoundEnd || manager.Phase == MatchPhase.MatchEnd;

        for (int i = 0; i < segments.Count; i++)
        {
            int r = i + 1;
            bool attacking = SearchDestroyMode.AttackerIn(r) == myTeam;
            var (box, label, ring) = segments[i];
            string side = attacking ? "ATK" : "DEF";
            if (r <= history.Length)
            {
                bool won = history[r - 1] == myMark;
                box.color = won ? UIKit.WithAlpha(theme.lime, 0.85f) : UIKit.WithAlpha(theme.danger, 0.75f);
                label.text = $"<size=70%>R{r}</size>\n{side} <size=70%>{(won ? "WON" : "LOST")}</size>";
                label.color = theme.textDark;
            }
            else
            {
                bool current = r == round && !roundOver;
                box.color = current ? UIKit.WithAlpha(theme.primary, 0.25f) : UIKit.WithAlpha(theme.field, 0.9f);
                label.text = $"<size=70%>R{r}</size>\n{side}";
                label.color = current ? theme.text : theme.textMuted;
            }
            ring.enabled = r == round && !roundOver;
        }

        bool attackingNow = SearchDestroyMode.AttackerIn(round) == myTeam;
        timelineTitle.text = $"ROUND {round} OF {count}  <color=#B9B2E0>·</color>  YOU {(attackingNow ? "<color=#FF8A3D>ATTACK</color>" : "<color=#22D3EE>DEFEND</color>")}" +
                             $"  <color=#B9B2E0>·  FIRST TO {MatchRules.RoundsToWin}</color>";
    }

    // ─── HUD ──────────────────────────────────────────────────────────────────

    private void BuildHud()
    {
        var theme = UITheme.Current;
        canvas = UIKit.Canvas("SnD Canvas", 3);
        canvas.transform.SetParent(transform, false);

        aliveText = UIKit.OneLine(UIKit.Text(canvas.transform, "", 26f, UIKit.Font.Display, theme.text, TextAlignmentOptions.Center, "Alive"));
        UIKit.Place(aliveText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -176f), new Vector2(420f, 34f));

        carrierText = UIKit.OneLine(UIKit.Text(canvas.transform, "", 22f, UIKit.Font.Display, theme.primary, TextAlignmentOptions.Center, "Carrier"));
        UIKit.Place(carrierText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 210f), new Vector2(600f, 30f));

        spectateText = UIKit.OneLine(UIKit.Text(canvas.transform, "", 24f, UIKit.Font.Bold, theme.text, TextAlignmentOptions.Center, "Spectate"));
        UIKit.Place(spectateText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(900f, 34f));

        barCard = UIKit.Card(canvas.transform, UIKit.WithAlpha(theme.panel, 0.92f), 16f, 0.4f, "Progress");
        UIKit.Place(barCard, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(420f, 64f));
        barCard.pivot = new Vector2(0.5f, 0.5f);
        barCard.anchoredPosition = new Vector2(0f, -110f);
        var track = UIKit.Panel(barCard, theme.field, 8f, "Track");
        UIKit.Stretch(track.rectTransform, 14f, 14f, 38f, 12f);
        barFill = UIKit.Panel(track.transform, theme.primary, 8f, "Fill");
        barFill.rectTransform.anchorMin = Vector2.zero;
        barFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        barFill.rectTransform.offsetMin = barFill.rectTransform.offsetMax = Vector2.zero;
        barLabel = UIKit.OneLine(UIKit.Text(barCard, "", 20f, UIKit.Font.Display, theme.text, TextAlignmentOptions.Center, "Label"));
        UIKit.Stretch(barLabel.rectTransform, 14f, 14f, 6f, 30f);
        barCard.gameObject.SetActive(false);

        foreach (var g in canvas.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
    }

    private void UpdateHud(Player local, MatchPhase phase)
    {
        bool round = phase == MatchPhase.Active || phase == MatchPhase.RoundEnd || phase == MatchPhase.Countdown;

        // Alive counts, your team first
        if (round)
        {
            PlayerRole mine = local != null && PlayerRoles.IsTeam(local.Role) ? local.Role : PlayerRole.TeamRed;
            PlayerRole theirs = PlayerRoles.Opponent(mine);
            string Hex(PlayerRole r) => r == PlayerRole.TeamBlue ? "#33A6FF" : "#FF4D6D";
            aliveText.text = $"<color={Hex(mine)}>{SearchDestroyMode.AliveCount(mine)}</color>  <size=70%><color=#B9B2E0>VS</color></size>  <color={Hex(theirs)}>{SearchDestroyMode.AliveCount(theirs)}</color>";
        }
        else aliveText.text = "";

        bool carrying = local != null && !local.IsCaught && round && SearchDestroyMode.Bomb == BombState.Carried
                        && SearchDestroyMode.Carrier == PhotonNetwork.LocalPlayer.ActorNumber;
        carrierText.text = carrying
            ? (bombOut ? "BOMB IN HAND" : $"YOU HAVE THE BOMB  <size=75%><color=#B9B2E0>{Keybinds.Label(Keybinds.Get(InputAction.Bomb))}: take it out</color></size>")
            : "";

        // Progress bar
        bool showBar = busy != Task.None;
        if (barCard.gameObject.activeSelf != showBar) barCard.gameObject.SetActive(showBar);
        if (showBar)
        {
            float needed = busy == Task.Plant ? MatchRules.PlantTime : MatchRules.DefuseTime;
            float t = Mathf.Clamp01(progress / needed);
            barFill.rectTransform.anchorMax = new Vector2(t, 1f);
            barFill.color = busy == Task.Plant ? UITheme.Current.orange : UITheme.Current.cyan;
            barLabel.text = $"{(busy == Task.Plant ? "PLANTING" : "DEFUSING")}  {Mathf.Max(0f, needed - progress):0.0}s";
        }

        UpdateMarkers(local, phase);
    }

    // Floating labels over the sites and the bomb (only what this player may know)
    private void UpdateMarkers(Player local, MatchPhase phase)
    {
        int used = 0;
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera : Camera.main;
        bool round = phase == MatchPhase.Active || phase == MatchPhase.Countdown;
        if (cam != null && local != null && round)
        {
            var bomb = SearchDestroyMode.Bomb;
            bool attacker = SearchDestroyMode.IsAttacker(local.Role);
            foreach (var zone in BombSite.Zones)
            {
                bool plantedHere = bomb == BombState.Planted && zone.Letter == SearchDestroyMode.Site;
                Marker(ref used, cam, zone.Marker, zone.Letter, plantedHere ? new Color(1f, 0.3f, 0.35f) : new Color(1f, 1f, 1f, 0.8f), 48f);
            }

            if (bomb == BombState.Planted)
            {
                bool blink = Time.time % 0.6f < 0.4f;
                Marker(ref used, cam, SearchDestroyMode.BombPos + Vector3.up * 1.2f, $"BOMB {manager.Clock.Remaining:0}",
                       blink ? new Color(1f, 0.3f, 0.35f) : new Color(1f, 0.6f, 0.6f), 36f);
            }
            else if (attacker && bomb == BombState.Dropped)
                Marker(ref used, cam, SearchDestroyMode.BombPos + Vector3.up * 1f, "BOMB", new Color(1f, 0.79f, 0.24f), 34f);
            else if (attacker && bomb == BombState.Carried && SearchDestroyMode.Carrier != PhotonNetwork.LocalPlayer.ActorNumber)
            {
                var holder = Player.Find(SearchDestroyMode.Carrier);
                if (holder != null && !holder.IsDownAfterTag)
                    Marker(ref used, cam, holder.transform.position + Vector3.up * (holder.TopY - holder.transform.position.y + 0.9f), "BOMB",
                           new Color(1f, 0.79f, 0.24f), 28f);
            }
        }
        for (int i = used; i < markers.Count; i++)
            if (markers[i].gameObject.activeSelf) markers[i].gameObject.SetActive(false);
    }

    private void Marker(ref int used, Camera cam, Vector3 world, string text, Color color, float size)
    {
        Vector3 screen = cam.WorldToScreenPoint(world);
        if (screen.z <= 0.1f) return;
        if (used >= markers.Count)
        {
            var t = UIKit.OneLine(UIKit.Text(canvas.transform, "", size, UIKit.Font.Display, Color.white, TextAlignmentOptions.Center, "Marker"));
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = Vector2.zero;
            t.rectTransform.pivot = new Vector2(0.5f, 0f);
            t.rectTransform.sizeDelta = new Vector2(240f, 40f);
            t.outlineWidth = 0.2f;
            t.outlineColor = new Color32(20, 12, 50, 255);
            markers.Add(t);
        }
        var m = markers[used++];
        if (!m.gameObject.activeSelf) m.gameObject.SetActive(true);
        float distance = Vector3.Distance(cam.transform.position, world);
        m.text = $"{text}\n<size=55%>{distance:0} m</size>";
        m.fontSize = size;
        m.color = color;
        float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        m.rectTransform.anchoredPosition = new Vector2(screen.x, screen.y) / scale;
    }

    // ─── Spectating a teammate while dead ─────────────────────────────────────

    private void UpdateSpectate(Player local, MatchPhase phase)
    {
        bool dead = local != null && local.IsCaught && (phase == MatchPhase.Active || phase == MatchPhase.RoundEnd);
        if (!dead) { deadSince = -1f; StopSpectating(); spectateText.text = ""; return; }
        if (deadSince < 0f) deadSince = Time.time;

        // After the killcam (and a moment on your own ragdoll)
        if (Killcam.IsPlaying || Time.time - deadSince < 2.5f) { StopSpectating(); spectateText.text = ""; return; }

        var mates = new List<Player>();
        foreach (var p in Player.All)
            if (p != null && p != local && p.Role == local.Role && !p.IsDownAfterTag && !SearchDestroyMode.IsDead(p.photonView.OwnerActorNr))
                mates.Add(p);
        mates.Sort((a, b) => a.photonView.OwnerActorNr.CompareTo(b.photonView.OwnerActorNr));
        if (mates.Count == 0)
        {
            StopSpectating();
            spectateText.text = "<color=#B9B2E0>Your team is out. Wait for the next round.</color>";
            return;
        }

        if (!PauseMenuManager.IsPaused && (Input.GetKeyDown(Keybinds.Get(InputAction.Action)) || Input.GetKeyDown(Keybinds.Get(InputAction.Jump))))
            spectateIndex++;
        spectating = mates[spectateIndex % mates.Count];

        if (!spectateOn)
        {
            spectateOn = true;
            spectateAnchor.position = spectating.transform.position + Vector3.up * 3f;
            if (PlayerCam.Local != null) PlayerCam.Local.SetViewOverride(spectateAnchor);
        }
        spectateText.text = $"<color=#B9B2E0>SPECTATING</color>  {spectating.DisplayName}   <size=75%><color=#B9B2E0>{Keybinds.Label(Keybinds.Get(InputAction.Action))}: next teammate</color></size>";
    }

    // Behind and above the teammate, looking where they look
    private void LateUpdate()
    {
        if (Mode != null) PlaceHeldBombs();
        if (!spectateOn || spectating == null) return;
        Vector3 back = Vector3.ProjectOnPlane(-spectating.transform.forward, Vector3.up);
        if (back.sqrMagnitude < 0.01f) back = Vector3.back;
        Vector3 focus = spectating.transform.position + Vector3.up * 1.6f;
        Vector3 want = focus + back.normalized * 4f + Vector3.up * 1.4f;
        if (Physics.Linecast(focus, want, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<Player>() == null)
            want = hit.point + (focus - want).normalized * 0.3f;
        spectateAnchor.position = Vector3.Lerp(spectateAnchor.position, want, 1f - Mathf.Exp(-10f * Time.deltaTime));
        spectateAnchor.rotation = Quaternion.LookRotation(focus + spectating.transform.forward * 6f - spectateAnchor.position);
    }

    private void StopSpectating()
    {
        if (!spectateOn) return;
        spectateOn = false;
        spectating = null;
        if (PlayerCam.Local != null && !Killcam.IsPlaying) PlayerCam.Local.SetViewOverride(null);
    }
}
