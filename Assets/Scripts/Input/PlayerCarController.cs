using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using static UnityEngine.ParticleSystem;
using UnityEngine.Android;

[RequireComponent(typeof(Rigidbody))]
public class PlayerCarController : NetworkBehaviour
{
    [Header("Wheels Colliders")]
    public WheelCollider wheelFL;
    public WheelCollider wheelFR;
    public WheelCollider wheelRL;
    public WheelCollider wheelRR;

    [Header("Wheels Visuals (передние — раздельные)")]
    [SerializeField] private Transform visualFL;
    [SerializeField] private Transform visualFR;

    [Header("Wheels Visuals (задние — раздельные)")]
    [SerializeField] private Transform visualRL;
    [SerializeField] private Transform visualRR;

    [Header("Settings")]
    [SerializeField] private float motorForce = 1500f;
    [SerializeField] private float maxSteerAngle = 30f;

    [Tooltip("Скорость падения \"оборотов\" при отпускании кнопок")]
    [SerializeField] private float slowdownOnReleaseSpeed = 30f;

    [Header("Speed-Sensitive Steering")]
    [SerializeField] private float steerSpeedReference = 25f;
    [SerializeField] private float minSteerAngleFactor = 0.35f;

    [Header("Boost (Shift)")]
    [SerializeField] private float boostMultiplier = 2f;
    [SerializeField] private float boostMaxSpeed = 35f;

    [Header("Handbrake (Spacebar)")]
    [SerializeField] private float handbrakeTorque = 3000f;

    [Header("Smart Brake / Reverse")]
    [Tooltip("Порог скорости вдоль корпуса (м/с), выше которого противоположный ввод трактуется как торможение, а не реверс")]
    [SerializeField] private float brakeToReverseThreshold = 0.5f;
    [Tooltip("Сила торможения, когда ввод противоположен текущему направлению движения")]
    [SerializeField] private float brakeForce = 4000f;

    [Header("Steering Smoothing")]
    [SerializeField] private float steerSpeed = 10f;

    [SerializeField] private float downforceAmount = 10f;

    [Range(0f, 1f)]
    [SerializeField] private float droppedStiffness = .5f;

    [Header("Wheel Visual Sync (клиентская отрисовка)")]
    [Tooltip("Множитель для перевода RPM WheelCollider'а в градусы/сек визуального вращения")]
    [SerializeField] private float rpmToDegreesPerSecond = 6f;

    [Header("Engine Sound")]
    [SerializeField] private AudioClip engineSound;
    [SerializeField] private AudioSource engineAudioSource;
    [SerializeField] private float engineSoundSmoothSpeed = 4f;
    [SerializeField] private float maxWheelRpmForFullSound = 600f;
    [SerializeField] private float engineMinPitch = 0f;
    [SerializeField] private float engineMaxPitch = 1f;
    [SerializeField] private float engineMinVolume = 0f;
    [SerializeField] private float engineMaxVolume = 1f;

    // ─────────────────────────────────────────────────────────────────────────
    [Header("Drift Sound")]
    [SerializeField] ParticleSystem ParticlesSys;
    [SerializeField] ParticleSystem ParticlesSys2;
    ParticleSystem.EmissionModule Particles;
    ParticleSystem.EmissionModule Particles2;
    [Tooltip("Аудиоклип звука дрифта / визга шин (луп)")]
    [SerializeField] private AudioClip driftSound;

    [Tooltip("AudioSource для звука дрифта (если не задан — добавится автоматически)")]
    [SerializeField] private AudioSource driftAudioSource;

    [Tooltip("Минимальный угол скольжения (градусы) между вектором скорости и осью корпуса, " +
             "при котором считаем, что машина дрифтует")]
    [SerializeField] private float driftAngleThreshold = 15f;

    [Tooltip("Минимальная скорость (м/с), при которой вообще возможен дрифт " +
             "(исключает медленное маневрирование)")]
    [SerializeField] private float driftMinSpeed = 3f;

    [Tooltip("Насколько быстро громкость/pitch звука дрифта догоняют целевое значение")]
    [SerializeField] private float driftSoundSmoothSpeed = 6f;

    [Tooltip("Pitch звука дрифта при минимальном скольжении")]
    [SerializeField] private float driftMinPitch = 0.8f;

    [Tooltip("Pitch звука дрифта при максимальном скольжении")]
    [SerializeField] private float driftMaxPitch = 1.2f;

    [Tooltip("Максимальный угол скольжения, при котором pitch достигает driftMaxPitch")]
    [SerializeField] private float driftMaxAngleForPitch = 60f;
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Player's Input node")]
    [SerializeField] private PlayerInput playerInput;

