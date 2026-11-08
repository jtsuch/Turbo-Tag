using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using Photon.Pun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Sets up the tactical (health-mode) abilities in one go, re-runnable:
///   - placeholder object prefabs built from primitives in Resources/Object (Smoke, Frag, Molly, ReconBolt, ShockDart,
///     SnowScreen, TripWire, Turret, IceWall, SlowField, Suppressor, UpdraftVent, Drone), with their scripts, colliders,
///     PhotonViews and Shootable health; materials in Assets/Materials/Abilities/Tactical;
///   - the ability components on ThePlayer, references copied from Flashbang (throws) and IceTrap (traps), with their
///     cooldowns, forces, uses and limits;
///   - placeholder icons (a circle in the slot's colour with two letters) in Resources/AbilityIcons/{Id}Icon.png.
/// Replace any prefab or icon with a real one later (same name, same script on the root).
/// Menu: Turbo Tag/Abilities/Set Up Tactical Abilities (editor job SetupTacticalAbilities).
/// </summary>
public static class TacticalAbilitySetup
{
    private const string PlayerPrefab = "Assets/Resources/Player/ThePlayer.prefab";
    private const string ObjectFolder = "Assets/Resources/Object/";
    private const string MaterialFolder = "Assets/Materials/Abilities/Tactical";
    private const string IconFolder = "Assets/Resources/AbilityIcons/";
    private const string Report = "Library/TurboTag/scratch/tactical_setup.txt";

    // id, slot, cooldown, uses, (throw: min/max force) or (trap: max out, placing distance), icon letters
    private static readonly (string id, AbilitySlot slot, float cooldown, int charges, float a, float b, string letters)[] Abilities =
    {
        ("Smoke",       AbilitySlot.Throw, 25f, 2, 8f, 28f, "SM"),
        ("Frag",        AbilitySlot.Throw, 22f, 1, 8f, 26f, "FR"),
        ("Molly",       AbilitySlot.Throw, 28f, 1, 8f, 26f, "MO"),
        ("ReconBolt",   AbilitySlot.Throw, 30f, 1, 25f, 55f, "RB"),
        ("ShockDart",   AbilitySlot.Throw, 14f, 1, 20f, 40f, "SD"),
        ("InkBlast",    AbilitySlot.Quick, 20f, 1, 0f, 0f, "IN"),
        ("EchoStomp",   AbilitySlot.Quick, 25f, 1, 0f, 0f, "EC"),
        ("HealOrb",     AbilitySlot.Quick, 30f, 1, 0f, 0f, "HE"),
        ("BubbleShield",AbilitySlot.Quick, 30f, 1, 0f, 0f, "SH"),
        ("BellySlide",  AbilitySlot.Quick, 12f, 1, 0f, 0f, "BS"),
        ("Drone",       AbilitySlot.Quick, 30f, 1, 0f, 0f, "DR"),
        ("SnowScreen",  AbilitySlot.Trap,  30f, 1, 1f, 15f, "SN"),
        ("TripWire",    AbilitySlot.Trap,  15f, 1, 2f, 6f,  "TW"),
        ("Turret",      AbilitySlot.Trap,  40f, 1, 1f, 8f,  "TU"),
        ("IceWall",     AbilitySlot.Trap,  30f, 1, 1f, 12f, "IW"),
        ("SlowField",   AbilitySlot.Trap,  25f, 1, 1f, 15f, "SF"),
        ("Suppressor",  AbilitySlot.Trap,  35f, 1, 1f, 15f, "SU"),
        ("UpdraftVent", AbilitySlot.Trap,  25f, 1, 1f, 8f,  "UV"),
    };

    private static readonly List<string> log = new();
    private static readonly Dictionary<string, Material> mats = new();

    [MenuItem("Turbo Tag/Abilities/Set Up Tactical Abilities")]
    public static void Run()
    {
        log.Clear();
        mats.Clear();
        Directory.CreateDirectory(MaterialFolder);
        BuildPrefabs();
        AddToPlayer();
        BuildIcons();
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllLines(Report, log);
        Debug.Log("[TacticalAbilitySetup] " + string.Join(" | ", log));
    }

    // ─── Materials ────────────────────────────────────────────────────────────

    private static Material Solid(string name, Color color) => Mat(name, color, false);
    private static Material Clear(string name, Color color) => Mat(name, color, true);

