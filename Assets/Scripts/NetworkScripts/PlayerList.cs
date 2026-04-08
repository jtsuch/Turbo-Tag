using System;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class PlayerList : MonoBehaviourPunCallbacks
{
    public GameObject PlayerButtonPrefab;
    public GameObject[] AllPlayers;
    void Start()
    {
        // Get current players
        Photon.Realtime.Player[] photonPlayers = PhotonNetwork.PlayerList;

        // Create new room list
        AllPlayers = new GameObject[photonPlayers.Length];

        // Repopulate room list with all rooms that are still open and visible
        for (int j = 0; j < photonPlayers.Length; j++)
        {
            GameObject playerButton = Instantiate(PlayerButtonPrefab, Vector3.zero, Quaternion.identity, GameObject.Find("Content").transform);
            // Set up the icons on the player card
            playerButton.GetComponent<PlayerButton>().SetUpPlayerCard(
                photonPlayers[j].NickName, 
                photonPlayers[j] == PhotonNetwork.MasterClient,
                photonPlayers[j].NickName == PhotonNetwork.LocalPlayer.NickName
                );
            AllPlayers[j] = playerButton;
        }
    }

    private void AddPlayer(Photon.Realtime.Player newPlayer)
    {
        if (newPlayer == null) return;

        GameObject playerButton = Instantiate(PlayerButtonPrefab, Vector3.zero, Quaternion.identity, GameObject.Find("Content").transform);
        // Set up the icons on the player card
        playerButton.GetComponent<PlayerButton>().SetUpPlayerCard(
            newPlayer.NickName, 
            false,
            true
            );
        Array.Resize(ref AllPlayers, AllPlayers.Length + 1); // Increase array size by 1
        AllPlayers[^1] = playerButton; // Add new player button to the end of the array
    }

    private void RemovePlayer(Photon.Realtime.Player photonPlayer)
    {
        if (photonPlayer == null) return;
        bool hostLeft = false;
        foreach (var playerButton in AllPlayers)
        {
            PlayerButton pb = playerButton.GetComponent<PlayerButton>();
            if (pb != null && pb.Name.text == photonPlayer.NickName)
            {
                if (pb.isHost) hostLeft = true;
                AllPlayers = AllPlayers.Where(p => p.GetComponent<PlayerButton>().Name.text != pb.Name.text).ToArray(); // Remove the player button from the array
                Destroy(playerButton);
                break;
            }
        }
        
        if (hostLeft && AllPlayers.Length > 0)
        {
            // If the host left, we need to update the host icon for the new host
            Photon.Realtime.Player newHost = PhotonNetwork.MasterClient;
            foreach (var pb in AllPlayers)
            {
                PlayerButton pbComp = pb.GetComponent<PlayerButton>();
                if (pbComp != null && pbComp.Name.text == newHost.NickName)
                {
                    pbComp.SetHostIcon(true);
                    break;
                }
            }
        }
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        Debug.Log("[PlayerList] A player has connected: " + newPlayer.NickName);
        AddPlayer(newPlayer);
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player oldPlayer)
    {
        Debug.Log("[PlayerList] A player has disconnected: " + oldPlayer.NickName);
        RemovePlayer(oldPlayer);
    }
}
