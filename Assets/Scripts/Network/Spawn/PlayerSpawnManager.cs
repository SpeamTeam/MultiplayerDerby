using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Assets.Scripts.Network.Spawn
{
    public class PlayerSpawnManager : NetworkBehaviour
    {
        private GameObject playerPrefab;

        private List<ulong> spawnedPlayers;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            GameConfig gameConfig = GameManager.Instance.Config;
            playerPrefab = gameConfig.playerPrefab;

            spawnedPlayers = new List<ulong>();

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

            SpawnPlayer(NetworkManager.Singleton.LocalClientId);
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;

            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

        private void OnClientConnected(ulong clientID)
        {
            SpawnPlayer(clientID);
        }


        private void SpawnPlayer(ulong clientID)
        {
            if (!IsServer) return;
            if (spawnedPlayers.Contains(clientID)) return;

            // TODO: Change spawn position
            GameObject instance = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            networkObject.SpawnAsPlayerObject(clientID);
            spawnedPlayers.Add(clientID);
            Debug.Log($"New client with id: {clientID} spawned! From {gameObject}");
            Debug.Log(spawnedPlayers.ToArray().ToString());
        }
    }
}