    private static Material Mat(string name, Color color, bool clear)
    {
        if (mats.TryGetValue(name, out var cached)) return cached;
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find(clear ? "Sprites/Default" : "Universal Render Pipeline/Lit");
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = shader;
        if (clear) mat.color = color; else mat.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(mat);
        mats[name] = mat;
        return mat;
    }

    // ─── Prefabs ──────────────────────────────────────────────────────────────

    private static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    private static GameObject Root(string id)
    {
        var root = new GameObject(id);
        root.AddComponent<PhotonView>();
        return root;
    }

    private static void Throwable(GameObject root, float radius, float bounce)
    {
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 0.4f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var col = root.AddComponent<SphereCollider>();
        col.radius = radius;
        if (bounce > 0f)
        {
            string path = $"{MaterialFolder}/Bounce{Mathf.RoundToInt(bounce * 100)}.physicMaterial";
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (pm == null) { pm = new PhysicsMaterial("Bounce") { bounciness = bounce, dynamicFriction = 0.4f, staticFriction = 0.4f, bounceCombine = PhysicsMaterialCombine.Maximum }; AssetDatabase.CreateAsset(pm, path); }
            col.sharedMaterial = pm;
        }
    }

    private static BoxCollider Box(GameObject root, Vector3 center, Vector3 size, bool trigger)
    {
        var box = root.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
        box.isTrigger = trigger;
        return box;
    }

    private static void Shootable(GameObject root, float health) => root.AddComponent<Shootable>().health = health;

