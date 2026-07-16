using Unity.Netcode;
using UnityEngine;

public class PostCombatPointScript : NetworkBehaviour
{
    public GameObject firstPlacePoint;
    public GameObject secondPlacePoint;
    public GameObject thirdPlacePoint;

    public Transform cameraTargetTransform;
}
