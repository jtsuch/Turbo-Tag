using TMPro;
using UnityEngine;

public class PauseMenuManager : MonoBehaviour
{
    private static PauseMenuManager _instance;
    public static PauseMenuManager Instance
    {
        get
        {
            if (_instance != null) return _instance;
            _instance = FindFirstObjectByType<PauseMenuManager>(FindObjectsInactive.Include);
            return _instance;
        }
    }

    // Panel active = paused; panel inactive = not paused
    public bool Paused => gameObject.activeSelf;

    [Header("Page References")]
    public GameObject generalPage;
    public GameObject rulesPage;
    public GameObject cheatsPage;

    [Header("Tab Button References")]
    public TabButton generalTab;
    public TabButton rulesTab;
    public TabButton cheatsTab;
    private TabButton activeTab;

    [Header("Tab's Text References")]
    public TextMeshProUGUI GeneralText;
    public TextMeshProUGUI RulesText;
    public TextMeshProUGUI CheatsText;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        // Do NOT call SetActive here. If this object starts inactive, Awake fires on
        // first activation — calling SetActive(false) here would immediately undo Pause().
        // Initialize() called by Spawner handles the initial hidden state.
    }

    /// <summary>Called by Spawner once the player is ready.</summary>
    public void Initialize()
    {
        gameObject.SetActive(false);
        // Cursor lock is enforced by PlayerCam.LateUpdate every frame when not paused.
    }

    public void TogglePauseMenu()
    {
        if (Paused) Resume();
        else        Pause();
    }

    public void Show() { if (!Paused) Pause(); }
    public void Hide() { if (Paused)  Resume(); }

    void Pause()
    {
        gameObject.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    void Resume()
    {
        gameObject.SetActive(false);
        // Cursor re-lock is handled by PlayerCam.LateUpdate on the next frame,
        // which avoids the same-frame EventSystem/editor cursor-state race.
    }

    public void OpenTab(TabButton tab)
    {
        if (activeTab == tab) return;

        generalTab.SetActive(false);
        rulesTab.SetActive(false);
        cheatsTab.SetActive(false);

        generalPage.SetActive(false);
        rulesPage.SetActive(false);
        cheatsPage.SetActive(false);

        tab.SetActive(true);
        activeTab = tab;

        if (tab == generalTab)     generalPage.SetActive(true);
        else if (tab == rulesTab)  rulesPage.SetActive(true);
        else if (tab == cheatsTab) cheatsPage.SetActive(true);
    }

    public void GeneralTabChange()
    {
        generalPage.SetActive(true);
        rulesPage.SetActive(false);
        cheatsPage.SetActive(false);
        GeneralText.fontSize = 40;
        RulesText.fontSize   = 32;
        CheatsText.fontSize  = 32;
    }

    public void RulesTabChange()
    {
        generalPage.SetActive(false);
        rulesPage.SetActive(true);
        cheatsPage.SetActive(false);
        GeneralText.fontSize = 32;
        RulesText.fontSize   = 40;
        CheatsText.fontSize  = 32;
    }

    public void CheatsTabChange()
    {
        generalPage.SetActive(false);
        rulesPage.SetActive(false);
        cheatsPage.SetActive(true);
        GeneralText.fontSize = 32;
        RulesText.fontSize   = 32;
        CheatsText.fontSize  = 40;
    }
}
