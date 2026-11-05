using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The feel of every kit button: it grows a little and brightens on hover (or keyboard/controller focus),
/// presses down onto its lip when clicked, plays a tick and a pop, and greys out when not interactable.
/// The button's "Face" child carries the colour and moves; the root's Image is the darker lip under it.
/// Animated with unscaled time, so it works while paused.
/// </summary>
[RequireComponent(typeof(Selectable))]
public class UIButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
    IPointerUpHandler, ISelectHandler, IDeselectHandler, IPointerClickHandler, ISubmitHandler
{
    public Image          Face;
    public Image          Lip;
    public Color          FaceColor;
    public float          LipDepth  = 6f;
    public float          HoverScale = 1.04f;
    public UISound.Kind   ClickSound = UISound.Kind.Click;

    private Selectable selectable;
    private bool  hovered, pressed, selected;
    private float scale = 1f, press, brighten;
    private Vector2 faceBase;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
        if (Face != null) faceBase = Face.rectTransform.anchoredPosition;
    }

    private void OnDisable()
    {
        hovered = pressed = selected = false;
        scale = 1f; press = brighten = 0f;
        transform.localScale = Vector3.one;
        Apply();
    }

    public void OnPointerEnter(PointerEventData e)
    {
        hovered = true;
        if (selectable.IsInteractable()) UISound.Play(UISound.Kind.Hover);
    }

    public void OnPointerExit(PointerEventData e) { hovered = false; pressed = false; }
    public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) pressed = true; }
    public void OnPointerUp(PointerEventData e)   { pressed = false; }
    public void OnSelect(BaseEventData e)         { selected = true; }
    public void OnDeselect(BaseEventData e)       { selected = false; }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left && selectable.IsInteractable()) UISound.Play(ClickSound);
    }

    public void OnSubmit(BaseEventData e)
    {
        if (selectable.IsInteractable()) UISound.Play(ClickSound);
    }

    private void Update()
    {
        bool live = selectable.IsInteractable();
        bool lit  = live && (hovered || selected);
        float k   = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f);

        scale    = Mathf.Lerp(scale, live && pressed ? 0.97f : lit ? HoverScale : 1f, k);
        press    = Mathf.Lerp(press, live && pressed ? 1f : 0f, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 30f));
        brighten = Mathf.Lerp(brighten, lit ? 1f : 0f, k);
        transform.localScale = new Vector3(scale, scale, 1f);
        Apply();
    }

    private void Apply()
    {
        if (Face == null) return;
        bool live = selectable == null || selectable.IsInteractable();
        Color c = Color.Lerp(FaceColor, Color.white, 0.12f * brighten);
        if (!live) c = Color.Lerp(c, new Color(0.35f, 0.33f, 0.45f, c.a), 0.65f);
        Face.color = c;
        if (Lip != null) Lip.color = Darken(c, 0.55f);
        // Only a separate face on a lip moves; when the face is the button itself, layout owns its position
        if (LipDepth > 0f && Face.transform != transform)
            Face.rectTransform.anchoredPosition = faceBase + Vector2.down * (LipDepth * press);
    }

    private static Color Darken(Color c, float factor) => new(c.r * factor, c.g * factor, c.b * factor, c.a);
}
