using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCarController : MonoBehaviour
{
    [Header("Wheels Colliders")]
    public WheelCollider wheelFL;
    public WheelCollider wheelFR;
    public WheelCollider wheelRL;
    public WheelCollider wheelRR;

    [Header("Wheels Visuals (передние — раздельные)")]
    public Transform visualFL;
    public Transform visualFR;

    [Header("Задний общий визуал (Truck_Wheel_AR — только вращение)")]
    [Tooltip("Один меш на обе задние колёса. Крутится по средней скорости задних WheelCollider, позицию не трогаем.")]
    public Transform visualRearShared;
    [Tooltip("Локальная ось вращения заднего меша. Обычно X (1,0,0). Если крутится не так — поменяй на (0,0,1).")]
    public Vector3 rearSpinAxis = Vector3.right;

    [Header("Settings")]
    public float motorForce = 1500f;
    public float maxSteerAngle = 30f;

    [Header("Boost (Shift)")]
    public float boostMultiplier = 2f;
    public float boostMaxSpeed = 35f;

    [Header("Handbrake / Drift (Ctrl)")]
    [Tooltip("Одна кнопка (Ctrl): едешь прямо — тормозит, поворачиваешь — уводит в занос")]
    public float handbrakeTorque = 3000f;
    [Tooltip("Боковое сцепление ЗАДНИХ колёс в обычном режиме (выше = крепче держит)")]
    public float normalRearGrip = 1f;
    [Tooltip("Боковое сцепление ЗАДНИХ в дрифте. Для тяжёлого грузовика ставь НИЗКО (0.15-0.3), иначе корму не пускает")]
    public float driftRearGrip = 0.2f;
    [Tooltip("Как быстро сцепление переключается (больше = резче срыв)")]
    public float driftBlendSpeed = 10f;
    [Tooltip("Прибавка к углу руля в заносе")]
    public float driftSteerBonus = 10f;
    [Tooltip("Мин. скорость (м/с) для срыва в занос")]
    public float minDriftSpeed = 3f;
    [Tooltip("Какой поворот руля (0..1) считается 'занос'. Ниже — Ctrl тормозит, выше — уводит в занос")]
    [Range(0.05f, 1f)] public float driftSteerThreshold = 0.25f;
    [Tooltip("Доп. подкрут кормы в заносе (аркадный толчок, чтобы тяжёлую машину заносило заметно). 0 = выключить")]
    public float driftYawAssist = 4000f;

    [Header("Steering Smoothing")]
    public float steerSpeed = 10f;

    private Rigidbody rb;
    private float moveInput;
    private float steerInput;
    private bool isBoosting;
    private bool handbrakeHeld;
    private bool isDrifting;

    private float currentSteerAngle;
    private float currentRearGrip;
    private float rearVisualSpin;
    private Quaternion rearVisualBaseRot;
    private bool baseRotInit;

    private WheelFrictionCurve rearFrictionRL;
    private WheelFrictionCurve rearFrictionRR;

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

        rearFrictionRL = wheelRL.sidewaysFriction;
        rearFrictionRR = wheelRR.sidewaysFriction;
        currentRearGrip = normalRearGrip;

        if (visualRearShared != null)
        {
            rearVisualBaseRot = visualRearShared.localRotation;
            baseRotInit = true;
        }
    }

    void Update()
    {
        moveInput = inputActions.Driving.Accelerate.ReadValue<float>();
        steerInput = inputActions.Driving.Steer.ReadValue<float>();

        var kb = Keyboard.current;
        isBoosting = kb != null && kb.leftShiftKey.isPressed;
        handbrakeHeld = kb != null && kb.leftCtrlKey.isPressed;

        bool steeringHard = Mathf.Abs(steerInput) > driftSteerThreshold;
        isDrifting = handbrakeHeld
                     && rb.linearVelocity.magnitude > minDriftSpeed
                     && steeringHard;

        UpdateWheelVisual(wheelFL, visualFL);
        UpdateWheelVisual(wheelFR, visualFR);
        UpdateRearSharedVisual();
    }

    void FixedUpdate()
    {
        // Тяга с бустом
        float currentForce = motorForce;
        if (isBoosting && rb.linearVelocity.magnitude < boostMaxSpeed)
            currentForce *= boostMultiplier;

        wheelRL.motorTorque = moveInput * currentForce;
        wheelRR.motorTorque = moveInput * currentForce;

        // Сцепление задних
        float targetGrip = normalRearGrip;
        if (isDrifting)
            targetGrip = driftRearGrip;
        else if (handbrakeHeld)
            targetGrip = Mathf.Lerp(normalRearGrip, driftRearGrip, 0.4f);

        currentRearGrip = Mathf.Lerp(currentRearGrip, targetGrip, driftBlendSpeed * Time.fixedDeltaTime);
        ApplyRearGrip(currentRearGrip);

        // Тормоз задними, когда Ctrl зажат и НЕ в заносе
        float brake = (handbrakeHeld && !isDrifting) ? handbrakeTorque : 0f;
        wheelRL.brakeTorque = brake;
        wheelRR.brakeTorque = brake;
        wheelFL.brakeTorque = 0f;
        wheelFR.brakeTorque = 0f;

        // Аркадный подкрут кормы в заносе — помогает тяжёлой машине скользить заметно
        if (isDrifting && driftYawAssist > 0f)
            rb.AddTorque(Vector3.up * steerInput * driftYawAssist, ForceMode.Force);

        // Руль (в заносе больше угол)
        float effectiveMaxSteer = maxSteerAngle + (isDrifting ? driftSteerBonus : 0f);
        float targetSteerAngle = steerInput * effectiveMaxSteer;
        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, steerSpeed * Time.fixedDeltaTime);
        wheelFL.steerAngle = currentSteerAngle;
        wheelFR.steerAngle = currentSteerAngle;
    }

    private void ApplyRearGrip(float grip)
    {
        rearFrictionRL.stiffness = grip;
        rearFrictionRR.stiffness = grip;
        wheelRL.sidewaysFriction = rearFrictionRL;
        wheelRR.sidewaysFriction = rearFrictionRR;
    }

    private void UpdateWheelVisual(WheelCollider col, Transform visual)
    {
        if (visual == null || col == null) return;
        col.GetWorldPose(out Vector3 position, out Quaternion rotation);
        visual.position = position;
        visual.rotation = rotation;
    }

    // Общий задний меш: не привязываем к позиции колёс (их два, меш один),
    // только докручиваем по средней скорости вращения задних WheelCollider.
    private void UpdateRearSharedVisual()
    {
        if (visualRearShared == null || !baseRotInit) return;

        float rpm = 0f;
        int count = 0;
        if (wheelRL != null) { rpm += wheelRL.rpm; count++; }
        if (wheelRR != null) { rpm += wheelRR.rpm; count++; }
        if (count > 0) rpm /= count;

        rearVisualSpin += rpm * 6f * Time.deltaTime; // rpm -> градусы/сек
        if (rearVisualSpin > 360f) rearVisualSpin -= 360f;
        else if (rearVisualSpin < -360f) rearVisualSpin += 360f;

        visualRearShared.localRotation = rearVisualBaseRot * Quaternion.AngleAxis(rearVisualSpin, rearSpinAxis.normalized);
    }
}