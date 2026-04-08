using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

public class TabButton : MonoBehaviour
{
    //public LayoutElement layoutElement;
    public float normalWidth = 160f;
    public float normalHeight = 60f;
    public float normalTextSize = 32f;
    public float expandedWidth = 240f;
    public float expandedHeight = 90f;
    public float expandedTextSize = 40f;
    public float animDuration = 0.25f;
    public Ease ease = Ease.OutQuart;
    public TMP_Text text;
    private Tween currentTween;

    public void SetActive(bool active)
    {
        float targetWidth = active ? expandedWidth : normalWidth;
        float targetHeight = active ? expandedHeight : normalHeight;
        float targetTextSize = active ? expandedTextSize : normalTextSize;
        
        RectTransform rt = gameObject.GetComponent<RectTransform>();
        rt.DOSizeDelta(new Vector2(targetWidth, targetHeight), animDuration).SetEase(ease);
        
        currentTween?.Kill();
        currentTween = DOTween.To(
            () => text.fontSize,
            x => text.fontSize = x,
            targetTextSize,
            animDuration
        ).SetEase(ease);
        
    }
}