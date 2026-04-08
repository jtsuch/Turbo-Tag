using TMPro;
using UnityEngine;

public class Room : MonoBehaviour
{
    public TMP_Text Name;
    public TMP_Text PlayerCount;

    public void JoinRoom()
    {
        GameObject.Find("JoinListManager").GetComponent<JoinListManager>().JoinRoomInList(Name.text);
    }
}
