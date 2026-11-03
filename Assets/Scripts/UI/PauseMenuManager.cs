using System;
using UnityEngine;

/// <summary>
/// The in-level pause menu with General, Rules and Cheats tabs. Paused == this panel is active.
/// Pausing never stops time (it's multiplayer) — it just frees the cursor and blocks gameplay
/// input. Cursor re-locking on resume is handled by PlayerCam to avoid EventSystem races.
/// Attach to: the PauseMenu panel in each level (may start inactive; Spawner calls Initialize).
/// </summary>
public class PauseMenuManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    private static PauseMenuManager _instance;
    public static PauseMenuManager Instance
    {
        get
        {
            // The panel usually starts inactive, so Awake may not have run yet
            if (_instance == null)
                _instance = FindFirstObjectByType<PauseMenuManager>(FindObjectsInactive.Include);
            return _instance;
        }
    }

    public static bool IsPaused => _instance != null && _instance.gameObject.activeSelf;

    public static event Action<bool> PausedChanged;

    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Pages")]
    [SerializeField] private GameObject generalPage;
    [SerializeField] private GameObject rulesPage;
    [SerializeField] private GameObject cheatsPage;

    [Header("Tabs")]
    [SerializeField] private TabButton generalTab;
    [SerializeField] private TabButton rulesTab;
    [SerializeField] private TabButton cheatsTab;

    private TabButton activeTab;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>Called by Spawner once the local player exists: start hidden on the General tab.</summary>
    public void Initialize()
    {
        _instance = this;
        OpenTab(generalTab);
        gameObject.SetActive(false);
    }

    // ─── Pause state ──────────────────────────────────────────────────────────

    public void Toggle()
    {
        if (gameObject.activeSelf) Resume();
        else                       Pause();
    }

    public void Pause()
    {
        if (gameObject.activeSelf) return;
        gameObject.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        PausedChanged?.Invoke(true);
    }

    public void Resume()
    {
        if (!gameObject.activeSelf) return;
        gameObject.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        PausedChanged?.Invoke(false);
    }

    // ─── Tabs ─────────────────────────────────────────────────────────────────

    public void OpenTab(TabButton tab)
    {
        if (tab == null || activeTab == tab) return;
        activeTab = tab;

        generalTab.SetActive(tab == generalTab);
        rulesTab.SetActive(tab == rulesTab);
        cheatsTab.SetActive(tab == cheatsTab);

        generalPage.SetActive(tab == generalTab);
        rulesPage.SetActive(tab == rulesTab);
        cheatsPage.SetActive(tab == cheatsTab);
    }

    // Button onClick entry points (wired in the scene)
    public void GeneralTabChange() => OpenTab(generalTab);
    public void RulesTabChange()   => OpenTab(rulesTab);
    public void CheatsTabChange()  => OpenTab(cheatsTab);
}
