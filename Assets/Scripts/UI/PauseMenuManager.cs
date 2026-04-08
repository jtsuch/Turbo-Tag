using TMPro;
using UnityEngine;

public class PauseMenuManager : MonoBehaviour
{
    // Prefer a safe accessor that will locate an existing instance in the scene or create one from a prefab.
    // public static PauseMenuManager Instance { get; private set; }

    private static PauseMenuManager _instance;
    public static PauseMenuManager Instance
    {
        get
        {
            if (_instance != null) return _instance;

            // Try to find an existing instance in the scene
            _instance = FindFirstObjectByType<PauseMenuManager>(FindObjectsInactive.Include);
            return _instance;
        }
    }

    // remove public paused syncing errors: base state is derived from activeSelf
    public bool Paused => gameObject.activeSelf; // unchanged

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
        // Ensure single instance in the scene. We'll use a private backing field so external code
        // can optionally locate the manager via Instance and we won't accidentally overwrite it.
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        // Start hidden
        gameObject.SetActive(false);
    }

    public void TogglePauseMenu()
    {
        bool newState = !gameObject.activeSelf;
        //Debug.Log($"PauseMenu toggled. Now paused = {newState}");

        if (newState)
        {
            // Pause game time
            Pause();
        }
        else
        {
            // Resume game time
            Resume();
        }
    }

    // Public helpers for clarity
    public void Show()
    {
        if (!gameObject.activeSelf) Pause();
    }

    public void Hide()
    {
        if (gameObject.activeSelf) Resume();
    }

    void Pause()
    {
        gameObject.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Resume()
    {
        gameObject.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Try to force the cursor lock immediately. Some platforms require the window to be focused
        // for Cursor.lockState = Locked to take effect, so we apply it immediately and again next frame.
        //ApplyLockedCursorImmediately();

        // If you also changed Time.timeScale for local pause, restore it here.
    }
    
    public void OpenTab(TabButton tab)
    {
        if (activeTab == tab) return;

        // Reset all
        generalTab.SetActive(false);
        rulesTab.SetActive(false);
        cheatsTab.SetActive(false);

        generalPage.SetActive(false);
        rulesPage.SetActive(false);
        cheatsPage.SetActive(false);

        // Activate selected
        tab.SetActive(true);
        activeTab = tab;

        if (tab == generalTab) generalPage.SetActive(true);
        else if (tab == rulesTab) rulesPage.SetActive(true);
        else if (tab == cheatsTab) cheatsPage.SetActive(true);
    }

    public void GeneralTabChange()
    {
        // Show available pages
        generalPage.SetActive(true);
        rulesPage.SetActive(false);
        cheatsPage.SetActive(false);

        // Scale the text size for selected tab
        GeneralText.fontSize = 40;
        RulesText.fontSize = 32;
        CheatsText.fontSize = 32;
    }

    public void RulesTabChange()
    {
        generalPage.SetActive(false);
        rulesPage.SetActive(true);
        cheatsPage.SetActive(false);
        GeneralText.fontSize = 32;
        RulesText.fontSize = 40;
        CheatsText.fontSize = 32;
    }

    public void CheatsTabChange()
    {
        generalPage.SetActive(false);
        rulesPage.SetActive(false);
        cheatsPage.SetActive(true);
        GeneralText.fontSize = 32;
        RulesText.fontSize = 32;
        CheatsText.fontSize = 40;
    }

    /*private void ApplyLockedCursorImmediately()
    {
        // Fast toggle + coroutine to reliably re-lock the cursor on the next frame
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        // Start coroutine to re-apply on the next frame in case the first attempt didn't take
        StartCoroutine(EnsureCursorLockedNextFrame());
    }

    private IEnumerator EnsureCursorLockedNextFrame()
    {
        yield return null; // wait one frame
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        gameObject.SetActive(false);
    }*/
}
