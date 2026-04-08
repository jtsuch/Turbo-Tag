using UnityEngine;
using Photon.Pun;

[RequireComponent(typeof(PhotonView))]
public class PlayerInitializer : MonoBehaviour
{

    public JimmyMove pm;
    public GameObject enemyCamera;
    public GameObject enemyScripts;
    public SkinnedMeshRenderer myPenguinBody;
    public GameObject armature;

    [SerializeField] MonoBehaviour[] scriptsToDisable;
    [SerializeField] Ability[] abilityScriptsToDisable;

    void Start()
    {
        if (!GetComponent<PhotonView>().IsMine)
        {
            // ENEMY PLAYERS:
            // Destroy camera view
            Destroy(enemyCamera);

            // Disable their normal scripts
            foreach (var script in scriptsToDisable)
                script.enabled = false;

            // Disable their ability scripts
            foreach (var script in abilityScriptsToDisable)
                script.enabled = false;
        }
        else
        {
            // LOCAL PLAYER:
            myPenguinBody.enabled = false; // Hide my body in first person
            armature.SetActive(false); // Hide my gun and bones

            // Remove all scripts that aren't the player's chosen abilities (must be called in Start method)
            foreach (var script in abilityScriptsToDisable)
            {
                if (script.GetType().Name != Player.Instance.abilityList[0] && script.GetType().Name != Player.Instance.abilityList[1] && script.GetType().Name != Player.Instance.abilityList[2] && script.GetType().Name != Player.Instance.abilityList[3])
                    script.enabled = false;
            }

        }
    }
}
