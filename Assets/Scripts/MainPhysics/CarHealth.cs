using Assets.Scripts.Network;
using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Здоровье машины: хранит HP, принимает урон, сообщает о смерти.
///
/// СЕТЬ: HP и факт смерти — авторитативное состояние сервера (NetworkVariable),
/// реплицируется всем пирам, включая тех, кто подключился позже удара.
/// ApplyDamage/Heal/ResetState выполняют реальную работу ТОЛЬКО на сервере
/// (вызов с клиента тихо игнорируется) — это чистое СОСТОЯНИЕ, оно не решает,
/// КОГДА наносить урон (это делает CarCollision/CarCollisionDetector на сервере),
/// оно только применяет его и рассылает события.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarHealth : NetworkBehaviour
{
    [Header("Настройки здоровья")]
    [Tooltip("Максимальное и стартовое здоровье машины")]
    public float maxHealth = 100f;

    [Tooltip("Неуязвимость (в секундах) сразу после спавна, чтобы не убивало на респавне")]
    public float spawnInvulnerability = 2f;

    // Авторитативное HP. Пишет только сервер, читают все пиры.
    private readonly NetworkVariable<float> netHealth = new(
        writePerm: NetworkVariableWritePermission.Server);

    // Авторитативный флаг смерти — нужен отдельно от netHealth, чтобы
    // опоздавший клиент сразу видел правильное состояние машины.
    private readonly NetworkVariable<bool> netIsDead = new(
        writePerm: NetworkVariableWritePermission.Server);

    // Момент (в серверном времени NGO), до которого машина неуязвима.
    // Хранится как метка времени, а не тикающий таймер — не гоняем лишний трафик каждый кадр,
    // а IsInvulnerable честно считается и на сервере, и на клиентах через синхронизированные часы.
    private readonly NetworkVariable<double> invulnerableUntil = new(
        writePerm: NetworkVariableWritePermission.Server);

    public bool IsDead => netIsDead.Value;
    public float CurrentHealth => netHealth.Value;
    public float HealthNormalized => maxHealth > 0f ? netHealth.Value / maxHealth : 0f;
    public bool IsInvulnerable => NetworkManager.ServerTime.Time < invulnerableUntil.Value;

    // ==================== События (для UI, звука, очков) ====================

    /// <summary>(текущее HP, нормализованное 0..1). Вызывается при любом изменении HP.</summary>
    public event Action<float, float> OnHealthChanged;
    public static event Action<float, CarHealth> OnGettingDamage;

    /// <summary>(атакующий — может быть null при падении/самоуроне). Вызывается один раз при смерти, на каждом пире.</summary>
    public event Action<CarHealth> OnDied;

    public override void OnNetworkSpawn()
    {
        netHealth.OnValueChanged += HandleNetHealthChanged;

        if (IsServer)
        {
            netHealth.Value = maxHealth;
            netIsDead.Value = false;
            invulnerableUntil.Value = NetworkManager.ServerTime.Time + spawnInvulnerability;
        }
    }

    public override void OnNetworkDespawn()
    {
        netHealth.OnValueChanged -= HandleNetHealthChanged;
    }

    private void HandleNetHealthChanged(float previous, float current)
    {
        OnHealthChanged?.Invoke(current, HealthNormalized);
    }

    /// <summary>
    /// Применить урон. Вызов с клиента игнорируется — реальный расчёт живёт только на сервере.
    /// </summary>
    /// <param name="amount">Величина урона (уже посчитанная физикой удара).</param>
    /// <param name="attacker">Тот, кто нанёс урон (для начисления очков и записи киллера). Может быть null.</param>
    public void ApplyDamage(float amount, CarHealth attacker = null)
    {
        if (!IsServer) return;
        if (IsDead || IsInvulnerable || amount <= 0f) return;

        netHealth.Value = Mathf.Max(0f, netHealth.Value - amount);
        OnGettingDamage?.Invoke(amount, attacker);

        if (netHealth.Value <= 0f)
            Die(attacker);

        Debug.Log($"GOT DAMAGE! {amount}");
    }

    /// <summary>Лечение / ремонт (пикапы и т.п.). Вызов с клиента игнорируется.</summary>
    public void Heal(float amount)
    {
        if (!IsServer) return;
        if (IsDead || amount <= 0f) return;
        netHealth.Value = Mathf.Min(maxHealth, netHealth.Value + amount);
    }

    private void Die(CarHealth attacker)
    {
        if (netIsDead.Value) return;
        netIsDead.Value = true;
        NotifyDiedClientRpc(attacker != null ? attacker.NetworkObjectId : 0);
        // Заметь: отключение управления делает внешний менеджер, подписавшийся на OnDied.
        // Этот класс не знает про геймплейные правила — respawn он только ЗАКАЗЫВАЕТ
        // у GameManager (тот читает задержку/автореспавн из GameConfig и поручает сам
        // респавн NetworkProvider'у, server-authoritative). Die() выполняется только
        // на сервере (см. IsServer-проверку в ApplyDamage), так что вызов ниже — тоже.
        if (NetworkProvider.Instance != null)
            NetworkProvider.Instance.HandleCarDeath(this);
        Debug.Log("GODDAMN, I'M SO DEAD RN");
    }

    [ContextMenu("Kill Myself")]
    public void DieFromEditor()
    {
        if (netIsDead.Value) return;
        netIsDead.Value = true;
        NotifyDiedClientRpc(0);
        if (NetworkProvider.Instance != null)
            NetworkProvider.Instance.HandleCarDeath(this);
    }

    [ClientRpc]
    private void NotifyDiedClientRpc(ulong attackerNetworkId)
    {
        CarHealth attacker = null;
        if (attackerNetworkId != 0 &&
            NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerNetworkId, out NetworkObject attackerObj))
        {
            attacker = attackerObj.GetComponent<CarHealth>();
        }

        OnDied?.Invoke(attacker);
    }

    /// <summary>Полный сброс — для респавна. Вызов с клиента игнорируется.</summary>
    public void ResetState()
    {
        if (!IsServer) return;
        netHealth.Value = maxHealth;
        invulnerableUntil.Value = NetworkManager.ServerTime.Time + spawnInvulnerability;
        netIsDead.Value = false;
    }
}
