using UnityEngine;
using Photon.Pun;
using System.Linq;

public class Spawner : MonoBehaviour
{
    void Start()
    {
        var room = PhotonNetwork.CurrentRoom;
        if (room == null)
        {
            Debug.LogError("[Spawner] No current room found when trying to spawn. Aborting spawn.");
            return;
        }

        var props = room.CustomProperties;
        int[] hunters = new int[0];

        if (props != null && props.TryGetValue("Hunters", out object rawHunters))
        {
            if (rawHunters is int[] intHunters)
            {
                hunters = intHunters;
            }
            else if (rawHunters is object[] objArr)
            {
                hunters = objArr.OfType<int>().ToArray();
            }
            else
            {
                Debug.LogWarning($"[Spawner] Unexpected Hunters type: {rawHunters?.GetType()}. Defaulting to empty list.");
            }
        }
        else
        {
            Debug.LogWarning("[Spawner] No 'Hunters' property found on room. Defaulting to non-hunter spawn.");
        }

        bool isHunter = hunters.Contains(PhotonNetwork.LocalPlayer.ActorNumber);

        Vector3 spawnPoint;
        if (isHunter)
        {
            spawnPoint = GetCirclePosition(transform.position, 1f, PhotonNetwork.LocalPlayer.ActorNumber - 1, PhotonNetwork.CurrentRoom.PlayerCount);
        }
        else
        {
            spawnPoint = GetCirclePosition(transform.position, 5f, PhotonNetwork.LocalPlayer.ActorNumber - 1, PhotonNetwork.CurrentRoom.PlayerCount);
        }

        GameObject spawned = PhotonNetwork.Instantiate("Player/ThePlayer", spawnPoint, Quaternion.identity);
        if (spawned == null) return;

        if (spawned.TryGetComponent<PhotonView>(out var pv))
        {
            Debug.Log($"[Spawner] Instantiated player prefab. Name={spawned.name} ViewID={pv.ViewID} IsMine={pv.IsMine} Owner={pv.Owner}");
        }
        else
        {
            Debug.LogError("[Spawner] PhotonNetwork.Instantiate returned null! Check the Resources path and Photon setup.");
        }

        // Load in the HUD prefab from the Resources folder
        GameObject hudPrefab = Resources.Load<GameObject>("UI/PlayerHUD");
        if (hudPrefab != null)
        {
            // Instantiate the HUD locally using Unity's Instantiate.
            GameObject playerHUD = Instantiate(hudPrefab);

            // Find the main Canvas in the scene to parent the HUD to.
            Canvas mainCanvas = FindFirstObjectByType<Canvas>();
            if (mainCanvas != null)
            {
                playerHUD.transform.SetParent(mainCanvas.transform, false);
            }
            else
            {
                Debug.LogWarning("No Canvas found in the scene to parent the HUD to!");
            }
        }
        else
        {
            // Original: Debug.LogError("Failed to load HUD prefab from Resources: UI/UIManager");
            Debug.LogError("Failed to load HUD prefab from Resources: UI/PlayerHUD");
        }

        // Load in the Pause Menu prefab from the Resources folder
        GameObject pausePrefab = Resources.Load<GameObject>("UI/PauseMenu");
        if (pausePrefab != null)
        {
            // Instantiate the menu locally using Unity's Instantiate.
            GameObject pauseMenu = Instantiate(pausePrefab);

            // Find the main Canvas in the scene to parent the PauseMenu to.
            Canvas mainCanvas = FindFirstObjectByType<Canvas>();
            if (mainCanvas != null)
            {
                pauseMenu.transform.SetParent(mainCanvas.transform, false);
            }
            else
            {
                Debug.LogWarning("No Canvas found in the scene to parent the PauseMenu to!");
            }
        }
        else
        {
            // Original: Debug.LogError("Failed to load HUD prefab from Resources: UI/PauseMenu");
            Debug.LogError("Failed to load PauseMenu prefab from Resources: UI/PauseMenu");
        }
    }

    Vector3 GetCirclePosition(Vector3 center, float radius, float index, float total)
    {
        float angle = (360f / total) * index * Mathf.Deg2Rad;
        float x = center.x + radius * Mathf.Cos(angle);
        float z = center.z + radius * Mathf.Sin(angle);
        return new Vector3(x, center.y, z);
    }
}
