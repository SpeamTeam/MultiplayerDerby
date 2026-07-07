using UnityEngine;
using System;

/// <summary>
/// Водитель, который может покинуть машину при сильном ударе или перевороте.
///
/// ДВЕ ФАЗЫ (это важно и отличается от первой версии):
///   1. Seated -> Launched: водитель физически отрывается от кресла и летит
///      под действием импульса. Это ЕЩЁ НЕ проигрыш — водитель может, например,
///      удариться о капот и остаться в пределах машины.
///   2. Launched -> FullyEjected: водитель физически покидает габариты машины
///      (пересекает границу зоны салона, см. CabinExitRelay). Только ЗДЕСЬ
///      применяются последствия (killCarOnEject, отключение управления, событие).
///
/// Это событийная механика: переход Launched -> FullyEjected определяется
/// физическим триггером (OnTriggerExit через CabinExitRelay), а не опросом
/// расстояния в Update — дешевле и точнее.
///
/// СЕТЬ (для коллег): решение "покинул машину да/нет" стоит принимать на
/// сервере (там же, где урон), а сам полёт рэгдолла проигрывать локально
/// на клиентах от одинаковых стартовых условий (позиция+импульс), не
/// синхронизируя каждый кадр.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CarHealth))]
public class DriverEjection : MonoBehaviour
{
    private enum DriverState { Seated, Launched, FullyEjected }

    [Header("Водитель")]
    [Tooltip("Child-объект водителя с Rigidbody и Collider, сидящий в машине")]
    public Transform driverRoot;

    [Header("Триггеры отрыва от кресла")]
    [Tooltip("Импульс удара (тот же collision.impulse.magnitude, что и в CarCollisionDetector), выше которого водителя срывает с места")]
    public float ejectImpulseThreshold = 22f;

    [Tooltip("Срывать с места также при долгом перевороте (использует CarStabilizer)")]
    public bool ejectOnLongFlip = true;

    [Header("Сила отрыва")]
    public float ejectForceMultiplier = 0.06f;
    public float minEjectForce = 4f;
    public float maxEjectForce = 14f;
    [Range(0f, 2f)] public float upwardBias = 1.1f;
    public float tumbleTorque = 6f;

    [Header("Последствия ПОСЛЕ фактического выхода из машины")]
    [Tooltip("Если true — когда водитель реально покидает габариты машины, это мгновенно 'убивает' машину (рекомендуемый дефолт для дерби)")]
    public bool killCarOnEject = true;

    [Tooltip("Отключать управление уже в момент отрыва от кресла (Launched) — логично: без водителя в кресле не газуешь. Не путать с killCarOnEject, который срабатывает позже.")]
    public bool disableControlOnLaunch = true;

    [Tooltip("Если водитель завис в Launched дольше этого времени, не долетев до выхода (застрял в геометрии) — форсируем FullyEjected, чтобы не было вечного подвешенного состояния")]
    public float forceEjectTimeout = 5f;

    /// <summary>Полностью покинул машину — это и есть "луз" для геймплея.</summary>
    public bool IsEjected => state == DriverState.FullyEjected;

    /// <summary>Уже сорван с кресла, но ещё в пределах машины.</summary>
    public bool IsLaunched => state == DriverState.Launched;

    /// <summary>Вызывается в момент, когда водитель ФАКТИЧЕСКИ покинул машину.</summary>
    public event Action OnDriverEjected;

    private DriverState state = DriverState.Seated;

    private Rigidbody driverRb;
    private Collider driverCollider;
    private Transform originalParent;
    private Vector3 seatLocalPos;
    private Quaternion seatLocalRot;

    private Rigidbody carRb;
    private CarHealth carHealth;
    private CarStabilizer stabilizer;
    private CarController controller;

    private void Awake()
    {
        carRb = GetComponent<Rigidbody>();
        carHealth = GetComponent<CarHealth>();
        stabilizer = GetComponent<CarStabilizer>();
        controller = GetComponent<CarController>();

        if (driverRoot == null)
        {
            Debug.LogWarning($"{name}: DriverEjection.driverRoot не назначен — водитель работать не будет.");
            enabled = false;
            return;
        }

        driverRb = driverRoot.GetComponent<Rigidbody>();
        driverCollider = driverRoot.GetComponent<Collider>();

        originalParent = driverRoot.parent;
        seatLocalPos = driverRoot.localPosition;
        seatLocalRot = driverRoot.localRotation;

        SeatDriver();
    }

