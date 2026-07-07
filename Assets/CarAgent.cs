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
/// (CarController.enabled) — тоже через сервер/овнершип.
/// </summary>
[RequireComponent(typeof(CarHealth))]
[RequireComponent(typeof(PlayerScore))]
public class CarAgent : MonoBehaviour
{
    [Header("Ссылки")]
    public CarController controller;      // чтобы отключать управление при смерти
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
        if (controller == null) controller = GetComponent<CarController>();
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
        // Точку спавна выдаёт внешний менеджер арены (заглушка — текущая позиция + высота).
        Vector3 spawnPos = SpawnManager.Instance != null
            ? SpawnManager.Instance.GetSpawnPoint()
            : transform.position + Vector3.up * 1f;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(spawnPos, Quaternion.identity);

        health.ResetState();
        driverEjection?.ResetState();

        if (controller != null) controller.enabled = true;
    }
}
