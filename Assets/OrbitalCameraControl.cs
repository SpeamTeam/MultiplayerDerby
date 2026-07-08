using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// Управление орбитальной камерой (Cinemachine 3, CinemachineOrbitalFollow):
///   - Вращение вокруг машины (горизонталь + вертикаль) ТОЛЬКО пока зажата ЛКМ.
///   - Зум приближение/отдаление на колёсике мыши, с ограничением по границам.
///
/// ВОЗВРАТ КАМЕРЫ ЗА МАШИНУ делает ВСТРОЕННЫЙ Recentering самого Orbital Follow
/// (настраивается в инспекторе) — он учитывает поворот машины и возвращает
/// камеру строго за корму. Ручного возврата в этом скрипте больше нет.
///
/// ВАЖНО: НЕ вешай на эту камеру CinemachineInputAxisController —
/// он крутил бы камеру постоянно от движения мыши.
///
/// КУДА ВЕШАТЬ: на объект CinemachineCamera (с компонентом OrbitalFollow).
/// </summary>
public class OrbitalCameraControl : MonoBehaviour
{
    [Header("Ссылка на орбиту (авто-поиск, если пусто)")]
    public CinemachineOrbitalFollow orbital;

    [Header("Скорость вращения при зажатой ЛКМ")]
    public float horizontalSpeed = 180f;
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

    /// <summary>Вращаем ТОЛЬКО пока зажата левая кнопка мыши.</summary>
    private void HandleRotation(Mouse mouse)
    {
        if (!mouse.leftButton.isPressed) return;

        Vector2 delta = mouse.delta.ReadValue();

        float h = delta.x * horizontalSpeed * 0.01f;
        float v = delta.y * verticalSpeed * 0.01f * (invertVertical ? 1f : -1f);

        orbital.HorizontalAxis.Value += h;

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
            targetRadius -= Mathf.Sign(scroll) * zoomSpeed;
            targetRadius = Mathf.Clamp(targetRadius, minRadius, maxRadius);
        }

        float current = orbital.RadialAxis.Value;
        orbital.RadialAxis.Value = zoomSmoothing > 0f
            ? Mathf.Lerp(current, targetRadius, zoomSmoothing * Time.deltaTime)
            : targetRadius;
    }
}