    private void OnEnable()
    {
        if (stabilizer != null)
            stabilizer.OnFlippedTooLong += HandleFlippedTooLong;
    }

    private void OnDisable()
    {
        if (stabilizer != null)
            stabilizer.OnFlippedTooLong -= HandleFlippedTooLong;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (state != DriverState.Seated) return;
        if (carHealth != null && (carHealth.IsDead || carHealth.IsInvulnerable)) return;

        // Тот же фильтр, что и в CarCollisionDetector: вертикальный контакт
        // с землёй (кочка/бордюр) — не полноценный удар, водителя не срывает.
        ContactPoint groundCheck = collision.GetContact(0);
        if (Mathf.Abs(Vector3.Dot(groundCheck.normal, Vector3.up)) > 0.7f) return;

        float impulse = collision.impulse.magnitude;
        if (impulse < ejectImpulseThreshold) return;

        Vector3 awayFromHit = -collision.GetContact(0).normal;
        Launch(awayFromHit, impulse);
    }

    private void HandleFlippedTooLong()
    {
        if (!ejectOnLongFlip) return;
        if (state != DriverState.Seated) return;
        if (carHealth != null && (carHealth.IsDead || carHealth.IsInvulnerable)) return;

        Launch(transform.right * (UnityEngine.Random.value > 0.5f ? 1f : -1f), 0f);
    }

    /// <summary>Фаза 1: физически срываем водителя с кресла. Это ЕЩЁ НЕ конец — см. HandleCabinExit.</summary>
    private void Launch(Vector3 direction, float impulseMagnitude)
    {
        state = DriverState.Launched;

        driverRoot.SetParent(null, true);
        driverRb.isKinematic = false;
        if (driverCollider != null) driverCollider.isTrigger = false;

        driverRb.linearVelocity = carRb != null ? carRb.linearVelocity : Vector3.zero;

        Vector3 finalDir = (direction.normalized + Vector3.up * upwardBias).normalized;
        float forceAmount = Mathf.Clamp(impulseMagnitude * ejectForceMultiplier, minEjectForce, maxEjectForce);

        driverRb.AddForce(finalDir * forceAmount, ForceMode.Impulse);
        driverRb.AddTorque(UnityEngine.Random.insideUnitSphere * tumbleTorque, ForceMode.Impulse);

        if (disableControlOnLaunch && controller != null)
            controller.enabled = false;

        // Страховка от вечного "подвешенного" состояния, если водитель застрял
        // в геометрии машины и физически никогда не пересечёт границу салона.
        CancelInvoke(nameof(ForceEjectTimeout));
        Invoke(nameof(ForceEjectTimeout), forceEjectTimeout);
    }

    /// <summary>Вызывается CabinExitRelay, когда что-то покидает зону салона.</summary>
    public void HandleCabinExit(Collider other)
    {
        if (state != DriverState.Launched) return;

        // Проверяем, что из салона вышел именно водитель, а не что-то ещё.
        bool isOurDriver = other == driverCollider ||
                            (other.attachedRigidbody != null && other.attachedRigidbody == driverRb);
        if (!isOurDriver) return;

        CompleteEjection();
    }

    private void ForceEjectTimeout()
    {
        if (state == DriverState.Launched)
            CompleteEjection();
    }

    /// <summary>Фаза 2: водитель реально покинул машину — вот теперь применяем последствия.</summary>
    private void CompleteEjection()
    {
        state = DriverState.FullyEjected;
        CancelInvoke(nameof(ForceEjectTimeout));

        if (killCarOnEject && carHealth != null && !carHealth.IsDead)
            carHealth.ApplyDamage(carHealth.CurrentHealth);

        OnDriverEjected?.Invoke();
    }

    /// <summary>Посадить водителя обратно на место (без физики, следует за машиной).</summary>
    private void SeatDriver()
    {
        driverRoot.SetParent(originalParent, false);
        driverRoot.localPosition = seatLocalPos;
        driverRoot.localRotation = seatLocalRot;

        driverRb.linearVelocity = Vector3.zero;
        driverRb.angularVelocity = Vector3.zero;
        driverRb.isKinematic = true;
        if (driverCollider != null) driverCollider.isTrigger = true;
    }

    /// <summary>Вызывать из CarAgent при респавне машины.</summary>
    public void ResetState()
    {
        CancelInvoke(nameof(ForceEjectTimeout));
        state = DriverState.Seated;
        SeatDriver();
    }
}
