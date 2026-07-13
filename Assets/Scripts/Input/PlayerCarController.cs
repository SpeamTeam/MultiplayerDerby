using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

[RequireComponent(typeof(PlayerInput))]
[RequireComponent(typeof(Rigidbody))]
public class PlayerCarController : NetworkBehaviour
{
    [Header("Wheels Colliders")]
    [SerializeField] private WheelCollider wheelFL;
    [SerializeField] private WheelCollider wheelFR;
    [SerializeField] private WheelCollider wheelRL;
    [SerializeField] private WheelCollider wheelRR;

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

    [Header("Steering Smoothing")]
    [SerializeField] private float steerSpeed = 10f;

    [SerializeField] private float downforceAmount = 10f;

    [Range(0f, 1f)]
    [SerializeField] private float droppedStiffness = .5f;

    [Header("Wheel Visual Sync (клиентская отрисовка)")]
    [Tooltip("Множитель для перевода RPM WheelCollider'а в градусы/сек визуального вращения")]
    [SerializeField] private float rpmToDegreesPerSecond = 6f; // rpm * 6 = deg/sec

    [Header("Player's Input node")]
    [SerializeField] private PlayerInput playerInput;

    [Header("Rigidbody's center offset")]
    [SerializeField] private Vector3 centerOfMass = new(0f, -0.3f, 0f);

    private Rigidbody rb;

    // Ввод — валиден и используется ТОЛЬКО на сервере
    private float moveInput;
    private float steerInput;
    private bool isBoosting;
    private bool brakeHeld;

    private float currentSteerAngle;

    // Реплицируемое состояние для визуала колёс на всех клиентах (включая владельца)
    private readonly NetworkVariable<float> netSteerAngle = new(
        writePerm: NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netFrontRpm = new(
        writePerm: NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netRearRpm = new(
        writePerm: NetworkVariableWritePermission.Server);

    // Локальные накопители угла вращения для визуала (на клиентах)
    private float visualSpinFront;
    private float visualSpinRear;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();
    }

    public override void OnNetworkSpawn()
    {
        // Ввод с устройства нужен только владельцу
        if (IsOwner)
            playerInput.actions.Enable();

        // Физику реально двигает только сервер — на остальных инстансах
        // отключаем симуляцию, чтобы не было рассинхрона и лишней нагрузки.
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
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
            playerInput.actions.Disable();
    }

    void Update()
    {
        // Позицию/поворот кузова реплицирует NetworkTransform (server authoritative).
        // Здесь только визуальное вращение/поворот колёс — на КАЖДОМ клиенте,
        // включая владельца, на основе синхронизированных значений с сервера.
        UpdateWheelVisualsFromNetwork(Time.deltaTime);
    }

    // Input Methods {

    public void OnSteer(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        float steeringValue = context.ReadValue<float>();
        UpdateSteeringServerRpc(steeringValue);
    }

    public void OnAccelerate(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        float accelerationValue = context.ReadValue<float>();
        UpdateAccelerationServerRpc(accelerationValue);
    }

    public void OnBrake(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        bool? brakeValue = null;
        if (context.performed) brakeValue = true;
        else if (context.canceled) brakeValue = false;
        if (brakeValue != null) UpdateBrakeServerRpc((bool)brakeValue);
    }

    public void OnBoost(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        if (context.performed) UpdateBoostServerRpc(true);
        else if (context.canceled) UpdateBoostServerRpc(false);
    }

    // Input Methods }

    // Input Server RPC's — выполняются на сервере, ownership уже проверен Netcode'ом {
    [ServerRpc]
    private void UpdateSteeringServerRpc(float steering) => steerInput = steering;

    [ServerRpc]
    private void UpdateAccelerationServerRpc(float acceleration) => moveInput = acceleration;

    [ServerRpc]
    private void UpdateBrakeServerRpc(bool brakeInput) => brakeHeld = brakeInput;

    [ServerRpc]
    private void UpdateBoostServerRpc(bool boosting) => isBoosting = boosting;

    // Input Server RPC's }

    void FixedUpdate()
    {
        // Вся физика — только на сервере.
        if (!IsServer) return;

        float currentForce = motorForce;
        if (isBoosting && rb.linearVelocity.magnitude < boostMaxSpeed)
            currentForce *= boostMultiplier;

        wheelFL.motorTorque = moveInput * currentForce;
        wheelFR.motorTorque = moveInput * currentForce;

        float brake = brakeHeld ? handbrakeTorque : 0f;
        wheelRL.brakeTorque = brake;
        wheelRR.brakeTorque = brake;
        wheelFL.brakeTorque = 0f;
        wheelFR.brakeTorque = 0f;

        if (brakeHeld && moveInput == 0f)
        {
            wheelRL.brakeTorque = slowdownOnReleaseSpeed;
            wheelRR.brakeTorque = slowdownOnReleaseSpeed;
            wheelFL.brakeTorque = slowdownOnReleaseSpeed;
            wheelFR.brakeTorque = slowdownOnReleaseSpeed;
        }

        ApplyStiffness(wheelRL, brakeHeld);
        ApplyStiffness(wheelRR, brakeHeld);

        float speedFactor = brakeHeld
            ? 1f
            : Mathf.Lerp(1f, minSteerAngleFactor, Mathf.Clamp01(rb.linearVelocity.magnitude / steerSpeedReference));
        float targetSteerAngle = steerInput * maxSteerAngle * speedFactor;
        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, steerSpeed * Time.fixedDeltaTime);
        wheelFL.steerAngle = currentSteerAngle;
        wheelFR.steerAngle = currentSteerAngle;

        rb.AddForce(Vector3.down * downforceAmount * rb.linearVelocity.sqrMagnitude, ForceMode.Force);

        // Публикуем состояние для визуала всем клиентам
        netSteerAngle.Value = currentSteerAngle;
        netFrontRpm.Value = (wheelFL.rpm + wheelFR.rpm) * 0.5f;
        netRearRpm.Value = (wheelRL.rpm + wheelRR.rpm) * 0.5f;
    }

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
        visual.localRotation = Quaternion.Euler(0f, steerAngle, 0f) * Quaternion.Euler(spinAngle, 0f, 0f);
    }

    private void ApplyStiffness(WheelCollider collider, bool flag)
    {
        var sidewaysFriction = collider.sidewaysFriction;
        float target = flag ? droppedStiffness : 1f;
        sidewaysFriction.stiffness = Mathf.Lerp(sidewaysFriction.stiffness, target, Time.fixedDeltaTime);
        collider.sidewaysFriction = sidewaysFriction;
    }
}