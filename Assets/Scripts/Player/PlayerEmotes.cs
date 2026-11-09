using Photon.Pun;
using TMPro;
using UnityEngine;

/// <summary>
/// Emotes: a speech bubble over the penguin's head plus a move, seen by everyone: a real animation (Justin's
/// WaveFriendly and DanceShuffle, on the Animator's "Emote Layer" built by Editor/EmoteAnimationSetup; it fades out
/// early if the player starts moving) or a procedural one (hop, spin, wiggle).
/// The emote wheel (<see cref="EmoteMenu"/>, the Emote key, default T) calls <see cref="Play"/> on the local player;
/// one small RPC tells every screen. Added to every player copy by Player, so the RPC lines up.
/// </summary>
[RequireComponent(typeof(Player))]
public class PlayerEmotes : MonoBehaviourPun
{
    /// <summary>The emotes: menu label, bubble text, move.</summary>
    public static readonly (string label, string bubble, Move move, string anim, float seconds)[] All =
    {
        ("Wave",       "Hi!",        Move.Anim,   "WaveFriendly", 2f),
        ("Dance",      "Let's go!",  Move.Anim,   "DanceShuffle", 4f),
        ("GG",         "GG!",        Move.Hop,    null, 0f),
        ("Laugh",      "Haha!",      Move.Wiggle, null, 0f),
        ("Over here",  "Over here!", Move.Hop,    null, 0f),
        ("Taunt",      "Catch me!",  Move.Dance,  null, 0f),
    };

    public enum Move { Hop, Dance, Wiggle, Anim }

    private const float Cooldown = 1.2f;

    private Player    player;
    private Transform armature;
    private Quaternion armatureBase;
    private float     lastPlayed = -10f;

    // Bubble
    private TextMeshPro bubble, bubbleShadow;
    private float bubbleAt = -10f;

    // Move
    private Move  move;
    private int   lastIndex = -1;
    private float moveAt = -10f;

    private void Awake() => player = GetComponent<Player>();

    /// <summary>Local player: plays emote <paramref name="index"/> on every screen (a short cooldown stops spam).</summary>
    public bool Play(int index)
    {
        // The cooldown only stops spamming the same emote: picking a different one switches to it at once
        if (!player.IsLocal || index < 0 || index >= All.Length || player.IsCaught) return false;
        if (index == lastIndex && Time.time - lastPlayed < Cooldown) return false;
        lastPlayed = Time.time;
        lastIndex = index;
        photonView.RPC(nameof(RPC_Emote), RpcTarget.All, (byte)index);
        return true;
    }

    [PunRPC]
    private void RPC_Emote(byte index)
    {
        if (index >= All.Length || player.IsDownAfterTag) return;
        var emote = All[index];
        ShowBubble(emote.bubble);
        move = emote.move;
        moveAt = Time.time;
        if (move == Move.Anim) PlayAnim(emote.anim, emote.seconds);
        else StopAnim(); // A procedural emote replaces an animated one straight away
        if (move != Move.Anim && move != Move.Wiggle && TryGetComponent(out Animator animator))
            foreach (var p in animator.parameters)
                if (p.name == "Jump" && p.type == AnimatorControllerParameterType.Trigger) animator.SetTrigger("Jump");
        Sfx.At(SfxKind.Pop, transform.position + Vector3.up * 2f, 0.5f, 30f, 1.5f);
    }

    // ─── Bubble ───────────────────────────────────────────────────────────────

    private void ShowBubble(string text)
    {
        if (bubble == null)
        {
            bubbleShadow = MakeText("Emote Shadow", new Color(0.08f, 0.05f, 0.15f, 0.85f));
            bubble = MakeText("Emote Bubble", Color.white);
        }
        bubble.text = bubbleShadow.text = text;
        bubbleAt = Time.time;
    }

    private static TextMeshPro MakeText(string name, Color color)
    {
        var go = new GameObject(name);
        var t = go.AddComponent<TextMeshPro>(); // Swaps the Transform for a RectTransform
        t.font = UIKit.FontAsset(UIKit.Font.Display);
        t.fontSize = 6f;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.color = color;
        return t;
    }

    // ─── Moves and bubble placement ───────────────────────────────────────────

    private void LateUpdate()
    {
        UpdateBubble();
        UpdateMove();
        UpdateAnim();
    }

