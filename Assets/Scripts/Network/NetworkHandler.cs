using System;
using System.Text;
using Assets.Scripts.Network.Lobby;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        [SerializeField] private GameObject lobbyManagerPrefab;

        [Header("Don't have to be assigned")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private UnityTransport unityTransport;

        public string localPlayerName;

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

            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.ConnectionApprovalCallback = HandleConnectionApproval;
        }

        public void MakeHost(string nickname = null)
        {
            Debug.Log("Trying to make host");

            localPlayerName = nickname;

            if (networkManager.StartHost())
            {
                Debug.Log("Hosted successfully");
                SpawnLobbyManager();
            }
            else
            { Debug.LogWarning("Something went wrong on hosting"); }
        }

        public void MakeClient(string ip, ushort port, string nickname = null)
        {
            Debug.Log("Trying to make client");

            unityTransport.SetConnectionData(ip, port);
            networkManager.NetworkConfig.ConnectionData = string.IsNullOrEmpty(nickname)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(nickname);

            if (networkManager.StartClient())
            {
                Debug.Log("Made a client connection successfully");
            }
            else
            {
                Debug.LogWarning("Something went wrong on clint initialization");
            }
        }

        /// <summary>
        /// Переход из лобби (MenuScene) в матч. Раньше вызывался инлайн внутри MakeHost сразу
        /// после StartHost — теперь только по нажатию «Начать игру» в лобби, см. LobbyManager.TryStartGame.
        /// </summary>
        public void LoadWorldScene()
        {
            if (!networkManager.IsServer) return;

            worldSceneName = gameConfig.worldSceneName;

            var status = networkManager.SceneManager.LoadScene(worldSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError("Something went wrong on WorldScene loading. Status: " + status.ToString());
            }
        }

        private void SpawnLobbyManager()
        {
            GameObject instance = Instantiate(lobbyManagerPrefab, Vector3.zero, Quaternion.identity);
            DontDestroyOnLoad(instance);

            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            // Лобби должно пережить LoadSceneMode.Single переход в WorldScene — иначе подключение
            // новых игроков во время уже идущего матча (см. LobbyManager.TryReserveSlotFor) не с чем сверять.
            networkObject.DestroyWithScene = false;
            networkObject.Spawn();
        }

        private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;

            if (networkManager.IsHost && request.ClientNetworkId == networkManager.LocalClientId)
            {
                response.Approved = true;
                return;
            }

            string nickname = request.Payload != null && request.Payload.Length > 0
                ? Encoding.UTF8.GetString(request.Payload)
                : null;

            bool reserved = LobbyManager.Instance != null && LobbyManager.Instance.TryReserveSlotFor(request.ClientNetworkId, nickname);

            response.Approved = reserved;
            if (!reserved)
            {
                response.Reason = "Lobby is full";
            }
        }

        public void Disconnect()
        {
            networkManager.Shutdown();
            LoadMenu();
            Debug.Log("Disconnecting");
        }

        private void LoadMenu()
        {
            var menuSceneName = gameConfig.menuSceneName;
            SceneManager.LoadScene(menuSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
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