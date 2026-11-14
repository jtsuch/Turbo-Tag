using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The shop in gun modes (the Shop key, B; it replaced the gun picker). Columns of item cards: shields and ability
/// upgrades only with an economy (Search &amp; Destroy), then secondaries and primaries. Left click buys (or, without an
/// economy, picks for free), right click sells something bought this pre-round. Cards show the price, a short stat
/// line, and OWNED / SELL / can't-afford states. The mouse is freed while it's open (PlayerCam: GunPicker.IsOpen),
/// and you can still walk. Without an economy it opens on each spawn and closes itself after the Gun Pick Time
/// (Space/Enter close it early). Also draws the credits chip under the clock in the pre-round and the "+3000" toasts.
/// Its own canvas, built in code by Loadout.
/// </summary>
public class ShopMenu : MonoBehaviour
{
    private static ShopMenu instance;

    public static bool IsOpen { get; private set; }

    private Canvas canvas;
    private RectTransform window, columns;
    private TMP_Text title, creditsText, footer, chipText, toastText;
    private GameObject chip;
    private readonly List<Item> items = new();
    private bool builtForEconomy, built;

    private sealed class Item
    {
        public Image Face; public TMP_Text Name, Stats, Price, State;
        public Func<bool> Owned, Sellable, Affordable;
    }

    private static UITheme Theme => UITheme.Current;

    public static void Create()
    {
        if (instance != null) return;
        instance = new GameObject("ShopMenu").AddComponent<ShopMenu>();
    }

