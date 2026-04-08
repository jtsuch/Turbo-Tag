using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Photon.Pun;

public class Username : MonoBehaviour
{
    public TMP_InputField inputField;

    private string[] defaultNames = new string[23]
    {
        "AngryArmadillo", "BeardedBadger", "ChoppedChicken", "DefectiveDuck",
        "EmotionalEmu", "FilthyFerret", "GassyGoat", "HungryHippo",
        "IncredibleIguana", "JammedJaguar", "KillerKoala", "LemonyLlama",
        "MajorMouse", "NewNewt", "OfficialOtter", "PackingPenguine",
        "QuickQuail", "RuthlessRabbit", "SexySloth", "ThiccTurtle",
        "WildWalrus", "ZestyZebra", "ISuck"

    };

    void Start()
    {
        int ind = Random.Range(0, 23);
        int following = Random.Range(0, 100);
        inputField.text = defaultNames[ind] + following;
    }


    //public void SaveUsername()
    //{
    //    PhotonNetwork.NickName = inputField.text;
    //    PlayerPrefs.SetString("Username", inputField.text);
    //}
}
