using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class PlayerCarController : NetworkBehaviour
{
    [Header("Wheels Colliders")]
    [SerializeField] private WheelCollider wheelFL;
    [SerializeField] private WheelCollider wheelFR;
    [SerializeField] private WheelCollider wheelRL;
    [SerializeField] private WheelCollider wheelRR;

    [Header("Wheels Visuals")]
    [SerializeField] private Transform visualFL;
    [SerializeField] private Transform visualFR;
    [SerializeField] private Transform visualRL;
    [SerializeField] private Transform visualRR;

    [Header("Settings")]
    [SerializeField] private float motorForce = 1500f;
    [SerializeField] private float maxSteerAngle = 30f;
    [SerializeField] private float brakeForce = 3000f;

    [Header("Boost (Shift)")]
    [Tooltip("Во сколько раз больше тяги при зажатом Shift")]
    [SerializeField] private float boostMultiplier = 2f;
    [Tooltip("Максимальная скорость (м/с), выше которой буст перестаёт добавлять тягу — чтобы не разгонялось бесконечно")]
    [SerializeField] private float boostMaxSpeed = 35f;

    [Header("Steering Smoothing")] // Настройки плавности руля
    [SerializeField] private float steerSpeed = 10f; // Чем меньше значение, тем медленнее крутится руль

    public NetworkVariable<float> NetSteerAngle = new(writePerm: NetworkVariableWritePermission.Owner);
    public NetworkVariable<float> NetWheelRpm   = new(writePerm: NetworkVariableWritePermission.Owner);

    private Rigidbody rb;
    private float moveInput;
    private float steerInput;
    private bool isBraking;

    private float currentSteerAngle; // Сглаженный текущий угол поворота

    private CarControls inputActions;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        inputActions = new CarControls();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
            inputActions.Enable();
    }
    public override void OnNetworkDespawn()
    {
        if (IsOwner)
            inputActions.Disable();
    }

    void Start()
    {
        
        // rb.centerOfMass = new Vector3(0f, -0.3f, 0f); // WTF?
    }

    public void OnAccelerate(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<float>();
    }

    public void OnSteer(InputAction.CallbackContext context)
    {
        steerInput = context.ReadValue<float>();
    }

    public void OnBrake(InputAction.CallbackContext context)
    {
        if (context.performed)
            isBraking = true;
        else if (context.canceled)
            isBraking = false;
    }

    void Update()
    {
        float steerAngle = IsOwner ? currentSteerAngle : NetSteerAngle.Value;
        UpdateWheelVisual(wheelFL, visualFL, steerAngle);
        UpdateWheelVisual(wheelFR, visualFR, steerAngle);
        UpdateWheelVisual(wheelRL, visualRL, steerAngle);
        UpdateWheelVisual(wheelRR, visualRR, steerAngle);
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;
        // --- ТЯГА С УЧЁТОМ БУСТА ---
        // Буст добавляет тягу, только пока скорость ниже boostMaxSpeed —
        // иначе на WheelCollider машину разгоняло бы неограниченно.
        float currentForce = motorForce;

        wheelRL.motorTorque = moveInput * currentForce;
        wheelRR.motorTorque = moveInput * currentForce;

        // --- Плавный поворот руля ---
        float targetSteerAngle = steerInput * maxSteerAngle;
        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, steerSpeed * Time.fixedDeltaTime);
        wheelFL.steerAngle = currentSteerAngle;
        wheelFR.steerAngle = currentSteerAngle;

        // --- Тормоз ---
        float brake = isBraking ? brakeForce : 0f;
        wheelFL.brakeTorque = brake;
        wheelFR.brakeTorque = brake;
        wheelRL.brakeTorque = brake;
        wheelRR.brakeTorque = brake;

        NetSteerAngle.Value = currentSteerAngle;
    }

    private void UpdateWheelVisual(WheelCollider col, Transform visual, float steerAngle)
    {
        if (visual == null) return;
    //  if (!IsOwner) { 
    //      col.steerAngle = steerAngle;
    //  }

        Quaternion steerRotation = Quaternion.AngleAxis(steerAngle, transform.up);

        col.GetWorldPose(out Vector3 position, out Quaternion rotation);
        visual.position = position;
        visual.rotation = transform.rotation * steerRotation;
    }
}