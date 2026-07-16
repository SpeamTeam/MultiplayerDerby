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
        [Tooltip("На сколько приподнять машину над точкой падения ящика при респавне, чтобы её коллайдер не влип в поверхность арены.")]
        [SerializeField] private float carSpawnHeightOffset = 0.5f;

        private readonly List<CarAgent> playersList = new List<CarAgent>();
        public IReadOnlyList<CarAgent> PlayersList => playersList;

        // Активные корутины респавна — чтобы разом отменить их, когда бой окончен (PostCombat).
        // Храним именно НАПРЯМУЮ запущенные RespawnSequence: их try/finally гарантированно
        // отрабатывает при StopCoroutine и возвращает занятые маршруты (см. финальный блок там).
        private readonly List<Coroutine> activeRespawns = new List<Coroutine>();

        // После PostCombat респавнов больше нет: начатые отменены, новые не заказываются.
        private bool combatEnded;

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
        // Бой окончён (сработал PostCombat) — машины замерли на подиуме, новых респавнов нет.
        if (combatEnded) return;
        var cfg = GameManager.Instance.Config;
        if (cfg == null || !cfg.autoRespawn) return;
        activeRespawns.Add(StartCoroutine(RespawnSequence(carHealth.NetworkObjectId, cfg)));
    }

    /// <summary>
    /// Отменяет все респавны — и уже идущие (доставка дроном), и будущие. Зовётся из
    /// ScoreManager.PostCombat: по окончании боя машины замирают на подиуме и не должны
    /// воскресать, даже если ящик уже «запрошен» или дрон в полёте. Сервер, идемпотентно.
    /// </summary>
    public void CancelAllRespawns()
    {
        if (!IsServer) return;
        combatEnded = true;

        foreach (var routine in activeRespawns)
            if (routine != null) StopCoroutine(routine);
        activeRespawns.Clear();
        // Занятые маршруты вернутся сами: try/finally в RespawnSequence отрабатывает и при
        // StopCoroutine (см. комментарий в его finally). Дрон, если уже летит, доигрывает
        // свою корутину и деспавнится сам — машину он уже не возродит, т.к. RespawnSequence
        // остановлена до вызова RespawnObjectAt.
    }

    /// <summary>
    /// Оркестрация кинематографического респавна КОНКРЕТНОЙ машины (по NetworkObjectId).
    /// Раскадровка (общая шкала сервера и клиента, отсчёт от смерти):
    ///   0–2с   камера на машине (клиент), 2–7с обзорные камеры (клиент),
    ///   с droneDeliveryStartTime дрон входит на арену по маршруту и начинается высадка.
    ///
    /// Дрон спавнится в begin выбранного маршрута и летит к end, неся ящик; момент сброса
    /// решает зона высадки над ареной (см. RespawnDrone: OnTriggerEnter/Exit):
    ///   • машина игрока — на входе в зону открывается окно высадки, игрок роняет ящик по E
    ///     (не успел — сервер роняет сам, с запасом до выхода из зоны);
    ///   • машина бота — сервер роняет ящик на случайной доле зоны высадки.
    /// ServerRespawn вызывается ТОЛЬКО после того, как ящик сброшен и растворён, и машина
    /// встаёт в точку падения ящика (её возвращает дрон в onComplete).
    ///
    /// Привязка строго по networkObjectId — одинаково корректно для игроков и ботов.
    /// Если кинематик выключен или нет префаба дрона — работает старый путь через respawnDelay.
    /// </summary>
    private IEnumerator RespawnSequence(ulong networkObjectId, GameConfig cfg)
    {
        // --- Fallback: старое поведение через respawnDelay ---
        // Условия разделены намеренно: без этого молчаливый выход отсюда неотличим от
        // «дрон не работает», а в логе не остаётся ни следа причины.
        if (!cfg.useCinematicRespawn)
        {
            Debug.LogWarning("[NetworkProvider] Кинематик выключен (GameConfig.useCinematicRespawn = false) — респавн по respawnDelay.");
            yield return new WaitForSeconds(cfg.respawnDelay);
            RespawnObject(networkObjectId);
            yield break;
        }
        if (cfg.respawnDronePrefab == null)
        {
            Debug.LogWarning("[NetworkProvider] GameConfig.respawnDronePrefab не назначен — дрон не будет заспавнен, респавн по respawnDelay.");
            yield return new WaitForSeconds(cfg.respawnDelay);
            RespawnObject(networkObjectId);
            yield break;
        }
        if (cfg.respawnCratePrefab == null)
        {
            // Не fallback: дрон долетит и без ящика, машина всё равно встанет в точку сброса.
            Debug.LogWarning("[NetworkProvider] GameConfig.respawnCratePrefab не назначен — дрон полетит без ящика.");
        }

        // Машина ещё должна существовать, чтобы выбрать ветку высадки.
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject carObj))
        {
            Debug.LogWarning($"[NetworkProvider] RespawnSequence: машина {networkObjectId} уже деспавнена — отмена кинематика.");
            yield break;
        }
        CarAgent carAgent = carObj.GetComponent<CarAgent>();
        if (carAgent == null)
        {
                Debug.Log("[NetworkProvider] Пытаемся возродить не машину");
                // Не машина — на всякий случай ведём себя как раньше.
                yield return new WaitForSeconds(cfg.respawnDelay);
                RespawnObject(networkObjectId);
                yield break;
        }

        // Ветка высадки — по флагу бота, а НЕ по OwnerClientId: у ботов владелец сервер,
        // а на хосте это тот же ID, что и у машины самого игрока-хоста.
        bool manualDeploy = !carAgent.IsBotControlled;
        ulong deployClientId = carObj.OwnerClientId;

        // Дрон вступает по раскадровке, после обзорных камер.
        yield return new WaitForSeconds(Mathf.Max(0f, cfg.droneDeliveryStartTime));

        RespawnRouteManager.RespawnRoute route = null;
        try
        {
            // Две разные поломки — два разных сообщения: нет объекта в сцене против
            // «объект есть, но маршруты в нём не заполнены».
            if (RespawnRouteManager.Instance == null)
            {
                Debug.LogWarning("[NetworkProvider] RespawnRouteManager.Instance == null — объект не найден в WorldScene. Fallback на прямой респавн.");
                RespawnObject(networkObjectId);
                yield break;
            }

            route = RespawnRouteManager.Instance.AcquireRoute();
            if (route == null)
            {
                Debug.LogWarning("[NetworkProvider] AcquireRoute вернул null — в RespawnRouteManager не настроено ни одного маршрута (нужны begin и end). Fallback на прямой респавн.");
                RespawnObject(networkObjectId);
                yield break;
            }

            // Спавн дрона (как SpawnProvider): Instantiate → NetworkObject.Spawn().
            GameObject droneGo = Instantiate(cfg.respawnDronePrefab, route.begin.position, Quaternion.identity);
            NetworkObject droneNo = droneGo.GetComponent<NetworkObject>();
            RespawnDrone drone = droneGo.GetComponent<RespawnDrone>();
            if (droneNo == null || drone == null)
            {
                Debug.LogError("[NetworkProvider] На префабе дрона нет NetworkObject/RespawnDrone — fallback на прямой респавн.");
                Destroy(droneGo);
                RespawnObject(networkObjectId);
                yield break;
            }

            // Владение отдаём клиенту только в ветке игрока: иначе его E некому прислать.
            // Если клиент отвалился в момент смерти — SpawnWithOwnership кинет исключение,
            // поэтому проверяем и молча уходим в автоматику.
            if (manualDeploy && NetworkManager.ConnectedClients.ContainsKey(deployClientId))
            {
                droneNo.SpawnWithOwnership(deployClientId);
            }
            else
            {
                droneNo.Spawn();
                manualDeploy = false;
            }

            Vector3? dropPoint = null;
            bool delivered = false;
            drone.BeginDelivery(new RespawnDrone.DeliveryContext
            {
                routeBegin = route.begin.position,
                routeEnd = route.end.position,
                arenaCenter = RespawnRouteManager.Instance.ArenaCenter,
                manualDeploy = manualDeploy,
                deployClientId = deployClientId,
                cratePrefab = cfg.respawnCratePrefab,
                crateFallSettleTime = cfg.crateFallSettleTime,
                crateDissolveDuration = cfg.crateDissolveDuration,
                onComplete = point => { dropPoint = point; delivered = true; }
            });

            // Ждём, пока ящик доставлен и растворён. Условие drone == null закрывает ожидание,
            // если доставка сорвалась (дрон деспавнен) — иначе корутина висела бы вечно.
            yield return new WaitUntil(() => delivered || drone == null);

            // Только теперь — возрождаем именно эту машину (по её id), в точке падения ящика.
            if (dropPoint.HasValue)
                RespawnObjectAt(networkObjectId,
                                dropPoint.Value + Vector3.up * carSpawnHeightOffset,
                                RotationTowardsArenaCenter(dropPoint.Value));
            else
                RespawnObject(networkObjectId);   // проекция не удалась / доставка сорвалась
        }
        finally
        {
            // Маршрут освобождается в любом случае — в т.ч. если корутину остановили
            // (StopCoroutine/деспавн), иначе занятые маршруты утекали бы навсегда.
            RespawnRouteManager.Instance?.ReleaseRoute(route);
        }
    }

    /// <summary>Разворот машины на центр арены (в точке падения ящика опорной ротации нет).</summary>
    private Quaternion RotationTowardsArenaCenter(Vector3 from)
    {
        Vector3 center = RespawnRouteManager.Instance != null ? RespawnRouteManager.Instance.ArenaCenter : Vector3.zero;
        Vector3 flatDir = center - from;
        flatDir.y = 0f;
        return flatDir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(flatDir.normalized, Vector3.up)
            : Quaternion.identity;
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
            RespawnObjectInternal(networkObjectId, null, null);
        }

        /// <summary>
        /// То же самое, но в ЗАДАННУЮ позу — при кинематическом респавне это точка падения
        /// ящика. Поиск объекта тот же (по NetworkObjectId), поэтому одинаково корректен
        /// для игроков и ботов. Вызов с клиента игнорируется.
        /// </summary>
        public void RespawnObjectAt(ulong networkObjectId, Vector3 position, Quaternion rotation)
        {
            RespawnObjectInternal(networkObjectId, position, rotation);
        }

        private void RespawnObjectInternal(ulong networkObjectId, Vector3? position, Quaternion? rotation)
        {
            if (!IsServer) return;

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
            {
                Debug.LogWarning($"[NetworkProvider] RespawnObject: объект {networkObjectId} не найден (уже деспавнен?).");
                return;
            }

            CarAgent agent = netObj.GetComponent<CarAgent>();
            if (agent == null) return;

            if (position.HasValue)
                agent.ServerRespawnAt(position.Value, rotation ?? Quaternion.identity);
            else
                agent.ServerRespawn();   // плановая точка из SpawnPointsScript
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