using UnityEngine;
using Photon.Pun;
using System.Collections;
using System.Linq;

/// <summary>
/// Spawns the local player prefab when a scene loads and initialises the HUD and pause menu
/// once the Player singleton is ready. Spawn position is determined by the player's hunter
/// role and actor number so that players spread out in a circle rather than stacking.
/// Attach to: a scene-persistent Spawner GameObject — one per gameplay scene.
/// </summary>
public class Spawner : MonoBehaviour
{
    // Drag the scene's PlayerHUD and PauseMenu objects into these fields in the Inspector.
    // If left empty, Spawner will locate them in the scene automatically via their singletons.
    [SerializeField] private GameObject playerHUD;
    [SerializeField] private GameObject pauseMenu;

    void Start()
    {
        SpawnPlayer();
        StartCoroutine(InitializeUIWhenReady());
    }

    // Wait until the local player has registered itself, then init UI
    private IEnumerator InitializeUIWhenReady()
    {
        while (Player.Instance == null)
            yield return null;

        InitializeHUD();
        InitializePauseMenu();
    }

    private void SpawnPlayer()
    {
        var room = PhotonNetwork.CurrentRoom;
        if (room == null)
        {
            Debug.LogError("[Spawner] No current room found. Aborting spawn.");
            return;
        }

        var props = room.CustomProperties;
        int[] hunters = new int[0];

        if (props != null && props.TryGetValue("Hunters", out object rawHunters))
        {
            if (rawHunters is int[] intHunters)
                hunters = intHunters;
            else if (rawHunters is object[] objArr)
                hunters = objArr.OfType<int>().ToArray();
            else
                Debug.LogWarning($"[Spawner] Unexpected Hunters type: {rawHunters?.GetType()}");
        }
        else
        {
            Debug.LogWarning("[Spawner] No 'Hunters' property found. Defaulting to non-hunter.");
        }

        bool isHunter = hunters.Contains(PhotonNetwork.LocalPlayer.ActorNumber);

        // Hunters spawn in a tight circle (radius 1); hiders spread out wider (radius 5)
        Vector3 spawnPoint = isHunter
            ? GetCirclePosition(transform.position, 1f, PhotonNetwork.LocalPlayer.ActorNumber - 1, PhotonNetwork.CurrentRoom.PlayerCount)
            : GetCirclePosition(transform.position, 5f, PhotonNetwork.LocalPlayer.ActorNumber - 1, PhotonNetwork.CurrentRoom.PlayerCount);

        GameObject spawned = PhotonNetwork.Instantiate("Player/ThePlayer", spawnPoint, Quaternion.identity);
        if (spawned == null)
        {
            Debug.LogError("[Spawner] PhotonNetwork.Instantiate returned null.");
            return;
        }

        Debug.Log($"[Spawner] Spawned player. Name={spawned.name}");
    }

    private void InitializeHUD()
    {
        HUDManager hud = playerHUD != null
            ? playerHUD.GetComponent<HUDManager>()
            : HUDManager.Instance;

        if (hud == null)
        {
            Debug.LogWarning("[Spawner] HUDManager not found.");
            return;
        }

        hud.gameObject.SetActive(true);
        hud.Initialize();
    }

    private void InitializePauseMenu()
    {
        PauseMenuManager pm = PauseMenuManager.Instance;
        if (pm == null)
        {
            Debug.LogWarning("[Spawner] PauseMenuManager not found.");
            return;
        }
        pm.Initialize();
    }

    // Distributes players evenly around a circle. index is 0-based (ActorNumber - 1).
    Vector3 GetCirclePosition(Vector3 center, float radius, float index, float total)
    {
        float angle = (360f / total) * index * Mathf.Deg2Rad;
        float x = center.x + radius * Mathf.Cos(angle);
        float z = center.z + radius * Mathf.Sin(angle);
        return new Vector3(x, center.y, z);
    }
}