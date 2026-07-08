using UnityEngine;

/// <summary>
/// Считает урон от столкновений машина-об-машину и раздаёт его.
///
/// ФИЗИКА УРОНА (это ядро дерби):
/// Мы НЕ берём просто relativeVelocity — она не учитывает массы.
/// Мы берём collision.impulse — это уже импульс, приложенный физдвижком
/// с учётом масс обоих тел. Его magnitude пропорционален "жёсткости" удара.
///
/// Дальше домножаем на два множителя:
///   1. Угол удара — таранить носом больно, а получить по касательной — почти нет.
///   2. Кто агрессор — тот, кто едет НА цель, наносит больше (бонус тарана).
///
/// СЕТЬ (для коллег):
/// OnCollisionEnter на server-authoritative физике срабатывает на СЕРВЕРЕ.
/// Оставить весь расчёт как есть, но ApplyDamage вызывать только если IsServer.
/// На клиентах этот компонент можно не считать (физика там выключена).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CarHealth))]
public class CarCollisionDetector : MonoBehaviour
{
    [Header("Порог")]
    [Tooltip("Минимальный импульс удара, ниже которого урон не наносится (отсекает мелкие тычки)")]
    public float minImpactImpulse = 4f;

    [Header("Множители урона")]
    [Tooltip("Базовый множитель: урон = impulse * damageMultiplier * (модификаторы)")]
    public float damageMultiplier = 0.35f;

    [Tooltip("Во сколько раз больнее лобовой таран носом по сравнению с боковым касанием")]
    public float frontalDamageBonus = 2f;

    [Tooltip("Насколько сильнее бьёт тот, кто ЕДЕТ на цель (агрессор), чем тот, кого таранят")]
    public float aggressorBonus = 1.5f;

    [Tooltip("Максимальный урон за одно столкновение (чтобы редкие пиковые импульсы не выносили с одного касания)")]
    public float maxDamagePerHit = 60f;

    [Header("Кулдаун")]
    [Tooltip("Мин. интервал между засчитанными ударами по одной цели (сек) — гасит спам контактов")]
    public float perTargetCooldown = 0.25f;

    [Header("Только с этих слоёв считаем урон")]
    [Tooltip("Слой других машин. Столкновения со стенами/землёй урон не наносят.")]
    public LayerMask carLayer;

    [Header("Фильтр контактов с землёй")]
    [Tooltip("Если нормаль контакта почти вертикальная (машина подпрыгнула на кочке/бордюре или чиркнула днищем о землю на скорости) — это НЕ полноценный удар. И урон, и тряска камеры от таких контактов игнорируются. 0.7 ~ фильтрует контакты в пределах ~45° от вертикали.")]
    [Range(0f, 1f)] public float groundContactNormalThreshold = 0.7f;

    [Header("Debug (для тестирования баланса)")]
    [Tooltip("Печатать в консоль impulse/damage каждого засчитанного удара — удобно для подбора коэффициентов")]
    public bool logImpactsToConsole = false;

    private Rigidbody rb;
    private CarHealth ownHealth;

    // Последнее время удара по конкретной цели (по instanceID) — для кулдауна
    private readonly System.Collections.Generic.Dictionary<int, float> lastHitTime
        = new System.Collections.Generic.Dictionary<int, float>();

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ownHealth = GetComponent<CarHealth>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleImpact(collision);
    }

    private void HandleImpact(Collision collision)
    {
        // --- ФИЛЬТР "ЭТО НЕ УДАР, А ПОДПРЫГИВАНИЕ НА ЗЕМЛЕ" ---
        // Берём нормаль контакта один раз и переиспользуем ниже (не дёргаем
        // GetContact(0) дважды за кадр).
        ContactPoint contact = collision.GetContact(0);
        float verticalness = Mathf.Abs(Vector3.Dot(contact.normal, Vector3.up));

        // Контакт почти вертикальный = машина коснулась земли/бордюра снизу,
        // а не врезалась во что-то сбоку. Именно эти контакты давали ложную
        // тряску камеры на скорости на ровной дороге — impulse от них растёт
        // вместе со скоростью, хотя реального "удара" нет.
        if (verticalness > groundContactNormalThreshold) return;

        // collision.impulse учитывает массы обоих тел — это правильная база урона.
        float impulse = collision.impulse.magnitude;

        // Тряска камеры — от любого НЕ вертикального удара (в т.ч. об стену), это чисто визуал.

        if (impulse < minImpactImpulse) return;

        // Урон наносим только другим машинам.
        var otherHealth = collision.rigidbody != null
            ? collision.rigidbody.GetComponent<CarHealth>()
            : null;
        if (otherHealth == null) return;

        // Проверка слоя (страховка).
        if (carLayer.value != 0 &&
            (carLayer.value & (1 << collision.gameObject.layer)) == 0) return;

        int targetId = otherHealth.GetInstanceID();

        // Кулдаун по цели — не даём одному контакту сработать десятки раз.
        if (lastHitTime.TryGetValue(targetId, out float t) &&
            Time.time - t < perTargetCooldown)
            return;
        lastHitTime[targetId] = Time.time;

        // --- УГОЛ УДАРА ---
        // Нормаль контакта уже посчитана выше. Сравниваем направление на цель с нашим forward.
        // Если бьём носом (forward совпадает с направлением на цель) — бонус.
        Vector3 toTarget = (collision.transform.position - transform.position).normalized;
        float frontalDot = Mathf.Clamp01(Vector3.Dot(transform.forward, toTarget));
        // frontalDot=1 -> строго носом, 0 -> вбок/назад
        float angleMultiplier = Mathf.Lerp(1f, frontalDamageBonus, frontalDot);

        // --- КТО АГРЕССОР ---
        // Если наша скорость направлена НА цель — мы таранящий.
        float closingSpeed = Vector3.Dot(rb.linearVelocity, toTarget);
        float aggressorMultiplier = closingSpeed > 1f ? aggressorBonus : 1f;

        float damage = impulse * damageMultiplier * angleMultiplier * aggressorMultiplier;
        damage = Mathf.Min(damage, maxDamagePerHit);

        // Наносим урон цели, атакующий — мы (для очков и записи киллера).
        // Считаем фактически снятое HP, чтобы не давать очки за "оверкилл" или урон по неуязвимой цели.
        float before = otherHealth.CurrentHealth;
        otherHealth.ApplyDamage(damage, ownHealth);
        float actualDamage = before - otherHealth.CurrentHealth;

        // Очки атакующему за реально нанесённый урон.
        if (actualDamage > 0f)
        {
            var myAgent = GetComponent<CarAgent>();
            if (myAgent != null) myAgent.RegisterDamageDealt(actualDamage);
        }

        if (logImpactsToConsole)
        {
            Debug.Log($"[Impact] {name} -> {otherHealth.name} | impulse={impulse:F1} " +
                      $"frontal={frontalDot:F2} aggressor={(aggressorMultiplier > 1f)} " +
                      $"dmg={actualDamage:F1} targetHP={otherHealth.CurrentHealth:F0}/{otherHealth.maxHealth:F0}");
        }
    }
}
