using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// A penguin that stands still and shows the damage it takes (the Shooting Range; Justin, Nov 14): a copy of Jimmy's
/// model (stripped like the menu preview: just the mesh, skeleton and idle Animator) inside a player-sized capsule.
/// Bullets (PlayerGun.Trace, headshots by height like a player's: the top half), shotgun pellets and the knife hit it;
/// each hit shows a damage number, and a board over its head shows the last hit, the total, hits/headshots and the
/// damage per second of the current string of hits, cleared 3 s after the last one. Local to each screen: nothing is
/// sent, so everyone can test at once. Never dies.
/// </summary>
public class TrainingDummy : MonoBehaviour
{
    public static readonly List<TrainingDummy> All = new();

    private const float Height = 2.9f, Radius = 0.7f, HeadFrom = 1.45f;

    private float total, last, firstHitAt, lastHitAt = -10f;
    private int hits, heads;
    private TextMeshPro board;

    private void Awake()
    {
        All.Add(this);
        if (!TryGetComponent(out CapsuleCollider capsule)) capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, Height * 0.5f, 0f);
        capsule.height = Height;
        capsule.radius = Radius;
        if (Application.isPlaying) BuildModel();
        BuildBoard();
    }

    private void OnDestroy() => All.Remove(this);

    // Jimmy, standing in idle (no scripts, no physics: PenguinPreview.Strip)
    private void BuildModel()
    {
        var prefab = Resources.Load<GameObject>("Player/ThePlayer");
        if (prefab == null) return;
        var holder = new GameObject("Model");
        holder.SetActive(false);   // Nothing on the copy wakes up while it's stripped
        holder.transform.SetParent(transform, false);
        var copy = Instantiate(prefab, holder.transform);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        PenguinPreview.Strip(copy);
        holder.SetActive(true);
        var animator = copy.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            foreach (var p in animator.parameters)
                if (p.name == "onGround" && p.type == AnimatorControllerParameterType.Bool) animator.SetBool(p.name, true);
        }
    }

    private void BuildBoard()
    {
        var go = new GameObject("Damage Board");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, Height + 0.9f, 0f);
        board = go.AddComponent<TextMeshPro>();
        board.fontSize = 4f;
        board.alignment = TextAlignmentOptions.Center;
        board.rectTransform.sizeDelta = new Vector2(8f, 3f);
        board.outlineWidth = 0.2f;
        board.outlineColor = new Color32(20, 16, 40, 255);
        board.text = "SHOOT ME";
    }

    /// <summary>A shot point counts as a headshot in the top half of the body, like a player's head.</summary>
    public bool IsHead(Vector3 point) => point.y - transform.position.y >= HeadFrom;

    /// <summary>Local: a hit for <paramref name="amount"/> (already × the headshot multiplier when it's one).</summary>
    public void TakeHit(float amount, bool head, Vector3 at)
    {
        if (Time.time - lastHitAt > 3f) { total = 0f; hits = 0; heads = 0; firstHitAt = Time.time; }
        lastHitAt = Time.time;
        total += amount;
        last = amount;
        hits++;
        if (head) heads++;
        DamageNumber.Show(at, amount, head);
    }

    private void Update()
    {
        if (board == null) return;
        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera : Camera.main;
        if (cam != null) board.transform.rotation = Quaternion.LookRotation(board.transform.position - cam.transform.position);
        if (Time.time - lastHitAt > 3f) { board.text = "SHOOT ME"; return; }
        float span = Mathf.Max(0.001f, lastHitAt - firstHitAt);
        string dps = hits > 1 ? $"{total / span:0} DPS" : "";
        board.text = $"<size=140%>{last:0.#}</size>\nTOTAL {total:0}   {hits} HIT{(hits == 1 ? "" : "S")} ({heads} HEAD)\n{dps}";
    }
}
