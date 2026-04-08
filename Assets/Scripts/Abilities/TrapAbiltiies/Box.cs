using Photon.Pun;
using UnityEngine;

public class Box : TrapAbility
{
    protected PhotonView view;

    private void Start()
    {
        view = GetComponent<PhotonView>();
        isLocalPlayer = view.IsMine;
    }
}