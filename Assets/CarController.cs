using UnityEngine;
using UnityEngine.InputSystem; // Подключаем библиотеку новой системы ввода

public class CarController : MonoBehaviour
{
    [Header("Wheels")]
    public WheelCollider wheelFL;
    public WheelCollider wheelFR;
    public WheelCollider wheelRL;
    public WheelCollider wheelRR;

    [Header("Settings")]
    public float motorForce = 1500f;
    public float maxSteerAngle = 30f;
    public float brakeForce = 3000f;

    private Rigidbody rb;
    private float moveInput;
    private float steerInput;
    private bool isBraking;

    // Ссылка на сгенерированный класс управления
    private CarControls inputActions;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // Создаем экземпляр управления
        inputActions = new CarControls();
    }

    // Включаем чтение кнопок, когда объект активен
    private void OnEnable()
    {
        inputActions.Enable();
    }

    // Выключаем, когда не активен
    private void OnDisable()
    {
        inputActions.Disable();
    }

    void Start()
    {
        rb.centerOfMass = new Vector3(0f, -0.3f, 0f);
    }

    void Update()
    {
        // Считываем значения из новой системы ввода
        // ReadValue<float>() вернет от -1 до 1 (для W/S и A/D)
        moveInput = inputActions.Driving.Accelerate.ReadValue<float>();
        steerInput = inputActions.Driving.Steer.ReadValue<float>();

        // IsPressed() вернет true, пока нажат пробел
        isBraking = inputActions.Driving.Brake.IsPressed();
    }

    void FixedUpdate()
    {
        // Вся физика осталась абсолютно такой же!

        // Газ — крутим задние колёса
        wheelRL.motorTorque = moveInput * motorForce;
        wheelRR.motorTorque = moveInput * motorForce;

        // Руль — поворачиваем передние колёса
        wheelFL.steerAngle = steerInput * maxSteerAngle;
        wheelFR.steerAngle = steerInput * maxSteerAngle;

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
}