using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CarController))]
public class CarNavMeshAgent : MonoBehaviour
{
    [Header("Navigation")]
    public Transform target;
    public float pathUpdateInterval = 0.5f;
    public float waypointReachDistance = 2f;
    public float destinationReachDistance = 3f;

    [Header("Steering Settings")]
    public float steeringSensitivity = 1.5f;
    public float lookAheadDistance = 5f;

    [Header("Speed Control")]
    public float approachDistance = 10f;      // Дистанция начала торможения
    public float minApproachSpeed = 0.3f;     // Минимальный газ при подъезде
    public float cornerSlowdownFactor = 0.5f; // Замедление в поворотах
    public float sharpTurnAngle = 45f;        // Угол резкого поворота

    [Header("Reversing")]
    public bool allowReversing = true;
    public float stuckCheckInterval = 2f;
    public float stuckThreshold = 0.5f;      // Минимальное смещение чтобы не считаться застрявшим
    public float reverseTime = 1.5f;

    [Header("Debug")]
    public bool showDebugGizmos = true;

    // Компоненты
    private CarController carController;
    private NavMeshPath navMeshPath;

    // Состояние навигации
    private Vector3[] pathCorners = new Vector3[0];
    private int currentWaypointIndex = 0;
    private bool hasPath = false;
    private bool reachedDestination = false;

    // Состояние застревания
    private Vector3 lastPosition;
    private float stuckTimer = 0f;
    private bool isReversing = false;
    private float reverseTimer = 0f;

    // Состояние машины
    private float motorInput = 0f;
    private float steerInput = 0f;
    private float brakeInput = 0f;

    private Coroutine pathUpdateCoroutine;

    private void Awake()
    {
        carController = GetComponent<CarController>();
        navMeshPath = new NavMeshPath();
    }

    private void Start()
    {
        lastPosition = transform.position;

        if (target != null)
            SetDestination(target.position);

        pathUpdateCoroutine = StartCoroutine(UpdatePathRoutine());
    }

    private void Update()
    {
        if (target != null && target.hasChanged)
        {
            target.hasChanged = false;
            SetDestination(target.position);
        }
    }

    private void FixedUpdate()
    {
        if (isReversing)
        {
            HandleReversing();
        }
        else
        {
            CheckIfStuck();

            if (hasPath && !reachedDestination)
                FollowPath();
            else
                StopCar();
        }

        carController.SetInputs(motorInput, steerInput, brakeInput);
    }

    // ──────────────────────────────────────────────
    //  Публичные методы
    // ──────────────────────────────────────────────

