using System;
using UnityEngine;

/// <summary>
/// Очки одного игрока/машины. Вешается на ту же машину, что CarHealth.
///
/// Три источника очков в дерби:
///   1. Урон  — за каждую единицу нанесённого урона.
///   2. Выживание — очки капают за каждую секунду, пока жив.
///   3. Киллы — крупный бонус за уничтожение чужой машины.
///
/// Подписывается на события CarCollisionDetector/CarHealth своей машины,
/// а начисление за КИЛЛ приходит извне (от жертвы -> её killer).
///
/// СЕТЬ (для коллег): TotalScore -> NetworkVariable&lt;int&gt;, начислять только на сервере.
/// Событие OnScoreChanged останется для клиентского табло.
/// </summary>
public class PlayerScore : MonoBehaviour
{
    [Header("Правила начисления")]
    [Tooltip("Очков за 1 единицу нанесённого урона")]
    public float pointsPerDamage = 1f;

    [Tooltip("Очков за каждую секунду выживания")]
    public float pointsPerSecondAlive = 2f;

    [Tooltip("Бонус за уничтожение чужой машины")]
    public int pointsPerKill = 250;

    [Header("Инфо (только чтение)")]
    [SerializeField] private int totalScore;
    [SerializeField] private int kills;

    public int TotalScore => totalScore;
    public int Kills => kills;

    /// <summary>(новый счёт). Для обновления табло/HUD.</summary>
    public event Action<int> OnScoreChanged;

    private CarHealth health;
    private float aliveAccumulator; // дробное накопление очков за выживание

    private void Awake()
    {
        health = GetComponent<CarHealth>();
    }

    private void Update()
    {
        // Очки за выживание — только пока жив.
        if (health != null && !health.IsDead)
        {
            aliveAccumulator += pointsPerSecondAlive * Time.deltaTime;
            if (aliveAccumulator >= 1f)
            {
                int whole = Mathf.FloorToInt(aliveAccumulator);
                aliveAccumulator -= whole;
                AddScore(whole);
            }
        }
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

    public void ResetScore()
    {
        totalScore = 0;
        kills = 0;
        aliveAccumulator = 0f;
        OnScoreChanged?.Invoke(totalScore);
    }
}