    [Header("Rigidbody's center offset")]
    [SerializeField] private Vector3 centerOfMass = new(0f, -0.3f, 0f);

    private Rigidbody rb;

    private float moveInput;
    private float steerInput;
    private bool isBoosting;
    private bool brakeHeld;

    private float currentSteerAngle;

    private readonly NetworkVariable<float> netSteerAngle = new(
        writePerm: NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netFrontRpm = new(
        writePerm: NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netRearRpm = new(
        writePerm: NetworkVariableWritePermission.Server);

    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Угол скольжения (градусы) — реплицируется на все клиенты для воспроизведения
    /// звука дрифта без дополнительных вычислений на клиентах.
    /// </summary>
    private readonly NetworkVariable<float> netDriftAngle = new(
        writePerm: NetworkVariableWritePermission.Server);
    // ─────────────────────────────────────────────────────────────────────────

    private float visualSpinFront;
    private float visualSpinRear;

    private float currentEnginePitch;
    private float currentEngineVolume;

    // ─────────────────────────────────────────────────────────────────────────
    // Локальные сглаженные значения для звука дрифта
    private float currentDriftVolume;
    private float currentDriftPitch;
    // ─────────────────────────────────────────────────────────────────────────

    private bool isBotControlled;
    public bool IsBotControlled => isBotControlled;

    public float CurrentSpeed => rb != null ? rb.linearVelocity.magnitude * 3.6f : 0f;

    private void Awake()
    {
        Particles = ParticlesSys.emission;
        Particles2 = ParticlesSys2.emission;
        rb = GetComponent<Rigidbody>();
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();

        // ── Engine AudioSource ──────────────────────────────────────────────
        if (engineAudioSource == null)
            engineAudioSource = gameObject.AddComponent<AudioSource>();

        engineAudioSource.clip = engineSound;
        engineAudioSource.loop = true;
        engineAudioSource.playOnAwake = false;
        engineAudioSource.spatialBlend = 1f;

        // ── Drift AudioSource ───────────────────────────────────────────────
        if (driftAudioSource == null)
            driftAudioSource = gameObject.AddComponent<AudioSource>();

        driftAudioSource.clip = driftSound;
        driftAudioSource.loop = true;
        driftAudioSource.playOnAwake = false;
        driftAudioSource.spatialBlend = 1f;
        driftAudioSource.volume = 0f; // стартуем с тишины, плавно нарастим
    }

    public void ConfigureAsBot() => isBotControlled = true;

    public void SetBotInputs(float motor, float steer, bool brake)
    {
        if (!IsServer) return;
        moveInput = Mathf.Clamp(motor, -1f, 1f);
        steerInput = Mathf.Clamp(steer, -1f, 1f);
        brakeHeld = brake;
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner && !isBotControlled)
            playerInput.actions.Enable();

        if (IsServer)
        {
            rb.isKinematic = false;
            rb.centerOfMass = centerOfMass;
        }
        else
        {
            rb.isKinematic = true;
            wheelFL.enabled = false;
            wheelFR.enabled = false;
            wheelRL.enabled = false;
            wheelRR.enabled = false;
        }

        // Запускаем оба AudioSource; громкость дрифта = 0 до момента скольжения
        if (engineSound != null && !engineAudioSource.isPlaying)
        {
            engineAudioSource.volume = engineMinVolume;
            engineAudioSource.pitch = Mathf.Max(engineMinPitch, 0.01f);
            engineAudioSource.Play();
        }

        if (driftSound != null && !driftAudioSource.isPlaying)
        {
            
            driftAudioSource.volume = 0f;
            driftAudioSource.Play();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && !isBotControlled)
            playerInput.actions.Disable();

        engineAudioSource?.Stop();
        driftAudioSource?.Stop();
    }

    // ── Input ────────────────────────────────────────────────────────────────

    void Update()
    {
        UpdateWheelVisualsFromNetwork(Time.deltaTime);
        UpdateEngineSound(Time.deltaTime);
        UpdateDriftSound(Time.deltaTime);   // ← новый вызов
    }

    public void OnSteer(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        UpdateSteeringServerRpc(context.ReadValue<float>());
    }

    public void OnAccelerate(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        UpdateAccelerationServerRpc(context.ReadValue<float>());
    }

    public void OnBrake(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (context.performed) UpdateBrakeServerRpc(true);
        else if (context.canceled) UpdateBrakeServerRpc(false);
    }

    public void OnBoost(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (context.performed) UpdateBoostServerRpc(true);
        else if (context.canceled) UpdateBoostServerRpc(false);
    }

    [ServerRpc] private void UpdateSteeringServerRpc(float v) => steerInput = v;
    [ServerRpc] private void UpdateAccelerationServerRpc(float v) => moveInput = v;
    [ServerRpc] private void UpdateBrakeServerRpc(bool v) => brakeHeld = v;
    [ServerRpc] private void UpdateBoostServerRpc(bool v) => isBoosting = v;

    // ── Physics (Server only) ────────────────────────────────────────────────

    void FixedUpdate()
    {
        if (!IsServer) return;

        float currentForce = motorForce;
        if (isBoosting && rb.linearVelocity.magnitude < boostMaxSpeed)
            currentForce *= boostMultiplier;

        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);

        bool wantsForward = moveInput > 0.01f;
        bool wantsReverse = moveInput < -0.01f;

        bool brakingAgainstMotion =
            (wantsReverse && forwardSpeed > brakeToReverseThreshold) ||
            (wantsForward && forwardSpeed < -brakeToReverseThreshold);

        float motorBrakeTorque = 0f;
        float appliedMotorTorque = moveInput * currentForce;

        if (brakingAgainstMotion)
        {
            motorBrakeTorque = Mathf.Abs(moveInput) * brakeForce;
            appliedMotorTorque = 0f;
        }

        wheelFL.motorTorque = appliedMotorTorque;
        wheelFR.motorTorque = appliedMotorTorque;
        wheelFL.brakeTorque = motorBrakeTorque;
        wheelFR.brakeTorque = motorBrakeTorque;

        float brake = brakeHeld ? handbrakeTorque : 0f;
        wheelRL.brakeTorque = brake;
        wheelRR.brakeTorque = brake;

        if (brakeHeld && moveInput == 0f)
        {
            wheelRL.brakeTorque = slowdownOnReleaseSpeed;
            wheelRR.brakeTorque = slowdownOnReleaseSpeed;
            wheelFL.brakeTorque = Mathf.Max(wheelFL.brakeTorque, slowdownOnReleaseSpeed);
            wheelFR.brakeTorque = Mathf.Max(wheelFR.brakeTorque, slowdownOnReleaseSpeed);
        }

        ApplyStiffness(wheelRL, brakeHeld);
        ApplyStiffness(wheelRR, brakeHeld);

        float speedFactor = brakeHeld
            ? 1f
            : Mathf.Lerp(1f, minSteerAngleFactor,
                Mathf.Clamp01(rb.linearVelocity.magnitude / steerSpeedReference));

        float targetSteerAngle = steerInput * maxSteerAngle * speedFactor;
        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle,
                                       steerSpeed * Time.fixedDeltaTime);
        wheelFL.steerAngle = currentSteerAngle;
        wheelFR.steerAngle = currentSteerAngle;

