using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The abilities reference: <see cref="OpenList"/> shows every ability by category (icon, name, description,
/// stats, and a Watch button), <see cref="ShowClip"/> a pop-up playing one ability's clip
/// (<see cref="AbilityClipPlayer"/>) with its description. Used by the Lobby's Abilities button (study them while
/// waiting) and the ▶ on Pregame's ability cards. Create once per screen, on its canvas root.
/// </summary>
public class AbilityBrowser : MonoBehaviour
{
    private static UITheme Theme => UIKit.Theme;

    private UIModal listModal, clipModal;
    private AbilityClipPlayer player;
    private TMP_Text clipName, clipDescription, clipStats, noClip;
    private Image clipIcon;

    public static AbilityBrowser Create(Transform root)
    {
        var browser = root.gameObject.AddComponent<AbilityBrowser>();
        browser.Build(root);
        return browser;
    }

    private static Color SlotColor(AbilitySlot slot) => slot switch
    {
        AbilitySlot.Basic => Theme.cyan,
        AbilitySlot.Quick => Theme.primary,
        AbilitySlot.Throw => Theme.pink,
        _                 => Theme.lime,
    };

    private void Build(Transform root)
    {
        // The list
        listModal = UIKit.Modal(root, "Abilities", new Vector2(1300f, 900f));
        listModal.NoFooter();
        var list = UIKit.ScrollList(listModal.Body, 12f, UIKit.Pad(8));
        foreach (AbilitySlot slot in System.Enum.GetValues(typeof(AbilitySlot)))
        {
            var head = UIKit.Text(list, $"{slot}  <size=60%><color=#B9B2E0>{SlotHint(slot)}</color></size>", 40f, UIKit.Font.Display, SlotColor(slot));
            UIKit.Size(head, height: 56f);
            foreach (var id in AbilityCatalog.InSlot(slot))
                if (AbilityCatalog.IsAvailable(id)) AddRow(list, slot, id);
        }

        // The clip pop-up (built after the list, so it sits on top of it)
        clipModal = UIKit.Modal(root, "", new Vector2(1000f, 800f));
        clipModal.NoFooter();
        var body = clipModal.Body;
        UIKit.VStack(body, 14f, null, TextAnchor.UpperCenter);

        var top = UIKit.Rect("Top", body);
        UIKit.HStack(top, 16f, null, TextAnchor.MiddleLeft, expandHeight: false);
        UIKit.Size(top, height: 72f);
        clipIcon = UIKit.Rect("Icon", top).gameObject.AddComponent<Image>();
        clipIcon.preserveAspect = true;
        UIKit.Size(clipIcon, 72f, 72f);
        clipName = UIKit.OneLine(UIKit.Text(top, "", 44f, UIKit.Font.Display));
        UIKit.Size(clipName, flexWidth: 1f);

        var frame = UIKit.Panel(body, Theme.field, 18f, "Clip");
        UIKit.Size(frame, 896f, 504f);
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var raw = UIKit.Rect("Video", frame.transform).gameObject.AddComponent<RawImage>();
        UIKit.Stretch(raw.rectTransform);
        raw.raycastTarget = false;
        player = raw.gameObject.AddComponent<AbilityClipPlayer>();
        noClip = UIKit.Text(frame.transform, "No clip for this one yet.", 28f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Center);
        UIKit.Stretch(noClip.rectTransform);

        clipDescription = UIKit.Text(body, "", 24f, UIKit.Font.Body, Theme.text);
        clipStats = UIKit.Text(body, "", 20f, UIKit.Font.Bold, Theme.textMuted);
    }

    private static string SlotHint(AbilitySlot slot) => slot switch
    {
        AbilitySlot.Basic => "held: drains a meter",
        AbilitySlot.Quick => "tap: instant, then a cooldown",
        AbilitySlot.Throw => "aim, hold the Action key to charge, release",
        _                 => "place it, then it waits",
    };

    private void AddRow(Transform list, AbilitySlot slot, string id)
    {
        var row = UIKit.Panel(list, Theme.card, 18f, id);
        var h = UIKit.HStack(row.transform, 16f, UIKit.Pad(16, 12), TextAnchor.MiddleLeft, expandHeight: false);
        h.childForceExpandWidth = false;
        UIKit.Size(row, height: 112f);

        var iconBack = UIKit.Panel(row.transform, Theme.field, 14f, "Icon");
        UIKit.Size(iconBack, 80f, 80f);
        var sprite = AbilityIcons.Get(id);
        if (sprite != null)
        {
            var icon = UIKit.Rect("Image", iconBack.transform).gameObject.AddComponent<Image>();
            UIKit.Stretch(icon.rectTransform, 6f, 6f, 6f, 6f);
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }

        var text = UIKit.Rect("Text", row.transform);
        UIKit.VStack(text, 2f, null, TextAnchor.MiddleLeft);
        UIKit.Size(text, flexWidth: 1f);
        UIKit.OneLine(UIKit.Text(text, AbilityCatalog.DisplayName(id), 30f, UIKit.Font.Display, SlotColor(slot)));
        UIKit.Text(text, AbilityCatalog.Description(id), 19f, UIKit.Font.Body, Theme.textMuted);
        string stats = AbilityCatalog.Stats(id);
        if (!string.IsNullOrEmpty(stats)) UIKit.OneLine(UIKit.Text(text, stats, 16f, UIKit.Font.Bold, UIKit.WithAlpha(Theme.textMuted, 0.8f)));

        bool has = AbilityClipPlayer.Has(id);
        var watch = UIKit.Button(row.transform, has ? "▶ Watch" : "No clip", has ? UIKit.Style.Secondary : UIKit.Style.Ghost, () => ShowClip(id), 56f, 22f);
        UIKit.Size(watch, 170f, 56f);
        watch.interactable = has;
    }

    /// <summary>Opens the list of every ability.</summary>
    public void OpenList() => listModal.Open();

    /// <summary>Opens the pop-up playing <paramref name="id"/>'s clip.</summary>
    public void ShowClip(string id)
    {
        clipName.text = AbilityCatalog.DisplayName(id);
        clipIcon.sprite = AbilityIcons.Get(id);
        clipIcon.enabled = clipIcon.sprite != null;
        clipDescription.text = AbilityCatalog.Description(id);
        clipStats.text = AbilityCatalog.Stats(id);
        bool has = player.Play(id);
        noClip.gameObject.SetActive(!has);
        clipModal.Open();
    }
}
