using UnityEngine;

/// <summary>
/// Скрипт камеры для Demolition Derby.
/// Обеспечивает плавное следование, динамический FOV, тряску при ударах,
/// защиту от переворотов (Flip Handling), Look Ahead и коллизию со стенами.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Header("Цель слежения")]
    [Tooltip("Rigidbody машины (нужен для чтения скорости)")]
    public Rigidbody targetRb;
    [Tooltip("Transform машины (обычно targetRb.transform)")]
    public Transform target;

    [Header("1. Позиционирование и сглаживание")]
    public float distance = 6f;
    public float height = 2.5f;
    [Tooltip("Время сглаживания позиции камеры")]
    public float positionSmoothTime = 0.15f;
    [Tooltip("Скорость сглаживания поворота камеры")]
    public float rotationSmoothSpeed = 8f;

    [Header("2. Динамический FOV")]
    public float baseFOV = 60f;
    public float maxSpeedFOV = 75f;
    [Tooltip("Скорость (м/с), при которой FOV достигает максимума")]
    public float maxSpeedForFOV = 40f;
    [Tooltip("Дополнительный FOV при ударе")]
    public float impactFOVKick = 8f;
    public float fovDampSpeed = 4f;

    [Header("3. Тряска камеры")]
    public float shakeMagnitudeMultiplier = 0.05f;
    public float maxShakeMagnitude = 0.8f;
    public float shakeDuration = 0.5f;
    [Tooltip("Частота Перлин-шума для тряски")]
    public float shakeFrequency = 20f;

    [Header("4. Обработка переворотов")]
    [Tooltip("Высота точки фокуса на машине (0 = центр, выше = ближе к крыше)")]
    public float lookHeightRatio = 0.3f;

    [Header("5. Look Ahead (упреждение фокуса)")]
    public float lookAheadAmount = 2f;
    public float lookAheadSmoothTime = 0.3f;

    [Header("6. Коллизия камеры")]
    [Tooltip("Слой стен арены (НЕ включайте сюда слой машин!)")]
    public LayerMask collisionMask;
    public float cameraRadius = 0.3f;
    public float collisionBuffer = 0.2f;

    // ==================== Внутренние переменные ====================

    private Camera cam;

    private Vector3 currentPosVelocity;      // для SmoothDamp позиции
    private Vector3 currentLookAheadVelocity; // для SmoothDamp look-ahead
    private Vector3 currentLookAheadOffset;

    private float currentShakeTimer;
    private float currentShakeMagnitude;
    private float shakeSeedX, shakeSeedY;

    private float currentFOVVelocity;

    // ==================== Инициализация ====================

    private void Awake()
    {
        cam = GetComponent<Camera>();

        // Уникальные сиды для шума, чтобы тряска по X и Y не была синхронной
        shakeSeedX = Random.Range(0f, 100f);
        shakeSeedY = Random.Range(0f, 100f);

        // Автопоиск target, если не назначен вручную
        if (targetRb != null && target == null)
            target = targetRb.transform;
    }

    private void LateUpdate()
    {
        if (target == null || targetRb == null) return;

        HandlePositionAndRotation();
        UpdateFOV();
        ApplyShake();
    }

    // ==================== Блок 1 + 4 + 5 + 6: Позиция и поворот ====================

    private void HandlePositionAndRotation()
    {
        // --- БЛОК 4: FLIP HANDLING ---
        // Убираем компонент Y из forward-вектора машины, чтобы получить только Yaw.
        // Это гарантирует, что при перевороте машины камера НЕ будет крениться вслед за ней.
        Vector3 flatForward = target.forward;
        flatForward.y = 0f;

        // Fallback на случай, если машина стоит "свечкой" (forward смотрит вверх/вниз)
        if (flatForward.sqrMagnitude < 0.01f)
        {
            flatForward = target.up;
            flatForward.y = 0f;
        }
        flatForward.Normalize();

        Quaternion yawRotation = Quaternion.LookRotation(flatForward, Vector3.up);

        // --- БЛОК 5: LOOK AHEAD ---
        // Смещаем точку фокуса вбок в зависимости от бокового скольжения (заноса) машины
        Vector3 localVelocity = target.InverseTransformDirection(targetRb.linearVelocity);
        float lateralInput = Mathf.Clamp(localVelocity.x / 10f, -1f, 1f);

        Vector3 targetLookAhead = target.right * lateralInput * lookAheadAmount;
        currentLookAheadOffset = Vector3.SmoothDamp(
            currentLookAheadOffset,
            targetLookAhead,
            ref currentLookAheadVelocity,
            lookAheadSmoothTime
        );

        // --- БЛОК 1: Расчёт желаемой позиции камеры (сзади-сверху) ---
        Vector3 desiredPosition = target.position
                                   + yawRotation * (Vector3.back * distance)
                                   + Vector3.up * height;

        // --- БЛОК 6: КОЛЛИЗИЯ КАМЕРЫ ---
        Vector3 pivotPoint = target.position + Vector3.up * (height * 0.5f);
        Vector3 directionToCamera = desiredPosition - pivotPoint;
        float desiredDistance = directionToCamera.magnitude;
        directionToCamera.Normalize();

        if (Physics.SphereCast(pivotPoint, cameraRadius, directionToCamera,
                out RaycastHit hit, desiredDistance, collisionMask))
        {
            // Камера уперлась в стену — подтягиваем её ближе к машине
            float adjustedDistance = Mathf.Max(hit.distance - collisionBuffer, 0.5f);
            desiredPosition = pivotPoint + directionToCamera * adjustedDistance;
        }

        // Плавное перемещение камеры без рывков
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref currentPosVelocity,
            positionSmoothTime
        );

        // --- Поворот камеры (игнорируя крен/тангаж машины) ---
        Vector3 lookAtTarget = target.position + Vector3.up * (height * lookHeightRatio) + currentLookAheadOffset;
        Vector3 lookDir = (lookAtTarget - transform.position);

        if (lookDir.sqrMagnitude > 0.001f)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                Time.deltaTime * rotationSmoothSpeed
            );
        }
    }

    // ==================== Блок 2: Динамический FOV ====================

    private void UpdateFOV()
    {
        float speed = targetRb.linearVelocity.magnitude;
        float speedT = Mathf.Clamp01(speed / maxSpeedForFOV);
        float targetFOV = Mathf.Lerp(baseFOV, maxSpeedFOV, speedT);

        // Добавляем затухающий импульс FOV при недавнем ударе
        if (currentShakeTimer > 0f)
        {
            float impactT = currentShakeTimer / shakeDuration;
            targetFOV += impactFOVKick * impactT;
        }

        cam.fieldOfView = Mathf.SmoothDamp(
            cam.fieldOfView,
            targetFOV,
            ref currentFOVVelocity,
            1f / fovDampSpeed
        );
    }

    // ==================== Блок 3: Тряска камеры ====================

    private void ApplyShake()
    {
        if (currentShakeTimer <= 0f) return;

        currentShakeTimer -= Time.deltaTime;
        float damper = Mathf.Clamp01(currentShakeTimer / shakeDuration);

        float offsetX = (Mathf.PerlinNoise(shakeSeedX, Time.time * shakeFrequency) - 0.5f) * 2f;
        float offsetY = (Mathf.PerlinNoise(shakeSeedY, Time.time * shakeFrequency) - 0.5f) * 2f;

        Vector3 shakeOffset = new Vector3(offsetX, offsetY, 0f) * currentShakeMagnitude * damper;

        // Тряска в локальных осях камеры (right/up), чтобы выглядела как экранный шейк
        transform.position += transform.right * shakeOffset.x + transform.up * shakeOffset.y;
    }

    // ==================== Публичный API ====================

    /// <summary>
    /// Вызывается извне (например, из скрипта машины при OnCollisionEnter),
    /// чтобы запустить тряску камеры пропорционально силе удара.
    /// </summary>
    /// <param name="impactForce">Сила удара, обычно collision.relativeVelocity.magnitude</param>
    public void TriggerShake(float impactForce)
    {
        float magnitude = Mathf.Clamp(impactForce * shakeMagnitudeMultiplier, 0f, maxShakeMagnitude);

        // Берём максимум, чтобы слабый удар не "перебивал" уже идущую сильную тряску
        currentShakeMagnitude = Mathf.Max(currentShakeMagnitude, magnitude);
        currentShakeTimer = shakeDuration;
    }

    // ==================== Отладка ====================

    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Vector3 pivotPoint = target.position + Vector3.up * (height * 0.5f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pivotPoint, transform.position);
        Gizmos.DrawWireSphere(transform.position, cameraRadius);
    }
}