using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class JoinListManager : MonoBehaviourPunCallbacks
{
    public void Start()
    {
        PhotonNetwork.JoinLobby(); // Triggers all available rooms to be listed
    }

    public void JoinRoomInList(string RoomName)
    {
        PhotonNetwork.JoinRoom(RoomName);
    }

    public override void OnJoinedRoom()
    {
        PhotonNetwork.LoadLevel("Lobby");
    }
}
