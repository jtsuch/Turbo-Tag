using UnityEngine;
using Photon.Pun;
using ExitGames.Client.Photon;
using System.Collections;
using System.Linq;

public class GameModeManager : MonoBehaviourPunCallbacks
{
    private string gameMode;
    private int roundTime;
    private int hideTime;
    private bool cheats;

    private float currentTimer = 0f;
    private int roundPhase = 1; // 1: Spawn, 2: Hiding, 3: Active, 4: Restart

    private void Start()
    {
        
        LoadSettingsFromRoom();
        StartCoroutine(StartRound());
    }

    private void LoadSettingsFromRoom()
    {
        var props = PhotonNetwork.CurrentRoom.CustomProperties;

        gameMode = props.ContainsKey("GameMode") ? (string)props["GameMode"] : "Default";
        roundTime = props.ContainsKey("RoundTime") ? (int)props["RoundTime"] : 480;
        hideTime = props.ContainsKey("HideTime") ? (int)props["HideTime"] : 45;
        cheats = props.ContainsKey("Cheats") && (bool)props["Cheats"];

        Debug.Log($"[GameManager] Mode={gameMode}, Time={roundTime}, Hide={hideTime}, Cheats={cheats}");
    }

    private IEnumerator StartRound()
    {
        yield return new WaitForSeconds(5f); // Small delay for spawn setup etc.
        roundPhase++;
        currentTimer = hideTime;

        if (PhotonNetwork.IsMasterClient)
            photonView.RPC(nameof(RPC_StartRound), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_StartRound()
    {
        Debug.Log("Round started!");
        // Trigger UI updates, spawn players, etc.
        // Allow players to move
    }

    private void Update()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        currentTimer -= Time.deltaTime;
        if (currentTimer <= 0f)
        {
            roundPhase++;
            photonView.RPC(nameof(RPC_EndPhase), RpcTarget.All);
        }
    }

    [PunRPC]
    private void RPC_EndPhase()
    {
        Debug.Log("NewPhase!");
        // Keeps cycling phases
        if (roundPhase == 1)
        {
            // Spawn Phase
            currentTimer = 5f;
        }
        else if (roundPhase == 2)
        {
            // Hiding Phase
            currentTimer = hideTime;
            // Allow hiders to move, disable seekers
        }
        else if (roundPhase == 3)
        {
            // Active Phase
            currentTimer = roundTime;
            // Enable seekers, start game logic
        }
        else if (roundPhase == 4)
        {
            // Restart Phase
            StartCoroutine(RestartRound());
        }
    }

    private IEnumerator RestartRound()
    {
        Debug.Log("Round Over! Restarting...");
        yield return new WaitForSeconds(6f);
        roundPhase = 1;
        StartCoroutine(StartRound());
    }
}
