using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Leaves a ragdoll where a player was tagged. Built locally on every client from the tag event (nothing
/// extra is networked): a copy of the player's skeleton and skinned mesh in their current pose (the local
/// player's hidden skeleton is posed by its Animator first, so it never starts from the model's rest pose),
/// with a Rigidbody, collider and joint on the main bones, carrying the player's momentum. Joints twist along
/// each bone with loose limits, so the body crumples rather than snapping into a pose; a shot throws it along
/// the shot (the head snaps back on a headshot). Corpses collide with the world but never with players
/// (no hitbox) and have no Player component, so they can't be tagged.
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
    /// <summary>Removes every ragdoll on this screen (a new round starts clean).</summary>
    public static void ClearAll()
    {
        while (corpses.Count > 0)
        {
            var corpse = corpses.Dequeue();
            if (corpse != null) Object.Destroy(corpse);
        }
    }

    public static Transform Create(Player player, Vector3 velocity, Vector3? shot = null, bool headshot = false)
    {
        Transform root = CreateCopy(player, $"Corpse ({player.DisplayName})", out Transform bones);
        if (root == null) return null;
        Transform torso = BuildRagdoll(bones, velocity, out List<Collider> colliders, out var bodies);
        IgnorePlayers(colliders);
        if (shot.HasValue) Throw(bodies, shot.Value, headshot);

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

        // The local player's own skeleton is switched off (hidden from them), so it isn't being animated: pose it
        // now so the copy starts from how they were standing, not the model's rest pose
        bool wasInactive = !armature.gameObject.activeSelf;
        if (wasInactive)
        {
            armature.gameObject.SetActive(true);
            if (player.TryGetComponent(out Animator animator) && animator.isActiveAndEnabled) animator.Update(0f);
        }

        // Skeleton copy in the current pose
        bones = Object.Instantiate(armature, root);
        if (wasInactive) armature.gameObject.SetActive(false);
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
        // Only the body's own materials: not the extra passes PlayerAppearance adds (hunter/team outline, X-ray),
        // so corpses and killcam stand-ins never glow red
        var materials = new List<Material>();
        foreach (var m in source.sharedMaterials)
        {
            if (m == null || materials.Count >= Mathf.Max(1, source.sharedMesh != null ? source.sharedMesh.subMeshCount : 1)) continue;
            if (m.shader != null && m.shader.name.StartsWith("TurboTag/")) continue;
            materials.Add(m);
        }
        skin.sharedMaterials = materials.ToArray();
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

    // A shot throws the chest along it with a little spin (and snaps the head back on a headshot)
    private static void Throw(Dictionary<string, Rigidbody> bodies, Vector3 shot, bool headshot)
    {
        Vector3 dir = shot.normalized;
        if (bodies.TryGetValue("Chest", out var chest))
        {
            chest.AddForce(dir * 5f + Vector3.up * 1.5f, ForceMode.VelocityChange);
            chest.AddTorque(Vector3.Cross(Vector3.up, dir) * 6f, ForceMode.VelocityChange); // Tips over backwards
        }
        if (bodies.TryGetValue("Head", out var head))
            head.AddForce(dir * (headshot ? 9f : 3f), ForceMode.VelocityChange);
        if (bodies.TryGetValue("Stomach", out var stomach))
            stomach.AddForce(dir * 2.5f, ForceMode.VelocityChange);
    }

    private static Transform BuildRagdoll(Transform bones, Vector3 velocity, out List<Collider> colliders, out Dictionary<string, Rigidbody> bodies)
    {
        colliders = new List<Collider>();
        bodies = new Dictionary<string, Rigidbody>();
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
            body.linearDamping  = 0.05f;
            body.angularDamping = 0.8f;
            body.solverIterations = 12; // Steadier joints: no jitter or limbs popping into place
            body.interpolation  = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.linearVelocity = velocity;
            bodies[part.Bone] = body;

            if (part.Parent != null && bodies.TryGetValue(part.Parent, out Rigidbody parent))
            {
                // Twist around the bone itself, swing either side of it: with the default axes the limits sat
                // on the wrong directions, so the first physics step yanked every limb into a stiff pose
                Vector3 twist = local.sqrMagnitude > 0.000001f ? local.normalized : Vector3.up;
                Vector3 swing = Vector3.Cross(twist, Mathf.Abs(Vector3.Dot(twist, Vector3.forward)) < 0.9f ? Vector3.forward : Vector3.right).normalized;

                var joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody    = parent;
                joint.axis             = twist;
                joint.swingAxis        = swing;
                joint.enableProjection = true; // Keeps limbs from stretching apart under big forces
                joint.projectionAngle  = 60f;
                joint.lowTwistLimit    = new SoftJointLimit { limit = -40f };
                joint.highTwistLimit   = new SoftJointLimit { limit = 40f };
                joint.swing1Limit      = new SoftJointLimit { limit = part.Bone == "Head" ? 45f : 70f };
                joint.swing2Limit      = new SoftJointLimit { limit = part.Bone == "Head" ? 35f : 55f };
                joint.twistLimitSpring = new SoftJointLimitSpring { spring = 0f, damper = 2f };
                joint.swingLimitSpring = new SoftJointLimitSpring { spring = 0f, damper = 2f };
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
