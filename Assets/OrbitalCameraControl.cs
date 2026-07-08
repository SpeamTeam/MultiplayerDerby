using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// Управление орбитальной камерой (Cinemachine 3, CinemachineOrbitalFollow):
///   - Вращение вокруг машины (горизонталь + вертикаль) ТОЛЬКО пока зажата ЛКМ.
///   - Зум приближение/отдаление на колёсике мыши, с ограничением по границам.
///
/// ВАЖНО: НЕ вешай на эту камеру компонент CinemachineInputAxisController —
/// он бы крутил камеру постоянно от движения мыши. Здесь мы управляем осями
/// OrbitalFollow вручную (.HorizontalAxis / .VerticalAxis / .RadialAxis),
/// поэтому легко включать вращение только по условию (зажата ЛКМ).
///
/// КУДА ВЕШАТЬ: на объект CinemachineCamera (с компонентом OrbitalFollow).
/// Если orbital не назначен вручную — найдётся на этом же объекте.
/// </summary>
public class OrbitalCameraControl : MonoBehaviour
{
    [Header("Ссылка на орбиту (авто-поиск, если пусто)")]
    public CinemachineOrbitalFollow orbital;

    [Header("Скорость вращения при зажатой ЛКМ")]
    public float horizontalSpeed = 180f; // градусов в секунду при полном отклонении мыши
    public float verticalSpeed = 120f;
    [Tooltip("Инвертировать вертикаль (тянешь мышь вверх — камера смотрит вниз)")]
    public bool invertVertical = false;

    [Header("Зум колёсиком (радиус камеры)")]
    [Tooltip("Насколько сильно один щелчок колёсика меняет дистанцию")]
    public float zoomSpeed = 2f;
    [Tooltip("Минимальная дистанция (ближе не приблизить)")]
    public float minRadius = 4f;
    [Tooltip("Максимальная дистанция (дальше не отдалить)")]
    public float maxRadius = 14f;
    [Tooltip("Плавность зума (0 = мгновенно)")]
    public float zoomSmoothing = 10f;

    private float targetRadius;

    private void Awake()
    {
        if (orbital == null)
            orbital = GetComponent<CinemachineOrbitalFollow>();

        if (orbital == null)
        {
            Debug.LogError($"{name}: OrbitalCameraControl не нашёл CinemachineOrbitalFollow. " +
                           "Повесь скрипт на камеру с компонентом Orbital Follow.");
            enabled = false;
            return;
        }

        // Стартовый радиус берём из текущего значения радиальной оси.
        targetRadius = Mathf.Clamp(orbital.RadialAxis.Value, minRadius, maxRadius);
        orbital.RadialAxis.Value = targetRadius;
    }

    private void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        HandleRotation(mouse);
        HandleZoom(mouse);
    }

    private void HandleRotation(Mouse mouse)
    {
        // Вращаем ТОЛЬКО пока зажата левая кнопка мыши.
        if (!mouse.leftButton.isPressed) return;

        // delta — смещение мыши за кадр (в пикселях). Нормируем через deltaTime.
        Vector2 delta = mouse.delta.ReadValue();

        float h = delta.x * horizontalSpeed * 0.01f;
        float v = delta.y * verticalSpeed * 0.01f * (invertVertical ? 1f : -1f);

        orbital.HorizontalAxis.Value += h;

        // Вертикаль ограничиваем диапазоном самой оси (Range задаётся в инспекторе OrbitalFollow),
        // чтобы камера не переворачивалась через полюс.
        float newVertical = orbital.VerticalAxis.Value + v;
        orbital.VerticalAxis.Value = Mathf.Clamp(
            newVertical,
            orbital.VerticalAxis.Range.x,
            orbital.VerticalAxis.Range.y);
    }

    private void HandleZoom(Mouse mouse)
    {
        float scroll = mouse.scroll.ReadValue().y;

        if (Mathf.Abs(scroll) > 0.01f)
        {
            // scroll обычно приходит порциями по 120 (Windows) — нормируем.
            targetRadius -= Mathf.Sign(scroll) * zoomSpeed;
            targetRadius = Mathf.Clamp(targetRadius, minRadius, maxRadius);
        }

        // Плавно подводим фактический радиус к целевому.
        float current = orbital.RadialAxis.Value;
        orbital.RadialAxis.Value = zoomSmoothing > 0f
            ? Mathf.Lerp(current, targetRadius, zoomSmoothing * Time.deltaTime)
            : targetRadius;
    }
}
