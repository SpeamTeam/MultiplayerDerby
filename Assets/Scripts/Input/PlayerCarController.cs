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

    private Rigidbody rb;
    private float moveInput;
    private float steerInput;
    private bool isBraking;
    private bool isBoosting;

    private float currentSteerAngle; // Сглаженный текущий угол поворота

    private CarControls inputActions;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        inputActions = new CarControls();
    }

    private void OnEnable()
    {
        if (!IsOwner)
            inputActions.Enable();
    }
    private void OnDisable()
    {
        if (!IsOwner)
            inputActions.Disable();
    }

    void Start()
    {
        rb.centerOfMass = new Vector3(0f, -0.3f, 0f); // WTF?
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
        UpdateWheelVisual(wheelFL, visualFL);
        UpdateWheelVisual(wheelFR, visualFR);
        UpdateWheelVisual(wheelRL, visualRL);
        UpdateWheelVisual(wheelRR, visualRR);
    }

    void FixedUpdate()
    {
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
    }

    private void UpdateWheelVisual(WheelCollider col, Transform visual)
    {
        if (visual == null) return;

        // col.GetWorldPose(out Vector3 position, out Quaternion rotation);
        // visual.position = position;
        // visual.rotation = rotation;
    }
}