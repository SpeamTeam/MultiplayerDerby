using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkHandler : MonoBehaviour
{
    public static NetworkHandler Instance { get; private set; }

    public event Action<ulong> OnClientConnected;
    public event Action<ulong> OnClientDisconnected;

    //public event Action<ulong> OnClientConnected;
    //public event Action<ulong> OnClientDisconnected;

    [SerializeField] NetworkManager networkManager;
    [SerializeField] UnityTransport unityTransport;
     
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Probably already satisfied because
        // component is on the NetworkManager's object
        DontDestroyOnLoad(gameObject);

        if (networkManager == null) networkManager = NetworkManager.Singleton;
    }

    public void MakeHost()
    {
        Debug.Log("Trying to make host");

        if (networkManager.StartHost())
        { Debug.Log("Hosted successfully"); }
        else
        { Debug.LogWarning("Something went wrong on hosting"); }

        var status = networkManager.SceneManager.LoadScene("WorldScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError("Something went wrong on WorldScene loading. Status: " + status.ToString());
        }
    }

    public void MakeClient(string ip, ushort port)
    {
        Debug.Log("Trying to make client");

        unityTransport.SetConnectionData(ip, port);

        if (networkManager.StartClient())
        {
            Debug.Log("Made a client connection successfully");
        }
        else
        {
            Debug.LogWarning("Something went wrong on clint initialization");
        }
    }

    public void Disconnect()
    {
        networkManager.Shutdown();
        Debug.Log("Disconnecting");
    }

    // Action Handlers {//
    private void HandleClientConnected(ulong clientID)
    {
        OnClientConnected?.Invoke(clientID);
    }

    private void HandleClientDisconnected(ulong clientID)
    {
        OnClientDisconnected?.Invoke(clientID);
    }
    // Action Handlers }//


}
