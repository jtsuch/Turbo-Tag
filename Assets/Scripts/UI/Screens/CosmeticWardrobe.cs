using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Wardrobe: a pop-up with Jimmy on the left (wearing your colour and picks, turning round to show a back item)
/// and, on the right, a grid of item cards per slot: each card the item's picture (Resources/Cosmetics/Icons, made
/// by the BuildCosmetics job) with its name below, "None" first, the worn one outlined. Picks apply at once and are
/// remembered (<see cref="Cosmetics.SetLocal"/>). Opened from the Wardrobe button on the main menu card and in the
/// Lobby's "Your penguin" card. Create once per screen, on its canvas root (after the rest, so it draws on top).
/// </summary>
public class CosmeticWardrobe : MonoBehaviour
{
    private static UITheme Theme => UIKit.Theme;

    private UIModal modal;
    private PenguinPreview preview;
    private readonly List<(Cosmetics.Slot slot, string id, Outline outline, Image face)> cards = new();

    /// <summary>Raised after a pick (the main menu card refreshes its own preview).</summary>
    public event System.Action Changed;

    public static CosmeticWardrobe Create(Transform root)
    {
        var wardrobe = root.gameObject.AddComponent<CosmeticWardrobe>();
        wardrobe.Build(root);
        return wardrobe;
    }

    /// <summary>A "Wardrobe" button that opens it.</summary>
    public Button MakeButton(Transform parent, float height = 60f, float width = 260f)
    {
        var button = UIKit.Button(parent, "Wardrobe", UIKit.Style.Secondary, Open, height, 26f);
        UIKit.Size(button, width, height);
        return button;
    }

    private void Build(Transform root)
    {
        modal = UIKit.Modal(root, "Wardrobe", new Vector2(1360f, 860f));
        modal.NoFooter();
        var body = modal.Body;

        // Jimmy (left)
        var left = UIKit.Panel(body, Theme.field, 24f, "Jimmy");
        var lr = left.rectTransform;
        lr.anchorMin = new Vector2(0f, 0f);
        lr.anchorMax = new Vector2(0f, 1f);
        lr.pivot = new Vector2(0f, 0.5f);
        lr.sizeDelta = new Vector2(440f, 0f);
        lr.anchoredPosition = Vector2.zero;
        preview = PenguinPreview.Create(left.transform, 420f);
        var pr = (RectTransform)preview.transform;
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(420f, 420f); // No layout group here: size it directly
        pr.anchoredPosition = Vector2.zero;

        // Items (right)
        var right = UIKit.Rect("Items", body);
        UIKit.Stretch(right, 470f, 0f, 0f, 0f);
        var list = UIKit.ScrollList(right, 10f, UIKit.Pad(4));
        foreach (Cosmetics.Slot slot in System.Enum.GetValues(typeof(Cosmetics.Slot)))
        {
            var head = UIKit.Text(list, slot == Cosmetics.Slot.Hat ? "Hats" : "Back", 34f, UIKit.Font.Display, Theme.text);
            UIKit.Size(head, height: 48f);
            var grid = UIKit.Rect(slot + " Grid", list);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(170f, 196f);
            layout.spacing = new Vector2(16f, 16f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;
            var items = Cosmetics.InSlot(slot);
            int count = items.Count + 1;
            UIKit.Size(grid, height: Mathf.CeilToInt(count / 4f) * 212f);
            AddCard(grid, slot, Cosmetics.None, "None", null);
            foreach (var item in items)
                AddCard(grid, slot, item.Id, item.Name, Resources.Load<Sprite>("Cosmetics/Icons/" + item.Id));
        }
    }

    private void AddCard(Transform grid, Cosmetics.Slot slot, string id, string name, Sprite icon)
    {
        var face = UIKit.Panel(grid, Theme.card, 18f, name);
        var outline = face.gameObject.AddComponent<Outline>();
        outline.effectColor = Theme.primary;
        outline.effectDistance = new Vector2(4f, -4f);

        var pic = UIKit.Panel(face.transform, Theme.field, 14f, "Picture");
        UIKit.Stretch(pic.rectTransform, 10f, 10f, 10f, 46f);
        if (icon != null)
        {
            var image = UIKit.Rect("Icon", pic.transform).gameObject.AddComponent<Image>();
            UIKit.Stretch(image.rectTransform, 6f, 6f, 6f, 6f);
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
        else
        {
            var none = UIKit.Text(pic.transform, id == Cosmetics.None ? "X" : "?", 64f, UIKit.Font.Display, UIKit.WithAlpha(Theme.textMuted, 0.6f), TextAlignmentOptions.Center);
            UIKit.Stretch(none.rectTransform);
        }
        var label = UIKit.OneLine(UIKit.Text(face.transform, name, 18f, UIKit.Font.Bold, Theme.text, TextAlignmentOptions.Center));
        label.rectTransform.anchorMin = new Vector2(0f, 0f);
        label.rectTransform.anchorMax = new Vector2(1f, 0f);
        label.rectTransform.pivot = new Vector2(0.5f, 0f);
        label.rectTransform.sizeDelta = new Vector2(-12f, 40f);
        label.rectTransform.anchoredPosition = new Vector2(0f, 4f);
        label.enableAutoSizing = true;
        label.fontSizeMin = 13f;
        label.fontSizeMax = 18f;

        var button = face.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        var fx = face.gameObject.AddComponent<UIButtonFx>();
        fx.Face = face;
        fx.FaceColor = Theme.card;
        fx.LipDepth = 0f;
        fx.HoverScale = 1.04f;
        button.onClick.AddListener(() => Pick(slot, id));
        cards.Add((slot, id, outline, face));
    }

    public void Open()
    {
        string skin = PhotonNetwork.InRoom ? PenguinSkins.Of(PhotonNetwork.LocalPlayer) : PenguinSkins.Saved;
        preview.SetSkin(skin);
        preview.SetCosmetics(null);
        Refresh();
        modal.Open();
    }

    private void Pick(Cosmetics.Slot slot, string id)
    {
        Cosmetics.SetLocal(slot, id);
        preview.SetCosmetics(null);
        if (slot == Cosmetics.Slot.Back && id != Cosmetics.None) preview.ShowBack();
        Refresh();
        Changed?.Invoke();
    }

    private void Refresh()
    {
        foreach (var (slot, id, outline, face) in cards)
        {
            bool on = Cosmetics.Saved(slot) == id;
            outline.enabled = on;
            face.GetComponent<UIButtonFx>().FaceColor = on ? Theme.cardHover : Theme.card;
        }
    }
}
