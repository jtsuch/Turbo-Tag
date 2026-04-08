using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerButton : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text Name;
    public RawImage HostIndicator;
    public Button MuteIndicator;
    public Button KickIndicator;
    public bool isHost = false;
    public bool isMuted = false;
    public bool isLocalPlayer = false;

    public void SetUpPlayerCard(string playerName, bool hostStatus, bool localPlayer)
    {
        Name.text = playerName;
        isHost = hostStatus;
        isLocalPlayer = localPlayer;

        if (isHost)
        {
            HostIndicator.enabled = true;
        }
        else
        {
            HostIndicator.enabled = false;
        }

        if (isLocalPlayer)
        {
            KickIndicator.gameObject.SetActive(false);
            MuteIndicator.gameObject.SetActive(false);
        }
        else 
        {
            KickIndicator.gameObject.SetActive(true);
            MuteIndicator.gameObject.SetActive(true);
        }
    }

    public void SetHostIcon(bool hostStatus)
    {
        isHost = hostStatus;
        HostIndicator.enabled = hostStatus;
    }

    public void ToggleMute()
    {
        isMuted = !isMuted;
        Debug.Log("Player " + Name.text + " muted: " + isMuted);
        // Update MuteIndicator UI here based on isMuted state
    }

    public void KickPlayer()
    {
        // Implement kick logic here
        Debug.Log("Player " + Name.text + " has been kicked.");
    }
}
