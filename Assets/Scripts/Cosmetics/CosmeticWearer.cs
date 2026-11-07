using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wears cosmetics on one penguin (a player copy, a menu preview, a podium dancer), one per slot. Rigid items are
/// children of the body that ride a bone every frame (Head gear the head, the rest the chest, or the definition's
/// socket), at an offset worked out once from Jimmy's rest pose (<see cref="Cosmetics.RestPlacement"/>), so they
/// stay put through Wall Magnet tilts, prone, crouching and animations (<see cref="Cosmetics.Place"/> is the fallback). Skinned outfits are instantiated under
/// the body and every SkinnedMeshRenderer is rebound to Jimmy's bones by name (rootBone: Stomach), then the outfit's
/// own armature is destroyed, so the outfit follows his animations, crouch squash and size exactly.
/// The outfit's bind poses are recomputed against Jimmy's rest skeleton, so an outfit exported with a differently
/// scaled armature (same bone names and rest positions) still fits exactly.
/// Changing a slot destroys the old instance. Everything takes the body's layer.
/// </summary>
public class CosmeticWearer
{
    private readonly Transform body;
    private readonly int layer;
    private readonly Dictionary<string, Transform> bones = new();
    private readonly Transform head, chest, stomach;
    private readonly Dictionary<CosmeticSlot, (string id, GameObject instance, CosmeticDefinition def)> worn = new();
    private readonly Dictionary<CosmeticSlot, (Transform bone, Matrix4x4 offset)> anchors = new();

    public CosmeticWearer(Transform body, int layer)
    {
        this.body = body;
        this.layer = layer;
        var armature = body.Find("Armature");
        foreach (var t in (armature != null ? armature : body).GetComponentsInChildren<Transform>(true))
            if (!bones.ContainsKey(t.name)) bones[t.name] = t;
        bones.TryGetValue("Head", out head);
        bones.TryGetValue("Chest", out chest);
        bones.TryGetValue("Stomach", out stomach);
    }

