using Unity.Netcode;
using UnityEngine;

public class NetworkHandler : MonoBehaviour
{
    [SerializeField] NetworkManager networkManager;


    public void MakeHost()
    {
        Debug.Log("Trying to make host");
    }

    public void MakeClient(string ip, int port)
    {
        Debug.Log("Trying to make client");
    }
}
