using UnityEngine;

namespace Assets.Scripts.AI
{ 

public class CarController : MonoBehaviour
{
    [Header("Wheel Colliders")]
    public WheelCollider wheelFL;
    public WheelCollider wheelFR;
    public WheelCollider wheelRL;
    public WheelCollider wheelRR;

    [Header("Wheel Meshes")]
    public Transform meshFL;
    public Transform meshFR;
    public Transform meshRL;
    public Transform meshRR;

    [Header("Car Settings")]
    public float maxMotorTorque = 1500f;
    public float maxSteerAngle = 35f;
    public float maxBrakeTorque = 3000f;
    public float maxSpeed = 20f;

    [Header("Stability")]
    public float antiRollForce = 5000f;
    public float centerOfMassOffset = -0.5f;

    [Header("Health")]
    public float maxHealth = 100f;
    public ParticleSystem smokeEffect;
    public ParticleSystem fireEffect;
    public float destroyDelay = 3f;

    // События для CarCollision и UI
    public System.Action<float> OnHealthChanged;  // 0..1
    public System.Action OnDeath;

    public float CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }
    public float CurrentSpeed => rb != null ? rb.linearVelocity.magnitude * 3.6f : 0f;
    public Rigidbody Rb => rb;

    private Rigidbody rb;
    private float currentMotorInput;
    private float currentSteerInput;
    private float currentBrakeInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0f, centerOfMassOffset, 0f);
        CurrentHealth = maxHealth;
    }

    private void FixedUpdate()
    {
        if (IsDead)
            SetInputs(0f, 0f, 1f);

        ApplyMotor();
        ApplySteering();
        ApplyBrakes();
        ApplyAntiRoll();
        UpdateWheelMeshes();
    }

    // ─── Управление ─────────────────────────────────────────────────────────

    public void SetInputs(float motor, float steer, float brake)
    {
        currentMotorInput = Mathf.Clamp(motor, -1f, 1f);
        currentSteerInput = Mathf.Clamp(steer, -1f, 1f);
        currentBrakeInput = Mathf.Clamp01(brake);
    }

    private void ApplyMotor()
    {
        float torqueMultiplier = CurrentSpeed < maxSpeed ? 1f : 0f;
        float motorTorque = currentMotorInput * maxMotorTorque * torqueMultiplier;
        wheelRL.motorTorque = motorTorque;
        wheelRR.motorTorque = motorTorque;
    }

    private void ApplySteering()
    {
        float steerAngle = currentSteerInput * maxSteerAngle;
        wheelFL.steerAngle = steerAngle;
        wheelFR.steerAngle = steerAngle;
    }

    private void ApplyBrakes()
    {
        float brakeTorque = currentBrakeInput * maxBrakeTorque;
        wheelFL.brakeTorque = brakeTorque;
        wheelFR.brakeTorque = brakeTorque;
        wheelRL.brakeTorque = brakeTorque;
        wheelRR.brakeTorque = brakeTorque;
    }

    private void ApplyAntiRoll()
    {
        ApplyAntiRollBar(wheelFL, wheelFR);
        ApplyAntiRollBar(wheelRL, wheelRR);
    }

    private void ApplyAntiRollBar(WheelCollider leftWheel, WheelCollider rightWheel)
    {
        WheelHit hit;
        float travelLeft = 1f;
        float travelRight = 1f;

        bool groundedLeft = leftWheel.GetGroundHit(out hit);
        if (groundedLeft)
            travelLeft = (-leftWheel.transform.InverseTransformPoint(hit.point).y
                          - leftWheel.radius) / leftWheel.suspensionDistance;

        bool groundedRight = rightWheel.GetGroundHit(out hit);
        if (groundedRight)
            travelRight = (-rightWheel.transform.InverseTransformPoint(hit.point).y
                           - rightWheel.radius) / rightWheel.suspensionDistance;

        float antiRollForceApplied = (travelLeft - travelRight) * antiRollForce;

        if (groundedLeft)
            rb.AddForceAtPosition(leftWheel.transform.up * -antiRollForceApplied,
                leftWheel.transform.position);

        if (groundedRight)
            rb.AddForceAtPosition(rightWheel.transform.up * antiRollForceApplied,
                rightWheel.transform.position);
    }

    private void UpdateWheelMeshes()
    {
        UpdateWheelMesh(wheelFL, meshFL);
        UpdateWheelMesh(wheelFR, meshFR);
        UpdateWheelMesh(wheelRL, meshRL);
        UpdateWheelMesh(wheelRR, meshRR);
    }

    private void UpdateWheelMesh(WheelCollider col, Transform mesh)
    {
        if (mesh == null) return;
        col.GetWorldPose(out Vector3 pos, out Quaternion rot);
        mesh.position = pos;
        mesh.rotation = rot;
    }

    // ─── Здоровье ───────────────────────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        if (IsDead) return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        OnHealthChanged?.Invoke(CurrentHealth / maxHealth);

        if (smokeEffect != null && CurrentHealth / maxHealth < 0.5f && !smokeEffect.isPlaying)
            smokeEffect.Play();

        if (CurrentHealth <= 0f)
            Die();
    }

    private void Die()
    {
        IsDead = true;
        OnDeath?.Invoke();

        if (smokeEffect != null) smokeEffect.Stop();
        if (fireEffect != null) fireEffect.Play();

        Destroy(gameObject, destroyDelay);
    }
}

}