using UnityEngine;

/// <summary>
/// Временный экранный оверлей для тестирования физики без готового UI/HUD от команды.
/// Показывает HP, очки, статус водителя и текущую скорость машины.
/// Повесить на любой активный GameObject в сцене (например, на саму машину или пустышку).
/// Удали/выключи, когда появится нормальный UI.
/// </summary>
public class DebugHud : MonoBehaviour
{
    [Tooltip("Машины, за которыми следим. Можно оставить пустым — тогда найдёт все CarHealth в сцене сам")]
    public CarHealth[] watchedCars;

    private void Start()
    {
        if (watchedCars == null || watchedCars.Length == 0)
            watchedCars = FindObjectsByType<CarHealth>(FindObjectsSortMode.None);
    }

    private void OnGUI()
    {
        GUI.skin.label.fontSize = 16;
        int y = 10;

        foreach (var health in watchedCars)
        {
            if (health == null) continue;

            var score = health.GetComponent<PlayerScore>();
            var eject = health.GetComponent<DriverEjection>();
            var rb = health.GetComponent<Rigidbody>();

            string status = health.IsDead ? "МЁРТВ" : (health.IsInvulnerable ? "неуязвим" : "жив");
            string driverStatus = eject != null ? (eject.IsEjected ? "ВЫЛЕТЕЛ" : "в машине") : "-";
            float speed = rb != null ? rb.linearVelocity.magnitude : 0f;

            string line = $"{health.name}: HP {health.CurrentHealth:F0}/{health.maxHealth:F0} [{status}] " +
                          $"| Очки {(score != null ? score.TotalScore : 0)} (килы {(score != null ? score.Kills : 0)}) " +
                          $"| Водитель: {driverStatus} | Скорость {speed:F1} м/с";

            GUI.Label(new Rect(10, y, 900, 24), line);
            y += 24;
        }
    }
}
