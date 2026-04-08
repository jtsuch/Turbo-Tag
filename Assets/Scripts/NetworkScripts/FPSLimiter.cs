using UnityEngine;

public class FPSLimiter : MonoBehaviour
{
    public static FPSLimiter Instance { get; private set; }

    [Range(1, 240)]
    public int targetFPS = 60;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        targetFPS = PlayerPrefs.GetInt("FPS");

        DontDestroyOnLoad(gameObject);
        ApplyFPS();
    }

    public void SetFPS(int newFPS)
    {
        targetFPS = newFPS;
        ApplyFPS();
    }

    private void ApplyFPS()
    {
        Debug.Log($"FPSLimiter: Setting target FPS to {targetFPS}");
        Application.targetFrameRate = targetFPS;
        QualitySettings.vSyncCount = 0; // disable vsync so frame rate cap applies properly
    }
}
