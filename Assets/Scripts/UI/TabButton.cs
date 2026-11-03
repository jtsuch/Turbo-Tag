using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// A pause-menu tab that grows (size and font) when selected.
/// Attach to: each tab button in the PauseMenu.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class TabButton : MonoBehaviour
{
    [SerializeField] private float    normalWidth      = 160f;
    [SerializeField] private float    normalHeight     = 60f;
    [SerializeField] private float    normalTextSize   = 32f;
    [SerializeField] private float    expandedWidth    = 240f;
    [SerializeField] private float    expandedHeight   = 90f;
    [SerializeField] private float    expandedTextSize = 40f;
    [SerializeField] private float    animDuration     = 0.25f;
    [SerializeField] private Ease     ease             = Ease.OutQuart;
    [SerializeField] private TMP_Text text;

    private Tween sizeTween;
    private Tween textTween;

    public void SetActive(bool active)
    {
        var rect = (RectTransform)transform;
        Vector2 targetSize = active ? new Vector2(expandedWidth, expandedHeight) : new Vector2(normalWidth, normalHeight);
        float   targetFont = active ? expandedTextSize : normalTextSize;

        sizeTween?.Kill();
        textTween?.Kill();

        // Tweens run on unscaled time and also work while the menu is being hidden
        sizeTween = rect.DOSizeDelta(targetSize, animDuration).SetEase(ease).SetUpdate(true);
        if (text != null)
            textTween = DOTween.To(() => text.fontSize, x => text.fontSize = x, targetFont, animDuration)
                               .SetEase(ease).SetUpdate(true);
    }

    private void OnDestroy()
    {
        sizeTween?.Kill();
        textTween?.Kill();
    }
}
