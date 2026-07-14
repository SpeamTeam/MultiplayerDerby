using System.IO;
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
        float y = 10;

        foreach (var health in watchedCars)
        {
            if (health == null) continue;

            var eject = health.GetComponent<DriverEjection>();
            var rb = health.GetComponent<Rigidbody>();
            var agent = GetComponent<CarAgent>();
            var wheelSetup = agent.wheelSetup;

            // string status = health.IsDead ? "МЁРТВ" : (health.IsInvulnerable ? "неуязвим" : "жив");
            // string driverStatus = eject != null ? (eject.IsEjected ? "ВЫЛЕТЕЛ" : "в машине") : "-";

            float speed = rb != null ? rb.linearVelocity.magnitude : 0f;

            float sidewaysStifnessFL = wheelSetup.wheelColliders[0].sidewaysFriction.stiffness;
            float sidewaysStifnessFR = wheelSetup.wheelColliders[1].sidewaysFriction.stiffness;
            float sidewaysStifnessRL = wheelSetup.wheelColliders[2].sidewaysFriction.stiffness;
            float sidewaysStifnessRR = wheelSetup.wheelColliders[3].sidewaysFriction.stiffness;

            // string line = $"{health.name}: HP {health.CurrentHealth:F0}/{health.maxHealth:F0} [{status}] " +
            //               $"| Очки {(score != null ? score.TotalScore : 0)} (килы {(score != null ? score.Kills : 0)}) " +
            //               $"| Водитель: {driverStatus} | Скорость {speed:F1} м/с";

            int lineCount = 3; 
            float lineHeight = 20f;
            float blockHeight = lineCount * lineHeight;

            string line = $"speed: {speed:F1}\n" +
                $"SIDEWAYS STIFNESS:\n" +
                $"FL:{sidewaysStifnessFL:F2}    FR:{sidewaysStifnessFR:F2}    " +
                $"RL:{sidewaysStifnessRL:F2}    RR:{sidewaysStifnessRR:F2}";

            GUI.Label(new Rect(10, y, 900, blockHeight), line);
            y += blockHeight;
        }
    }
}
