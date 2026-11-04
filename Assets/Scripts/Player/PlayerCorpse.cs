using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Leaves a ragdoll where a player was tagged. Built locally on every client from the tag event (nothing
/// extra is networked): a copy of the player's skeleton and skinned mesh in their current pose, with a
/// Rigidbody, collider and joint on the main bones, carrying the player's momentum. Corpses collide with the
/// world but never with players (no hitbox) and have no Player component, so they can't be tagged.
/// The oldest corpses are removed once there are more than <see cref="MaxCorpses"/>.
/// </summary>
public static class PlayerCorpse
{
    private const int   MaxCorpses = 12;
    private const float TotalMass  = 10f;

    private static readonly Queue<GameObject> corpses = new();

    /// <summary>One ragdoll part: the bone, the bone its length reaches to, and the part it hangs from.</summary>
    private readonly struct Part
    {
        public readonly string Bone, LengthTo, Parent;
        public readonly float  Thickness, MassShare;

        public Part(string bone, string lengthTo, string parent, float thickness, float massShare)
        {
            Bone = bone; LengthTo = lengthTo; Parent = parent; Thickness = thickness; MassShare = massShare;
        }
    }

    // Bone names from the player model ("RigthHip" is spelled that way in the model).
    private static readonly Part[] Parts =
    {
        new("Stomach",   "Chest",         null,        0.35f, 0.20f),
        new("Chest",     "Neck",          "Stomach",   0.35f, 0.20f),
        new("Head",      "Head_end",      "Chest",     0.50f, 0.10f),
        new("LeftArm1",  "LeftArm3",      "Chest",     0.25f, 0.05f),
        new("LeftArm3",  "LeftArm4_end",  "LeftArm1",  0.20f, 0.05f),
        new("RightArm1", "RightArm3",     "Chest",     0.25f, 0.05f),
        new("RightArm3", "RightArm4_end", "RightArm1", 0.20f, 0.05f),
        new("LeftHip",   "LeftLeg",       "Stomach",   0.25f, 0.075f),
        new("LeftLeg",   "LeftFoot",      "LeftHip",   0.20f, 0.075f),
        new("RigthHip",  "RightLeg",      "Stomach",   0.25f, 0.075f),
        new("RightLeg",  "RightFoot",     "RigthHip",  0.20f, 0.075f),
    };

    /// <summary>Builds the corpse of <paramref name="player"/>. Returns the torso (for the caught camera), or null.</summary>
    public static Transform Create(Player player, Vector3 velocity)
    {
        Transform root = CreateCopy(player, $"Corpse ({player.DisplayName})", out Transform bones);
        if (root == null) return null;
        Transform torso = BuildRagdoll(bones, velocity, out List<Collider> colliders);
        IgnorePlayers(colliders);

        corpses.Enqueue(root.gameObject);
        while (corpses.Count > MaxCorpses)
        {
            var oldest = corpses.Dequeue();
            if (oldest != null) Object.Destroy(oldest);
        }

        return torso;
    }

    /// <summary>
    /// A look-alike of <paramref name="player"/> in their current pose (skeleton copy + skinned mesh with their colour),
    /// with no physics, at their position. Used by the ragdoll and the killcam's stand-ins (<paramref name="keepGuns"/>:
    /// the pistol in the hand stays). Null if the model is missing.
    /// </summary>
    public static Transform CreateCopy(Player player, string name, out Transform bones, bool keepGuns = false)
    {
        bones = null;
        Transform armature = player.transform.Find("Armature");
        var skin = player.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (armature == null || skin == null) return null;

        var root = new GameObject(name).transform;
        root.SetPositionAndRotation(player.transform.position, player.transform.rotation);

        // Skeleton copy in the current pose (the local player's own armature is inactive, so activate the copy)
        bones = Object.Instantiate(armature, root);
        bones.name = "Armature";
        bones.SetLocalPositionAndRotation(armature.localPosition, armature.localRotation);
        bones.localScale = armature.localScale;
        bones.gameObject.SetActive(true);
        if (!keepGuns)
            foreach (var renderer in bones.GetComponentsInChildren<MeshRenderer>(true))
                Object.Destroy(renderer.gameObject); // The gun stays with the player

        CopySkin(skin, armature, bones, root);
        return root;
    }

    // A new skinned mesh on the copied bones (bones are matched by their path under the armature)
    private static void CopySkin(SkinnedMeshRenderer source, Transform sourceArmature, Transform copyArmature, Transform root)
    {
        var meshObject = new GameObject(source.name);
        meshObject.transform.SetParent(root, false);
        meshObject.transform.SetLocalPositionAndRotation(source.transform.localPosition, source.transform.localRotation);
        meshObject.transform.localScale = source.transform.localScale;

        var skin = meshObject.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh      = source.sharedMesh;
        skin.sharedMaterials = source.sharedMaterials;
        skin.localBounds     = source.localBounds;
        skin.updateWhenOffscreen = true;

        var mapped = new Transform[source.bones.Length];
        for (int i = 0; i < mapped.Length; i++)
            mapped[i] = Map(source.bones[i], sourceArmature, copyArmature);
        skin.bones    = mapped;
        skin.rootBone = Map(source.rootBone, sourceArmature, copyArmature);

        // Keep the player's colour (set through a property block)
        var block = new MaterialPropertyBlock();
        source.GetPropertyBlock(block);
        skin.SetPropertyBlock(block);
        skin.enabled = true;
    }

    private static Transform Map(Transform bone, Transform sourceArmature, Transform copyArmature)
    {
        if (bone == null) return null;
        if (bone == sourceArmature) return copyArmature;

        var path = new List<string>();
        for (Transform t = bone; t != null && t != sourceArmature; t = t.parent) path.Insert(0, t.name);
        return copyArmature.Find(string.Join("/", path));
    }

    private static Transform BuildRagdoll(Transform bones, Vector3 velocity, out List<Collider> colliders)
    {
        colliders = new List<Collider>();
        var bodies = new Dictionary<string, Rigidbody>();
        var all = new Dictionary<string, Transform>();
        foreach (var t in bones.GetComponentsInChildren<Transform>(true))
            if (!all.ContainsKey(t.name)) all[t.name] = t;

        foreach (var part in Parts)
        {
            if (!all.TryGetValue(part.Bone, out Transform bone)) continue;

            // The part's shape, in the bone's local space (bone scales are kept as they are)
            Vector3 local = all.TryGetValue(part.LengthTo, out Transform end) ? bone.InverseTransformPoint(end.position) : Vector3.up * 0.1f;
            float length = local.magnitude;

            Collider shape;
            if (part.Bone == "Head")
            {
                var sphere = bone.gameObject.AddComponent<SphereCollider>();
                sphere.center = local * 0.5f;
                sphere.radius = Mathf.Max(0.001f, length * part.Thickness);
                shape = sphere;
            }
            else
            {
                var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                Vector3 abs = new(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
                capsule.direction = abs.x > abs.y && abs.x > abs.z ? 0 : abs.y > abs.z ? 1 : 2;
                capsule.center    = local * 0.5f;
                capsule.radius    = Mathf.Max(0.001f, length * part.Thickness);
                capsule.height    = length + capsule.radius;
                shape = capsule;
            }
            colliders.Add(shape);

            var body = bone.gameObject.AddComponent<Rigidbody>();
            body.mass = TotalMass * part.MassShare;
            body.linearDamping  = 0.1f;
            body.angularDamping = 0.5f;
            body.interpolation  = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.linearVelocity = velocity;
            bodies[part.Bone] = body;

            if (part.Parent != null && bodies.TryGetValue(part.Parent, out Rigidbody parent))
            {
                var joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody   = parent;
                joint.enableProjection = true; // Keeps limbs from stretching apart under big forces
                joint.lowTwistLimit   = new SoftJointLimit { limit = -30f };
                joint.highTwistLimit  = new SoftJointLimit { limit = 30f };
                joint.swing1Limit     = new SoftJointLimit { limit = 50f };
                joint.swing2Limit     = new SoftJointLimit { limit = 30f };
            }
        }

        return bodies.TryGetValue("Stomach", out Rigidbody torso) ? torso.transform : bones;
    }

    // No hitbox: the corpse lies in the world but players pass through it
    private static void IgnorePlayers(List<Collider> colliders)
    {
        foreach (var player in Player.All)
        {
            if (player == null) continue;
            foreach (var playerCollider in player.GetComponentsInChildren<Collider>(true))
                foreach (var part in colliders)
                    Physics.IgnoreCollision(part, playerCollider);
        }
    }
}
