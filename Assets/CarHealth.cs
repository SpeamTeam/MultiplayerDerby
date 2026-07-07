using System;
using UnityEngine;

/// <summary>
/// Здоровье машины: хранит HP, принимает урон, сообщает о смерти.
///
/// АРХИТЕКТУРА (важно для перехода на сеть):
/// Этот компонент — чистое СОСТОЯНИЕ. Он не решает, КОГДА наносить урон
/// (это делает CarCollisionDetector), он только применяет его и генерирует события.
///
/// Когда коллеги будут делать NGO:
///   - Наследовать от NetworkBehaviour вместо MonoBehaviour.
///   - Заменить поле currentHealth на NetworkVariable&lt;float&gt;.
///   - Метод ApplyDamage вызывать ТОЛЬКО на сервере (обернуть в проверку IsServer).
///   - События (OnHealthChanged / OnDied) останутся как есть — они пригодятся
///     для клиентского UI и звуков.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarHealth : MonoBehaviour
{
    [Header("Настройки здоровья")]
    [Tooltip("Максимальное и стартовое здоровье машины")]
    public float maxHealth = 100f;

    [Tooltip("Неуязвимость (в секундах) сразу после спавна, чтобы не убивало на респавне")]
    public float spawnInvulnerability = 2f;

    // Текущее здоровье. При переходе на сеть -> NetworkVariable<float>.
    private float currentHealth;

    // Таймер неуязвимости
    private float invulnTimer;

    public bool IsDead { get; private set; }
    public float CurrentHealth => currentHealth;
    public float HealthNormalized => maxHealth > 0f ? currentHealth / maxHealth : 0f;

    /// <summary>Кто владелец этой машины — используется для начисления очков атакующему.</summary>
    public int OwnerId { get; set; } = -1;

    // ==================== События (для UI, звука, очков) ====================

    /// <summary>(текущее HP, нормализованное 0..1). Вызывается при любом изменении HP.</summary>
    public event Action<float, float> OnHealthChanged;

    /// <summary>(атакующий — может быть null при падении/самоуроне). Вызывается один раз при смерти.</summary>
    public event Action<CarHealth> OnDied;

    private void Awake()
    {
        currentHealth = maxHealth;
        invulnTimer = spawnInvulnerability;
    }

    private void Update()
    {
        if (invulnTimer > 0f)
            invulnTimer -= Time.deltaTime;
    }

    public bool IsInvulnerable => invulnTimer > 0f;

    /// <summary>
    /// Применить урон. В сетевой версии вызывать ТОЛЬКО на сервере.
    /// </summary>
    /// <param name="amount">Величина урона (уже посчитанная физикой удара).</param>
    /// <param name="attacker">Тот, кто нанёс урон (для начисления очков и записи киллера). Может быть null.</param>
    public void ApplyDamage(float amount, CarHealth attacker = null)
    {
        if (IsDead || IsInvulnerable || amount <= 0f) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        OnHealthChanged?.Invoke(currentHealth, HealthNormalized);

        if (currentHealth <= 0f)
            Die(attacker);
    }

    /// <summary>Лечение / ремонт (пикапы и т.п.).</summary>
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        OnHealthChanged?.Invoke(currentHealth, HealthNormalized);
    }

    private void Die(CarHealth attacker)
    {
        if (IsDead) return;
        IsDead = true;
        OnDied?.Invoke(attacker);
        // Заметь: сам respawn / отключение управления делает внешний менеджер,
        // подписавшийся на OnDied. Этот класс не знает про геймплейные правила.
    }

    /// <summary>Полный сброс — для респавна.</summary>
    public void ResetState()
    {
        currentHealth = maxHealth;
        invulnTimer = spawnInvulnerability;
        IsDead = false;
        OnHealthChanged?.Invoke(currentHealth, HealthNormalized);
    }
}
