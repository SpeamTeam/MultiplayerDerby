using TMPro;
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

    [Header("Постбоевой режим (ник над машиной)")]
    [Tooltip("Ссылка на TMP-текст ника. Если не задана — создаётся в рантайме в этом же канвасе.")]
    [SerializeField] private TMP_Text nameLabel;

    [Tooltip("Смещение ника в координатах канваса (ДО его scale), относительно корня полоски")]
    [SerializeField] private Vector2 namePlateOffset = new Vector2(0f, 14f);

    [Tooltip("Размер поля ника в координатах канваса")]
    [SerializeField] private Vector2 namePlateSize = new Vector2(120f, 20f);

    [Tooltip("Кегль ника в координатах канваса")]
    [SerializeField] private float namePlateFontSize = 12f;

    [SerializeField] private Color namePlateColor = Color.white;

    private Camera cam;
    private CanvasGroup canvasGroup; // необязательно, для скрытия без Destroy
    private bool postCombat;

    private void Awake()
    {
        cam = Camera.main;
        if (targetHealth == null)
            targetHealth = GetComponentInParent<CarHealth>();

        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        // В постбоевом режиме шкала HP выключена — не воскрешаем подписку при переактивации.
        if (targetHealth == null || postCombat) return;

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

    /// <summary>
    /// Переключает полоску в постбоевой режим: прячет шкалу HP и показывает ник над машиной.
    /// Вызывается локально на каждом клиенте (см. CarAgent.EnterPostCombatModeClientRpc), т.к.
    /// полоска — это локальный world-space канвас на каждом экземпляре машины у каждого пира.
    /// Канвас уже висит над кузовом и биллбордится к камере, так что ник наследует это даром.
    /// </summary>
    public void EnterPostCombatMode(string nickname)
    {
        postCombat = true;

        // HP больше не нужно ни показывать, ни обновлять.
        if (targetHealth != null)
            targetHealth.OnHealthChanged -= HandleHealthChanged;
        if (healthSlider != null)
            healthSlider.gameObject.SetActive(false);
        if (canvasGroup != null)
            canvasGroup.alpha = 1f; // на случай, если hideWhenFullHealth ранее спрятал канвас

        EnsureNameLabel();
        nameLabel.text = nickname;
        nameLabel.gameObject.SetActive(true);
    }

    // Создаёт TMP-ник в этом же канвасе, если он не назначен в префабе. Отдельный объект —
    // чтобы не конфликтовать со слайдером и наследовать биллборд от корня полоски.
    private void EnsureNameLabel()
    {
        if (nameLabel != null) return;

        var go = new GameObject("Nameplate", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = namePlateSize;
        rt.anchoredPosition = namePlateOffset;

        nameLabel = go.AddComponent<TextMeshProUGUI>();
        nameLabel.alignment = TextAlignmentOptions.Center;
        nameLabel.fontSize = namePlateFontSize;
        nameLabel.color = namePlateColor;
        nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
        nameLabel.overflowMode = TextOverflowModes.Overflow;
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
