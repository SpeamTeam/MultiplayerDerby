using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Network.Respawn
{
    /// <summary>
    /// УСТАРЕЛ И БОЛЬШЕ НЕ ИСПОЛЬЗУЕТСЯ — вытеснен RespawnRouteManager.
    /// Дрон теперь летит не от одиночной точки старта, а по маршруту (пара begin/end),
    /// который пул одиночных Transform'ов выразить не может. Вызывающих не осталось;
    /// учёт занятости живёт только в RespawnRouteManager. Файл и объект в сцене оставлены
    /// временно для отката — удалить отдельным коммитом, когда маршруты отъездят.
    ///
    /// Пул точек спавна дронов респавна в сцене арены (WorldScene).
    ///
    /// СЕТЬ: намеренно НЕ NetworkBehaviour. Выбор точки — чисто серверное решение
    /// (вызывается только из NetworkProvider на сервере), а сам дрон уже networked
    /// и реплицируется всем клиентам через свой NetworkObject/NetworkTransform.
    /// Точки не нужно синхронизировать: клиентам достаточно видеть летящий дрон.
    ///
    /// Занятость точки — простой флаг «в работе»: пока дрон стартовал с точки и
    /// ещё не завершил доставку, точка занята, чтобы двух дронов не спавнило из
    /// одной позиции (важно, т.к. игроков и ботов много и смерти идут пачками).
    /// </summary>
    public class DroneSpawnPointManager : MonoBehaviour
    {
        public static DroneSpawnPointManager Instance { get; private set; }

        [Tooltip("Точки, из которых стартуют дроны респавна (по краям/над ареной). Дрон всегда развёрнут на центр арены.")]
        [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

        [Tooltip("Центр арены — дрон всегда смотрит на него. Если не задан — используется (0,0,0).")]
        [SerializeField] private Transform arenaCenter;

        // Точки, занятые сейчас активной доставкой. Сервер помечает при выдаче и снимает по завершении.
        private readonly HashSet<Transform> busy = new HashSet<Transform>();

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
        /// Выдать свободную точку спавна (сервер). Возвращает null, если свободных нет —
        /// вызывающая сторона обязана обработать это (например, взять запасную позицию).
        /// </summary>
        public Transform AcquirePoint()
        {
            if (spawnPoints == null || spawnPoints.Count == 0) return null;

            // Небольшой рандом старта перебора, чтобы дроны не всегда лезли из одной первой точки.
            int start = Random.Range(0, spawnPoints.Count);
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                Transform point = spawnPoints[(start + i) % spawnPoints.Count];
                if (point == null || busy.Contains(point)) continue;
                busy.Add(point);
                return point;
            }
            return null;
        }

        /// <summary>Освободить точку по завершении доставки (сервер).</summary>
        public void ReleasePoint(Transform point)
        {
            if (point == null) return;
            busy.Remove(point);
        }
    }
}
