using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class PlayerCarController : MonoBehaviour
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

    [Header("Steering Smoothing")] // НОВОЕ: Настройки плавности руля
    public float steerSpeed = 10f; // Чем меньше значение, тем медленнее крутится руль

    private Rigidbody rb;
    private float moveInput;
    private float steerInput;
    private bool isBraking;

    private float currentSteerAngle; // НОВОЕ: Запоминаем текущий угол поворота

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

    public void OnBrake(bool value)
    {
        isBraking = value;
    }

    void Update()
    {
        moveInput = inputActions.Driving.Accelerate.ReadValue<float>();
        steerInput = inputActions.Driving.Steer.ReadValue<float>();
        isBraking = inputActions.Driving.Brake.IsPressed();

        UpdateWheelVisual(wheelFL, visualFL);
        UpdateWheelVisual(wheelFR, visualFR);
        UpdateWheelVisual(wheelRL, visualRL);
        UpdateWheelVisual(wheelRR, visualRR);
    }

    void FixedUpdate()
    {
        SteerAndWheelServerRPC();
    }

    [ServerRpc]
    void SteerAndWheelServerRPC()
    {
        // Газ
        wheelRL.motorTorque = moveInput * motorForce;
        wheelRR.motorTorque = moveInput * motorForce;

        // НОВОЕ: Плавный поворот руля
        // 1. Вычисляем желаемый угол (куда игрок хочет повернуть)
        float targetSteerAngle = steerInput * maxSteerAngle;

        // 2. Плавно меняем текущий угол в сторону желаемого
        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, steerSpeed * Time.fixedDeltaTime);

        // 3. Применяем сглаженный угол к колёсам
        wheelFL.steerAngle = currentSteerAngle;
        wheelFR.steerAngle = currentSteerAngle;

        // Тормоз
        if (isBraking)
        {
            wheelFL.brakeTorque = brakeForce;
            wheelFR.brakeTorque = brakeForce;
            wheelRL.brakeTorque = brakeForce;
            wheelRR.brakeTorque = brakeForce;
        }
        else
        {
            wheelFL.brakeTorque = 0f;
            wheelFR.brakeTorque = 0f;
            wheelRL.brakeTorque = 0f;
            wheelRR.brakeTorque = 0f;
        }
    }

    private void UpdateWheelVisual(WheelCollider col, Transform visual)
    {
        if (visual == null) return;

        col.GetWorldPose(out Vector3 position, out Quaternion rotation);
        visual.position = position;
        visual.rotation = rotation;
    }
}