    public static void Open()
    {
        if (instance == null || IsOpen) return;
        instance.Rebuild();
        IsOpen = true;
        UISound.Play(UISound.Kind.Click);
    }

    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        GunPicker.NoteClosed();
    }

    // The shop goes with its level: a static IsOpen left true would keep the mouse free and abilities blocked on the
    // next map (PlayerCam and InputHandler read it), even in modes without a shop
    private void OnDestroy()
    {
        if (instance == this) instance = null;
        IsOpen = false;
    }

    private void Awake()
    {
        canvas = UIKit.Canvas("Shop Canvas", 3);
        canvas.transform.SetParent(transform, false);
        var root = (RectTransform)canvas.transform;

        // Credits under the clock (pre-round, economy)
        chip = UIKit.Panel(root, UIKit.WithAlpha(Theme.panel, 0.92f), 14f, "Credits").gameObject;
        UIKit.Place((RectTransform)chip.transform, new Vector2(0.5f, 1f), new Vector2(0f, -216f), new Vector2(300f, 44f));
        chipText = UIKit.Text(chip.transform, "", 24f, UIKit.Font.Bold, Theme.primary, TextAlignmentOptions.Center);
        UIKit.Stretch(chipText.rectTransform);
        toastText = UIKit.Text(root, "", 30f, UIKit.Font.Display, Theme.lime, TextAlignmentOptions.Center, "Income");
        UIKit.Place(toastText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -266f), new Vector2(600f, 44f));
    }

    // ─── Building ─────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        var loadout = Loadout.Instance;
        bool economy = loadout != null && loadout.HasEconomy;
        if (window != null) Destroy(window.gameObject);
        items.Clear();
        prices.Clear();
        upgradeItems.Clear();
        builtForEconomy = economy;
        built = true;

        var root = (RectTransform)canvas.transform;
        float width = economy ? 1640f : 840f, height = 760f;
        window = UIKit.Card(root, Theme.panel, 28f, 0.55f, "Shop");
        UIKit.Place(window, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(width, height));

        title = UIKit.Text(window, economy ? "SHOP" : "PICK YOUR GUNS", 44f, UIKit.Font.Display, Theme.text);
        UIKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -22f), new Vector2(700f, 56f));
        title.rectTransform.pivot = new Vector2(0f, 1f);
        creditsText = UIKit.Text(window, "", 40f, UIKit.Font.Display, Theme.primary, TextAlignmentOptions.Right);
        UIKit.Place(creditsText.rectTransform, new Vector2(1f, 1f), new Vector2(-36f, -22f), new Vector2(500f, 56f));
        creditsText.rectTransform.pivot = new Vector2(1f, 1f);
        footer = UIKit.Text(window, "", 20f, UIKit.Font.Bold, Theme.textMuted, TextAlignmentOptions.Center);
        UIKit.Place(footer.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(width - 60f, 30f));
        footer.rectTransform.pivot = new Vector2(0.5f, 0f);

        float x = 36f;
        if (economy)
        {
            var shields = Column("SHIELDS", ref x);
            for (int i = 0; i < Economy.Shields.Length; i++)
            {
                int index = i;
                var (name, amount, price) = Economy.Shields[i];
                Card(shields, name, $"+{amount:0} shield, doesn't regenerate", price,
                     () => loadout.BuyShield(index), () => loadout.SellShield(index),
                     owned: () => loadout.Armor >= amount, sellable: () => loadout.ShieldBoughtThisRound(index),
                     affordable: () => loadout.Credits >= price);
            }
        }
        var secondaries = Column("SECONDARY  ·  2", ref x);
        foreach (var gun in GunCatalog.InSlot(GunSlot.Secondary)) GunCard(secondaries, gun, loadout);
        var primaries = Column("PRIMARY  ·  3", ref x);
        foreach (var gun in GunCatalog.InSlot(GunSlot.Primary)) GunCard(primaries, gun, loadout);
        if (economy && Player.Local != null && Player.Local.Abilities != null)
        {
            var abilities = Column("ABILITY UPGRADES", ref x);
            foreach (var (ability, _) in Player.Local.Abilities.Usable())
            {
                var a = ability;
                Card(abilities, AbilityCatalog.DisplayName(a.Id), "", 0,
                     () => loadout.BuyUpgrade(a), () => loadout.SellUpgrade(a),
                     owned: () => loadout.UpgradeLevel(a.Id) >= Economy.MaxUpgrades,
                     sellable: () => loadout.UpgradeBoughtThisRound(a.Id),
                     affordable: () => loadout.UpgradeLevel(a.Id) < Economy.MaxUpgrades
                                       && loadout.Credits >= Economy.UpgradePrice[loadout.UpgradeLevel(a.Id)],
                     upgrade: a);
            }
        }
    }

    private RectTransform Column(string header, ref float x)
    {
        const float w = 370f;
        var label = UIKit.Text(window, header, 22f, UIKit.Font.Bold, Theme.textMuted);
        UIKit.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(x, -96f), new Vector2(w, 30f));
        label.rectTransform.pivot = new Vector2(0f, 1f);
        var col = UIKit.Rect(header, window);
        UIKit.Place(col, new Vector2(0f, 1f), new Vector2(x, -132f), new Vector2(w, 560f));
        col.pivot = new Vector2(0f, 1f);
        var stack = UIKit.VStack(col, 10f);
        stack.childControlHeight = false;
        stack.childForceExpandHeight = false;
        x += w + 26f;
        return col;
    }

    private void GunCard(RectTransform column, GunInfo gun, Loadout loadout) =>
        Card(column, gun.Name, gun.Stats, gun.Price, () => loadout.Buy(gun), () => loadout.Sell(gun),
             owned: () => loadout.Owns(gun), sellable: () => loadout.BoughtThisRound(gun),
             affordable: () => loadout.Credits >= loadout.PriceOf(gun));

    private void Card(RectTransform column, string name, string stats, int price, Action buy, Action sell,
                      Func<bool> owned, Func<bool> sellable, Func<bool> affordable, Ability upgrade = null)
    {
        var face = UIKit.Panel(column, Theme.card, 16f, name);
        ((RectTransform)face.transform).sizeDelta = new Vector2(370f, 74f);
        var click = face.gameObject.AddComponent<ShopClick>();
        click.Left = buy; click.Right = sell;

        var nameText = UIKit.Text(face.transform, name, 24f, UIKit.Font.Bold, Theme.text);
        UIKit.Place(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -8f), new Vector2(240f, 30f));
        nameText.rectTransform.pivot = new Vector2(0f, 1f);
        var statText = UIKit.Text(face.transform, stats, 15f, UIKit.Font.Body, Theme.textMuted);
        UIKit.Place(statText.rectTransform, new Vector2(0f, 0f), new Vector2(16f, 8f), new Vector2(340f, 26f));
        statText.rectTransform.pivot = new Vector2(0f, 0f);
        statText.textWrappingMode = TextWrappingModes.NoWrap;
        var priceText = UIKit.Text(face.transform, "", 22f, UIKit.Font.Bold, Theme.primary, TextAlignmentOptions.Right);
        UIKit.Place(priceText.rectTransform, new Vector2(1f, 1f), new Vector2(-14f, -8f), new Vector2(120f, 30f));
        priceText.rectTransform.pivot = new Vector2(1f, 1f);
        var stateText = UIKit.Text(face.transform, "", 15f, UIKit.Font.Bold, Theme.lime, TextAlignmentOptions.Right);
        UIKit.Place(stateText.rectTransform, new Vector2(1f, 0f), new Vector2(-14f, 8f), new Vector2(150f, 24f));
        stateText.rectTransform.pivot = new Vector2(1f, 0f);

        var item = new Item { Face = face, Name = nameText, Stats = statText, Price = priceText, State = stateText,
                              Owned = owned, Sellable = sellable, Affordable = affordable };
        items.Add(item);
        if (upgrade != null) upgradeItems[item] = upgrade;
        else prices[item] = price;
    }

    private readonly Dictionary<Item, int> prices = new();
    private readonly Dictionary<Item, Ability> upgradeItems = new();

    // ─── Every frame ──────────────────────────────────────────────────────────

    private void Update()
    {
        var loadout = Loadout.Instance;
        bool economy = loadout != null && loadout.HasEconomy;
        var manager = GameModeManager.Instance;

        if (IsOpen && (loadout == null || !loadout.CanShop)) Close();
        if (IsOpen && built && builtForEconomy != economy) Rebuild();
        if (IsOpen && !economy && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))) Close();
        bool show = IsOpen && !PauseMenuManager.IsPaused;
        if (window != null && window.gameObject.activeSelf != show) window.gameObject.SetActive(show);

        // The credits chip in the pre-round, and the latest income
        bool chipOn = economy && manager != null && Player.Local != null && (manager.Phase == MatchPhase.Countdown || IsOpen);
        if (chip.activeSelf != chipOn) chip.SetActive(chipOn);
        if (chipOn) chipText.text = $"{Economy.Format(loadout.Credits)}   ·   {Keybinds.Label(Keybinds.Get(InputAction.Shop))}: shop";
        if (loadout != null && economy)
        {
            var (amount, why, at) = loadout.LastIncome;
            float age = Time.time - at;
            toastText.text = age < 2.5f ? $"+{Economy.Format(amount)}  {why}" : "";
            toastText.alpha = Mathf.Clamp01((2.5f - age) / 0.6f);
        }
        else toastText.text = "";

        if (!show) return;
        creditsText.text = economy ? Economy.Format(loadout.Credits) : $"{Mathf.CeilToInt(Mathf.Max(0f, loadout.FreePickLeft))}s";
        footer.text = economy
            ? "Left click: buy   ·   Right click: sell back (this pre-round only)   ·   " + Keybinds.Label(Keybinds.Get(InputAction.Shop)) + ": close"
            : "Click a gun to take it   ·   Space / Enter: done";

        foreach (var item in items)
        {
            bool owned = item.Owned(), sellable = item.Sellable(), afford = item.Affordable();
            int price;
            if (upgradeItems.TryGetValue(item, out var ability))
            {
                int level = loadout.UpgradeLevel(ability.Id);
                price = level < Economy.MaxUpgrades ? Economy.UpgradePrice[level] : 0;
                item.Stats.text = $"Level {level}/{Economy.MaxUpgrades}   ·   next: {Loadout.UpgradeEffect(ability)}";
                item.Price.text = level < Economy.MaxUpgrades ? Economy.Format(price) : "MAX";
            }
            else
            {
                price = prices.TryGetValue(item, out int p) ? p : 0;
                item.Price.text = !economy || price == 0 ? (economy ? "FREE" : "") : Economy.Format(price);
            }
            item.State.text = sellable ? "RIGHT CLICK: SELL" : owned ? "OWNED" : "";
            item.State.color = sellable ? Theme.orange : Theme.lime;
            item.Face.color = owned ? UIKit.WithAlpha(Theme.secondary, 0.85f) : afford ? Theme.card : UIKit.WithAlpha(Theme.card, 0.45f);
            item.Name.alpha = owned || afford ? 1f : 0.45f;
        }
    }
}

/// <summary>Left and right clicks on a shop card.</summary>
public class ShopClick : MonoBehaviour, IPointerClickHandler
{
    public Action Left, Right;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) Left?.Invoke();
        else if (e.button == PointerEventData.InputButton.Right) Right?.Invoke();
    }
}
