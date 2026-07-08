using UnityEngine;

/// <summary>
/// "Мозг" одной машины: связывает CarHealth + PlayerScore + управление + респавн.
/// Подписывается на события здоровья и раздаёт очки нужным сторонам.
///
/// Почему отдельный класс, а не всё в CarHealth:
/// CarHealth должен оставаться тупым хранилищем HP, чтобы его легко было
/// сделать сетевым. Вся ГЕЙМПЛЕЙНАЯ РЕАКЦИЯ (начислить очки, отключить
/// управление, заказать респавн) живёт здесь.
///
/// СЕТЬ (для коллег): начисление очков и respawn — серверные операции.
/// Обернуть тело HandleDied в if (IsServer). Отключение управления
/// (PlayerCarController.enabled) — тоже через сервер/овнершип.
/// </summary>
[RequireComponent(typeof(CarHealth))]
[RequireComponent(typeof(PlayerScore))]
[RequireComponent(typeof(DriverEjection))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerCarController))]
public class CarAgent : MonoBehaviour
{
    [Header("Ссылки")]
    public PlayerCarController controller;      // чтобы отключать управление при смерти
    public Rigidbody rb;

    [Header("Респавн")]
    public bool autoRespawn = true;
    public float respawnDelay = 3f;

    private CarHealth health;
    private PlayerScore score;
    private DriverEjection driverEjection;

    private void Awake()
    {
        health = GetComponent<CarHealth>();
        score = GetComponent<PlayerScore>();
        driverEjection = GetComponent<DriverEjection>(); // может отсутствовать — необязательный компонент
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (controller == null) controller = GetComponent<PlayerCarController>();
    }

    private void OnEnable()
    {
        health.OnHealthChanged += HandleHealthChanged;
        health.OnDied += HandleDied;
    }

    private void OnDisable()
    {
        health.OnHealthChanged -= HandleHealthChanged;
        health.OnDied -= HandleDied;
    }

    private void HandleHealthChanged(float current, float normalized)
    {
        // Хук для UI/звука получения урона. Начисление очков за урон
        // делает детектор атакующего через RegisterDamageDealt.
    }

    /// <summary>
    /// Вызывается на машине АТАКУЮЩЕГО, когда она нанесла урон.
    /// Детектор столкновений вызывает это на своей стороне.
    /// </summary>
    public void RegisterDamageDealt(float amount)
    {
        score.AddDamageScore(amount);
    }

    private void HandleDied(CarHealth attacker)
    {
        // Отключаем управление и глушим машину.
        if (controller != null) controller.enabled = false;

        // Начисляем килл атакующему (если это не самоуничтожение).
        if (attacker != null && attacker != health)
        {
            var killerAgent = attacker.GetComponent<CarAgent>();
            if (killerAgent != null)
                killerAgent.score.AddKill();
        }

        if (autoRespawn)
            Invoke(nameof(Respawn), respawnDelay);
    }

    private void Respawn()
    {
        Vector3 spawnPos;
        Quaternion spawnRot;

        if (SpawnManager.Instance != null)
        {
            SpawnManager.Instance.GetSpawnPose(out spawnPos, out spawnRot);
        }
        else
        {
            // Заглушка на случай отсутствия SpawnManager в сцене (например, в тестовой сцене).
            spawnPos = transform.position + Vector3.up * 1f;
            spawnRot = Quaternion.identity;
        }

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(spawnPos, spawnRot);

        health.ResetState();
        driverEjection?.ResetState();
        score.OnRespawn(); // сбрасывает коэффициент за время жизни, totalScore не трогает

        if (controller != null) controller.enabled = true;
    }
}