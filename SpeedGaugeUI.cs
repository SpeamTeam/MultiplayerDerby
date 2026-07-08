using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Спидометр в стиле цифровой приборки Honda Civic: крупная сегментная
/// синяя цифра (шрифт DSEG через TextMeshPro) + боковые "крылья" из
/// сегментов, загорающихся по мере роста скорости.
///
/// ОПТИМИЗАЦИЯ: скорость меняется непрерывно, поэтому опрос неизбежен, но
/// крутится он в FixedUpdate (частота физики ~50 Гц), а не в Update
/// (частота кадров, которая на мощном ПК может быть 144+ Гц). Быстрее
/// физического тика скорость всё равно не меняется, так что для игрока
/// плавность та же, а вызовов меньше.
///
/// СТРУКТУРА В СЦЕНЕ (screen-space Canvas, правый верхний угол):
///   Panel (фон-окошко)
///    ├── SpeedText  (TMP, шрифт DSEG, синий + Glow в материале)
///    ├── UnitText   (TMP, "km/h")
///    ├── WingLeft   (пустой контейнер) -> N штук Image (сегменты)
///    └── WingRight  (пустой контейнер) -> N штук Image (сегменты)
/// Этот скрипт вешается на Panel. targetCar = Rigidbody СВОЕЙ машины.
/// </summary>
public class SpeedGaugeUI : MonoBehaviour
{
    [Header("Источник скорости")]
    [Tooltip("Rigidbody машины ЛОКАЛЬНОГО игрока (не всех на арене)")]
    public Rigidbody targetCar;

    [Header("Цифра")]
    [Tooltip("TMP-текст с цифрой скорости (шрифт DSEG)")]
    public TMP_Text speedText;
    [Tooltip("Показывать км/ч (иначе м/с)")]
    public bool displayKmh = true;

    [Header("Сегменты-крылья (Image по бокам)")]
    [Tooltip("Сегменты ЛЕВОГО крыла, по порядку ОТ ЦЕНТРА наружу")]
    public Image[] leftWingSegments;
    [Tooltip("Сегменты ПРАВОГО крыла, по порядку ОТ ЦЕНТРА наружу")]
    public Image[] rightWingSegments;

    [Tooltip("Скорость (м/с), при которой все сегменты крыла загораются")]
    public float maxSpeedForWings = 50f;

    [Header("Цвета")]
    public Color litColor = new Color(0.36f, 0.78f, 1f, 1f);   // синий свет
    public Color dimColor = new Color(0.07f, 0.22f, 0.31f, 1f); // приглушённый

    [Header("Сглаживание отображаемой скорости")]
    [Tooltip("Насколько плавно цифра догоняет реальную скорость (0 = мгновенно)")]
    public float smoothing = 8f;

    private float displayedSpeed;

    private void FixedUpdate()
    {
        if (targetCar == null) return;

        float realSpeed = targetCar.linearVelocity.magnitude;

        // Плавное догоняние — чтобы цифра не дёргалась на резких толчках.
        displayedSpeed = smoothing > 0f
            ? Mathf.Lerp(displayedSpeed, realSpeed, smoothing * Time.fixedDeltaTime)
            : realSpeed;

        UpdateText();
        UpdateWings();
    }

    private void UpdateText()
    {
        if (speedText == null) return;
        float shown = displayKmh ? displayedSpeed * 3.6f : displayedSpeed;
        speedText.text = Mathf.RoundToInt(shown).ToString();
    }

    private void UpdateWings()
    {
        float fill = Mathf.Clamp01(displayedSpeed / maxSpeedForWings);

        ApplyWing(leftWingSegments, fill);
        ApplyWing(rightWingSegments, fill);
    }

    // Сегменты в массиве идут ОТ ЦЕНТРА наружу: ближние к цифре
    // загораются первыми, дальние — при большей скорости.
    private void ApplyWing(Image[] segments, float fill)
    {
        if (segments == null || segments.Length == 0) return;

        int litCount = Mathf.RoundToInt(fill * segments.Length);
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            segments[i].color = i < litCount ? litColor : dimColor;
        }
    }
}