    /// <summary>Wears <paramref name="id"/> in <paramref name="slot"/> ("" or unknown takes it off).</summary>
    public void Wear(CosmeticSlot slot, string id)
    {
        id ??= Cosmetics.None;
        if (worn.TryGetValue(slot, out var current) && current.id == id) return;
        if (current.instance != null) Object.Destroy(current.instance);
        worn.Remove(slot);
        anchors.Remove(slot);

        var def = CosmeticRegistry.Find(id);
        if (def == null || def.prefab == null || def.slot != slot) return;
        var instance = Object.Instantiate(def.prefab, body);
        instance.name = $"Cosmetic {def.id}";
        foreach (var c in instance.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        foreach (var a in instance.GetComponentsInChildren<Animator>(true)) Object.Destroy(a);
        if (def.kind == CosmeticKind.Skinned) BindToSkeleton(instance);
        else if (RestAnchor(def, out var bone, out var offset)) anchors[slot] = (bone, offset);
        foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        worn[slot] = (id, instance, def);
    }

    /// <summary>Takes everything off.</summary>
    public void Clear()
    {
        foreach (var w in worn.Values) if (w.instance != null) Object.Destroy(w.instance);
        worn.Clear();
        anchors.Clear();
    }

    /// <summary>
    /// Places the rigid items for this frame and shows or hides each slot (<paramref name="visible"/> null: all shown).
    /// </summary>
    public void Update(float topY, float size, System.Func<CosmeticSlot, bool> visible = null)
    {
        foreach (var pair in worn)
        {
            var (_, instance, def) = pair.Value;
            if (instance == null) continue;
            bool show = visible == null || visible(pair.Key);
            if (instance.activeSelf != show) instance.SetActive(show);
            if (!show || def.kind != CosmeticKind.Rigid) continue;
            if (anchors.TryGetValue(pair.Key, out var anchor) && anchor.bone != null)
            {
                // Bone × rest offset; the item keeps its own (uniform) scale, so a crouch squash doesn't flatten it
                Matrix4x4 m = anchor.bone.localToWorldMatrix * anchor.offset;
                Vector3 up = m.GetColumn(1), forward = m.GetColumn(2);
                if (forward.sqrMagnitude > 1e-8f && up.sqrMagnitude > 1e-8f)
                {
                    instance.transform.SetPositionAndRotation(m.GetColumn(3), Quaternion.LookRotation(forward, up));
                    instance.transform.localScale = Vector3.one * size;
                    continue;
                }
            }
            Transform socket = !string.IsNullOrEmpty(def.socket) && bones.TryGetValue(def.socket, out var s) ? s : null;
            Cosmetics.Place(instance.transform, def.slot, body, head, chest, socket, topY, size);
        }
    }

    // A rigid item's bone and its offset from it: its standing spot (or the socket itself) seen from the bone's rest pose
    private bool RestAnchor(CosmeticDefinition def, out Transform bone, out Matrix4x4 offset)
    {
        offset = Matrix4x4.identity;
        bool socketed = !string.IsNullOrEmpty(def.socket);
        string name = socketed ? def.socket : Cosmetics.AnchorBone(def.slot);
        var rest = JimmyRest();
        if (!bones.TryGetValue(name, out bone) || bone == null) return false;
        if (socketed) return true; // On the bone itself, as before
        if (rest == null || !rest.TryGetValue(name, out var boneRest)) return false;
        offset = boneRest.inverse * Cosmetics.RestPlacement(def.slot, boneRest.GetColumn(3));
        return true;
    }

    /// <summary>True if something is worn in the slot.</summary>
    public bool Wearing(CosmeticSlot slot) => worn.ContainsKey(slot);

    // Outfit mesh → copy with bind poses that put it on Jimmy's rest skeleton. The outfit's vertices sit where its
    // renderer puts them (relative to the outfit's root, which lines up with the player's root, both exported from
    // Blender's origin), so for each bone: new = (Jimmy's rest bone)^-1 × (outfit renderer, relative to the body).
    // Independent of the outfit armature's saved pose (the Dino's isn't its bind pose: using it tilted the onesie
    // ~20 degrees) and of where the body is. Cached per mesh: every copy of an outfit shares one.
    private static readonly Dictionary<Mesh, Mesh> refitted = new();

    // Jimmy's rest skeleton relative to the player root, per bone name, from the player prefab's own mesh (its bind
    // poses: bindpose = bone.worldToLocal × renderer.localToWorld). From the prefab, never a live player, so a crouch
    // squash or a size change at the moment of dressing can't creep in.
    private static Dictionary<string, Matrix4x4> jimmyRest;

    private static Dictionary<string, Matrix4x4> JimmyRest()
    {
        if (jimmyRest != null) return jimmyRest;
        var prefab = Resources.Load<GameObject>("Player/ThePlayer");
        var smr = prefab != null ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        if (smr == null || smr.sharedMesh == null) return null;
        var root = prefab.transform.worldToLocalMatrix;
        var bind = smr.sharedMesh.bindposes;
        jimmyRest = new Dictionary<string, Matrix4x4>();
        for (int i = 0; i < smr.bones.Length && i < bind.Length; i++)
            if (smr.bones[i] != null) jimmyRest[smr.bones[i].name] = root * smr.transform.localToWorldMatrix * bind[i].inverse;
        return jimmyRest;
    }

    private Mesh Refit(SkinnedMeshRenderer smr, Transform[] outfitBones)
    {
        var source = smr.sharedMesh;
        if (source == null) return null;
        if (refitted.TryGetValue(source, out var cached) && cached != null) return cached;

        var jimmyRest = JimmyRest();
        if (jimmyRest == null) return null;

        var oldBind = source.bindposes;
        var newBind = new Matrix4x4[oldBind.Length];
        Matrix4x4 renderer = body.worldToLocalMatrix * smr.transform.localToWorldMatrix;
        for (int i = 0; i < oldBind.Length; i++)
        {
            var own = i < outfitBones.Length ? outfitBones[i] : null;
            newBind[i] = own != null && jimmyRest.TryGetValue(own.name, out var rest) ? rest.inverse * renderer : oldBind[i];
        }
        var mesh = Object.Instantiate(source);
        mesh.name = source.name + " (fitted)";
        mesh.bindposes = newBind;
        refitted[source] = mesh;
        return mesh;
    }

    // Rebinds every skinned mesh in the outfit to Jimmy's bones (by name), then removes the outfit's own armature
    private void BindToSkeleton(GameObject outfit)
    {
        var renderers = outfit.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var keep = new HashSet<Transform>();
        foreach (var smr in renderers)
        {
            var old = smr.bones;
            var mesh = Refit(smr, old);
            if (mesh != null) smr.sharedMesh = mesh;
            var rebound = new Transform[old.Length];
            for (int i = 0; i < old.Length; i++)
            {
                if (old[i] != null && bones.TryGetValue(old[i].name, out var match)) rebound[i] = match;
                else
                {
                    rebound[i] = stomach != null ? stomach : body;
                    Debug.LogWarning($"[Cosmetics] {outfit.name}: Jimmy has no bone named '{(old[i] != null ? old[i].name : "null")}'");
                }
            }
            smr.bones = rebound;
            smr.rootBone = stomach != null ? stomach : rebound.Length > 0 ? rebound[0] : body;
            smr.updateWhenOffscreen = true; // Its bounds come from Jimmy's bones now
            for (var t = smr.transform; t != null && t != outfit.transform; t = t.parent) keep.Add(t);
        }
        // The outfit's own armature (anything that isn't a renderer or on the way to one)
        for (int i = outfit.transform.childCount - 1; i >= 0; i--)
        {
            var child = outfit.transform.GetChild(i);
            if (!keep.Contains(child)) Object.Destroy(child.gameObject);
        }
    }
}
