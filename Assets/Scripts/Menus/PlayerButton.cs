using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// One player card in the lobby: name, host crown, and a kick button visible to the host.
/// Attach to: the PlayerButton prefab.
/// </summary>
public class PlayerButton : MonoBehaviour
{
    [Header("UI References")]
    [FormerlySerializedAs("Name")]
    [SerializeField] private TMP_Text nameText;
    [FormerlySerializedAs("HostIndicator")]
    [SerializeField] private RawImage hostIndicator;
    [Tooltip("Hidden until voice chat exists.")]
    [FormerlySerializedAs("MuteIndicator")]
    [SerializeField] private Button   muteButton;
    [FormerlySerializedAs("KickIndicator")]
    [SerializeField] private Button   kickButton;

    public PhotonPlayer Player { get; private set; }

    public void Setup(PhotonPlayer player)
    {
        Player = player;
        if (muteButton != null) muteButton.gameObject.SetActive(false);
        Refresh();
    }

    /// <summary>Updates name, crown and kick visibility (call when the host changes).</summary>
    public void Refresh()
    {
        if (Player == null) return;

        nameText.text = Player.IsLocal ? $"{Player.NickName} (You)" : Player.NickName;
        hostIndicator.enabled = Player.IsMasterClient;
        if (kickButton != null)
            kickButton.gameObject.SetActive(PhotonNetwork.IsMasterClient && !Player.IsLocal);
    }

    /// <summary>Host only: removes this player from the room.</summary>
    public void KickPlayer()
    {
        if (PhotonNetwork.IsMasterClient && Player != null && !Player.IsLocal)
            PhotonNetwork.CloseConnection(Player);
    }
}
