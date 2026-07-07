using UnityEngine;

/// <summary>
/// Выдаёт точки спавна на арене. Простая версия: список Transform'ов,
/// выбирается наиболее свободный (без машин рядом), чтобы не спавнить друг в друга.
///
/// СЕТЬ (для коллег): спавн — серверная операция. Instance-паттерн заменить
/// на серверный синглтон, GetSpawnPoint вызывать на сервере при respawn.
/// </summary>
public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    [Tooltip("Точки спавна, расставленные по арене")]
    public Transform[] spawnPoints;

    [Tooltip("Радиус проверки, свободна ли точка от других машин")]
    public float clearRadius = 3f;

    [Tooltip("Слой машин — чтобы проверять занятость точки")]
    public LayerMask carLayer;

    private void Awake()
    {
        Instance = this;
    }

    public Vector3 GetSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return transform.position + Vector3.up;

        // Ищем свободную точку; если все заняты — берём случайную.
        for (int attempt = 0; attempt < spawnPoints.Length; attempt++)
        {
            Transform p = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (!Physics.CheckSphere(p.position, clearRadius, carLayer))
                return p.position;
        }
        return spawnPoints[Random.Range(0, spawnPoints.Length)].position;
    }
}
