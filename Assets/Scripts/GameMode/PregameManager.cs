using System.Linq;
using System.Collections;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(PhotonView))]
public class PregameManager : MonoBehaviour
{
    [Header("Top UI Elements")]
    public TMP_Text teamText;
    public TMP_Text countDown;
    public GameObject endPhaseButtonObject;

    [Header("Organizing UI Elements")]
    public GameObject baseScrollView;
    public GameObject quickScrollView;
    public GameObject throwScrollView;
    public GameObject trapScrollView;

    [Header("Base UI Elements")]
    public ToggleGroup basicToggleGroup;
    public Toggle basicGrappleToggle;
    public Toggle stiffGrappleToggle;
    public Toggle springyGrappleToggle;
    public Toggle flappyToggle;
    private Toggle activeBasicToggle; // Default base toggle

    [Header("Quick UI Elements")]
    public ToggleGroup quickToggleGroup;
    public Toggle dashToggle;
    private Toggle activeQuickToggle; // Default quick toggle

    [Header("Throw UI Elements")]
    public ToggleGroup throwToggleGroup;
    public Toggle boomBombToggle;
    private Toggle activeThrowToggle; // Default throw toggle

    [Header("Trap UI Elements")]
    public ToggleGroup trapToggleGroup;
    public Toggle boxToggle;
    private Toggle activeTrapToggle; // Default trap toggle

    private float currentTimer = 10f;
    private bool isHunter = false;
    private PhotonView photonView;
    private string selectedMap = "Random";
    private bool phaseEnded = false;
    private void Start()
    {
        if(PhotonNetwork.IsMasterClient)
            endPhaseButtonObject.SetActive(true);
        photonView = GetComponent<PhotonView>();
        SetHunterStatus();
    }
    void Update()
    {
        if (phaseEnded) return;
        if (PhotonNetwork.IsMasterClient)
        {
            currentTimer -= Time.deltaTime;
            photonView.RPC(nameof(RPC_UpdateTimer), RpcTarget.Others, currentTimer);

            if (currentTimer <= 0f)
            {
                //phaseEnded = true;
                photonView.RPC(nameof(RPC_EndPhase), RpcTarget.All);
                EndGamePhase();
            }
        }            
        int timeOnClock = Mathf.CeilToInt(currentTimer);
        countDown.text = timeOnClock.ToString();
    }

    [PunRPC]
    private void RPC_UpdateTimer(float newTime)
    {
        currentTimer = newTime;
    }

    [PunRPC]
    private void RPC_EndPhase()
    {
        //if (phaseEnded) return;
        phaseEnded = true;
        //EndGamePhase();
    }

    public void OnEndPhaseButtonPressed()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("Only Master Client can end the phase early");
            return;
        }
        //phaseEnded = true;
        photonView.RPC(nameof(RPC_EndPhase), RpcTarget.All);
        EndGamePhase();
    }

    public void EndGamePhase()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log("Calling all players to upload their data");
            // Request that all clients upload their local player data to their own CustomProperties
            photonView.RPC(nameof(RPC_RequestUploadAllPlayerData), RpcTarget.All);

            // Master waits a short moment for uploads to propagate, then starts the game

            StartCoroutine(StartGameAfterUploads());
        }
    }

    private IEnumerator StartGameAfterUploads()
    {
        if(PhotonNetwork.IsMasterClient)
        {
            // Small delay to allow LocalPlayer.SetCustomProperties to be sent
            yield return new WaitForSeconds(0.5f);

            var props = PhotonNetwork.CurrentRoom.CustomProperties;
            Debug.Log("Map from props: " + (props != null && props.ContainsKey("Map")));
            if (props != null && props.ContainsKey("Map"))
                selectedMap = (string)props["Map"];
            else
            {
                Debug.Log("Failed to load selected level. That means there's a bug in your code Sir");
            }
            photonView.RPC("RPC_EnterNextPhase", RpcTarget.All, selectedMap);
        }
    }

    [PunRPC]
    public void RPC_EnterNextPhase(string chosenMap)
    {
        Debug.Log("Entering map " + chosenMap);
        PhotonNetwork.LoadLevel(chosenMap);
    }

    [PunRPC]
    private void RPC_RequestUploadAllPlayerData()
    {
        Debug.Log(PhotonNetwork.LocalPlayer.ActorNumber + " is uploading their data");
        UploadAllPlayerData();
    }

    private readonly string[] basicAbilityList = { "BasicGrapple", "StiffGrapple", "SpringyGrapple", "Flappy" };
    private readonly string[] quickAbilityList = { "Dash" };
    private readonly string[] throwAbilityList = { "BoomBomb" };
    private readonly string[] trapAbilityList = { "Box", "Ladder", "Nuke" };
    public void UploadAllPlayerData()
    {
        // Hunter Status
        UpdatePlayerProperty("isHunter", isHunter);

        // Basic Ability
        if (activeBasicToggle == null) activeBasicToggle = basicGrappleToggle;
        int index = activeBasicToggle.transform.parent.GetSiblingIndex();
        UpdatePlayerProperty("BasicAbility", basicAbilityList[index]);

        // Quick Ability
        UpdatePlayerProperty("QuickAbility", "Dash");

        // Throw Ability
        UpdatePlayerProperty("ThrowAbility", "BoomBomb");

        // Trap Ability
        if (activeTrapToggle == null) activeTrapToggle = boxToggle;
        index = activeTrapToggle.transform.parent.GetSiblingIndex();
        UpdatePlayerProperty("TrapAbility", trapAbilityList[index]);
    }

    public void OnBasicToggleChanged(Toggle changedToggle)
    {
        if (changedToggle.isOn)
        {
            activeBasicToggle = changedToggle;
        }
    }

    public void OnQuickToggleChanged(Toggle changedToggle)
    {
        if (changedToggle.isOn)
        {
            activeQuickToggle = changedToggle;
        }
    }

    public void OnThrowToggleChanged(Toggle changedToggle)
    {
        if (changedToggle.isOn)
        {
            activeThrowToggle = changedToggle;
        }
    }

    public void OnTrapToggleChanged(Toggle changedToggle)
    {
        if (changedToggle.isOn)
        {
            activeTrapToggle = changedToggle;
        }
    }

    public void UpdatePlayerProperty(string key, object value)
    {
        var props = new ExitGames.Client.Photon.Hashtable() { {key, value} };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    public void OnBaseTabPressed()
    {
        baseScrollView.SetActive(true);
        quickScrollView.SetActive(false);
        throwScrollView.SetActive(false);
        trapScrollView.SetActive(false);
    }

    public void OnQuickTabPressed()
    {
        baseScrollView.SetActive(false);
        quickScrollView.SetActive(true);
        throwScrollView.SetActive(false);
        trapScrollView.SetActive(false);
    }

    public void OnThrowTabPressed()
    {
        baseScrollView.SetActive(false);
        quickScrollView.SetActive(false);
        throwScrollView.SetActive(true);
        trapScrollView.SetActive(false);
    }

    public void OnTrapTabPressed()
    {
        baseScrollView.SetActive(false);
        quickScrollView.SetActive(false);
        throwScrollView.SetActive(false);
        trapScrollView.SetActive(true);
    }

    public void SetHunterStatus()
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
            return;
        }
        isHunter = hunters.Contains(PhotonNetwork.LocalPlayer.ActorNumber);
        if(isHunter)
            teamText.text = "Hunter";
    }
}
