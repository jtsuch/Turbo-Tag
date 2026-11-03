using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Shows the joinable rooms from <see cref="RoomDirectory"/> in the Join List and rebuilds the
/// entries whenever the directory changes.
/// Attach to: the room list GameObject in the JoinList scene.
/// </summary>
public class RoomList : MonoBehaviour
{
    [FormerlySerializedAs("RoomPrefab")]
    [SerializeField] private GameObject       roomPrefab;
    [Tooltip("Parent for room entries. Falls back to the first object named 'Content'.")]
    [SerializeField] private Transform        content;
    [SerializeField] private JoinListManager  joinListManager;

    private readonly List<GameObject> entries = new();

    private void Awake()
    {
        if (content == null)
        {
            var found = GameObject.Find("Content");
            if (found != null) content = found.transform;
        }
        if (joinListManager == null) joinListManager = FindFirstObjectByType<JoinListManager>();
    }

    private void OnEnable()
    {
        RoomDirectory.Changed += Rebuild;
        Rebuild();
    }

    private void OnDisable() => RoomDirectory.Changed -= Rebuild;

    private void Rebuild()
    {
        foreach (var entry in entries) if (entry != null) Destroy(entry);
        entries.Clear();
        if (roomPrefab == null || content == null) return;

        var rooms = new List<Photon.Realtime.RoomInfo>(RoomDirectory.Rooms);
        rooms.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        foreach (var info in rooms)
        {
            var entry = Instantiate(roomPrefab, content);
            entry.GetComponent<Room>().Setup(info, OnRoomChosen);
            entries.Add(entry);
        }
    }

    private void OnRoomChosen(string roomName)
    {
        if (joinListManager != null) joinListManager.JoinRoomInList(roomName);
        else PhotonNetwork.JoinRoom(roomName);
    }
}
