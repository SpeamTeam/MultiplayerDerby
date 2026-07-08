using UnityEngine;
using UnityEngine.InputSystem;

public class CarController : MonoBehaviour
{
    [Header("Wheels Colliders")]
    public WheelCollider wheelFL;
    public WheelCollider wheelFR;
    public WheelCollider wheelRL;
    public WheelCollider wheelRR;

    [Header("Wheels Visuals")]
    public Transform visualFL;
    public Transform visualFR;
    public Transform visualRL;
    public Transform visualRR;

    [Header("Settings")]
    public float motorForce = 1500f;
    public float maxSteerAngle = 30f;
    public float brakeForce = 3000f;

    [Header("Boost (Shift)")]
    [Tooltip("Во сколько раз больше тяги при зажатом Shift")]
    public float boostMultiplier = 2f;
    [Tooltip("Максимальная скорость (м/с), выше которой буст перестаёт добавлять тягу — чтобы не разгонялось бесконечно")]
    public float boostMaxSpeed = 35f;

    [Header("Steering Smoothing")] // Настройки плавности руля
    public float steerSpeed = 10f; // Чем меньше значение, тем медленнее крутится руль

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

    private void OnEnable() => inputActions.Enable();
    private void OnDisable() => inputActions.Disable();

    void Start()
    {
        rb.centerOfMass = new Vector3(0f, -0.3f, 0f);
    }

    void Update()
    {
        moveInput = inputActions.Driving.Accelerate.ReadValue<float>();
        steerInput = inputActions.Driving.Steer.ReadValue<float>();
        isBraking = inputActions.Driving.Brake.IsPressed();

        // Буст читаем напрямую с клавиатуры (не через Input asset) — быстрый путь.
        // Keyboard.current может быть null (нет клавиатуры) — поэтому проверка.
        isBoosting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

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
        if (isBoosting && rb.linearVelocity.magnitude < boostMaxSpeed)
            currentForce *= boostMultiplier;

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

        col.GetWorldPose(out Vector3 position, out Quaternion rotation);
        visual.position = position;
        visual.rotation = rotation;
    }
}