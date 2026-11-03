using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hashtable    = ExitGames.Client.Photon.Hashtable;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Lobby penguin colour picker: a titled grid of colour swatches (<see cref="PenguinSkins.All"/>). Picking
/// one sets the local player's skin (synced, and remembered for next time); the current pick is ringed.
/// Built in code under this Canvas at <see cref="position"/>.
/// Attach to: the Lobby Canvas.
/// </summary>
public class SkinPicker : MonoBehaviourPunCallbacks
{
    [SerializeField] private Vector2       position   = new(714f, 40f);
    [SerializeField] private TMP_FontAsset titleFont;
    [SerializeField] private float         swatchSize = 64f;
    [SerializeField] private int           columns    = 3;

    private Outline[] rings;

    private void Start()
    {
        PenguinSkins.EnsureLocal();
        Build();
        Refresh();
    }

    // The Lobby can load before the join finishes; the skin is sent once it has
    public override void OnJoinedRoom()
    {
        PenguinSkins.EnsureLocal();
        Refresh();
    }

    public override void OnPlayerPropertiesUpdate(PhotonPlayer target, Hashtable changed)
    {
        if (target.IsLocal && changed.ContainsKey(PlayerKeys.Skin)) Refresh();
    }

    private void Build()
    {
        int   rows   = Mathf.CeilToInt(PenguinSkins.All.Length / (float)columns);
        float gap    = 14f;
        float width  = columns * swatchSize + (columns - 1) * gap;
        float height = rows * swatchSize + (rows - 1) * gap;

        var panel = NewRect("SkinPicker", transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = position;
        panel.sizeDelta = new Vector2(Mathf.Max(width, 360f), height + 70f);

        var title = NewRect("Title", panel).gameObject.AddComponent<TextMeshProUGUI>();
        if (titleFont != null) title.font = titleFont;
        title.text      = "Penguin Colour";
        title.fontSize  = 34f;
        title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot     = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(0f, 50f);
        titleRect.anchoredPosition = Vector2.zero;

        var grid = NewRect("Swatches", panel).gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize        = new Vector2(swatchSize, swatchSize);
        grid.spacing         = new Vector2(gap, gap);
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment  = TextAnchor.UpperCenter;
        var gridRect = (RectTransform)grid.transform;
        gridRect.anchorMin = new Vector2(0f, 0f);
        gridRect.anchorMax = new Vector2(1f, 1f);
        gridRect.offsetMin = Vector2.zero;
        gridRect.offsetMax = new Vector2(0f, -64f);

        rings = new Outline[PenguinSkins.All.Length];
        for (int i = 0; i < PenguinSkins.All.Length; i++)
        {
            var skin   = PenguinSkins.All[i];
            var swatch = NewRect(skin.Name, grid.transform).gameObject;
            swatch.AddComponent<Image>().color = skin.Color;
            swatch.AddComponent<Button>().onClick.AddListener(() =>
            {
                PenguinSkins.SetLocal(skin.Name);
                Refresh(); // Before the join finishes no property change comes back to do it
            });
            rings[i] = swatch.AddComponent<Outline>();
            rings[i].effectColor    = Color.white;
            rings[i].effectDistance = new Vector2(5f, -5f);
        }
    }

    private void Refresh()
    {
        if (rings == null) return;
        string current = PhotonNetwork.InRoom ? PenguinSkins.Of(PhotonNetwork.LocalPlayer) : PenguinSkins.Saved;
        for (int i = 0; i < rings.Length; i++)
            rings[i].enabled = PenguinSkins.All[i].Name == current;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