    public void SetDestination(Vector3 destination)
    {
        reachedDestination = false;
        currentWaypointIndex = 0;
        CalculatePath(destination);
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null)
            SetDestination(target.position);
    }

    public void Stop()
    {
        hasPath = false;
        reachedDestination = true;
        StopCar();
    }

    // ──────────────────────────────────────────────
    //  Навигация
    // ──────────────────────────────────────────────

    private void CalculatePath(Vector3 destination)
    {
        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, navMeshPath);

            if (navMeshPath.status == NavMeshPathStatus.PathComplete ||
                navMeshPath.status == NavMeshPathStatus.PathPartial)
            {
                pathCorners = navMeshPath.corners;
                hasPath = pathCorners.Length > 1;
                currentWaypointIndex = 1; // 0 - текущая позиция

                Debug.Log($"[CarNav] Путь рассчитан. Точек: {pathCorners.Length}, " +
                          $"Статус: {navMeshPath.status}");
            }
            else
            {
                Debug.LogWarning($"[CarNav] Не удалось построить путь до {destination}");
                hasPath = false;
            }
        }
        else
        {
            Debug.LogWarning($"[CarNav] Позиция {destination} не на NavMesh");
        }
    }

    private IEnumerator UpdatePathRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(pathUpdateInterval);

            if (target != null && !reachedDestination && !isReversing)
                CalculatePath(target.position);
        }
    }

    // ──────────────────────────────────────────────
    //  Следование по пути
    // ──────────────────────────────────────────────

    private void FollowPath()
    {
        if (currentWaypointIndex >= pathCorners.Length)
        {
            reachedDestination = true;
            StopCar();
            Debug.Log("[CarNav] Цель достигнута!");
            return;
        }

        Vector3 targetWaypoint = pathCorners[currentWaypointIndex];
        targetWaypoint.y = transform.position.y; // Игнорируем перепад высот для рулёжки

        float distanceToWaypoint = Vector3.Distance(transform.position, targetWaypoint);
        float distanceToDestination = Vector3.Distance(transform.position,
                                                       pathCorners[pathCorners.Length - 1]);

        // Проверка достижения точки назначения
        if (distanceToDestination <= destinationReachDistance)
        {
            reachedDestination = true;
            StopCar();
            Debug.Log("[CarNav] Цель достигнута!");
            return;
        }

        // Переход к следующей точке пути
        if (distanceToWaypoint <= waypointReachDistance)
        {
            currentWaypointIndex++;
            if (currentWaypointIndex >= pathCorners.Length)
            {
                reachedDestination = true;
                StopCar();
                return;
            }
            targetWaypoint = pathCorners[currentWaypointIndex];
            targetWaypoint.y = transform.position.y;
        }

        // Look-ahead: смотрим чуть вперёд по пути
        Vector3 lookAheadPoint = GetLookAheadPoint();
        lookAheadPoint.y = transform.position.y;

        // Рулёжка
        steerInput = CalculateSteering(lookAheadPoint);

        // Газ / тормоз
        CalculateThrottle(distanceToDestination);
    }

    /// <summary>
    /// Точка на пути на расстоянии lookAheadDistance от машины
    /// </summary>
    private Vector3 GetLookAheadPoint()
    {
        float accumulated = 0f;

        for (int i = currentWaypointIndex; i < pathCorners.Length; i++)
        {
            Vector3 from = i == currentWaypointIndex
                ? transform.position
                : pathCorners[i - 1];

            Vector3 to = pathCorners[i];
            float segmentLength = Vector3.Distance(from, to);

            if (accumulated + segmentLength >= lookAheadDistance)
            {
                float t = (lookAheadDistance - accumulated) / segmentLength;
                return Vector3.Lerp(from, to, t);
            }

            accumulated += segmentLength;
        }

        // Если путь короче lookAhead — берём последнюю точку
        return pathCorners[pathCorners.Length - 1];
    }

    private float CalculateSteering(Vector3 targetPoint)
    {
        // Переводим целевую точку в локальное пространство машины
        Vector3 localTarget = transform.InverseTransformPoint(targetPoint);

        // Угол поворота: localTarget.x положительный — цель справа
        float angle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;

        // Нормализуем и применяем чувствительность
        float steer = Mathf.Clamp(angle / 90f * steeringSensitivity, -1f, 1f);

        return steer;
    }

    private void CalculateThrottle(float distanceToDestination)
    {
        float speed = carController.CurrentSpeed;

        // Угол к следующей точке для определения резкости поворота
        Vector3 dirToWaypoint = (pathCorners[currentWaypointIndex] - transform.position).normalized;
        float angleToWaypoint = Vector3.Angle(transform.forward, dirToWaypoint);

        // Замедление на поворотах
        bool isSharpTurn = angleToWaypoint > sharpTurnAngle;
        float turnSlowdown = isSharpTurn
            ? Mathf.Lerp(1f, cornerSlowdownFactor, (angleToWaypoint - sharpTurnAngle) / 90f)
            : 1f;

        // Замедление при подъезде к цели
        float approachSlowdown = 1f;
        if (distanceToDestination < approachDistance)
            approachSlowdown = Mathf.Lerp(minApproachSpeed, 1f,
                                          distanceToDestination / approachDistance);

        float targetThrottle = Mathf.Min(turnSlowdown, approachSlowdown);

        // Торможение если поворот очень резкий и скорость высокая
        if (isSharpTurn && speed > 15f)
        {
            brakeInput = Mathf.Lerp(0f, 0.8f, (speed - 15f) / 15f);
            motorInput = 0f;
        }
        else
        {
            brakeInput = 0f;
            motorInput = targetThrottle;
        }
    }

    // ──────────────────────────────────────────────
    //  Система застревания
    // ──────────────────────────────────────────────

    private void CheckIfStuck()
    {
        if (!hasPath || reachedDestination) return;

        stuckTimer += Time.fixedDeltaTime;

        if (stuckTimer >= stuckCheckInterval)
        {
            float movedDistance = Vector3.Distance(transform.position, lastPosition);

            if (movedDistance < stuckThreshold && allowReversing)
            {
                Debug.Log("[CarNav] Машина застряла! Начинаем задний ход...");
                StartReversing();
            }

            lastPosition = transform.position;
            stuckTimer = 0f;
        }
    }

    private void StartReversing()
    {
        isReversing = true;
        reverseTimer = 0f;
    }

    private void HandleReversing()
    {
        reverseTimer += Time.fixedDeltaTime;

        // Задний ход с рулёжкой в сторону
        motorInput = -0.7f;
        steerInput = -0.5f; // поворачиваем при реверсе
        brakeInput = 0f;

        if (reverseTimer >= reverseTime)
        {
            isReversing = false;
            stuckTimer = 0f;
            lastPosition = transform.position;

            // Пересчитываем путь после выезда
            if (target != null)
                SetDestination(target.position);

            Debug.Log("[CarNav] Задний ход завершён, пересчитываем маршрут.");
        }
    }

    // ──────────────────────────────────────────────
    //  Вспомогательное
    // ──────────────────────────────────────────────

    private void StopCar()
    {
        motorInput = 0f;
        steerInput = 0f;
        brakeInput = 1f;
    }

    // ──────────────────────────────────────────────
    //  Gizmos
    // ──────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || pathCorners == null || pathCorners.Length == 0) return;

        // Путь
        Gizmos.color = Color.cyan;
        for (int i = 1; i < pathCorners.Length; i++)
            Gizmos.DrawLine(pathCorners[i - 1], pathCorners[i]);

        // Точки пути
        Gizmos.color = Color.yellow;
        foreach (var corner in pathCorners)
            Gizmos.DrawSphere(corner, 0.3f);

        // Текущая целевая точка
        if (currentWaypointIndex < pathCorners.Length)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(pathCorners[currentWaypointIndex], 0.5f);
        }

        // Look-ahead
        if (Application.isPlaying && hasPath)
        {
            Gizmos.color = Color.green;
            Vector3 lookAhead = GetLookAheadPoint();
            Gizmos.DrawSphere(lookAhead, 0.4f);
            Gizmos.DrawLine(transform.position, lookAhead);
        }

        // Зона достижения цели
        if (target != null)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
            Gizmos.DrawSphere(target.position, destinationReachDistance);
        }
    }
}