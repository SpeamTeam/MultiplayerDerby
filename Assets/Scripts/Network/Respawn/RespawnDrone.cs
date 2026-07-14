using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Assets.Scripts.Network.Respawn
{
    /// <summary>
    /// Дрон респавна. Прилетает к точке над местом возрождения машины, несёт ящик,
    /// сбрасывает его, ждёт растворения — и сигналит серверу, что можно возрождать машину.
    ///
    /// СЕТЬ (server-authoritative):
    ///   • Движение считает ТОЛЬКО сервер (if (!IsServer) return в Update). Позиция/поворот
    ///     реплицируются клиентам через NetworkTransform на префабе.
    ///   • Спавн дрона и спавн/сброс ящика — только на сервере.
    ///   • Звук двигателя — 3D-AudioSource (spatialBlend = 1) на префабе, играет на всех
    ///     клиентах сам (playOnAwake+loop), синхронизации не требует.
    ///
    /// Привязка к возрождаемой машине идёт по её NetworkObjectId на стороне NetworkProvider —
    /// сам дрон машину не знает, он лишь доставляет ящик в переданную точку и зовёт onComplete.
    /// </summary>
    public class RespawnDrone : NetworkBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("3D-звук двигателя дрона (spatialBlend = 1, loop, playOnAwake). Играет у всех клиентов сам.")]
        [SerializeField] private AudioSource engineAudio;

        [Tooltip("Точка под дроном, где висит ящик, пока его несут. Если не задана — берётся смещение вниз от дрона.")]
        [SerializeField] private Transform crateAttachPoint;

        [Tooltip("Смещение точки подвеса ящика от дрона (если crateAttachPoint не задан).")]
        [SerializeField] private Vector3 crateHangOffset = new Vector3(0f, -1.5f, 0f);

        [Tooltip("После сброса ящика дрон летит вверх на эту высоту, прежде чем деспавниться (уход из кадра).")]
        [SerializeField] private float exitRiseHeight = 30f;

        [Tooltip("Длительность (сек) ухода дрона вверх после доставки.")]
        [SerializeField] private float exitDuration = 3f;

        // Данные доставки задаёт сервер сразу после спавна.
        private DeliveryContext ctx;
        private bool started;

        /// <summary>Параметры одной доставки — заполняет NetworkProvider на сервере.</summary>
        public struct DeliveryContext
        {
            public Vector3 startPos;        // откуда дрон стартует (точка спавна)
            public Vector3 overheadPos;     // точка зависания над местом респавна
            public Vector3 dropPos;         // куда сбрасывается ящик (место респавна)
            public Vector3 arenaCenter;     // на него всегда развёрнут дрон
            public float travelDuration;    // сколько лететь start → overhead
            public GameObject cratePrefab;  // префаб ящика (NetworkObject)
            public float crateFallSettleTime;
            public float crateDissolveDuration;
            public Action onComplete;       // вызвать, когда ящик растворился (только на сервере)
        }

        /// <summary>
        /// Запустить доставку. Вызывать ТОЛЬКО на сервере, сразу после Spawn().
        /// </summary>
        public void BeginDelivery(DeliveryContext context)
        {
            if (!IsServer || started) return;
            started = true;
            ctx = context;

            // Стартовый разворот на центр арены.
            FaceArenaCenter();

            StartCoroutine(DeliverRoutine());
        }

        private IEnumerator DeliverRoutine()
        {
            // --- Фаза 1: спавним ящик и «берём» его под дрон ---
            RespawnCrate crate = SpawnCrate();

            // --- Фаза 2: полёт start → overhead за travelDuration ---
            float travel = Mathf.Max(0.01f, ctx.travelDuration);
            float t = 0f;
            while (t < travel)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / travel));
                transform.position = Vector3.Lerp(ctx.startPos, ctx.overheadPos, k);
                FaceArenaCenter();
                CarryCrate(crate);
                yield return null;
            }
            transform.position = ctx.overheadPos;
            CarryCrate(crate);

            // --- Фаза 3: сброс ящика ---
            if (crate != null)
                crate.ServerDrop();

            // --- Фаза 4: ждём, пока ящик приземлится и растворится ---
            // crate становится null после Despawn — оба условия закрывают ожидание.
            yield return new WaitUntil(() => crate == null || crate.IsFinished);

            // --- Фаза 5: сигналим серверу «можно возрождать машину» ---
            // Делаем это ДО ухода дрона, чтобы машина появилась вовремя по раскадровке.
            ctx.onComplete?.Invoke();

            // --- Фаза 6: дрон уходит вверх и деспавнится ---
            yield return StartCoroutine(ExitRoutine());

            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }

        private RespawnCrate SpawnCrate()
        {
            if (ctx.cratePrefab == null)
            {
                Debug.LogWarning("[RespawnDrone] cratePrefab не задан — ящик не будет создан.");
                return null;
            }

            Vector3 hang = crateAttachPoint != null ? crateAttachPoint.position : transform.position + crateHangOffset;
            GameObject crateGo = Instantiate(ctx.cratePrefab, hang, Quaternion.identity);

            NetworkObject crateNo = crateGo.GetComponent<NetworkObject>();
            if (crateNo == null)
            {
                Debug.LogError("[RespawnDrone] На префабе ящика нет NetworkObject.");
                Destroy(crateGo);
                return null;
            }
            crateNo.Spawn();

            RespawnCrate crate = crateGo.GetComponent<RespawnCrate>();
            crate?.ServerConfigure(ctx.crateFallSettleTime, ctx.crateDissolveDuration);
            return crate;
        }

        private void CarryCrate(RespawnCrate crate)
        {
            if (crate == null) return;
            Vector3 hang = crateAttachPoint != null ? crateAttachPoint.position : transform.position + crateHangOffset;
            crate.ServerFollow(hang, Quaternion.identity);
        }

        private IEnumerator ExitRoutine()
        {
            Vector3 from = transform.position;
            Vector3 to = from + Vector3.up * exitRiseHeight;
            float dur = Mathf.Max(0.01f, exitDuration);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(t / dur));
                FaceArenaCenter();
                yield return null;
            }
        }

        // Разворот на центр арены по горизонтали (не задираем/не опускаем нос дрона).
        private void FaceArenaCenter()
        {
            Vector3 flatDir = ctx.arenaCenter - transform.position;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
        }
    }
}
