using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;

/// <summary>
/// Builds the expansion of the Improvement map under one root ("Expansion"), leaving Justin's geometry
/// untouched. Re-running rebuilds it (the old root is deleted first); deleting the root removes it all.
/// Heights come from raycasts against the existing level, so it fits the real floor, tiers and rim.
///
/// Zones (see the plan in chat): hub cover, the island (4 m platform with a tunnel under it), the 12 m
/// catwalk east (moving shuttle) to a landing and a lift up to the 18 m rim, stepping ledges from the
/// 6 m north ledge to the rim, the west tunnel, the south-east workshop and shed (hiding nook with a
/// roof-hatch escape), bounce pads, lights and props; on the 18 m rim a tower, huts with a roof
/// bridge, a gallery and a watch platform; outside, risky jump chains over the void with lifts back up.
/// Menu: Turbo Tag/Levels/Rebuild Improvement Expansion (editor job: BuildImprovementExpansion).
/// </summary>
public static class ImprovementExpansion
{
    private const string ScenePath  = "Assets/Scenes/Levels/Improvement.unity";
    private const string RootName   = "Expansion";
    private const int    Structure  = 6;

    private static Material wallLight, wallOff, wallDark, accent;
    private static Transform root;
    private static readonly List<string> log = new();

    [MenuItem("Turbo Tag/Levels/Rebuild Improvement Expansion")]
    public static void Build()
    {
        log.Clear();
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
        bool wasOpen = scene.IsValid() && scene.isLoaded;
        if (!wasOpen) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);

        try
        {
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == RootName) Object.DestroyImmediate(go);

            LoadMaterials();
            root = new GameObject(RootName).transform;

            // ─── Real heights from the level ───
            float g   = Top(0f, -10f);    // Ground floor
            float t6  = Top(-14f, 20f);   // West block, lower tier
            float t6n = Top(10f, 37f);    // North ledge
            float t12 = Top(5f, 28f);     // West block, upper tier (ramp top)
            float r18 = Top(42f, 10f);    // East / north rim
            log.Add($"heights: ground {g:0.##}, west tier {t6:0.##}, north ledge {t6n:0.##}, upper tier {t12:0.##}, rim {r18:0.##}");

            BuildHub(g);
            BuildIsland(g);
            BuildCatwalk(g, t12, r18);
            BuildNorthSteps(g, t6n, r18);
            BuildWestTunnel(g, t6);
            BuildWorkshop(g);

            // ─── Upper (18 m rim) and outer (risky jumps over the void) ───
            float rimN  = Top(10f, 48f);    // North band of the rim
            float rimE  = Top(43f, 0f);     // East band of the rim
            float tail  = Top(-4f, 62f);    // Plank sticking out north at the lower level
            float strip = Top(-39f, 40f);   // Lower strip outside the west wall
            float ring  = Top(68f, 0f);     // East walkway around the pit
            log.Add($"outer heights: rim N {rimN:0.##}, rim E {rimE:0.##}, north tail {tail:0.##}, west strip {strip:0.##}, east ring {ring:0.##}");

