using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Assets.Scripts.Network
{
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public class NetworkHandler : MonoBehaviour
    {
        public static NetworkHandler Instance { get; private set; }

        public event Action<ulong> OnClientConnected;
        public event Action<ulong> OnClientDisconnected;

        [SerializeField] public GameConfig gameConfig;

        private string worldSceneName;

        [Header("Have to be assigned")]
        [SerializeField] private GameObject networkProviderPrefab;

        [Header("Don't have to be assigned")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private UnityTransport unityTransport;

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

            networkManager = GetComponent<NetworkManager>();
            unityTransport = GetComponent<UnityTransport>();

            networkManager.OnServerStarted += SubscribeToSceneEvents;
        }

        public void MakeHost()
        {
            Debug.Log("Trying to make host");

            if (networkManager.StartHost())
            { Debug.Log("Hosted successfully"); }
            else
            { Debug.LogWarning("Something went wrong on hosting"); }

            worldSceneName = gameConfig.worldSceneName;

            var status = networkManager.SceneManager.LoadScene(worldSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
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

        private void SubscribeToSceneEvents()
        {
            // Here we try to catch some events
            networkManager.SceneManager.OnSceneEvent += CatchWorldSceneLoad;
        }

        private void CatchWorldSceneLoad(SceneEvent sceneEvent)
        {
            if (!(sceneEvent.SceneEventType == SceneEventType.LoadComplete && networkManager.IsServer))
                return;
            if (sceneEvent.ClientId != networkManager.LocalClientId)
                return;
            if (sceneEvent.SceneName == worldSceneName)
                SpawnProvider();
        }

        private void SpawnProvider()
        {
            GameObject instance = Instantiate(networkProviderPrefab, Vector3.zero, Quaternion.identity);
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            networkObject.Spawn();
        }

    }
}