    // ─── Animated emotes (the Emote Layer) ────────────────────────────────────

    private Animator emoteAnimator;
    private int emoteLayer = -2; // -2: not looked up yet
    private float animUntil, animStarted, animWeight;

    private void PlayAnim(string state, float seconds)
    {
        if (emoteAnimator == null) emoteAnimator = GetComponentInChildren<Animator>();
        if (emoteAnimator == null) return;
        if (emoteLayer == -2) emoteLayer = emoteAnimator.GetLayerIndex("Emote Layer");
        if (emoteLayer < 0) return;
        emoteAnimator.CrossFadeInFixedTime(state, 0.1f, emoteLayer, 0f); // From another emote: straight into this one
        animStarted = Time.time;
        animUntil = Time.time + seconds;
    }

    /// <summary>Shooting cuts an emote short (the animated layer and the spin or wiggle; the bubble stays).</summary>
    public void Interrupt()
    {
        StopAnim();
        if (armature != null && Time.time - moveAt < 1.5f) armature.localRotation = armatureBase;
        moveAt = -10f;
    }

    private void StopAnim()
    {
        animUntil = Time.time;
        animWeight = 0f;
        if (emoteAnimator != null && emoteLayer >= 0) emoteAnimator.SetLayerWeight(emoteLayer, 0f);
    }

    // The layer blends in for the emote's length, and out early once the player moves off
    private void UpdateAnim()
    {
        if (emoteLayer < 0 || emoteAnimator == null) return;
        if (Time.time - animStarted > 0.3f && player.Velocity.magnitude > 3f) animUntil = Mathf.Min(animUntil, Time.time);
        animWeight = Mathf.MoveTowards(animWeight, Time.time < animUntil ? 1f : 0f, Time.deltaTime * 5f);
        emoteAnimator.SetLayerWeight(emoteLayer, animWeight);
    }

    private void UpdateBubble()
    {
        if (bubble == null) return;
        float age = Time.time - bubbleAt;
        bool on = age < 2.6f && !player.IsDownAfterTag && !player.IsLocal; // Your own head is hidden from you
        if (bubble.gameObject.activeSelf != on) { bubble.gameObject.SetActive(on); bubbleShadow.gameObject.SetActive(on); }
        if (!on) return;

        var cam = PlayerCam.Local != null ? PlayerCam.Local.Camera : Camera.main;
        Vector3 at = new(transform.position.x, player.TopY + 0.9f + Mathf.Min(age, 0.3f), transform.position.z);
        float pop = age < 0.15f ? 1f + (0.15f - age) * 3f : 1f;
        float fade = Mathf.Clamp01((2.6f - age) / 0.4f);
        Quaternion face = cam != null ? Quaternion.LookRotation(at - cam.transform.position) : Quaternion.identity;
        bubble.transform.SetPositionAndRotation(at, face);
        bubbleShadow.transform.SetPositionAndRotation(at + face * new Vector3(0.05f, -0.05f, 0.02f), face);
        bubble.transform.localScale = bubbleShadow.transform.localScale = Vector3.one * pop;
        bubble.alpha = fade;
        bubbleShadow.alpha = fade * 0.85f;
    }

    // Dance: two turns of the body; wiggle: a quick side-to-side. Turns the skeleton (the mesh follows it)
    private void UpdateMove()
    {
        float age = Time.time - moveAt;
        float duration = move == Move.Dance ? 1.3f : move == Move.Wiggle ? 0.9f : 0f;
        if (duration <= 0f || player.IsLocal) return;
        if (armature == null)
        {
            armature = transform.Find("Armature");
            if (armature == null) return;
            armatureBase = armature.localRotation;
        }
        if (age > duration + 0.05f)
        {
            if (age < duration + 0.5f) armature.localRotation = armatureBase;
            return;
        }
        float k = Mathf.Clamp01(age / duration);
        float angle = move == Move.Dance
            ? Mathf.SmoothStep(0f, 720f, k)
            : Mathf.Sin(k * Mathf.PI * 6f) * 25f * (1f - k);
        armature.localRotation = Quaternion.AngleAxis(angle, Vector3.up) * armatureBase;
    }

    private void OnDestroy()
    {
        if (bubble != null) Destroy(bubble.gameObject);
        if (bubbleShadow != null) Destroy(bubbleShadow.gameObject);
    }
}
