using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using Assets.Scripts.UI;
using Assets.Scripts.InGameLogic;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Game manager for multiplayer
    /// </summary>
    public class NetworkProvider : NetworkBehaviour
    {
        private readonly List<CarAgent> playersList = new List<CarAgent>();
        public IReadOnlyList<CarAgent> PlayersList => playersList;

        public static NetworkProvider Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        /// <summary>Регистрирует машину настоящего игрока (не бота) — для будущего таргетинга/статистики, а не для респавна. Вызывать только на сервере.</summary>
        public void RegisterPlayer(CarAgent agent)
        {
            if (!IsServer || agent == null || playersList.Contains(agent)) return;
            playersList.Add(agent);
            ScoreManager.Instance?.RegisterPlayer(agent);
        }

        /// <summary>Снимает регистрацию (например, при отключении клиента). Вызывать только на сервере.</summary>
        public void UnregisterPlayer(CarAgent agent)
        {
            if (!IsServer) return;
            playersList.Remove(agent);
            ScoreManager.Instance?.UnregisterPlayer(agent);
        }

        public void RestartGame()
        {
            // TODO: Implement
        }


    public void HandleCarDeath(CarHealth carHealth)
    {
        if (GameManager.Instance.Config == null || !GameManager.Instance.Config.autoRespawn) return;
        StartCoroutine(RespawnAfterDelay(carHealth.NetworkObjectId, GameManager.Instance.Config.respawnDelay));
    }

    private IEnumerator RespawnAfterDelay(ulong networkObjectId, float delay)
    {
        yield return new WaitForSeconds(delay);
        RespawnObject(networkObjectId);
    }


        /// <summary>
        /// Респавн КОНКРЕТНОГО объекта по NetworkObjectId. Не зависит от OwnerClientId —
        /// поэтому одинаково корректно работает и для игроков, и для ботов. Это важно,
        /// т.к. у ботов нет владеющего клиента: их OwnerClientId по умолчанию == серверу,
        /// а на хосте это тот же ID, что и у машины самого хоста — по clientId их не различить.
        /// Вызывается GameManager'ом после смерти машины (см. CarHealth.Die → HandleCarDeath).
        /// Вызов с клиента игнорируется.
        /// </summary>
        public void RespawnObject(ulong networkObjectId)
        {
            if (!IsServer) return;

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
            {
                Debug.LogWarning($"[NetworkProvider] RespawnObject: объект {networkObjectId} не найден (уже деспавнен?).");
                return;
            }

            CarAgent agent = netObj.GetComponent<CarAgent>();
            if (agent != null)
                agent.ServerRespawn();
        }

        /// <summary>
        /// Респавн машины КОНКРЕТНОГО ПОДКЛЮЧЁННОГО клиента (например, кнопка Suicide
        /// в паузе). Ищет через его PlayerObject, а не playersList/OwnerClientId — так
        /// не путается с ботами, у которых OwnerClientId тоже может совпасть с сервером.
        /// Вызов с клиента игнорируется.
        /// </summary>

        [ServerRpc(RequireOwnership = false)]
        public void RespawnServerRpc(ulong clientId)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
            {
                Debug.LogWarning($"[NetworkProvider] Respawn: клиент {clientId} не подключён или без машины.");
                return;
            }

            CarAgent agent = client.PlayerObject.GetComponent<CarAgent>();
            if (agent != null)
                agent.ServerRespawn();
        }
    }
}