    private static void SetFloat(Component c, string field, float value)
    {
        var so = new SerializedObject(c);
        var prop = so.FindProperty(field);
        if (prop == null) { log.Add($"! {c.GetType().Name}.{field} not found"); return; }
        prop.floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Save(GameObject root)
    {
        string path = ObjectFolder + root.name + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        log.Add("prefab " + root.name);
        Object.DestroyImmediate(root);
    }

    private static void BuildPrefabs()
    {
        var dark = Solid("TacDark", new Color(0.2f, 0.21f, 0.24f));
        var metal = Solid("TacMetal", new Color(0.55f, 0.58f, 0.62f));

        // Smoke canister
        var smoke = Root("Smoke");
        Throwable(smoke, 0.15f, 0.3f);
        Part(smoke.transform, PrimitiveType.Cylinder, "Can", Vector3.zero, new Vector3(0.16f, 0.15f, 0.16f), Solid("TacSmoke", new Color(0.6f, 0.62f, 0.66f)), new Vector3(90f, 0f, 0f));
        Part(smoke.transform, PrimitiveType.Cylinder, "Band", Vector3.zero, new Vector3(0.17f, 0.03f, 0.17f), Solid("TacYellow", new Color(0.95f, 0.75f, 0.2f)), new Vector3(90f, 0f, 0f));
        smoke.AddComponent<SmokeObject>();
        Save(smoke);

        // Frag grenade
        var frag = Root("Frag");
        Throwable(frag, 0.12f, 0.45f);
        Part(frag.transform, PrimitiveType.Sphere, "Body", Vector3.zero, Vector3.one * 0.24f, Solid("TacOlive", new Color(0.3f, 0.36f, 0.2f)));
        Part(frag.transform, PrimitiveType.Cube, "Lever", new Vector3(0f, 0.11f, 0f), new Vector3(0.05f, 0.05f, 0.12f), metal);
        frag.AddComponent<FragObject>();
        Save(frag);

        // Molly bottle
        var molly = Root("Molly");
        Throwable(molly, 0.12f, 0f);
        Part(molly.transform, PrimitiveType.Cylinder, "Bottle", Vector3.zero, new Vector3(0.13f, 0.13f, 0.13f), Solid("TacGlass", new Color(0.45f, 0.25f, 0.1f)), new Vector3(90f, 0f, 0f));
        Part(molly.transform, PrimitiveType.Cylinder, "Neck", new Vector3(0f, 0f, 0.17f), new Vector3(0.05f, 0.05f, 0.05f), Solid("TacGlass", default), new Vector3(90f, 0f, 0f));
        Part(molly.transform, PrimitiveType.Sphere, "Rag", new Vector3(0f, 0f, 0.23f), Vector3.one * 0.07f, Clear("TacFlame", new Color(1f, 0.55f, 0.15f, 1f)));
        molly.AddComponent<MollyObject>();
        Save(molly);

        // Recon Bolt
        var bolt = Root("ReconBolt");
        Throwable(bolt, 0.1f, 0f);
        var blue = Clear("TacBlue", new Color(0.35f, 0.7f, 1f, 1f));
        Part(bolt.transform, PrimitiveType.Cylinder, "Shaft", Vector3.zero, new Vector3(0.07f, 0.18f, 0.07f), dark, new Vector3(90f, 0f, 0f));
        Part(bolt.transform, PrimitiveType.Sphere, "Eye", new Vector3(0f, 0f, -0.16f), Vector3.one * 0.12f, blue);
        Part(bolt.transform, PrimitiveType.Cube, "Tip", new Vector3(0f, 0f, 0.2f), new Vector3(0.05f, 0.05f, 0.08f), metal, new Vector3(0f, 0f, 45f));
        bolt.AddComponent<ReconBoltObject>();
        Shootable(bolt, 40f);
        Save(bolt);

        // Shock Dart
        var dart = Root("ShockDart");
        Throwable(dart, 0.06f, 0f);
        var cyan = Clear("TacCyan", new Color(0.4f, 0.95f, 1f, 1f));
        Part(dart.transform, PrimitiveType.Cylinder, "Shaft", Vector3.zero, new Vector3(0.04f, 0.17f, 0.04f), cyan, new Vector3(90f, 0f, 0f));
        Part(dart.transform, PrimitiveType.Cube, "Fins", new Vector3(0f, 0f, -0.14f), new Vector3(0.12f, 0.12f, 0.02f), dark, new Vector3(0f, 0f, 45f));
        dart.AddComponent<ShockDartObject>();
        Save(dart);

        // Snow Screen: three curtains of snow across the view
        var snow = Root("SnowScreen");
        Box(snow, new Vector3(0f, 0.15f, 0f), new Vector3(8f, 0.3f, 0.6f), true);
        var flakes = Clear("TacSnow", new Color(0.96f, 0.98f, 1f, 0.55f));
        Part(snow.transform, PrimitiveType.Cube, "Base", new Vector3(0f, 0.05f, 0f), new Vector3(8f, 0.1f, 0.4f), Solid("TacIceBase", new Color(0.8f, 0.9f, 1f)));
        for (int i = 0; i < 3; i++)
            Part(snow.transform, PrimitiveType.Cube, "Curtain " + (i + 1), new Vector3(0f, 2.5f, (i - 1) * 0.6f), new Vector3(8f, 5f, 0.02f), flakes);
        SetFloat(snow.AddComponent<SnowScreenObject>(), "lifetime", 14f);
        Save(snow);

        // Trip Wire anchor (on a wall, facing out)
        var trip = Root("TripWire");
        Box(trip, Vector3.zero, new Vector3(0.3f, 0.3f, 0.15f), true);
        Part(trip.transform, PrimitiveType.Cube, "Anchor", Vector3.zero, new Vector3(0.22f, 0.22f, 0.1f), dark);
        Part(trip.transform, PrimitiveType.Sphere, "Light", new Vector3(0f, 0f, 0.06f), Vector3.one * 0.07f, Clear("TacRed", new Color(1f, 0.2f, 0.25f, 1f)));
        SetFloat(trip.AddComponent<TripWireObject>(), "lifetime", 0f);
        Save(trip);

        // Turret
        var turret = Root("Turret");
        Box(turret, new Vector3(0f, 0.65f, 0f), new Vector3(0.8f, 1.3f, 0.8f), false);
        Part(turret.transform, PrimitiveType.Cylinder, "Legs", new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.3f, 0.6f), dark);
        var head = new GameObject("Head").transform;
        head.SetParent(turret.transform, false);
        head.localPosition = new Vector3(0f, 0.9f, 0f);
        Part(head, PrimitiveType.Cube, "Body", Vector3.zero, new Vector3(0.5f, 0.35f, 0.6f), Solid("TacTurret", new Color(0.85f, 0.65f, 0.2f)));
        Part(head, PrimitiveType.Cylinder, "Barrel", new Vector3(0f, 0f, 0.45f), new Vector3(0.08f, 0.2f, 0.08f), dark, new Vector3(90f, 0f, 0f));
        Part(head, PrimitiveType.Sphere, "Eye", new Vector3(0f, 0.05f, 0.3f), Vector3.one * 0.1f, Clear("TacRed", default));
        var turretScript = turret.AddComponent<TurretObject>();
        SetFloat(turretScript, "lifetime", 30f);
        var so = new SerializedObject(turretScript);
        so.FindProperty("head").objectReferenceValue = head;
        so.ApplyModifiedPropertiesWithoutUndo();
        Shootable(turret, 80f);
        Save(turret);

        // Ice Wall: solid; blocks players and bullets
        var wall = Root("IceWall");
        Box(wall, new Vector3(0f, 2f, 0f), new Vector3(7f, 4f, 0.9f), false);
        Part(wall.transform, PrimitiveType.Cube, "Ice", new Vector3(0f, 2f, 0f), new Vector3(7f, 4f, 0.9f), Clear("TacIce", new Color(0.7f, 0.88f, 1f, 0.82f)));
        SetFloat(wall.AddComponent<IceWallObject>(), "lifetime", 20f);
        Shootable(wall, 250f);
        Save(wall);

        // Slow Field disc
        var field = Root("SlowField");
        Box(field, new Vector3(0f, 0.2f, 0f), new Vector3(0.6f, 0.4f, 0.6f), true);
        Part(field.transform, PrimitiveType.Cylinder, "Field", new Vector3(0f, 0.02f, 0f), new Vector3(10f, 0.01f, 10f), Clear("TacSlow", new Color(0.7f, 0.45f, 1f, 0.28f)));
        Part(field.transform, PrimitiveType.Cylinder, "Emitter", new Vector3(0f, 0.15f, 0f), new Vector3(0.45f, 0.15f, 0.45f), Solid("TacPurple", new Color(0.55f, 0.35f, 0.9f)));
        SetFloat(field.AddComponent<SlowFieldObject>(), "lifetime", 10f);
        Save(field);

        // Suppression device (the dome is built at runtime)
        var sup = Root("Suppressor");
        Box(sup, new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.6f, 0.6f), true);
        Part(sup.transform, PrimitiveType.Cube, "Device", new Vector3(0f, 0.2f, 0f), new Vector3(0.5f, 0.4f, 0.5f), dark);
        Part(sup.transform, PrimitiveType.Cylinder, "Antenna", new Vector3(0f, 0.65f, 0f), new Vector3(0.05f, 0.25f, 0.05f), metal);
        Part(sup.transform, PrimitiveType.Sphere, "Light", new Vector3(0f, 0.92f, 0f), Vector3.one * 0.12f, Clear("TacRed", default));
        SetFloat(sup.AddComponent<SuppressorObject>(), "lifetime", 8f);
        Save(sup);

