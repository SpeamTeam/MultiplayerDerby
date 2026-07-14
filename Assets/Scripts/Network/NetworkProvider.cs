using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using Assets.Scripts.UI;
using Assets.Scripts.InGameLogic;
using Assets.Scripts.Network.Respawn;

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
        if (!IsServer) return;
        var cfg = GameManager.Instance.Config;
        if (cfg == null || !cfg.autoRespawn) return;
        StartCoroutine(RespawnSequence(carHealth.NetworkObjectId, cfg));
    }

    /// <summary>
    /// Оркестрация кинематографического респавна КОНКРЕТНОЙ машины (по NetworkObjectId).
    /// Раскадровка (общая шкала сервера и клиента, отсчёт от смерти):
    ///   0–2с   камера на машине (клиент), 2–7с обзорные камеры (клиент),
    ///   с 7с   дрон над целью начинает сброс ящика.
    /// Сервер спавнит дрон в (droneDeliveryStartTime - droneTravelDuration), чтобы он
    /// прибыл к цели ровно к началу фазы сброса. ServerRespawn машины вызывается ТОЛЬКО
    /// после того, как ящик сброшен и растворён.
    ///
    /// Привязка строго по networkObjectId — одинаково корректно для игроков и ботов.
    /// Если кинематик выключен или нет префаба дрона — работает старый путь через respawnDelay.
    /// </summary>
    private IEnumerator RespawnSequence(ulong networkObjectId, GameConfig cfg)
    {
        // --- Fallback: старое поведение через respawnDelay ---
        if (!cfg.useCinematicRespawn || cfg.respawnDronePrefab == null)
        {
            yield return new WaitForSeconds(cfg.respawnDelay);
            RespawnObject(networkObjectId);
            yield break;
        }

        // Машина ещё должна существовать, чтобы узнать её будущую респавн-позу.
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject carObj))
        {
            Debug.LogWarning($"[NetworkProvider] RespawnSequence: машина {networkObjectId} уже деспавнена — отмена кинематика.");
            yield break;
        }
        CarAgent carAgent = carObj.GetComponent<CarAgent>();
        if (carAgent == null)
        {
            // Не машина — на всякий случай ведём себя как раньше.
            yield return new WaitForSeconds(cfg.respawnDelay);
            RespawnObject(networkObjectId);
            yield break;
        }

        carAgent.GetPlannedRespawnPose(out Vector3 respawnPos, out Quaternion respawnRot);
        Vector3 overhead = respawnPos + Vector3.up * cfg.droneOverheadHeight;

        // Спавним дрон так, чтобы он прибыл к overhead к droneDeliveryStartTime.
        float spawnDelay = Mathf.Max(0f, cfg.droneDeliveryStartTime - cfg.droneTravelDuration);
        yield return new WaitForSeconds(spawnDelay);

        // Выбираем свободную точку спавна дрона (сервер).
        Transform pad = DroneSpawnPointManager.Instance != null ? DroneSpawnPointManager.Instance.AcquirePoint() : null;
        Vector3 arenaCenter = DroneSpawnPointManager.Instance != null ? DroneSpawnPointManager.Instance.ArenaCenter : Vector3.zero;
        // Если свободных точек нет — стартуем сбоку-сверху от места респавна (кинематик не должен вставать колом).
        Vector3 droneStart = pad != null
            ? pad.position
            : overhead + new Vector3(cfg.droneOverheadHeight, cfg.droneOverheadHeight * 0.5f, 0f);

        // Спавн дрона (как SpawnProvider): Instantiate → NetworkObject.Spawn().
        GameObject droneGo = Instantiate(cfg.respawnDronePrefab, droneStart, Quaternion.identity);
        NetworkObject droneNo = droneGo.GetComponent<NetworkObject>();
        if (droneNo == null)
        {
            Debug.LogError("[NetworkProvider] На префабе дрона нет NetworkObject — fallback на прямой респавн.");
            Destroy(droneGo);
            if (pad != null) DroneSpawnPointManager.Instance?.ReleasePoint(pad);
            RespawnObject(networkObjectId);
            yield break;
        }
        droneNo.Spawn();

        RespawnDrone drone = droneGo.GetComponent<RespawnDrone>();
        bool delivered = false;
        drone.BeginDelivery(new RespawnDrone.DeliveryContext
        {
            startPos = droneStart,
            overheadPos = overhead,
            dropPos = respawnPos,
            arenaCenter = arenaCenter,
            travelDuration = cfg.droneTravelDuration,
            cratePrefab = cfg.respawnCratePrefab,
            crateFallSettleTime = cfg.crateFallSettleTime,
            crateDissolveDuration = cfg.crateDissolveDuration,
            onComplete = () => delivered = true
        });

        // Ждём, пока ящик доставлен и растворён.
        yield return new WaitUntil(() => delivered);

        // Только теперь — возрождаем именно эту машину (по её id).
        RespawnObject(networkObjectId);

        // Освобождаем точку спавна дрона.
        if (pad != null) DroneSpawnPointManager.Instance?.ReleasePoint(pad);
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