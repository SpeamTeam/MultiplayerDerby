using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Assets.Scripts.Network.Spawn
{
    public class PlayerSpawnManager : NetworkBehaviour
    {
        private GameObject playerPrefab;
        private GameObject RagDollPrefab;

        private List<ulong> spawnedPlayers;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            GameConfig gameConfig = GameManager.Instance.Config;
            playerPrefab = gameConfig.playerPrefab;
            RagDollPrefab = gameConfig.RagDollPrefab;

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

            var spawnPoints = SpawnPointsScript.Instance.getSpawnPoints();

            bool spawned = false;

            if (spawnPoints.Count == 0)
                Debug.LogWarning("No spawn points at scene");
            else
                Debug.Log($"There're {spawnPoints.Count} at scene");
            foreach (var point in spawnPoints)
            {
                if (!point.IsOccupied)
                {
                    Vector3 newPos = point.transform.position;

                    GameObject instance = Instantiate(playerPrefab, newPos, Quaternion.identity);
                    NetworkObject networkObject = instance.GetComponent<NetworkObject>();
                    networkObject.SpawnAsPlayerObject(clientID);
                    spawnedPlayers.Add(clientID);
                    GameObject RagDollInstance = Instantiate(RagDollPrefab,newPos,Quaternion.identity);
                    RagDollInstance.GetComponent<NetworkObject>().Spawn();
                    RagDollInstance.GetComponent<Ragdoll_spawn>().SetPlayerData(instance.GetComponent<CarAgent>());
                    spawned = true;
                    Debug.Log($"New client with id: {clientID} spawned! From {gameObject}");
                    Debug.Log(spawnedPlayers.ToArray().ToString());
                    break;
                }
            }
            if (!spawned) // If no free points
            {
                Debug.Log("No free spawn points, so instantiating in (0,0,0)");
                GameObject instance = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
                NetworkObject networkObject = instance.GetComponent<NetworkObject>();
                networkObject.SpawnAsPlayerObject(clientID);
                spawnedPlayers.Add(clientID);
                Debug.Log($"New client with id: {clientID} spawned! From {gameObject}");
                Debug.Log(spawnedPlayers.ToArray().ToString());
            }

        }
    }
}
