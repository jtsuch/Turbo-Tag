using System;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// One entry in the Join List: room name, player count and (optionally) mode and map.
/// Attach to: the Room prefab — its button calls <see cref="JoinRoom"/>.
/// </summary>
public class Room : MonoBehaviour
{
    [FormerlySerializedAs("Name")]
    [SerializeField] private TMP_Text nameText;
    [FormerlySerializedAs("PlayerCount")]
    [SerializeField] private TMP_Text playerCountText;
    [Tooltip("Optional: shows game mode and map.")]
    [SerializeField] private TMP_Text detailsText;

    private string         roomName;
    private Action<string> onJoin;

    public void Setup(RoomInfo info, Action<string> joinCallback)
    {
        roomName = info.Name;
        onJoin   = joinCallback;

        nameText.text        = info.Name;
        playerCountText.text = $"{info.PlayerCount}/{info.MaxPlayers}";

        if (detailsText != null)
        {
            string mode = NetProps.Get(info.CustomProperties, RoomKeys.GameMode, GameModeRegistry.DefaultKey);
            string map  = NetProps.Get(info.CustomProperties, RoomKeys.Map, SceneNames.DefaultMap);
            detailsText.text = $"{GameModeRegistry.DisplayName(mode)} · {map}";
        }
    }

    public void JoinRoom() => onJoin?.Invoke(roomName);
}
