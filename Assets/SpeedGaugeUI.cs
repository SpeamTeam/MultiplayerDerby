using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Шкала скорости (дашборд локального игрока, screen-space overlay).
///
/// В ОТЛИЧИЕ от HealthBarUI, здесь НЕЛЬЗЯ обойтись без опроса — скорость
/// меняется непрерывно, пока едешь, событием это не заменить.
/// Экономим иначе: опрашиваем в FixedUpdate (частота физического тика,
/// обычно 50 Гц), а не в Update (частота кадров рендера, которая на
/// мощном ПК может быть 144+ Гц) — итоговая плавность для игрока та же,
/// т.к. скорость и так считается физикой с частотой FixedUpdate, а
/// чаще физического тика она просто не меняется.
///
/// СТРУКТУРА В СЦЕНЕ: обычный screen-space Canvas -> Slider для шкалы
/// (+ опционально Text для числового значения). Этот скрипт вешается
/// рядом, targetCar указывает на Rigidbody машины ЛОКАЛЬНОГО игрока
/// (не всех машин на арене — спидометр показывает только свою скорость).
/// </summary>
public class SpeedGaugeUI : MonoBehaviour
{
    public Rigidbody targetCar;
    public Slider speedSlider;
    public Text speedLabel; // если в проекте TextMeshPro — замени на TMP_Text

    [Tooltip("Скорость (м/с), соответствующая полной шкале")]
    public float maxSpeedForGauge = 30f;

    [Tooltip("Показывать км/ч в тексте вместо м/с")]
    public bool displayKmh = true;

    private void FixedUpdate()
    {
        if (targetCar == null) return;

        float speed = targetCar.linearVelocity.magnitude;

        if (speedSlider != null)
            speedSlider.value = Mathf.Clamp01(speed / maxSpeedForGauge);

        if (speedLabel != null)
        {
            float displaySpeed = displayKmh ? speed * 3.6f : speed;
            speedLabel.text = Mathf.RoundToInt(displaySpeed) + (displayKmh ? " км/ч" : " м/с");
        }
    }
}
