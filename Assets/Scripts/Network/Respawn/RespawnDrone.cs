using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Assets.Scripts.Network.Respawn
{
    /// <summary>
    /// Дрон респавна. Пролетает по маршруту через арену (begin → end), неся ящик на
    /// подвесе, сбрасывает его — и сигналит серверу точку падения, где возродится машина.
    ///
    /// ДВЕ ВЕТКИ СБРОСА (решает сервер, ветку задаёт NetworkProvider через контекст):
    ///   • Игрок (manualDeploy = true): на begin открывается окно высадки, игрок жмёт E,
    ///     клиент шлёт RequestDropServerRpc — сервер проверяет и роняет ящик в текущую
    ///     проекцию дрона. Не нажал до end — сервер роняет автоматически в end.
    ///   • Бот (manualDeploy = false): окна нет, сервер роняет ящик, когда дрон прошёл
    ///     botDropFraction маршрута.
    ///
    /// СЕТЬ (server-authoritative):
    ///   • Движение считает ТОЛЬКО сервер. Позиция/поворот реплицируются клиентам через
    ///     NetworkTransform на префабе.
    ///   • Дрон двигается своим кинематическим Rigidbody (MovePosition/MoveRotation), а не
    ///     через transform: ящик висит на SpringJoint, привязанном к этому Rigidbody, и
    ///     раскачивается от его движения. Прямую запись в transform физика joint'а не видит.
    ///   • Спавн дрона, выбор момента сброса и сам сброс — только на сервере. Клиент может
    ///     лишь ПОПРОСИТЬ сброс (RequestDropServerRpc); роняет ящик всегда сервер.
    ///   • Звук двигателя — 3D-AudioSource (spatialBlend = 1) на префабе, играет на всех
    ///     клиентах сам (playOnAwake+loop), синхронизации не требует.
    ///
    /// Привязка к возрождаемой машине идёт по её NetworkObjectId на стороне NetworkProvider —
    /// сам дрон машину не знает: он летит по маршруту, роняет ящик и отдаёт в onComplete
    /// точку падения (или null, если спроецировать на арену не удалось).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RespawnDrone : NetworkBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("3D-звук двигателя дрона (spatialBlend = 1, loop, playOnAwake). Играет у всех клиентов сам.")]
        [SerializeField] private AudioSource engineAudio;

        [Tooltip("Точка под дроном, где висит ящик, пока его несут. Если не задана — берётся смещение вниз от дрона.")]
        [SerializeField] private Transform crateAttachPoint;

        [Tooltip("Смещение точки подвеса ящика от дрона (если crateAttachPoint не задан).")]
        [SerializeField] private Vector3 crateHangOffset = new Vector3(0f, -1.5f, 0f);

        [Header("Полёт по маршруту")]
        [Tooltip("Скорость полёта дрона по маршруту begin → end (мировые единицы в секунду).")]
        [SerializeField] private float flightSpeed = 12f;

        [Tooltip("Слои поверхности арены. По ним идёт вертикальная проекция дрона — она и есть точка падения ящика.")]
        [SerializeField] private LayerMask arenaMask = ~0;

        [Tooltip("Максимальная длина луча проекции вниз. Должна перекрывать высоту маршрута над ареной.")]
        [SerializeField] private float groundRayDistance = 200f;

        [Header("Уход после доставки")]
        [Tooltip("После сброса ящика дрон летит вверх на эту высоту, прежде чем деспавниться (уход из кадра).")]
        [SerializeField] private float exitRiseHeight = 30f;

        [Tooltip("Длительность (сек) ухода дрона вверх после доставки.")]
        [SerializeField] private float exitDuration = 3f;

        /// <summary>
        /// Окно высадки открыто (только ветка игрока). Пишет ТОЛЬКО сервер; клиент читает,
        /// чтобы знать, что кнопка E сейчас доступна — крестик/UI навесятся на это позже.
        /// </summary>
        private readonly NetworkVariable<bool> deployWindowOpen = new NetworkVariable<bool>(false);

        public bool IsDeployWindowOpen => deployWindowOpen.Value;

        // Данные доставки задаёт сервер сразу после спавна.
        private DeliveryContext ctx;
        private bool started;

        // Игрок нажал E и сервер запрос подтвердил (только на сервере).
        private bool dropRequested;

        // Кинематическое тело: сам дрон не падает, но служит якорем для подвеса ящика.
        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>Параметры одной доставки — заполняет NetworkProvider на сервере.</summary>
        public struct DeliveryContext
        {
            public Vector3 routeBegin;      // вход на арену: тут спавнится дрон и открывается окно высадки
            public Vector3 routeEnd;        // выход: досюда — дедлайн ручного сброса
            public Vector3 arenaCenter;     // на него всегда развёрнут дрон
            public bool manualDeploy;       // true — игрок (окно + E), false — бот (авто по доле маршрута)
            public float botDropFraction;   // 0..1: доля маршрута, на которой роняет бот
            public ulong deployClientId;    // чей клиент вправе просить сброс (ветка игрока)
            public GameObject cratePrefab;  // префаб ящика (NetworkObject)
            public float crateFallSettleTime;
            public float crateDissolveDuration;

            // Вызвать, когда ящик растворился (только на сервере). Аргумент — точка падения;
            // null, если спроецировать дрон на арену не удалось (вызывающий откатится на свою точку).
            public Action<Vector3?> onComplete;
        }

        /// <summary>
        /// Запустить доставку. Вызывать ТОЛЬКО на сервере, сразу после Spawn().
        /// </summary>
        public void BeginDelivery(DeliveryContext context)
        {
            if (!IsServer || started) return;
            started = true;
            ctx = context;

            // Стартовый разворот на центр арены — мгновенно, до первого FixedUpdate,
            // иначе дрон успеет мигнуть неверным поворотом.
            FaceArenaCenter(instant: true);

            StartCoroutine(DeliverRoutine());
        }

        private IEnumerator DeliverRoutine()
        {
            // --- Фаза 1: спавним ящик и подвешиваем его под дрон ---
            RespawnCrate crate = SpawnCrate();

            // --- Фаза 2: пролёт begin → end, по дороге решаем момент сброса ---
            yield return StartCoroutine(FlyRouteUntilDrop());

            // --- Фаза 3: сброс ящика в текущую проекцию дрона на арену ---
            // Ящик отцепляется от подвеса и падает вертикально вниз — сюда же встанет машина.
            Vector3? dropPoint = ProjectToArena(rb.position);
            if (crate != null)
                crate.ServerDrop();

            // --- Фаза 4: ждём, пока ящик приземлится и растворится ---
            // crate становится null после Despawn — оба условия закрывают ожидание.
            yield return new WaitUntil(() => crate == null || crate.IsFinished);

            // --- Фаза 5: сигналим серверу «можно возрождать машину» и куда именно ---
            // Делаем это ДО ухода дрона, чтобы машина появилась вовремя по раскадровке.
            ctx.onComplete?.Invoke(dropPoint);

            // --- Фаза 6: дрон уходит вверх и деспавнится ---
            yield return StartCoroutine(ExitRoutine());

            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }

        /// <summary>
        /// Летит по прямой begin → end со скоростью flightSpeed и выходит, когда пора ронять:
        /// у игрока — по подтверждённому запросу E (или по достижении end как таймаут),
        /// у бота — по прохождении botDropFraction маршрута.
        /// Движение через rb.MovePosition в такте физики: ящик на пружине качается сам.
        /// </summary>
        private IEnumerator FlyRouteUntilDrop()
        {
            float routeLength = Vector3.Distance(ctx.routeBegin, ctx.routeEnd);
            if (routeLength < 0.01f)
            {
                Debug.LogWarning("[RespawnDrone] Маршрут вырожден (begin == end) — роняем ящик сразу.");
                yield break;
            }

            // Окно высадки только для игрока: с этого момента его E принимается.
            if (ctx.manualDeploy)
                deployWindowOpen.Value = true;

            float botFraction = Mathf.Clamp01(ctx.botDropFraction);
            float traveled = 0f;

            while (traveled < routeLength)
            {
                yield return new WaitForFixedUpdate();

                traveled += flightSpeed * Time.fixedDeltaTime;
                float k = Mathf.Clamp01(traveled / routeLength);
                rb.MovePosition(Vector3.Lerp(ctx.routeBegin, ctx.routeEnd, k));
                FaceArenaCenter();

                if (ctx.manualDeploy)
                {
                    // Игрок нажал E — сброс здесь и сейчас. Если не нажмёт, цикл сам
                    // доедет до end: достижение end и есть таймаут.
                    if (dropRequested) break;
                }
                else if (k >= botFraction)
                {
                    // Бот: сброс на заранее выбранной сервером доле маршрута.
                    break;
                }
            }

            if (ctx.manualDeploy)
                deployWindowOpen.Value = false;
        }

        /// <summary>
        /// Клиент просит сбросить ящик (нажатие E). Решение и исполнение — на сервере:
        /// сам сброс делает DeliverRoutine, здесь только проверка права и валидности момента.
        /// RequireOwnership = false: право проверяем по deployClientId из контекста, а не по
        /// владельцу объекта — на хосте бот и хост неразличимы по OwnerClientId.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestDropServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer) return;

            if (!ctx.manualDeploy || !deployWindowOpen.Value || dropRequested)
                return;

            if (rpcParams.Receive.SenderClientId != ctx.deployClientId)
            {
                Debug.LogWarning($"[RespawnDrone] Клиент {rpcParams.Receive.SenderClientId} просит сброс с чужого дрона (ожидался {ctx.deployClientId}) — игнорирую.");
                return;
            }

            dropRequested = true;
        }

        /// <summary>
        /// Вертикальная проекция дрона на арену — точка падения ящика и будущая позиция машины.
        /// null, если под дроном нет поверхности из arenaMask: вызывающая сторона тогда
        /// откатится на свою плановую точку, а не поставит машину в дырку.
        /// </summary>
        private Vector3? ProjectToArena(Vector3 from)
        {
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, groundRayDistance, arenaMask, QueryTriggerInteraction.Ignore))
                return hit.point;

            Debug.LogWarning($"[RespawnDrone] Под дроном ({from}) нет поверхности арены в пределах {groundRayDistance} — точка падения неизвестна.");
            return null;
        }

        private RespawnCrate SpawnCrate()
        {
            if (ctx.cratePrefab == null)
            {
                Debug.LogWarning("[RespawnDrone] cratePrefab не задан — ящик не будет создан.");
                return null;
            }

            GameObject crateGo = Instantiate(ctx.cratePrefab, HangPointWorld(), Quaternion.identity);

            NetworkObject crateNo = crateGo.GetComponent<NetworkObject>();
            if (crateNo == null)
            {
                Debug.LogError("[RespawnDrone] На префабе ящика нет NetworkObject.");
                Destroy(crateGo);
                return null;
            }
            crateNo.Spawn();

            RespawnCrate crate = crateGo.GetComponent<RespawnCrate>();
            if (crate == null) return null;

            crate.ServerConfigure(ctx.crateFallSettleTime, ctx.crateDissolveDuration);

            // Подвешиваем один раз — дальше ящик держит SpringJoint, а не покадровое
            // позиционирование. connectedAnchor задаётся в локальных координатах дрона.
            crate.ServerAttach(rb, HangLocalOffset());
            return crate;
        }

        /// <summary>Точка подвеса ящика в локальных координатах дрона (для connectedAnchor).</summary>
        private Vector3 HangLocalOffset()
        {
            return crateAttachPoint != null
                ? transform.InverseTransformPoint(crateAttachPoint.position)
                : crateHangOffset;
        }

        /// <summary>Точка подвеса ящика в мировых координатах (стартовая позиция спавна).</summary>
        private Vector3 HangPointWorld()
        {
            return crateAttachPoint != null
                ? crateAttachPoint.position
                : transform.TransformPoint(crateHangOffset);
        }

        private IEnumerator ExitRoutine()
        {
            Vector3 from = rb.position;
            Vector3 to = from + Vector3.up * exitRiseHeight;
            float dur = Mathf.Max(0.01f, exitDuration);
            float t = 0f;
            while (t < dur)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                rb.MovePosition(Vector3.Lerp(from, to, Mathf.Clamp01(t / dur)));
                FaceArenaCenter();
            }
        }

        // Разворот на центр арены по горизонтали (не задираем/не опускаем нос дрона).
        // instant = true только для стартового кадра, до того как заработает физика.
        private void FaceArenaCenter(bool instant = false)
        {
            Vector3 flatDir = ctx.arenaCenter - rb.position;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude <= 0.0001f) return;

            Quaternion look = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
            if (instant)
                transform.rotation = look;
            else
                rb.MoveRotation(look);
        }
    }
}
