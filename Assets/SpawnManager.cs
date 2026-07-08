using UnityEngine;

/// <summary>
/// Спавнит машины на арене и выдаёт точки для респавна существующих.
///
/// ЧЕСТНАЯ РАВНОВЕРОЯТНОСТЬ ТОЧЕК:
/// Раньше выбор шёл через Random.Range на каждой попытке — это выборка
/// С ПОВТОРАМИ: одна точка могла проверяться дважды, другая — ни разу,
/// и при неудаче свободная точка могла остаться незамеченной.
/// Теперь используется перемешивание Фишера-Йетса: строится случайный
/// порядок обхода БЕЗ повторов, поэтому проверяются все точки, и у любой
/// точки шанс оказаться выбранной строго одинаков.
///
/// СЕТЬ (для коллег): и спавн, и респавн — серверные операции.
/// SpawnCar должен на сервере вызывать NetworkObject.Spawn() после
/// обычного Instantiate (или через NetworkManager.SpawnManager),
/// иначе объект не появится у клиентов. Сигнатуру менять не обязательно.
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

    /// <summary>
    /// Заспавнить НОВУЮ машину из префаба в свободной точке арены.
    /// Использовать при подключении игрока / появлении бота — то есть
    /// когда объекта машины ещё не существует.
    /// </summary>
    public GameObject SpawnCar(GameObject carPrefab)
    {
        if (carPrefab == null)
        {
            Debug.LogError("SpawnManager.SpawnCar: carPrefab не передан.");
            return null;
        }

        Transform point = PickSpawnPoint();
        return Instantiate(carPrefab, point.position, point.rotation);
    }

    /// <summary>Позиция свободной точки — для респавна УЖЕ существующей машины (см. CarAgent.Respawn).</summary>
    public Vector3 GetSpawnPoint()
    {
        return PickSpawnPoint().position;
    }

    /// <summary>Позиция и поворот свободной точки — предпочтительно перед GetSpawnPoint(), учитывает ориентацию точки на арене.</summary>
    public void GetSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        Transform point = PickSpawnPoint();
        position = point.position;
        rotation = point.rotation;
    }

    /// <summary>
    /// Возвращает случайную СВОБОДНУЮ точку с гарантированно равным шансом
    /// для каждой точки. Если все заняты — возвращает случайную из всех
    /// (тоже равновероятно), чтобы не блокировать спавн намертво.
    /// </summary>
    private Transform PickSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return transform;

        int n = spawnPoints.Length;
        int[] order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;

        // Fisher-Yates: честная случайная перестановка без повторов.
        for (int i = n - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        foreach (int idx in order)
        {
            Transform p = spawnPoints[idx];
            if (!Physics.CheckSphere(p.position, clearRadius, carLayer))
                return p;
        }

        // Все точки заняты — возвращаем любую (порядок уже перемешан честно).
        return spawnPoints[order[0]];
    }
}