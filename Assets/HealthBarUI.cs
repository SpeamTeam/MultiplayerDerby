using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Полоска здоровья над машиной (world-space Canvas).
///
/// ОПТИМИЗАЦИЯ: значение шкалы обновляется ТОЛЬКО когда меняется HP —
/// подписка на CarHealth.OnHealthChanged, без единого Update() для самого
/// значения. Единственное, что крутится каждый кадр — разворот к камере
/// (billboard), и это неизбежно: поворот должен быть непрерывным, пока
/// камера движется, событием его не заменить.
///
/// СТРУКТУРА В СЦЕНЕ:
/// Child машины -> Canvas (Render Mode: World Space, небольшой Scale ~0.01,
/// поднят над кузовом, например Y+2) -> внутри Slider (Background + Fill Area + Fill).
/// Этот скрипт вешается на корень Canvas. healthSlider = тот Slider.
/// targetHealth можно не указывать — найдётся в родителях автоматически.
/// </summary>
public class HealthBarUI : MonoBehaviour
{
    public CarHealth targetHealth;
    public Slider healthSlider;

    [Tooltip("Поворачивать полоску лицом к камере каждый кадр")]
    public bool billboardToCamera = true;

    [Tooltip("Скрывать полоску, когда HP полное (чтобы не загромождать экран здоровыми машинами)")]
    public bool hideWhenFullHealth = false;

    private Camera cam;
    private CanvasGroup canvasGroup; // необязательно, для скрытия без Destroy

    private void Awake()
    {
        cam = Camera.main;
        if (targetHealth == null)
            targetHealth = GetComponentInParent<CarHealth>();

        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        if (targetHealth == null) return;

        targetHealth.OnHealthChanged += HandleHealthChanged;
        // Проставляем стартовое значение сразу, не дожидаясь первого урона.
        HandleHealthChanged(targetHealth.CurrentHealth, targetHealth.HealthNormalized);
    }

    private void OnDisable()
    {
        if (targetHealth != null)
            targetHealth.OnHealthChanged -= HandleHealthChanged;
    }

    private void HandleHealthChanged(float current, float normalized)
    {
        if (healthSlider != null)
            healthSlider.value = normalized;

        if (hideWhenFullHealth && canvasGroup != null)
            canvasGroup.alpha = normalized >= 0.999f ? 0f : 1f;
    }

    private void LateUpdate()
    {
        if (!billboardToCamera) return;

        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("No main camera");
                return;
            }
        }

        transform.forward = (transform.position - cam.transform.position).normalized;
    }
}
