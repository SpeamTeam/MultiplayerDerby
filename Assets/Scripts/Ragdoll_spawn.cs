using Unity.Netcode;
using UnityEngine;

public class Ragdoll_spawn : NetworkBehaviour
{
    [SerializeField] private CarAgent carAgent;
    GameObject playerObject;
    [SerializeField] private Rigidbody Spinerb;
    private Rigidbody HandRb1;
    private Rigidbody HandRb2;
    [SerializeField] private FixedJoint HandJoint1;
    [SerializeField] private FixedJoint HandJoint2;
    public void SetPlayerData(CarAgent player)
    {
        carAgent = player;
        playerObject = carAgent.gameObject;
        GetRbData();
        SetJoints();
    }

    private void GetRbData()
    {
        HandRb1 = carAgent.HandPos1;
        HandRb2 = carAgent.HandPos2;
    }

    private void SetJoints()
    {
        playerObject.GetComponent<FixedJoint>().connectedBody = Spinerb;
        HandJoint1.connectedBody = HandRb1;
        HandJoint2.connectedBody = HandRb2;
    }
}
