using Photon.Pun;
using TMPro;
using UnityEngine;

/// <summary>
/// Emotes: a speech bubble over the penguin's head plus a little move (hop, spin dance, wiggle), seen by everyone.
/// The emote menu (<see cref="EmoteMenu"/>, the Emote key, default T) calls <see cref="Play"/> on the local player;
/// one small RPC tells every screen. Added to every player copy by Player, so the RPC lines up.
/// </summary>
[RequireComponent(typeof(Player))]
public class PlayerEmotes : MonoBehaviourPun
{
    /// <summary>The emotes: menu label, bubble text, move.</summary>
    public static readonly (string label, string bubble, Move move)[] All =
    {
        ("Wave",       "Hi!",        Move.Hop),
        ("Dance",      "Let's go!",  Move.Dance),
        ("GG",         "GG!",        Move.Hop),
        ("Laugh",      "Haha!",      Move.Wiggle),
        ("Over here",  "Over here!", Move.Hop),
        ("Taunt",      "Catch me!",  Move.Dance),
    };

    public enum Move { Hop, Dance, Wiggle }

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
    private float moveAt = -10f;

    private void Awake() => player = GetComponent<Player>();

    /// <summary>Local player: plays emote <paramref name="index"/> on every screen (a short cooldown stops spam).</summary>
    public bool Play(int index)
    {
        if (!player.IsLocal || index < 0 || index >= All.Length || Time.time - lastPlayed < Cooldown || player.IsCaught) return false;
        lastPlayed = Time.time;
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
        if (move != Move.Wiggle && TryGetComponent(out Animator animator))
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
