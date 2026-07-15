using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Network.Respawn
{
    /// <summary>
    /// Пул фиксированных маршрутов высадки в сцене арены (WorldScene).
    ///
    /// Маршрут — прямая через арену: дрон спавнится в begin, летит к end, неся ящик,
    /// и по дороге сбрасывает его (по нажатию E у игрока или по доле маршрута у бота).
    ///
    /// СЕТЬ: намеренно НЕ NetworkBehaviour. Выбор маршрута — чисто серверное решение
    /// (вызывается только из NetworkProvider на сервере), а сам дрон уже networked
    /// и реплицируется всем клиентам через свой NetworkObject/NetworkTransform.
    ///
    /// Вытесняет DroneSpawnPointManager: тот хранил одиночные точки старта, а маршруту
    /// нужна пара begin/end, поэтому его пул тут не переиспользуется. Учёт занятости
    /// живёт ТОЛЬКО здесь — двух систем бронирования быть не должно.
    /// </summary>
    public class RespawnRouteManager : MonoBehaviour
    {
        public static RespawnRouteManager Instance { get; private set; }

        /// <summary>Один маршрут высадки: вход на арену и выход с неё.</summary>
        [Serializable]
        public class RespawnRoute
        {
            [Tooltip("Точка входа: здесь спавнится дрон и открывается окно высадки.")]
            public Transform begin;

            [Tooltip("Точка выхода: если игрок не нажал E до неё — ящик роняется здесь автоматически.")]
            public Transform end;

            public bool IsValid => begin != null && end != null;
        }

        [Tooltip("Фиксированные маршруты высадки (по замыслу — 4 прямые через арену).")]
        [SerializeField] private RespawnRoute[] routes = new RespawnRoute[4];

        [Tooltip("Центр арены — дрон всегда развёрнут на него. Если не задан — используется (0,0,0).")]
        [SerializeField] private Transform arenaCenter;

        // Маршруты, занятые активной доставкой. Сервер помечает при выдаче и снимает по завершении.
        private readonly HashSet<RespawnRoute> busy = new HashSet<RespawnRoute>();

        // Курсор round-robin для случая «все маршруты заняты» — чтобы дубли расходились по разным прямым.
        private int fallbackCursor;

        public Vector3 ArenaCenter => arenaCenter != null ? arenaCenter.position : Vector3.zero;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Выдать маршрут для доставки (сервер).
        ///
        /// Если все маршруты заняты — НЕ возвращает null, а выдаёт следующий по кругу без
        /// пометки busy: при пачке одновременных смертей два дрона на одной прямой в разное
        /// время визуально приемлемы, а вот пачка игроков, застрявших без машины, — нет.
        /// Возвращает null только если маршруты вообще не настроены.
        /// </summary>
        public RespawnRoute AcquireRoute()
        {
            if (routes == null || routes.Length == 0) return null;

            // Небольшой рандом старта перебора, чтобы дроны не лезли всегда с первого маршрута.
            int start = UnityEngine.Random.Range(0, routes.Length);
            for (int i = 0; i < routes.Length; i++)
            {
                RespawnRoute route = routes[(start + i) % routes.Length];
                if (route == null || !route.IsValid || busy.Contains(route)) continue;
                busy.Add(route);
                return route;
            }

            // Все заняты — отдаём следующий по кругу, не блокируя респавн.
            for (int i = 0; i < routes.Length; i++)
            {
                RespawnRoute route = routes[(fallbackCursor + i) % routes.Length];
                if (route == null || !route.IsValid) continue;
                fallbackCursor = (fallbackCursor + i + 1) % routes.Length;
                Debug.LogWarning("[RespawnRouteManager] Все маршруты заняты — выдаю занятый по кругу, чтобы не блокировать респавн.");
                return route;
            }

            Debug.LogError("[RespawnRouteManager] Не настроено ни одного валидного маршрута (нужны begin и end).");
            return null;
        }

        /// <summary>Освободить маршрут по завершении доставки (сервер). Null-safe.</summary>
        public void ReleaseRoute(RespawnRoute route)
        {
            if (route == null) return;
            busy.Remove(route);
        }
    }
}
