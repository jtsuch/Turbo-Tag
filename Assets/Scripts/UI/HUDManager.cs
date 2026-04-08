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

    [Header("Upper Left References")]
    public TextMeshProUGUI fpsText;
    public TextMeshProUGUI otherText;
    [Header("Ability References")]
    public GameObject abilityList;
    public GameObject timer;
    public Image yellowCircle;

    private void Start()
    {
        UpdateAllAbilitiesInHotbar();
        timer.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        //fpsText.text = Mathf.RoundToInt(1f / Time.unscaledDeltaTime).ToString();
        if (Player.Instance != null)
        {
            string info = "";
            info += "Speed: " + Player.Instance.rb.linearVelocity.magnitude.ToString("F2") + "\n";
            info += "State: " + Player.Instance.currentState.ToString() + "\n";
            info += "Grounded: " + Player.Instance.IsGrounded + "\n";
            info += "Target Speed: " + Player.Instance.targetSpeed + "\n";

            otherText.text = info;
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

}
