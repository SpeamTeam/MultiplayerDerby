using Assets.Scripts.Network;
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UI { 
    public class PauseMenuScript : NetworkBehaviour
    {
        public static PauseMenuScript Instance { get; private set; }

        [SerializeField] private Button restartBtn;

        public Action<bool> MenuDeactivated;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer && restartBtn != null)
            {
                restartBtn.interactable = false;
            }
        }
        public void OnDisconnect()
        {
            NetworkHandler.Instance.Disconnect();
        }

        public void OnRestart()
        {
            NetworkProvider.Instance.RestartGame();
        }

        public void OnSuicide()
        {
            NetworkProvider.Instance.RespawnServerRpc(NetworkManager.Singleton.LocalClientId);
        }

        public void OnContinue()
        {
            gameObject.SetActive(false);
        }
        private void OnDisable() 
        { 
            MenuDeactivated?.Invoke(true);
        }
    }
}
