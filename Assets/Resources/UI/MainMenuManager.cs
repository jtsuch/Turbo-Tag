using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class MainMenuManager : MonoBehaviour
{
    public GameObject loginPanel;
    public GameObject gamePanel;

    private bool isPanelOpen = false;

    public void Start()
    {
        gamePanel.SetActive(false);
    }

    public void PlayButton()
    {
        if (isPanelOpen)
        {
            gamePanel.SetActive(false);
            isPanelOpen = false;
        }
        else
        {
            gamePanel.SetActive(true);
            isPanelOpen = true;
        }
    }

    public void CreateButton()
    {
        // Load the Lobby Scene
        UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
    }

    public void JoinButton() 
    {
        // Load the list of lobbies
        UnityEngine.SceneManagement.SceneManager.LoadScene("JoinList");
    }

    public void QuitButton()
    {
        // Quit the application
        Debug.Log("Quitting Application...");
        Application.Quit();
        #if UNITY_EDITOR
        EditorApplication.isPlaying = false;
        #endif
    }
}
