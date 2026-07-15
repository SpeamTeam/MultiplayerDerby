using System.Collections.Generic;
using Assets.Scripts.AI;
using Assets.Scripts.Network.Lobby;
using Unity.Netcode;
using UnityEngine;

namespace Assets.Scripts.Network.Spawn
{
    /// <summary>
    /// Спавнит машины по составу лобби (LobbyManager.Slots), а не по факту подключения "в лоб".
    /// При загрузке WorldScene разово спавнит всех, кто уже занял слот в лобби (люди + боты).
    /// После этого продолжает слушать OnClientConnectedCallback — это донабор игроков в свободные
    /// слоты уже во время идущего матча (слот для них уже зарезервирован в LobbyManager
    /// ConnectionApprovalCallback-ом на этапе подключения, см. NetworkHandler.HandleConnectionApproval).
    /// </summary>
    public class PlayerSpawnManager : NetworkBehaviour
    {
        private GameObject playerPrefab;
        private GameObject botPrefab;
        private GameObject RagDollPrefab;

        private readonly HashSet<int> spawnedSlots = new();

        // SpawnPoint.IsOccupied реагирует только на физический триггер, а он срабатывает не мгновенно —
        // при спавне нескольких машин подряд в одном кадре (SpawnAllOccupiedSlots) коллайдер ещё не
        // успевает выставить IsOccupied, и следующая машина выбирает ту же самую точку. Поэтому здесь
        // держим свой список точек, уже отданных кому-то в рамках этого PlayerSpawnManager — точка
        // блокируется сразу в момент выбора, а не когда до неё дойдёт физика.
        private readonly HashSet<SpawnPoint> claimedSpawnPoints = new();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            GameConfig gameConfig = GameManager.Instance.Config;
            playerPrefab = gameConfig.playerPrefab;
            botPrefab = gameConfig.botPrefab;
            RagDollPrefab = gameConfig.RagDollPrefab;

            SpawnAllOccupiedSlots();

            NetworkManager.Singleton.OnClientConnectedCallback += OnLateClientConnected;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;

            NetworkManager.Singleton.OnClientConnectedCallback -= OnLateClientConnected;
        }

        private void SpawnAllOccupiedSlots()
        {
            var lobby = LobbyManager.Instance;
            if (lobby == null)
            {
                Debug.LogWarning("[PlayerSpawnManager] LobbyManager.Instance is null, no lobby composition to spawn from.");
                return;
            }

            int max = Mathf.Min(lobby.MaxPlayers.Value, lobby.Slots.Count);
            for (int i = 0; i < max; i++)
            {
                var slot = lobby.Slots[i];
                if (slot.IsOccupied)
                    SpawnFromSlot(slot);
            }
        }

        private void OnLateClientConnected(ulong clientId)
        {
            var lobby = LobbyManager.Instance;
            if (lobby == null) return;

            int max = Mathf.Min(lobby.MaxPlayers.Value, lobby.Slots.Count);
            for (int i = 0; i < max; i++)
            {
                var slot = lobby.Slots[i];
                if (slot.IsOccupied && !slot.IsBot && slot.ClientId == clientId)
                {
                    SpawnFromSlot(slot);
                    break;
                }
            }
        }

        private void SpawnFromSlot(LobbySlotData slot)
        {
            if (spawnedSlots.Contains(slot.SlotIndex)) return;
            spawnedSlots.Add(slot.SlotIndex);

            SpawnPoint spawnPoint = FindFreeSpawnPoint();
            if (spawnPoint != null)
                claimedSpawnPoints.Add(spawnPoint);

            Vector3 pos = spawnPoint != null ? spawnPoint.transform.position : Vector3.zero;
            Quaternion rot = spawnPoint != null ? spawnPoint.transform.rotation : Quaternion.identity;

            if (slot.IsBot)
                SpawnBot(pos, rot, spawnPoint);
            else
                SpawnPlayer(slot.ClientId, slot.NickName.ToString(), pos, rot);
        }

        private void SpawnPlayer(ulong clientId, string nickName, Vector3 pos, Quaternion rot)
        {
            GameObject instance = Instantiate(playerPrefab, pos, rot);
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            networkObject.SpawnAsPlayerObject(clientId);

            var carAgent = instance.GetComponent<CarAgent>();
            if (carAgent != null)
                carAgent.nickName.Value = nickName;

            Debug.Log($"[PlayerSpawnManager] Spawned player for client {clientId} ({nickName})");
        }

        private void SpawnBot(Vector3 pos, Quaternion rot, SpawnPoint ownSpawnPoint)
        {
            GameObject instance = Instantiate(botPrefab, pos, rot);
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            networkObject.Spawn(); // без clientId => владелец сервер

            var navAgent = instance.GetComponent<CarNavMeshAgent>();
            if (navAgent != null)
            {
                var spawnPoints = SpawnPointsScript.Instance != null ? SpawnPointsScript.Instance.getSpawnPoints() : null;
                if (spawnPoints != null && spawnPoints.Count > 0)
                {
                    SpawnPoint target = spawnPoints.Find(p => p != ownSpawnPoint) ?? spawnPoints[0];
                    navAgent.SetTarget(target.transform);
                }
            }

            Debug.Log("[PlayerSpawnManager] Spawned bot");
        }

        private SpawnPoint FindFreeSpawnPoint()
        {
            var spawnPoints = SpawnPointsScript.Instance != null ? SpawnPointsScript.Instance.getSpawnPoints() : null;
            if (spawnPoints == null || spawnPoints.Count == 0) return null;

            foreach (var point in spawnPoints)
                if (point != null && !point.IsOccupied && !claimedSpawnPoints.Contains(point))
                    return point;

            // Все точки либо физически заняты, либо уже застолблены кем-то из этого же батча спавна —
            // лучше поставить машину поверх другой, чем не заспавнить игрока вовсе.
            return spawnPoints[0];
        }
    }
}
