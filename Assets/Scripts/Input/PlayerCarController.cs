using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

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

    [Header("Boost (Shift)")]
    [SerializeField] private float boostMultiplier = 2f;
    [SerializeField] private float boostMaxSpeed = 35f;

    [Tooltip("Одна кнопка (Space): едешь прямо — тормозит, поворачиваешь — уводит в занос")]
    [SerializeField] private float handbrakeTorque = 3000f;
    [Tooltip("Боковое сцепление ЗАДНИХ колёс в обычном режиме (выше = крепче держит)")]
    [SerializeField] private float normalRearGrip = 1f;

    [Header("Steering Smoothing")]
    [SerializeField] private float steerSpeed = 10f;

    [Header("Player's Input node")]
    [SerializeField] private PlayerInput playerInput;

    private Rigidbody rb;
    private float moveInput;
    private float steerInput;
    private bool isBoosting;
    private bool brakeHeld;

    private float currentSteerAngle;
    private float currentRearGrip;



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

        wheelRL.motorTorque = moveInput * currentForce;
        wheelRR.motorTorque = moveInput * currentForce;

        float brake = (brakeHeld) ? handbrakeTorque : 0f;
        wheelRL.brakeTorque = brake;
        wheelRR.brakeTorque = brake;
        wheelFL.brakeTorque = 0f;
        wheelFR.brakeTorque = 0f;

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

        float targetSteerAngle = steerInput * maxSteerAngle;
        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, steerSpeed * Time.fixedDeltaTime);
        wheelFL.steerAngle = currentSteerAngle;
        wheelFR.steerAngle = currentSteerAngle;

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
        sidewaysFriction.stiffness = flag ? Mathf.Lerp(sidewaysFriction.stiffness, .5f, Time.deltaTime * 1) : 1f;
        collider.sidewaysFriction = sidewaysFriction;
    }
}