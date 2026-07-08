using System;
using UnityEngine;

/// <summary>
/// Очки одного игрока/машины. Вешается на ту же машину, что CarHealth.
///
/// Источники очков:
///   1. Урон  — за каждую единицу нанесённого урона (начисляется сразу при ударе).
///   2. Выживание — капает через ScoreTickManager (НЕ через Update, см. его комментарий),
///      с коэффициентом, растущим по мере того, сколько машина живёт без смерти.
///   3. Киллы — крупный бонус за уничтожение чужой машины.
///
/// СЕТЬ (для коллег): TotalScore -> NetworkVariable&lt;int&gt;, начислять только на сервере.
/// Событие OnScoreChanged останется для клиентского табло.
/// </summary>
public class PlayerScore : MonoBehaviour
{
    [Header("Урон и киллы")]
    [Tooltip("Очков за 1 единицу нанесённого урона")]
    public float pointsPerDamage = 1f;

    [Tooltip("Бонус за уничтожение чужой машины")]
    public int pointsPerKill = 250;

    [Header("Очки за выживание")]
    [Tooltip("Базовые очки за один тик ScoreTickManager (обычно раз в секунду), ДО применения коэффициента")]
    public float basePointsPerTick = 2f;

    [Tooltip("Насколько растёт коэффициент за каждую секунду жизни без смерти. 0 = коэффициент не растёт (плоская ставка)")]
    public float survivalCoefficientGrowthPerSecond = 0.05f;

    [Tooltip("Потолок коэффициента — чтобы очень долгая жизнь не давала бесконечно растущие очки")]
    public float maxSurvivalCoefficient = 3f;

    [Header("Инфо (только чтение)")]
    [SerializeField] private int totalScore;
    [SerializeField] private int kills;
    [SerializeField] private float timeAliveThisLife;

    public int TotalScore => totalScore;
    public int Kills => kills;

    /// <summary>Текущий множитель очков за выживание — растёт, пока машина жива без смерти.</summary>
    public float CurrentSurvivalCoefficient =>
        Mathf.Min(1f + survivalCoefficientGrowthPerSecond * timeAliveThisLife, maxSurvivalCoefficient);

    /// <summary>(новый счёт). Для обновления табло/HUD.</summary>
    public event Action<int> OnScoreChanged;

    private CarHealth health;

    private void Awake()
    {
        health = GetComponent<CarHealth>();
    }

    private void OnEnable()
    {
        ScoreTickManager.Instance.Register(this);
    }

    private void OnDisable()
    {
        // Проверяем Exists, а не создаём менеджер заново ради того, чтобы из него выписаться.
        if (ScoreTickManager.Exists)
            ScoreTickManager.Instance.Unregister(this);
    }

    /// <summary>
    /// Вызывается ScoreTickManager раз в tickInterval секунд — НЕ через собственный Update.
    /// </summary>
    public void TickSurvival(float deltaSeconds)
    {
        if (health == null || health.IsDead) return;

        timeAliveThisLife += deltaSeconds;

        float coefficient = CurrentSurvivalCoefficient;
        int points = Mathf.RoundToInt(basePointsPerTick * coefficient);
        AddScore(points);
    }

    /// <summary>Вызывать при нанесении урона другой машине.</summary>
    public void AddDamageScore(float damageDealt)
    {
        AddScore(Mathf.RoundToInt(damageDealt * pointsPerDamage));
    }

    /// <summary>Вызывать, когда эта машина кого-то уничтожила.</summary>
    public void AddKill()
    {
        kills++;
        AddScore(pointsPerKill);
    }

    private void AddScore(int amount)
    {
        if (amount == 0) return;
        totalScore += amount;
        OnScoreChanged?.Invoke(totalScore);
    }

    /// <summary>Полный сброс — для начала нового матча (не для обычного респавна).</summary>
    public void ResetScore()
    {
        totalScore = 0;
        kills = 0;
        timeAliveThisLife = 0f;
        OnScoreChanged?.Invoke(totalScore);
    }

    /// <summary>
    /// Вызывать при респавне (не при старте матча): обнуляет только коэффициент
    /// за время текущей жизни. Накопленный totalScore за матч НЕ трогаем —
    /// очки за прошлые жизни остаются.
    /// </summary>
    public void OnRespawn()
    {
        timeAliveThisLife = 0f;
    }
}
