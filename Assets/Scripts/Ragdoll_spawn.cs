using Unity.Netcode;
using UnityEngine;

public class Ragdoll_spawn : MonoBehaviour
{
    [SerializeField] private CarAgent carAgent;
    GameObject playerObject;
    [SerializeField] private Rigidbody Spinerb;
    [SerializeField] private FixedJoint HandJoint1;
    [SerializeField] private FixedJoint HandJoint2;
    public void SetPlayerData(CarAgent player)
    {
        carAgent = player;
        playerObject = carAgent.gameObject;
        SetJoints();
    }


    private void SetJoints()
    {
        playerObject.GetComponent<FixedJoint>().connectedBody = Spinerb;
        HandJoint1.connectedBody = playerObject.GetComponent<Rigidbody>();
        HandJoint2.connectedBody = playerObject.GetComponent<Rigidbody>();
    }
}
