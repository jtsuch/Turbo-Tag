using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Serialization;
using PhotonPlayer = Photon.Realtime.Player;

/// <summary>
/// Lobby player list: one PlayerButton per player, keyed by actor number, updated as players
/// join, leave, or the host changes.
/// Attach to: the PlayerList GameObject in the Lobby scene.
/// </summary>
public class PlayerList : MonoBehaviourPunCallbacks
{
    [FormerlySerializedAs("PlayerButtonPrefab")]
    [SerializeField] private GameObject playerButtonPrefab;
    [Tooltip("Parent for player cards. Falls back to the first object named 'Content'.")]
    [SerializeField] private Transform content;

    private readonly Dictionary<int, PlayerButton> cards = new();

    private void Start()
    {
        if (content == null)
        {
            var found = GameObject.Find("Content");
            if (found != null) content = found.transform;
        }

        foreach (var player in PhotonNetwork.PlayerList) Add(player);
    }

    private void Add(PhotonPlayer player)
    {
        if (player == null || cards.ContainsKey(player.ActorNumber) || content == null) return;

        var card = Instantiate(playerButtonPrefab, content).GetComponent<PlayerButton>();
        card.Setup(player);
        cards[player.ActorNumber] = card;
    }

    private void Remove(PhotonPlayer player)
    {
        if (!cards.TryGetValue(player.ActorNumber, out var card)) return;
        Destroy(card.gameObject);
        cards.Remove(player.ActorNumber);
    }

    private void RefreshAll()
    {
        foreach (var card in cards.Values) card.Refresh();
    }

    public override void OnPlayerEnteredRoom(PhotonPlayer newPlayer) => Add(newPlayer);
    public override void OnPlayerLeftRoom(PhotonPlayer otherPlayer)  => Remove(otherPlayer);
    public override void OnMasterClientSwitched(PhotonPlayer newMaster) => RefreshAll();
}