        // Updraft Vent
        var vent = Root("UpdraftVent");
        Box(vent, new Vector3(0f, 0.1f, 0f), new Vector3(1f, 0.2f, 1f), true);
        Part(vent.transform, PrimitiveType.Cylinder, "Pad", new Vector3(0f, 0.05f, 0f), new Vector3(2.8f, 0.05f, 2.8f), metal);
        for (int i = 0; i < 3; i++)
            Part(vent.transform, PrimitiveType.Cylinder, "Ring " + (i + 1), new Vector3(0f, 0.11f, 0f), new Vector3(2.4f - i * 0.7f, 0.01f, 2.4f - i * 0.7f), i % 2 == 0 ? dark : Solid("TacVent", new Color(0.75f, 0.9f, 1f)));
        SetFloat(vent.AddComponent<UpdraftVentObject>(), "lifetime", 20f);
        Save(vent);

        // Drone (spawned by the Drone quick ability)
        var drone = Root("Drone");
        var drb = drone.AddComponent<Rigidbody>();
        drb.isKinematic = true;
        drone.AddComponent<SphereCollider>().radius = 0.35f;
        Part(drone.transform, PrimitiveType.Cube, "Body", Vector3.zero, new Vector3(0.42f, 0.12f, 0.42f), dark);
        Part(drone.transform, PrimitiveType.Sphere, "Eye", new Vector3(0f, -0.02f, 0.22f), Vector3.one * 0.1f, Clear("TacGreen", new Color(0.35f, 1f, 0.6f, 1f)));
        for (int i = 0; i < 4; i++)
        {
            float x = i % 2 == 0 ? -0.26f : 0.26f, z = i < 2 ? -0.26f : 0.26f;
            Part(drone.transform, PrimitiveType.Cylinder, "Rotor " + (i + 1), new Vector3(x, 0.08f, z), new Vector3(0.24f, 0.005f, 0.24f), metal);
        }
        drone.AddComponent<DroneObject>();
        Shootable(drone, 30f);
        Save(drone);
    }

    // ─── The player prefab ────────────────────────────────────────────────────

    private static readonly string[] ThrowRefs = { "throwOrigin", "trajectoryLine", "remoteHoldPoint", "trajectorySteps", "trajectoryTimeStep" };
    private static readonly string[] TrapRefs = { "placementLayers", "hologramMaterial", "cameraHolder", "validColor", "invalidColor" };

    private static void AddToPlayer()
    {
        var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
        try
        {
            var throwTemplate = root.GetComponent<Flashbang>();
            var trapTemplate = root.GetComponent<IceTrap>();
            foreach (var (id, slot, cooldown, charges, a, b, _) in Abilities)
            {
                var type = Type.GetType(id) ?? typeof(Ability).Assembly.GetType(id);
                if (type == null) { log.Add($"! no class {id}"); continue; }
                var ability = root.GetComponent(type) ?? root.AddComponent(type);
                var so = new SerializedObject(ability);
                if (slot == AbilitySlot.Throw && throwTemplate != null) Copy(new SerializedObject(throwTemplate), so, ThrowRefs);
                if (slot == AbilitySlot.Trap && trapTemplate != null) Copy(new SerializedObject(trapTemplate), so, TrapRefs);
                so.FindProperty("cooldown").floatValue = cooldown;
                so.FindProperty("maxCharges").intValue = charges;
                if (slot == AbilitySlot.Throw)
                {
                    so.FindProperty("minForce").floatValue = a;
                    so.FindProperty("maxForce").floatValue = b;
                    so.FindProperty("chargeSeconds").floatValue = 0.8f;
                }
                if (slot == AbilitySlot.Trap)
                {
                    so.FindProperty("maxPlaced").intValue = Mathf.RoundToInt(a);
                    so.FindProperty("placingDistance").floatValue = b;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                // Enabled like the existing abilities on the prefab (AbilityHandler turns on the equipped ones)
                ((Behaviour)ability).enabled = throwTemplate == null || throwTemplate.enabled;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            log.Add("ThePlayer: " + Abilities.Length + " tactical abilities");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void Copy(SerializedObject from, SerializedObject to, string[] fields)
    {
        foreach (string field in fields)
        {
            var src = from.FindProperty(field);
            if (src != null) to.CopyFromSerializedProperty(src);
        }
    }

    // ─── Icons ────────────────────────────────────────────────────────────────

    private static void BuildIcons()
    {
        var theme = UITheme.Current;
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var camGo = new GameObject("Icon Camera");
            SceneManagement(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.orthographic = true;
            cam.orthographicSize = 1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            var canvasGo = new GameObject("Icon Canvas", typeof(RectTransform));
            SceneManagement(canvasGo, scene);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var circle = new GameObject("Circle", typeof(RectTransform)).AddComponent<Image>();
            circle.transform.SetParent(canvasGo.transform, false);
            circle.sprite = UISprites.Circle;
            circle.rectTransform.anchorMin = Vector2.zero;
            circle.rectTransform.anchorMax = Vector2.one;
            circle.rectTransform.offsetMin = new Vector2(6f, 6f);
            circle.rectTransform.offsetMax = new Vector2(-6f, -6f);
            var inner = new GameObject("Inner", typeof(RectTransform)).AddComponent<Image>();
            inner.transform.SetParent(circle.transform, false);
            inner.sprite = UISprites.Circle;
            inner.rectTransform.anchorMin = Vector2.zero;
            inner.rectTransform.anchorMax = Vector2.one;
            inner.rectTransform.offsetMin = new Vector2(18f, 18f);
            inner.rectTransform.offsetMax = new Vector2(-18f, -18f);
            var text = new GameObject("Letters", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(canvasGo.transform, false);
            text.font = UIKit.FontAsset(UIKit.Font.Display);
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 110f;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;

            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            foreach (var (id, slot, _, _, _, _, letters) in Abilities)
            {
                Color accent = slot switch { AbilitySlot.Basic => theme.cyan, AbilitySlot.Quick => theme.primary, AbilitySlot.Throw => theme.pink, _ => theme.lime };
                circle.color = accent;
                inner.color = new Color(0.13f, 0.1f, 0.3f);
                text.text = letters;
                text.color = accent;
                Canvas.ForceUpdateCanvases();
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                string path = $"{IconFolder}{id}Icon.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
            }
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            log.Add("icons " + Abilities.Length);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void SceneManagement(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
}
