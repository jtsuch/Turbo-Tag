using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Where hunters wait while the hiders hide (Justin, Nov 14), in modes that say so (<see cref="GameMode.UsesHunterRoom"/>:
/// Tag, Protect the President, Hunter). During Countdown and Hiding (<see cref="GameMode.InHunterRoom"/>):
///   - each hunter is in their own room: a closed, lit box built locally on every screen 3 km off the map (past every
///     camera's 1 km far clip, so nobody on the map can see it), where they can walk around and pick their hunter class,
///     but not use abilities. They can't see anyone on the map.
///   - everyone sees a <see cref="HunterDecoy"/> where each hunter will start (<see cref="Spawner.HunterStart"/>): their
///     penguin, colour and cosmetics, covering its eyes. It's local to each screen and nothing the hunter does moves it.
///   - when the hunt starts, each hunter's own client moves them onto their decoy's spot and facing, and the decoys go:
///     the hunter "takes over" the penguin.
/// Every client decides all of this from synced state (roles and phase), so late joiners and a new host agree. The
/// local player is also checked every frame (a role or phase can arrive in either order), so a hunter is always moved
/// into the room, and anyone left inside once it no longer applies is moved out.
/// Added next to the GameModeManager.
/// </summary>
public class HunterRoom : MonoBehaviour
{
    public static HunterRoom Instance { get; private set; }

    private static readonly Vector3 Offset = new(3000f, 200f, 3000f);   // From the level's Spawner
    private const float Size = 48f, WallHeight = 16f;

    private Transform room;
    private readonly Dictionary<int, HunterDecoy> decoys = new();
    private readonly HashSet<int> wanted = new();
    private readonly List<int> gone = new();

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (room != null) Destroy(room.gameObject);
        foreach (var m in materials.Values) if (m != null) Destroy(m);
        foreach (var d in decoys.Values) if (d != null) Destroy(d.gameObject);
    }

    private static Vector3 Origin => (Spawner.Instance != null ? Spawner.Instance.transform.position : Vector3.zero) + Offset;

    /// <summary>Where the local hunter stands in the room (side by side by actor order, facing the sign).</summary>
    public static void Spot(int actor, out Vector3 position, out Quaternion rotation)
    {
        if (Instance != null) Instance.Ensure();
        int index = 0, count = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (PlayerRoles.Get(p) != PlayerRole.Hunter) continue;
            if (p.ActorNumber == actor) index = count;
            count++;
        }
        float x = (index - (Mathf.Max(1, count) - 1) * 0.5f) * 2.5f;
        position = Origin + new Vector3(-8f + Mathf.Clamp(x, -6f, 6f), 0.1f, -20f);   // The south-west, facing the tunnel and the steps
        rotation = Quaternion.identity;   // Facing +z: the sign on the far wall
    }

    private static bool Inside(Vector3 position)
    {
        Vector3 d = position - Origin;
        return Mathf.Abs(d.x) < Size * 0.5f + 3f && Mathf.Abs(d.z) < Size * 0.5f + 3f && d.y > -10f && d.y < WallHeight + 10f;
    }

    private void Update()
    {
        var manager = GameModeManager.Instance;
        var mode = manager != null ? manager.Mode : null;
        if (mode == null || !mode.UsesHunterRoom) { ClearDecoys(); return; }
        var phase = manager.Phase;

        // The decoys: one per hunter in the room, on every screen
        wanted.Clear();
        foreach (var p in PhotonNetwork.PlayerList)
            if (mode.InHunterRoom(PlayerRoles.Get(p), phase)) wanted.Add(p.ActorNumber);
        gone.Clear();
        foreach (var actor in decoys.Keys) if (!wanted.Contains(actor)) gone.Add(actor);
        foreach (var actor in gone) { if (decoys[actor] != null) Destroy(decoys[actor].gameObject); decoys.Remove(actor); }
        if (Spawner.Instance != null)
            foreach (var actor in wanted)
            {
                if (!decoys.TryGetValue(actor, out var decoy) || decoy == null)
                {
                    var owner = FindPlayer(actor);
                    if (owner == null) continue;
                    decoys[actor] = decoy = HunterDecoy.Create(owner);
                }
                Spawner.Instance.HunterStart(actor, out Vector3 at, out Quaternion facing);   // Moves along if the hunters change
                decoy.transform.SetPositionAndRotation(at, facing);
            }

        // The local player: into the room, or out of it onto their decoy's spot
        var local = Player.Local;
        if (local == null || local.IsCaught) return;
        bool belongs = mode.InHunterRoom(local.Role, phase);
        bool inside = room != null && Inside(local.transform.position);
        if (belongs && !inside)
        {
            Spot(local.ActorNumber, out Vector3 position, out Quaternion rotation);
            Move(local, position, rotation);
        }
        else if (!belongs && inside)
        {
            if (local.Role == PlayerRole.Hunter && Spawner.Instance != null)
            {
                Spawner.Instance.HunterStart(local.ActorNumber, out Vector3 position, out Quaternion rotation);
                Move(local, position, rotation);   // The hunt: take over the decoy
            }
            else if (Spawner.Instance != null) Spawner.Instance.RespawnLocalPlayer();
        }
    }

    private static void Move(Player local, Vector3 position, Quaternion rotation)
    {
        local.Teleport(position, rotation);
        if (PlayerCam.Local != null) PlayerCam.Local.SnapTo(rotation.eulerAngles.y);
    }

    private static PhotonPlayer FindPlayer(int actor)
    {
        foreach (var p in PhotonNetwork.PlayerList) if (p.ActorNumber == actor) return p;
        return null;
    }

    private void ClearDecoys()
    {
        if (decoys.Count == 0) return;
        foreach (var d in decoys.Values) if (d != null) Destroy(d.gameObject);
        decoys.Clear();
    }

    // ─── The room ─────────────────────────────────────────────────────────────

    // The Hunter's Lobby (Justin, Nov 14: "a little parkour course … some decorations and colour"): a 48 × 48 m gym, 16 m
    // tall, built from boxes. One loop, sized from the movement metrics (jump 3.2 m up, ledge grab 6.8 m):
    //   step stones up the west side (+1.2 m each) → the north-west deck (6) → a balance beam along the north wall →
    //   the north-east deck → four pillar hops down the east side, 4 m apart and 1 m higher each (6 → 9) → a post (10)
    //   → the summit tower (12, a flag and a trophy); the tower's west face has ledges at 4 and 8 to climb it from the
    //   floor, and a crawl tunnel and a crate pile sit near the start. Checker floor, striped walls, bunting, balloons.
    private static readonly Color Navy   = new(0.15f, 0.17f, 0.27f), NavyLight = new(0.21f, 0.24f, 0.37f);
    private static readonly Color Lilac  = new(0.44f, 0.46f, 0.66f);
    private static readonly Color Cyan   = new(0.2f, 0.78f, 0.95f), Pink = new(1f, 0.42f, 0.7f), Yellow = new(1f, 0.8f, 0.25f);
    private static readonly Color Lime   = new(0.55f, 0.9f, 0.32f), Purple = new(0.58f, 0.42f, 0.96f), Orange = new(1f, 0.56f, 0.22f);
    private static readonly Color[] Palette = { Cyan, Pink, Yellow, Lime, Purple, Orange };

    private void Ensure()
    {
        if (room != null) return;
        room = new GameObject("Hunter Room").transform;
        room.position = Origin;
        float h = Size * 0.5f;

        // Shell
        Box("Floor",   new Vector3(0f, -0.5f, 0f),             new Vector3(Size + 2f, 1f, Size + 2f), Navy);
        Box("Ceiling", new Vector3(0f, WallHeight + 0.5f, 0f), new Vector3(Size + 2f, 1f, Size + 2f), Navy);
        Box("Wall N",  new Vector3(0f, WallHeight * 0.5f, h + 0.5f),  new Vector3(Size + 2f, WallHeight, 1f), Lilac);
        Box("Wall S",  new Vector3(0f, WallHeight * 0.5f, -h - 0.5f), new Vector3(Size + 2f, WallHeight, 1f), Lilac);
        Box("Wall E",  new Vector3(h + 0.5f, WallHeight * 0.5f, 0f),  new Vector3(1f, WallHeight, Size), Lilac);
        Box("Wall W",  new Vector3(-h - 0.5f, WallHeight * 0.5f, 0f), new Vector3(1f, WallHeight, Size), Lilac);

        // Checker floor and wall stripes (decoration: no colliders)
        for (int i = 0; i < 12; i++)
            for (int j = 0; j < 12; j++)
                if ((i + j) % 2 == 0) Deco(new Vector3(-h + 2f + i * 4f, 0.01f, -h + 2f + j * 4f), new Vector3(4f, 0.02f, 4f), NavyLight);
        foreach (var (y, c) in new[] { (2.5f, Pink), (3f, Yellow), (11f, Cyan), (11.5f, Purple) })
        {
            Deco(new Vector3(0f, y, h - 0.02f),  new Vector3(Size, 0.35f, 0.06f), c);
            Deco(new Vector3(0f, y, -h + 0.02f), new Vector3(Size, 0.35f, 0.06f), c);
            Deco(new Vector3(h - 0.02f, y, 0f),  new Vector3(0.06f, 0.35f, Size), c);
            Deco(new Vector3(-h + 0.02f, y, 0f), new Vector3(0.06f, 0.35f, Size), c);
        }
        foreach (var (x, z) in new[] { (-h + 0.6f, -h + 0.6f), (h - 0.6f, -h + 0.6f), (-h + 0.6f, h - 0.6f), (h - 0.6f, h - 0.6f) })
            Deco(new Vector3(x, WallHeight * 0.5f, z), new Vector3(1.2f, WallHeight * 0.5f, 1.2f), Orange, PrimitiveType.Cylinder);

        // 1. Step stones up the west side
        for (int i = 0; i < 5; i++)
        {
            float top = 1.2f * (i + 1);
            Box($"Step {i + 1}", new Vector3(-19f, top * 0.5f, -12f + i * 4.5f), new Vector3(3f, top, 3f), Palette[i % Palette.Length]);
        }
        // 2. North-west deck (6) and 3. the balance beam to the north-east deck (6)
        Box("NW Deck", new Vector3(-18f, 3f, 17f), new Vector3(8f, 6f, 10f), Purple);
        Box("Beam", new Vector3(-4f, 5.75f, 19f), new Vector3(20f, 0.5f, 0.8f), Yellow);
        foreach (float x in new[] { -9f, -4f, 1f }) Deco(new Vector3(x, 2.75f, 19f), new Vector3(0.4f, 2.75f, 0.4f), Lilac, PrimitiveType.Cylinder);
        Box("NE Deck", new Vector3(10f, 3f, 18f), new Vector3(8f, 6f, 8f), Cyan);
        // 4. Pillar hops down the east side, 4 m gaps, one metre higher each
        for (int i = 0; i < 4; i++)
        {
            float top = 6f + i;
            Box($"Hop {i + 1}", new Vector3(18f, top * 0.5f, 12f - i * 8f), new Vector3(4f, top, 4f), Palette[(i + 1) % Palette.Length]);
        }
        // 5. A post, then the summit tower
        Box("Post", new Vector3(12f, 5f, -12f), new Vector3(2f, 10f, 2f), Lime);
        Box("Summit", new Vector3(4f, 6f, -10f), new Vector3(6f, 12f, 6f), Pink);
        Box("Ledge 4", new Vector3(0.25f, 3.75f, -10f), new Vector3(1.5f, 0.5f, 3f), Yellow);   // West face: floor → 4 → 8 → top
        Box("Ledge 8", new Vector3(0.25f, 7.75f, -10f), new Vector3(1.5f, 0.5f, 3f), Yellow);
        Deco(new Vector3(6.5f, 13.5f, -7.5f), new Vector3(0.15f, 1.5f, 0.15f), Color.white, PrimitiveType.Cylinder);   // Flag pole
        Deco(new Vector3(6.5f, 14.5f, -8.3f), new Vector3(0.05f, 0.9f, 1.4f), Yellow);                                // Flag
        Deco(new Vector3(3f, 12.75f, -11f), new Vector3(1.1f, 1.1f, 1.1f), Yellow, PrimitiveType.Sphere);             // Trophy
        Deco(new Vector3(3f, 12.15f, -11f), new Vector3(0.7f, 0.15f, 0.7f), Orange, PrimitiveType.Cylinder);

        // Near the start: a crawl tunnel (1.6 m under a 2.2 m top) and a crate pile
        Box("Tunnel Roof", new Vector3(-8f, 1.9f, -7f), new Vector3(4f, 0.6f, 6f), Orange);
        Box("Tunnel Side W", new Vector3(-10.25f, 0.8f, -7f), new Vector3(0.5f, 1.6f, 6f), Lilac);
        Box("Tunnel Side E", new Vector3(-5.75f, 0.8f, -7f), new Vector3(0.5f, 1.6f, 6f), Lilac);
        Box("Crate 1", new Vector3(10f, 0.75f, -18f), new Vector3(1.5f, 1.5f, 1.5f), Orange);
        Box("Crate 2", new Vector3(11.5f, 0.75f, -18f), new Vector3(1.5f, 1.5f, 1.5f), Yellow);
        Box("Crate 3", new Vector3(10.75f, 2.25f, -18f), new Vector3(1.5f, 1.5f, 1.5f), Lime);

        // Bunting along the north and south walls, balloons in the corners and over the middle
        int n = 0;
        for (float x = -h + 1.5f; x < h; x += 1.6f, n++)
            foreach (float z in new[] { h - 0.4f, -h + 0.4f })
            {
                var flag = Deco(new Vector3(x, 13.2f - Mathf.Sin((x + h) / Size * Mathf.PI) * 1.2f, z), new Vector3(0.7f, 0.7f, 0.04f), Palette[n % Palette.Length]);
                flag.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }
        int b = 0;
        foreach (var (x, z) in new[] { (-20f, -20f), (20f, -20f), (-20f, 20f), (20f, 20f), (-6f, 2f) })
            for (int k = 0; k < 3; k++, b++)
            {
                Vector3 at = new(x + (k - 1) * 0.9f, 13.6f + (k % 2) * 0.6f, z + (k == 1 ? 0.6f : 0f));
                Deco(at, new Vector3(1f, 1.2f, 1f), Palette[b % Palette.Length], PrimitiveType.Sphere);
                Deco(at + Vector3.down * 1.4f, new Vector3(0.03f, 0.8f, 0.03f), Color.white, PrimitiveType.Cylinder);
            }
        // Arrows on the floor from the start toward the first step
        for (int i = 0; i < 4; i++)
        {
            var arrow = Deco(new Vector3(-4f - i * 4f, 0.03f, -17f + i * 1.2f), new Vector3(1.4f, 0.02f, 1.4f), Yellow);
            arrow.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        }

        // Light: warm white overhead, a pink and a cyan glow
        foreach (var (x, z) in new[] { (-12f, -12f), (12f, -12f), (-12f, 12f), (12f, 12f), (0f, 0f) })
            Lamp(new Vector3(x, WallHeight - 1.5f, z), new Color(1f, 0.96f, 0.9f), 28f, 4f);
        Lamp(new Vector3(-18f, 4f, -18f), Pink, 18f, 3f);
        Lamp(new Vector3(18f, 4f, 18f), Cyan, 18f, 3f);

        var signGo = new GameObject("Sign");
        signGo.transform.SetParent(room, false);
        signGo.transform.localPosition = new Vector3(0f, 8.5f, h - 0.1f);
        var sign = signGo.AddComponent<TextMeshPro>();
        sign.rectTransform.sizeDelta = new Vector2(30f, 6f);
        sign.alignment = TextAlignmentOptions.Center;
        sign.fontSize = 11f;
        sign.color = Color.white;
        sign.outlineWidth = 0.2f;
        sign.outlineColor = new Color32(20, 16, 40, 255);
        sign.text = "<size=150%>HUNTER'S LOBBY</size>\nThe hiders see you counting. When the hunt starts, you take your penguin's place.\n<color=#FFD040>Steps on the left: reach the summit!</color>";
    }

    private void Lamp(Vector3 local, Color color, float range, float intensity)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(room, false);
        go.transform.localPosition = local;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }

    // Looks only: no collider, no shadow
    private GameObject Deco(Vector3 local, Vector3 size, Color color, PrimitiveType shape = PrimitiveType.Cube)
    {
        var go = GameObject.CreatePrimitive(shape);
        go.name = "Deco";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(room, false);
        go.transform.localPosition = local;
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = Material(color);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    // A plain URP Lit material in the colour. Not the primitive's default material: that one isn't always there (a
    // Multiplayer Play Mode player's room was all pink), so it's copied from one in Resources, whose shader is in builds
    private readonly Dictionary<Color, Material> materials = new();

    private Material Material(Color color)
    {
        if (materials.TryGetValue(color, out var m) && m != null) return m;
        var source = Resources.Load<Material>("Cosmetics/Materials/Cap Blue");
        m = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.name = "Hunter Room";
        m.SetColor("_BaseColor", color);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        return materials[color] = m;
    }

    private void Box(string name, Vector3 local, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = 6;   // Structure: walking, wall-running and climbing work in here
        go.transform.SetParent(room, false);
        go.transform.localPosition = local;
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = Material(color);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
