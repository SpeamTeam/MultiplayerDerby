using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.VisualScripting;

[RequireComponent(typeof(PlayerInput))]
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
    [Tooltip("Скорость (м/с), на которой угол руля уменьшается до minSteerAngleFactor от maxSteerAngle")]
    [SerializeField] private float steerSpeedReference = 25f;
    [Tooltip("Доля от maxSteerAngle на высокой скорости (0..1) — чем меньше, тем сильнее руль 'сужается' на скорости")]
    [SerializeField] private float minSteerAngleFactor = 0.35f;

    [Header("Boost (Shift)")]
    [SerializeField] private float boostMultiplier = 2f;
    [SerializeField] private float boostMaxSpeed = 35f;

    [Header("Handbrake (Spacebar)")]
    [SerializeField] private float handbrakeTorque = 3000f;
    // [Tooltip("Боковое сцепление ЗАДНИХ колёс в обычном режиме (выше = крепче держит)")]
    // [SerializeField] private float normalRearGrip = 1f;

    [Header("Steering Smoothing")]
    [SerializeField] private float steerSpeed = 10f;

    [SerializeField] private float downforceAmount = 10f;

    [Range(0f, 1f)]
    [SerializeField] private float droppedStiffness = .5f;

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
        rb.centerOfMass = centerOfMass ;

    }

    void Update()
    {
        if (!IsOwner)
            return;

        UpdateWheelVisual(wheelFL, visualFL);
        UpdateWheelVisual(wheelFR, visualFR);
        UpdateWheelVisual(wheelRL, visualRL);
        UpdateWheelVisual(wheelRR, visualRR);
    }


    // Input Methods {

    public void OnSteer(InputAction.CallbackContext context)
    {
        steerInput = context.ReadValue<float>();
    }
    public void OnAccelerate(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<float>();
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

        // TODO: Make boost exist in game and in Input Asset
        // Тяга с бустом 
        float currentForce = motorForce;
        if (isBoosting && rb.linearVelocity.magnitude < boostMaxSpeed)
            currentForce *= boostMultiplier;

        wheelFL.motorTorque = moveInput * currentForce;
        wheelFR.motorTorque = moveInput * currentForce;

        float brake = (brakeHeld) ? handbrakeTorque : 0f;
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

        if (brakeHeld)
        {
            ApplyStiffness(wheelRL, true);
            ApplyStiffness(wheelRR, true);
        }
        else
        {
            ApplyStiffness(wheelRL, false);
            ApplyStiffness(wheelRR, false);
        }

        float speedFactor = brakeHeld
            ? 1f
            : Mathf.Lerp(1f, minSteerAngleFactor, Mathf.Clamp01(rb.linearVelocity.magnitude / steerSpeedReference));
        float targetSteerAngle = steerInput * maxSteerAngle * speedFactor;
        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, steerSpeed * Time.fixedDeltaTime);
        wheelFL.steerAngle = currentSteerAngle;
        wheelFR.steerAngle = currentSteerAngle;

        rb.AddForce(Vector3.down * downforceAmount * rb.linearVelocity.sqrMagnitude, ForceMode.Force);
    }

    private void UpdateWheelVisual(WheelCollider col, Transform visual)
    {
        if (visual == null || col == null) return;
        col.GetWorldPose(out Vector3 position, out Quaternion rotation);
        visual.position = position;
        visual.rotation = rotation;
    }

    // <summary>
    // If flag is true then stifness goes to low value, else recovers to 1f
    // </summary>
    private void ApplyStiffness(WheelCollider collider, bool flag)
    {
        var sidewaysFriction = collider.sidewaysFriction;
        float target = flag ? droppedStiffness : 1f;
        sidewaysFriction.stiffness = Mathf.Lerp(sidewaysFriction.stiffness, target, Time.fixedDeltaTime * 1);
        collider.sidewaysFriction = sidewaysFriction;
    }
}