        rb.AddForce(Vector3.down * downforceAmount * rb.linearVelocity.sqrMagnitude,
                    ForceMode.Force);

        // ── Публикуем сетевые переменные ────────────────────────────────────
        netSteerAngle.Value = currentSteerAngle;
        netFrontRpm.Value = (wheelFL.rpm + wheelFR.rpm) * 0.5f;
        netRearRpm.Value = (wheelRL.rpm + wheelRR.rpm) * 0.5f;

        // ── Вычисляем угол дрифта и публикуем его ───────────────────────────
        netDriftAngle.Value = CalculateDriftAngle();
        if (IsDrifting)
        {
            Particles.rateOverTime = 200;
            Particles2.rateOverTime = 200;
        }
        else {
            Particles.rateOverTime = 0;
            Particles2.rateOverTime = 0;
        }
    }

    // ── Drift detection ──────────────────────────────────────────────────────

    /// <summary>
    /// Возвращает угол (градусы) между вектором скорости и продольной осью машины.
    /// Вызывается только на сервере в FixedUpdate.
    /// </summary>
    private float CalculateDriftAngle()
    {
        float speed = rb.linearVelocity.magnitude;

        // При слишком малой скорости угол ненадёжен — считаем 0
        if (speed < driftMinSpeed)
            return 0f;

        Vector3 velocityDir = rb.linearVelocity / speed;

        // Определяем, движемся ли мы преимущественно вперёд или назад,
        // и сравниваем угол скольжения именно с той продольной осью,
        // вдоль которой едем. Так простая езда задним ходом (скорость
        // почти точно вдоль -forward) даёт угол ~0, а не ~90/180.
        float forwardDot = Vector3.Dot(velocityDir, transform.forward);
        Vector3 referenceDir = forwardDot >= 0f ? transform.forward : -transform.forward;

        // Угол между вектором скорости и ближайшей продольной осью корпуса.
        // Уже гарантированно в диапазоне [0, 90].
        float angle = Vector3.Angle(velocityDir, referenceDir);

        return angle;
    }

    /// <summary>
    /// Определяет, дрифтует ли машина, на основе реплицированного угла скольжения.
    /// Можно вызывать с любого клиента.
    /// </summary>
    public bool IsDrifting => netDriftAngle.Value >= driftAngleThreshold;

    // ── Drift sound (каждый клиент, каждый Update) ───────────────────────────

    /// <summary>
    /// Плавно включает/выключает и регулирует звук дрифта в зависимости от
    /// реплицированного угла скольжения.
    /// </summary>
    private void UpdateDriftSound(float deltaTime)
    {
        if (driftAudioSource == null || driftSound == null) return;

        float driftAngle = netDriftAngle.Value;
        bool drifting = driftAngle >= driftAngleThreshold;

        // Целевая громкость: 1 при дрифте, 0 без него
        float targetVolume = drifting ? 1f : 0f;

        // Целевой pitch зависит от «интенсивности» скольжения
        float driftIntensity = drifting
            ? Mathf.Clamp01((driftAngle - driftAngleThreshold) /
                            Mathf.Max(driftMaxAngleForPitch - driftAngleThreshold, 1f))
            : 0f;
        float targetPitch = Mathf.Lerp(driftMinPitch, driftMaxPitch, driftIntensity);

        // Плавно подгоняем текущие значения к целевым
        currentDriftVolume = Mathf.Lerp(currentDriftVolume, targetVolume,
                                        driftSoundSmoothSpeed * deltaTime);
        currentDriftPitch = Mathf.Lerp(currentDriftPitch, targetPitch,
                                        driftSoundSmoothSpeed * deltaTime);

        driftAudioSource.volume = currentDriftVolume;
        driftAudioSource.pitch = Mathf.Max(currentDriftPitch, 0.01f);
    }

    // ── Wheel visuals ────────────────────────────────────────────────────────

    private void UpdateWheelVisualsFromNetwork(float deltaTime)
    {
        visualSpinFront += netFrontRpm.Value * rpmToDegreesPerSecond * deltaTime;
        visualSpinRear += netRearRpm.Value * rpmToDegreesPerSecond * deltaTime;

        float steer = netSteerAngle.Value;
        ApplyWheelVisual(visualFL, steer, visualSpinFront);
        ApplyWheelVisual(visualFR, steer, visualSpinFront);
        ApplyWheelVisual(visualRL, 0f, visualSpinRear);
        ApplyWheelVisual(visualRR, 0f, visualSpinRear);
    }

    private void ApplyWheelVisual(Transform visual, float steerAngle, float spinAngle)
    {
        if (visual == null) return;
        visual.localRotation =
            Quaternion.Euler(0f, steerAngle, 0f) * Quaternion.Euler(spinAngle, 0f, 0f);
    }

    private void ApplyStiffness(WheelCollider collider, bool flag)
    {
        var sidewaysFriction = collider.sidewaysFriction;
        float target = flag ? droppedStiffness : 1f;
        sidewaysFriction.stiffness = Mathf.Lerp(sidewaysFriction.stiffness, target,
                                                 Time.fixedDeltaTime);
        collider.sidewaysFriction = sidewaysFriction;
    }

    // ── Engine sound ─────────────────────────────────────────────────────────

    private void UpdateEngineSound(float deltaTime)
    {
        if (engineAudioSource == null || engineSound == null) return;

        float wheelRpm = Mathf.Abs(netFrontRpm.Value);
        float intensity = maxWheelRpmForFullSound > 0f
            ? Mathf.Clamp01(wheelRpm / maxWheelRpmForFullSound)
            : 0f;

        float targetPitch = Mathf.Lerp(engineMinPitch, engineMaxPitch, intensity);
        float targetVolume = Mathf.Lerp(engineMinVolume, engineMaxVolume, intensity);

        currentEnginePitch = Mathf.Lerp(currentEnginePitch, targetPitch,
                                         engineSoundSmoothSpeed * deltaTime);
        currentEngineVolume = Mathf.Lerp(currentEngineVolume, targetVolume,
                                         engineSoundSmoothSpeed * deltaTime);

        engineAudioSource.pitch = Mathf.Max(currentEnginePitch, 0.01f);
        engineAudioSource.volume = currentEngineVolume;
    }
}