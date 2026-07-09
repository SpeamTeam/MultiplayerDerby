using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

[RequireComponent (typeof (PlayerInput))]
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

    [Header("Задний общий визуал (Truck_Wheel_AR — только вращение)")]
    [Tooltip("Один меш на обе задние колёса. Крутится по средней скорости задних WheelCollider, позицию не трогаем.")]
    [SerializeField] private Transform visualRearShared;
    [Tooltip("Локальная ось вращения заднего меша. Обычно X (1,0,0). Если крутится не так — поменяй на (0,0,1).")]
    [SerializeField] private Vector3 rearSpinAxis = Vector3.right;

    [Header("Settings")]
    [SerializeField] private float motorForce = 1500f;
    [SerializeField] private float maxSteerAngle = 30f;

    [Header("Boost (Shift)")]
    [SerializeField] private float boostMultiplier = 2f;
    [SerializeField] private float boostMaxSpeed = 35f;

    [Header("Дрифт (Space)")]
    [Tooltip("Одна кнопка (Space): едешь прямо — тормозит, поворачиваешь — уводит в занос")]
    [SerializeField] private float handbrakeTorque = 3000f;
    [Tooltip("Боковое сцепление ЗАДНИХ колёс в обычном режиме (выше = крепче держит)")]
    [SerializeField] private float normalRearGrip = 1f;
    [Tooltip("Боковое сцепление ЗАДНИХ в дрифте. Для тяжёлого грузовика ставь НИЗКО (0.1-0.25), иначе корму не пускает")]
    [SerializeField] private float driftRearGrip = 0.20f;
    [Tooltip("Как быстро сцепление переключается (больше = резче срыв)")]
    [SerializeField] private float driftBlendSpeed = 12f;
    [Tooltip("Прибавка к углу руля в заносе")]
    [SerializeField] private float driftSteerBonus = 14f;
    [Tooltip("Мин. скорость (м/с) для срыва в занос")]
    [SerializeField] private float minDriftSpeed = 2.5f;
    [Tooltip("Какой поворот руля (0..1) считается 'занос'. Ниже — Space тормозит, выше — уводит в занос")]
    [Range(0.05f, 1f)] [SerializeField] private float driftSteerThreshold = 0.15f;
    [Tooltip("Доп. подкрут кормы в заносе (аркадный толчок, чтобы машину заметно уводило). 0 = выключить")]
    [SerializeField] private float driftYawAssist = 6000f;
    [Tooltip("Доп. тяга вперёд во время заноса (0..1 от motorForce) — не даёт скорости падать от потери сцепления, аркадный занос без торможения")]
    [Range(0f, 1f)] [SerializeField] private float driftForwardAssist = 0.35f;

    [Header("Steering Smoothing")]
    [SerializeField] private float steerSpeed = 10f;

    [Header("Player's Input node")]
    [SerializeField] private PlayerInput playerInput;

    private Rigidbody rb;
    private float moveInput;
    private float steerInput;
    private bool isBoosting;
    private bool brakeHeld;
    private bool isDrifting;

    private float currentSteerAngle;
    private float currentRearGrip;
    private float rearVisualSpin;
    private Quaternion rearVisualBaseRot;
    private bool baseRotInit;

    private WheelFrictionCurve rearFrictionRL;
    private WheelFrictionCurve rearFrictionRR;


    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
            playerInput.actions.Enable();
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
            playerInput.actions.Disable();
    }

    void Start()
    {
        if (!IsOwner)
            return;
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
        if (!IsOwner)
            return;
        bool steeringHard = Mathf.Abs(steerInput) > driftSteerThreshold;
        isDrifting = brakeHeld
                     && rb.linearVelocity.magnitude > minDriftSpeed
                     && steeringHard;

        UpdateWheelVisual(wheelFL, visualFL);
        UpdateWheelVisual(wheelFR, visualFR);
        UpdateRearSharedVisual();
    }


    // Input Methods {

    public void OnSteer(InputAction.CallbackContext context) 
    { 
        steerInput  = context.ReadValue<float>();
    }
    public void OnAccelerate(InputAction.CallbackContext context) 
    {
        moveInput= context.ReadValue<float>();
    }
    public void OnBrake(InputAction.CallbackContext context) 
    {
        if (context.performed)
        {
            brakeHeld = true;
        }
        else if (context.canceled)
        {
            brakeHeld = false;
        }
    }
    public void OnBoost(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            isBoosting = true;
        }
        else if (context.canceled)
        {
            isBoosting = false;
        }
    }
    // Input Methods }

    void FixedUpdate()
    {
        if (!IsOwner) 
            return;
        
        // Тяга с бустом TODO: Make boost exist in game and in Input Asset
        float currentForce = motorForce;
        if (isBoosting && rb.linearVelocity.magnitude < boostMaxSpeed)
            currentForce *= boostMultiplier;

        wheelRL.motorTorque = moveInput * currentForce;
        wheelRR.motorTorque = moveInput * currentForce;

        // Сцепление задних
        float targetGrip = normalRearGrip;
        if (isDrifting)
            targetGrip = driftRearGrip;
        else if (brakeHeld)
            targetGrip = Mathf.Lerp(normalRearGrip, driftRearGrip, 0.4f);

        currentRearGrip = Mathf.Lerp(currentRearGrip, targetGrip, driftBlendSpeed * Time.fixedDeltaTime);
        ApplyRearGrip(currentRearGrip);

        // Тормоз задними, когда Space зажат и НЕ в заносе
        float brake = (brakeHeld && !isDrifting) ? handbrakeTorque : 0f;
        wheelRL.brakeTorque = brake;
        wheelRR.brakeTorque = brake;
        wheelFL.brakeTorque = 0f;
        wheelFR.brakeTorque = 0f;

        // Аркадный занос: подкручиваем корму и держим тягу, чтобы скорость не терялась от потери сцепления
        if (isDrifting)
        {
            if (driftYawAssist > 0f)
                rb.AddTorque(Vector3.up * steerInput * driftYawAssist, ForceMode.Force);

            if (driftForwardAssist > 0f)
                rb.AddForce(transform.forward * (motorForce * driftForwardAssist), ForceMode.Force);
        }

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
