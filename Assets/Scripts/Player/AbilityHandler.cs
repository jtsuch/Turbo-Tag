using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(PhotonView))]
public class AbilityHandler : MonoBehaviour
{
    private Ability[] abilities;
    //private PhotonView view;
    //public bool isLocalPlayer;

    void Awake()
    {
        abilities = GetComponents<Ability>();
        //view = GetComponent<PhotonView>();
        //isLocalPlayer = view.IsMine;
    }

    public void TryUseAbility(string abilityName, bool keyDown)
    {
        //Debug.Log($"Trying to use ability: {abilityName}");
        for (int i = 0; i < abilities.Length; i++)
        {
            if (abilities[i].abilityName == abilityName)
            {
                abilities[i].TryActivate(keyDown);
                return;
            }
        }
    }
    
    public void PrintAbilities()
    {
        string abilitiesList = "Player Abilities:\n";
        foreach (var ability in abilities)
        {
            abilitiesList += $"- {ability.abilityName}\n";
        }
        Debug.Log(abilitiesList);
    }   
}
