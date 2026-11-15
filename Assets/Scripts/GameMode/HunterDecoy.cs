using TMPro;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// The penguin standing where a hunter will start, covering its eyes while the hiders hide (<see cref="HunterRoom"/>):
/// a stripped copy of ThePlayer (like the menu preview) in the hunter's colour and cosmetics, with the hunter's red
/// outline and name, inside a player-sized capsule so hiders bump into it. Local to each screen; never moves.
/// It plays Justin's CoverEyes clip (Nov 14), a looping state on the Action Layer (NewMoveAnimationSetup).
/// </summary>
public class HunterDecoy : MonoBehaviour
{
    public const string CoverEyesState = "CoverEyes";
    private const string CoverEyesLayer = "Action Layer";
    private const float Height = 2.9f, Radius = 0.7f;
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    private Animator animator;
    private int layer = -1;
    private CosmeticWearer wearer;
    private Transform model;
    private TextMeshPro label;

    public static HunterDecoy Create(PhotonPlayer owner)
    {
        var go = new GameObject($"Hunter Decoy ({owner.NickName})");
        var decoy = go.AddComponent<HunterDecoy>();
        decoy.Build(owner);
        return decoy;
    }

    private void Build(PhotonPlayer owner)
    {
        var capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, Height * 0.5f, 0f);
        capsule.height = Height;
        capsule.radius = Radius;

        var prefab = Resources.Load<GameObject>("Player/ThePlayer");
        if (prefab != null)
        {
            var holder = new GameObject("Model");
            holder.SetActive(false);   // Nothing on the copy wakes up while it's stripped
            holder.transform.SetParent(transform, false);
            var copy = Instantiate(prefab, holder.transform);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            PenguinPreview.Strip(copy);
            holder.SetActive(true);
            model = copy.transform;

            // Their colour, the hunters' red outline, their cosmetics
            var map = PenguinSkins.Texture(PenguinSkins.Of(owner));
            var outline = Resources.Load<Material>("Cosmetics/HunterOutline");
            var block = new MaterialPropertyBlock();
            foreach (var r in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (map != null) { r.GetPropertyBlock(block); block.SetTexture(BaseMapId, map); r.SetPropertyBlock(block); }
                if (outline != null)
                {
                    var mats = r.sharedMaterials;
                    var withOutline = new Material[mats.Length + 1];
                    mats.CopyTo(withOutline, 0);
                    withOutline[mats.Length] = outline;
                    r.sharedMaterials = withOutline;
                }
            }
            wearer = new CosmeticWearer(copy.transform, 0);
            foreach (var slot in Cosmetics.Slots) wearer.Wear(slot, Cosmetics.Of(owner, slot));

            animator = copy.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                foreach (var p in animator.parameters)
                    if (p.name == "onGround" && p.type == AnimatorControllerParameterType.Bool) animator.SetBool(p.name, true);
                layer = animator.GetLayerIndex(CoverEyesLayer);
                // Nothing drives the copy's layers, so they keep their defaults: the gun arm (RightArm) and HeadLayer
                // are on by default and held the right arm down (only the left eye was covered). Only Base + the clip
                for (int i = 1; i < animator.layerCount; i++) animator.SetLayerWeight(i, 0f);
                if (layer >= 0)
                {
                    animator.SetLayerWeight(layer, 1f);
                    animator.Play(CoverEyesState, layer, 0f);
                }
            }
        }

        var labelGo = new GameObject("Name");
        labelGo.transform.SetParent(transform, false);
        labelGo.transform.localPosition = new Vector3(0f, Height + 0.5f, 0f);
        label = labelGo.AddComponent<TextMeshPro>();
        label.rectTransform.sizeDelta = new Vector2(6f, 1f);
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 3f;
        label.color = new Color(1f, 0.45f, 0.4f);
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color32(20, 16, 40, 255);
        label.text = $"{owner.NickName}\n<size=70%>COUNTING...</size>";
    }

    private void Update()
    {
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera : Camera.main;
        if (label != null && cam != null)
            label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);
    }

    private void LateUpdate()
    {
        if (wearer != null && model != null) wearer.Update(model.position.y + Height, 1f);
    }
}