            BuildRimNorth(rimN);
            BuildRimEast(rimE);
            BuildOuterNorth(rimN, tail, strip);
            BuildOuterEast(rimE, ring);
            BuildSpawnPoints(rimE);

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.layer == 0 && t.GetComponent<Light>() == null) t.gameObject.layer = Structure;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
        }
        Debug.Log("[ImprovementExpansion]\n  " + string.Join("\n  ", log));
    }

    // ─── Zones ────────────────────────────────────────────────────────────────

    // Cover around the teleport pad so the hall isn't one long sightline
    private static void BuildHub(float g)
    {
        var hub = Group("Hub");
        Prop(hub, "Crate-1x1m", new Vector3(10f, g, -9f), 30f, 4f);          // One big crate
        Block(hub, "Cover Block W", new Vector3(-11f, g, -7f), new Vector3(3f, 3.5f, 3f), wallOff, wallLight);
        Block(hub, "Cover Block E", new Vector3(12f, g, 6f),   new Vector3(3f, 2.2f, 5f), wallOff, wallLight);

        Block(hub, "LowWall N",  new Vector3(21f, g, 12f),  new Vector3(9f, 2.4f, 0.8f),  wallOff, wallLight);
        Block(hub, "LowWall W",  new Vector3(-17f, g, -1f), new Vector3(0.8f, 2.4f, 8f),  wallOff, wallLight);
        Block(hub, "LowWall E",  new Vector3(17f, g, -4f),  new Vector3(0.8f, 2.4f, 8f),  wallOff, wallLight);
        Block(hub, "Pillar NE",  new Vector3(15f, g, 17f),  new Vector3(1.6f, 6f, 1.6f),  wallDark, wallLight);
        Block(hub, "Pillar SE",  new Vector3(23f, g, -6f),  new Vector3(1.6f, 6f, 1.6f),  wallDark, wallLight);

    }

    // A 4.5 m platform south of the hub with a tunnel through it (hiding spot, two ways out)
    private static void BuildIsland(float g)
    {
        var island = Group("Island");
        float top = g + 5f;
        Block(island, "Island W",    new Vector3(-6f, g, -20f), new Vector3(6f, 5f, 8f), wallOff, wallLight);
        Block(island, "Island E",    new Vector3(6f, g, -20f),  new Vector3(6f, 5f, 8f), wallOff, wallLight);
        Block(island, "Island Roof", new Vector3(0f, g + 3.5f, -20f), new Vector3(6f, 1.5f, 8f), wallOff, wallLight);
        Strip(island, new Vector3(0f, top, -16f), new Vector3(18f, 0.12f, 0.25f));
        Lamp(island, new Vector3(0f, g + 3f, -20f), 6f);

        // Ways up: crate steps on the west side, a bounce pad on the east
        Block(island, "Step Low",  new Vector3(-13f, g, -20f), new Vector3(3f, 2f, 4f), wallOff, wallLight);
        Block(island, "Step High", new Vector3(-10.5f, g, -20f), new Vector3(2f, 3.6f, 4f), wallOff, wallLight);
        Bounce(island, new Vector3(11.5f, g, -20f), 22f);
        Prop(island, "Checkpoint", new Vector3(0f, top, -20f), 0f, 1f);
    }

    // From the 12 m tier east across the hall: catwalk, a moving shuttle over the gap, a landing,
    // and a lift up to the 18 m rim
    private static void BuildCatwalk(float g, float t12, float r18)
    {
        var walk = Group("Catwalk");
        float deckY = t12 - 1f;
        Block(walk, "Catwalk A", new Vector3(15f, deckY, 29f), new Vector3(10f, 1f, 4f), wallOff, wallLight);
        Block(walk, "Landing",   new Vector3(31f, deckY, 27f), new Vector3(6f, 1f, 8f),  wallOff, wallLight);
        Support(walk, new Vector3(13f, g, 29f), deckY);
        Support(walk, new Vector3(18f, g, 29f), deckY);
        Support(walk, new Vector3(31f, g, 24.5f), deckY);
        Strip(walk, new Vector3(15f, t12, 27f), new Vector3(10f, 0.12f, 0.2f));
        Strip(walk, new Vector3(15f, t12, 31f), new Vector3(10f, 0.12f, 0.2f));
        for (float x = 11f; x <= 19f; x += 2f) Prop(walk, "Railing-2m", new Vector3(x, t12, 31f), 180f, 1f);
        Lamp(walk, new Vector3(31f, t12 + 2.5f, 27f), 9f);

        // Shuttle across the 8 m gap (catwalk ends at x 20, landing starts at x 28)
        Mover(walk, "Shuttle", new Vector3(22f, deckY, 29f), new Vector3(26f, deckY, 29f), new Vector3(4f, 1f, 4f), 3f);

        // Lift from the landing up to the rim (step off east, onto the rim)
        Mover(walk, "Lift", new Vector3(33f, deckY, 21f), new Vector3(33f, r18 - 0.95f, 21f), new Vector3(4f, 1f, 4f), 2.5f);
        Strip(walk, new Vector3(36.2f, r18 + 0.06f, 21f), new Vector3(0.2f, 0.12f, 4f)); // Marks where the lift arrives
        log.Add($"catwalk at {t12:0.##} m, lift to {r18:0.##} m");
    }

    // Floating ledges from the 6 m north ledge up to the 18 m rim (jump + ledge grab, ~4 m each)
    private static void BuildNorthSteps(float g, float t6n, float r18)
    {
        var steps = Group("North Steps");
        float s1 = t6n + 4f, s2 = t6n + 8f, s3 = r18 - 2f;
        Block(steps, "Step 1", new Vector3(14f, s1 - 1f, 33f), new Vector3(4f, 1f, 4f), wallOff, wallLight);
        Block(steps, "Step 2", new Vector3(20f, s2 - 1f, 35f), new Vector3(4f, 1f, 4f), wallOff, wallLight);
        Block(steps, "Step 3", new Vector3(26f, s3 - 1f, 37.5f), new Vector3(4f, 1f, 4f), wallOff, wallLight);
        Support(steps, new Vector3(14f, g, 33f), s1 - 1f);
        foreach (float x in new[] { 14f, 20f, 26f })
            Strip(steps, new Vector3(x, (x == 14f ? s1 : x == 20f ? s2 : s3), x == 26f ? 35.5f : x == 20f ? 33f : 31f), new Vector3(4f, 0.12f, 0.2f));
        Lamp(steps, new Vector3(20f, s2 + 2f, 33f), 8f);
        log.Add($"north steps at {s1:0.#}, {s2:0.#}, {s3:0.#} m");
    }

    // A covered tunnel along the west side (doors at both ends and one on the side), and a bounce pad
    // from the floor up to the 6 m west tier
    private static void BuildWestTunnel(float g, float t6)
    {
        var tunnel = Group("West Tunnel");
        float h = 4.5f;
        Block(tunnel, "Wall Outer", new Vector3(-33.5f, g, 0f),  new Vector3(1f, h, 18f), wallDark, wallDark);
        Block(tunnel, "Wall Inner N", new Vector3(-24.5f, g, 5.5f),  new Vector3(1f, h, 7f), wallDark, wallLight);
        Block(tunnel, "Wall Inner S", new Vector3(-24.5f, g, -5.5f), new Vector3(1f, h, 7f), wallDark, wallLight);
        Block(tunnel, "Roof", new Vector3(-29f, g + h, 0f), new Vector3(10f, 1f, 18f), wallOff, wallLight);
        Strip(tunnel, new Vector3(-24f, g + h + 1.06f, 0f), new Vector3(0.2f, 0.12f, 18f));
        Lamp(tunnel, new Vector3(-29f, g + h - 0.6f, 5f), 7f);
        Lamp(tunnel, new Vector3(-29f, g + h - 0.6f, -5f), 7f);
        Prop(tunnel, "Vent", new Vector3(-30f, g + h + 1f, -4f), 0f, 1f);

        Bounce(tunnel, new Vector3(-21f, g, 22f), 26f);   // Floor → 6 m west tier (drift east onto it)
    }

    // South-east: clutter to weave through, and a shed with one door and a roof hatch to escape by
    private static void BuildWorkshop(float g)
    {
        var shop = Group("Workshop");
        Prop(shop, "Crate-1x1m", new Vector3(16f, g, -24f), 10f, 4f);
        Block(shop, "Shelf A", new Vector3(19f, g, -14f), new Vector3(6f, 3.2f, 1f), wallDark, wallLight);
        Block(shop, "Shelf B", new Vector3(10f, g, -26f), new Vector3(1f, 3.2f, 5f), wallDark, wallLight);

        // Shed x 22..32, z -28..-20: door in the north wall, hatch in the roof's back corner
        float h = 4f;
        Block(shop, "Shed S", new Vector3(27f, g, -28f), new Vector3(10f, h, 0.8f), wallDark, wallLight);
        Block(shop, "Shed W", new Vector3(22.4f, g, -24f), new Vector3(0.8f, h, 8f), wallDark, wallLight);
        Block(shop, "Shed E", new Vector3(31.6f, g, -24f), new Vector3(0.8f, h, 8f), wallDark, wallLight);
        Block(shop, "Shed N L", new Vector3(24.5f, g, -20f), new Vector3(5f, h, 0.8f), wallDark, wallLight);
        Block(shop, "Shed N R", new Vector3(30.5f, g, -20f), new Vector3(2f, h, 0.8f), wallDark, wallLight);
        Block(shop, "Shed N Lintel", new Vector3(28f, g + 2.8f, -20f), new Vector3(3f, h - 2.8f, 0.8f), wallDark, wallLight);
        Block(shop, "Shed Roof A", new Vector3(26f, g + h, -24f), new Vector3(8f, 0.6f, 8.8f), wallOff, wallLight);
        Block(shop, "Shed Roof B", new Vector3(31f, g + h, -21.5f), new Vector3(2f, 0.6f, 3.8f), wallOff, wallLight);
        Block(shop, "Hatch Step", new Vector3(31f, g, -26f), new Vector3(1.8f, 2.2f, 2.5f), wallOff, wallLight); // Up to the hatch
        Prop(shop, "Vent 1", new Vector3(27f, g + h + 0.6f, -22f), 0f, 0.25f);
        Lamp(shop, new Vector3(26f, g + h - 0.7f, -24f), 6f);
    }

    // ─── Upper level: structures on the 18 m rim ──────────────────────────────

    // North band (x -34..50, z 40..56): a tower in the north-west corner, two huts joined by a roof bridge
    private static void BuildRimNorth(float r)
    {
        var north = Group("Rim North");

        // Tower: walk-through base (a passage north–south), ledges up its east face to a lookout at r + 12
        Block(north, "Tower W", new Vector3(-31.5f, r, 50f), new Vector3(3f, 12f, 8f), wallDark, wallLight);
        Block(north, "Tower E", new Vector3(-26.5f, r, 50f), new Vector3(3f, 12f, 8f), wallDark, wallLight);
        Block(north, "Tower Top", new Vector3(-29f, r + 3.5f, 50f), new Vector3(2f, 8.5f, 8f), wallDark, wallLight);
        Block(north, "Ledge 1", new Vector3(-24.4f, r + 3.5f, 48f), new Vector3(1.2f, 0.6f, 3f), wallOff, wallLight);
        Block(north, "Ledge 2", new Vector3(-24.4f, r + 7f, 51.5f), new Vector3(1.2f, 0.6f, 3f), wallOff, wallLight);
        Block(north, "Ledge 3", new Vector3(-24.4f, r + 10f, 48f), new Vector3(1.2f, 0.6f, 3f), wallOff, wallLight);
        Strip(north, new Vector3(-29f, r + 12.06f, 46f), new Vector3(8f, 0.12f, 0.2f));
        Lamp(north, new Vector3(-29f, r + 2.5f, 50f), 6f);

        // Huts with a door each side; a bridge across their roofs (clear of the crates at x 0..6)
        Hut(north, "Hut A", new Vector3(-12f, r, 49f), r);
        Hut(north, "Hut B", new Vector3(16f, r, 49f), r);
        float roof = r + 4.5f;
        Block(north, "Roof Bridge", new Vector3(2f, roof, 52.5f), new Vector3(20f, 0.6f, 3f), wallOff, wallLight);
        Support(north, new Vector3(-2f, r, 52.5f), roof);
        Support(north, new Vector3(6f, r, 52.5f), roof);
        Strip(north, new Vector3(2f, roof + 0.66f, 51f), new Vector3(20f, 0.12f, 0.2f));

        // Cover on the open stretches
        Block(north, "Low Wall 1", new Vector3(-20f, r, 44f), new Vector3(0.8f, 2.2f, 5f), wallOff, wallLight);
        Block(north, "Low Wall 2", new Vector3(30f, r, 46f), new Vector3(6f, 2.2f, 0.8f), wallOff, wallLight);
    }

    // A small building (8 x 8, 4.5 high) with doorways in its north and south walls
    private static void Hut(Transform parent, string name, Vector3 baseCentre, float r)
    {
        var hut = Group(name);
        hut.SetParent(parent, false);
        float h = 4.5f, x = baseCentre.x, z = baseCentre.z;
        Block(hut, "W", new Vector3(x - 3.6f, r, z), new Vector3(0.8f, h, 8f), wallDark, wallLight);
        Block(hut, "E", new Vector3(x + 3.6f, r, z), new Vector3(0.8f, h, 8f), wallDark, wallLight);
        foreach (float side in new[] { -1f, 1f })
        {
            float zw = z + side * 3.6f;
            Block(hut, "Wall L", new Vector3(x - 2.4f, r, zw), new Vector3(2.4f, h, 0.8f), wallDark, wallLight);
            Block(hut, "Wall R", new Vector3(x + 2.4f, r, zw), new Vector3(2.4f, h, 0.8f), wallDark, wallLight);
            Block(hut, "Lintel", new Vector3(x, r + 3f, zw), new Vector3(2.4f, h - 3f, 0.8f), wallDark, wallLight);
        }
        Block(hut, "Roof", new Vector3(x, r + h, z), new Vector3(8f, 0.6f, 8f), wallOff, wallLight);
        Lamp(hut, new Vector3(x, r + h - 0.8f, z), 6f);
    }

    // East band (x 36..50, z -36..40): a covered gallery, a stepped watch platform in the south-east corner
    private static void BuildRimEast(float r)
    {
        var east = Group("Rim East");

        // Gallery: a covered run along the band with a gap in each wall halfway (side ways in and out)
        float h = 4.5f;
        Block(east, "Gallery W N", new Vector3(39.4f, r, 6.5f),  new Vector3(0.8f, h, 7f), wallDark, wallLight);
        Block(east, "Gallery W S", new Vector3(39.4f, r, -4.5f), new Vector3(0.8f, h, 7f), wallDark, wallLight);
        Block(east, "Gallery E N", new Vector3(46.6f, r, 6.5f),  new Vector3(0.8f, h, 7f), wallDark, wallLight);
        Block(east, "Gallery E S", new Vector3(46.6f, r, -4.5f), new Vector3(0.8f, h, 7f), wallDark, wallLight);
        Block(east, "Gallery Roof", new Vector3(43f, r + h, 1f), new Vector3(8f, 0.6f, 18f), wallOff, wallLight);
        Strip(east, new Vector3(39f, r + h + 0.66f, 1f), new Vector3(0.2f, 0.12f, 18f));
        Lamp(east, new Vector3(43f, r + h - 0.8f, 5f), 7f);
        Lamp(east, new Vector3(43f, r + h - 0.8f, -3f), 7f);

        // Watch platform, r + 5, reached by three steps from the north
        Block(east, "Watch Deck", new Vector3(44f, r, -31f), new Vector3(8f, 5f, 6f), wallOff, wallLight);
        Block(east, "Watch Step 1", new Vector3(44f, r, -26.5f), new Vector3(4f, 1.6f, 3f), wallOff, wallLight);
        Block(east, "Watch Step 2", new Vector3(44f, r, -27.5f), new Vector3(4f, 3.2f, 1.5f), wallOff, wallLight);
        Strip(east, new Vector3(44f, r + 5.06f, -28f), new Vector3(8f, 0.12f, 0.2f));

        Block(east, "Low Wall 3", new Vector3(42f, r, 23f),  new Vector3(0.8f, 2.2f, 6f), wallOff, wallLight);
        Block(east, "Low Wall 4", new Vector3(45f, r, -16f), new Vector3(5f, 2.2f, 0.8f), wallOff, wallLight);
    }

    // ─── Outer: risky routes over the void (falling off in Tag makes you it) ────

    // North: a descending chain of small blocks from the rim to an outer island, a platform back up;
    // a lift from the lower plank to the rim; and a jump chain from the plank west to the west strip
    private static void BuildOuterNorth(float rimN, float tail, float strip)
    {
        var outer = Group("Outer North");

        // Rim edge (z ~57) → island. Small 3 m blocks, ~5–6 m apart: a 95% sprint jump at worst
        Block(outer, "Jump 1", new Vector3(22f, rimN - 3f, 63f), new Vector3(3f, 1f, 3f), wallOff, wallLight);
        Block(outer, "Jump 2", new Vector3(25f, rimN - 6f, 70f), new Vector3(3f, 1f, 3f), wallOff, wallLight);
        Block(outer, "Jump 3", new Vector3(21f, rimN - 9f, 77f), new Vector3(2.5f, 1f, 2.5f), wallOff, wallLight);
        float island = rimN - 11f;
        Block(outer, "Outer Island", new Vector3(14f, island - 2f, 86f), new Vector3(12f, 2f, 9f), wallOff, wallLight);
        Block(outer, "Island Shelter", new Vector3(10f, island, 88.5f), new Vector3(4f, 3f, 0.8f), wallDark, wallLight);
        Strip(outer, new Vector3(14f, island + 0.06f, 81.5f), new Vector3(12f, 0.12f, 0.2f));
        Lamp(outer, new Vector3(14f, island + 3f, 86f), 9f);

        // Back up: a platform riding from the island's west end to the rim edge
        Mover(outer, "Island Return", new Vector3(5f, island - 1f, 84f), new Vector3(5f, rimN - 1f, 59f), new Vector3(4f, 1f, 4f), 3f);

        // Lower plank (x -8..0, z 58..66) → rim
        Mover(outer, "Plank Lift", new Vector3(-4f, tail - 1f, 60f), new Vector3(-4f, rimN - 1f, 59.5f), new Vector3(4f, 1f, 3.5f), 3f);

        // Plank → west strip: across the void, slowly climbing then dropping to the strip
        Block(outer, "West Hop 1", new Vector3(-13f, tail - 0.5f, 64f), new Vector3(3f, 1f, 3f), wallOff, wallLight);
        Block(outer, "West Beam",  new Vector3(-20f, tail + 0.5f, 63f), new Vector3(7f, 1f, 1f),  wallOff, wallLight);
        Block(outer, "West Hop 2", new Vector3(-28f, tail + 1f, 60f),   new Vector3(3f, 1f, 3f), wallOff, wallLight);
        Block(outer, "West Hop 3", new Vector3(-34f, (tail + strip) * 0.5f - 0.5f, 55f), new Vector3(3f, 1f, 3f), wallOff, wallLight);
        Block(outer, "West Hop 4", new Vector3(-39f, strip - 1f, 51f), new Vector3(3f, 1f, 3f), wallOff, wallLight);
    }

    // East: a lift from the −4 m walkway up to the rim, and a crossing over the pit of blocks and narrow beams
    private static void BuildOuterEast(float rimE, float ring)
    {
        var outer = Group("Outer East");

        Mover(outer, "Ring Lift", new Vector3(54f, ring - 1f, 39f), new Vector3(54f, rimE - 1f, 39f), new Vector3(4f, 1f, 4f), 3f);

        // From the north side of the walkway (z ~36) south over the pit to its south side (z ~-32)
        float y = ring - 1f;
        Block(outer, "Pit 1",    new Vector3(59f, y, 29f),        new Vector3(3f, 1f, 3f), wallOff, wallLight);
        Block(outer, "Pit Beam 1", new Vector3(59f, y, 21f),      new Vector3(1f, 1f, 7f), wallOff, wallLight);
        Block(outer, "Pit 2",    new Vector3(57f, y + 1f, 12f),   new Vector3(3f, 1f, 3f), wallOff, wallLight);
        Block(outer, "Pit 3",    new Vector3(61f, y + 2f, 5f),    new Vector3(2.5f, 1f, 2.5f), wallOff, wallLight);
        Block(outer, "Pit 4",    new Vector3(58f, y + 1f, -3f),   new Vector3(3f, 1f, 3f), wallOff, wallLight);
        Block(outer, "Pit Beam 2", new Vector3(58f, y + 1f, -11f), new Vector3(1f, 1f, 7f), wallOff, wallLight);
        Block(outer, "Pit 5",    new Vector3(60f, y, -19f),       new Vector3(3f, 1f, 3f), wallOff, wallLight);
        Block(outer, "Pit 6",    new Vector3(57f, y, -26f),       new Vector3(3f, 1f, 3f), wallOff, wallLight);
        Lamp(outer, new Vector3(59f, ring + 3f, 0f), 12f);
    }

    // ─── Spawn points (Free for All) ──────────────────────────────────────────

    // Safe spots on every layer, facing the middle of the map. Heights come from the level (huts and the shed
    // are measured through, so their floors count); the watch deck is ours, so it's added by hand.
    private static void BuildSpawnPoints(float rimE)
    {
        var spawns = Group("Spawn Points");
        var spots = new (string name, float x, float z, float? y)[]
        {
            ("Hub West",      -6f, -10f, null),
            ("Hub East",      21f,   3f, null),
            ("Island South",  -6f, -27f, null),
            ("Shed",          27f, -24f, null),
            ("West Tunnel",  -29f,   0f, null),
            ("West Tier",    -12f,  20f, null),
            ("Upper Tier",     5f,  28f, null),
            ("North Ledge",   10f,  37f, null),
            ("Hut A",        -12f,  49f, null),
            ("Hut B",         16f,  49f, null),
            ("Rim East",      43f, -12f, null),
            ("Watch Deck",    44f, -31f, rimE + 5f),
        };

        Physics.SyncTransforms();
        int i = 0;
        foreach (var (name, x, z, y) in spots)
        {
            var feet = new Vector3(x, y ?? Top(x, z), z);
            var go = new GameObject($"Spawn {++i:00} {name}");
            go.transform.SetParent(spawns, false);
            Vector3 toMiddle = new Vector3(5f, feet.y, 10f) - feet;
            go.transform.SetPositionAndRotation(feet + Vector3.up * 0.1f,
                Quaternion.LookRotation(toMiddle.sqrMagnitude > 0.01f ? toMiddle : Vector3.forward, Vector3.up));
            go.AddComponent<SpawnPoint>();

            bool blocked = Physics.CheckCapsule(feet + Vector3.up * 0.6f, feet + Vector3.up * 2.4f, 0.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            log.Add($"spawn {name} at {feet.y:0.##} m{(blocked ? "  ! BLOCKED" : "")}");
        }
    }

    // ─── Building blocks ──────────────────────────────────────────────────────

    private static Transform Group(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        return t;
    }

    /// <summary>ProBuilder box with its base at <paramref name="baseCentre"/>: top face one material, sides another.</summary>
    private static GameObject Block(Transform parent, string name, Vector3 baseCentre, Vector3 size, Material sides, Material top)
    {
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
        pb.name = name;
        pb.transform.SetParent(parent, false);
        pb.transform.position = baseCentre + Vector3.up * (size.y * 0.5f);

        foreach (var face in pb.faces)
        {
            var uv = face.uv;
            uv.useWorldSpace = true; // Textures tile by the metre, like the hand-built geometry
            face.uv = uv;
            pb.SetMaterial(new[] { face }, UnityEngine.ProBuilder.Math.Normal(pb, face).y > 0.9f ? top : sides);
        }
        pb.ToMesh();
        pb.Refresh();

        var col = pb.gameObject.AddComponent<MeshCollider>();
        col.sharedMesh = pb.GetComponent<MeshFilter>().sharedMesh;
        GameObjectUtility.SetStaticEditorFlags(pb.gameObject, (StaticEditorFlags)~0);
        return pb.gameObject;
    }

    private static void Support(Transform parent, Vector3 foot, float topY) =>
        Block(parent, "Support", foot, new Vector3(0.8f, topY - foot.y, 0.8f), wallDark, wallDark);

    // Thin orange trim along an edge (the map's accent colour)
    private static void Strip(Transform parent, Vector3 centre, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Trim";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position   = centre;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = accent;
        GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)~0);
    }

    private static void Lamp(Transform parent, Vector3 pos, float range)
    {
        var go = new GameObject("Lamp");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var light = go.AddComponent<Light>();
        light.type      = LightType.Point;
        light.color     = new Color(1f, 0.62f, 0.3f);
        light.range     = range;
        light.intensity = 3f;
        light.shadows   = LightShadows.None;
    }

    /// <summary>A kit prefab with its base centred on <paramref name="basePoint"/>, turned and scaled.</summary>
    private static GameObject Prop(Transform parent, string prefab, Vector3 basePoint, float yaw, float scale)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Environment/Prefabs/{prefab}.prefab");
        if (asset == null) { log.Add($"! missing prefab {prefab}"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.transform.rotation   = Quaternion.Euler(0f, yaw, 0f) * asset.transform.rotation;
        go.transform.localScale = asset.transform.localScale * scale;

        Bounds b = Bounds(go);
        go.transform.position += basePoint - new Vector3(b.center.x, b.min.y, b.center.z);
        return go;
    }

    // BouncePadv2 launching straight up with the given impulse (40 ≈ 20 m, 26 ≈ 8 m, 22 ≈ 6 m)
    private static void Bounce(Transform parent, Vector3 basePoint, float force)
    {
        var pad = Prop(parent, "BouncePadv2", basePoint, 0f, 1f);
        if (pad == null) return;
        var bounce = pad.GetComponentInChildren<BouncePad>();
        var so = new SerializedObject(bounce);
        so.FindProperty("bounceForce").floatValue = force;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // A platform moving between two points (MovingPlatform: same position on every client)
    private static void Mover(Transform parent, string name, Vector3 aBase, Vector3 bBase, Vector3 size, float speed)
    {
        var group = Group(name);
        group.SetParent(parent, false);
        var a = new GameObject("Point A").transform; a.SetParent(group, false); a.position = aBase + Vector3.up * (size.y * 0.5f);
        var b = new GameObject("Point B").transform; b.SetParent(group, false); b.position = bBase + Vector3.up * (size.y * 0.5f);

        var platform = Block(group, "Platform", aBase, size, wallOff, wallLight);
        GameObjectUtility.SetStaticEditorFlags(platform, 0); // It moves
        var rb = platform.AddComponent<Rigidbody>();
        rb.isKinematic   = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var mover = platform.AddComponent<MovingPlatform>();
        var so = new SerializedObject(mover);
        so.FindProperty("pointA").objectReferenceValue = a;
        so.FindProperty("pointB").objectReferenceValue = b;
        so.FindProperty("speed").floatValue    = speed;
        so.FindProperty("waitAtEnds").boolValue = true;
        so.FindProperty("waitTime").floatValue  = 1.5f;
        so.ApplyModifiedPropertiesWithoutUndo();
        Strip(platform.transform, platform.transform.position + Vector3.up * (size.y * 0.5f + 0.06f), new Vector3(size.x, 0.12f, 0.2f));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    // The highest surface at (x, z) that isn't part of the expansion (RaycastAll isn't sorted)
    private static float Top(float x, float z)
    {
        float best = float.MinValue;
        foreach (var hit in Physics.RaycastAll(new Vector3(x, 200f, z), Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore))
            if (root == null || !hit.transform.IsChildOf(root)) best = Mathf.Max(best, hit.point.y);
        return best == float.MinValue ? 0f : best;
    }

    private static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    private static void LoadMaterials()
    {
        wallLight = Mat("Assets/Environment/Materials/Large/WallLight.mat");
        wallOff   = Mat("Assets/Environment/Materials/Large/WallOfflight.mat");
        wallDark  = Mat("Assets/Environment/Materials/Large/WallDark.mat");
        accent    = Mat("Assets/Environment/Materials/Concrete/AccentOrange.mat");
    }

    private static Material Mat(string path)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) log.Add($"! missing material {path}");
        return m;
    }
}
