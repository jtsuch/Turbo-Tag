using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HUDManager : MonoBehaviour
{
    private static HUDManager _instance;
    public static HUDManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<HUDManager>();
            return _instance;
        }
        private set => _instance = value;
    }
    [Header("Basic Ability Reference")]
    private BasicAbility trackedBasicAbility;

    [Header("Upper Left References")]
    public TextMeshProUGUI fpsText;
    public TextMeshProUGUI otherText;

    [Header("Ability References")]
    public GameObject abilityList;
    public GameObject timer;
    public Image yellowCircle;

    private void Start()
    {
        timer.SetActive(false);
        // Don't call Initialize here — Player.Instance isn't ready yet
    }

    public void Initialize()
    {
        UpdateAllAbilitiesInHotbar();

        if (Player.Instance != null)
            trackedBasicAbility = Player.Instance.GetComponent<BasicAbility>();
        else
            Debug.LogWarning("[HUDManager] Player.Instance is null during Initialize.");
    }

    // Update is called once per frame
    void Update()
    {
        //fpsText.text = Mathf.RoundToInt(1f / Time.unscaledDeltaTime).ToString();
        if (Player.Instance != null)
        {
            UpdateDebugText();
            UpdateBasicAbilityHUD();
        }
    }

    private void UpdateBasicAbilityHUD()
    {
        if (trackedBasicAbility == null) return;

        float fillAmount = trackedBasicAbility.currentDuration / trackedBasicAbility.maxDuration;
        if (fillAmount == 1)
            timer.SetActive(false);
        else
        {
            timer.SetActive(true);
            yellowCircle.fillAmount = trackedBasicAbility.currentDuration / trackedBasicAbility.maxDuration;
        }
    }

    private void UpdateAllAbilitiesInHotbar()
    {
        var props = PhotonNetwork.LocalPlayer.CustomProperties;
        if (props == null)
        {
            Debug.Log("Couldn't retreive player info in HUD Manager");
        }
        string[] abilityStringList = new string[4];
        abilityStringList[0] = props.ContainsKey("BasicAbility") ? (string)props["BasicAbility"] : "BasicGrapple";
        abilityStringList[1] = props.ContainsKey("QuickAbility") ? (string)props["QuickAbility"] : "BasicGrapple";
        abilityStringList[2] = props.ContainsKey("ThrowAbility") ? (string)props["ThrowAbility"] : "BasicGrapple";
        abilityStringList[3] = props.ContainsKey("TrapAbility") ? (string)props["TrapAbility"] : "BasicGrapple";
        string[] firstTimerKeybind = new string[] { "Mouse 1", "E", "Q", "X"};

        for (int i = 0; i < 4; i++)
        {
            Transform parent = abilityList.transform.GetChild(i);
            
            // Get children components to modify
            RawImage image = parent.GetChild(0).GetComponent<RawImage>();
            TMP_Text keybind = parent.GetChild(1).GetComponent<TMP_Text>();
            TMP_Text abilityName = parent.GetChild(2).GetComponent<TMP_Text>();

            // Modify children components
            image.texture = Resources.Load<Texture2D>("AbilityIcons/" + abilityStringList[i] + "Icon"); // calls only from resources
            keybind.text = PlayerPrefs.GetString("Keybind_Ability" + i, firstTimerKeybind[i]);
            abilityName.text = abilityStringList[i];
        }
    }

    private void UpdateDebugText() 
    {
        otherText.text = 
            "Speed: " + Player.Instance.rb.linearVelocity.magnitude.ToString("F2") + "\n" + 
            "State: " + Player.Instance.currentState.ToString() + "\n" + 
            "Grounded: " + Player.Instance.IsGrounded + "\n" + 
            "Target Speed: " + Player.Instance.targetSpeed
        ;
    }

}
