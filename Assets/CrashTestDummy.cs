using UnityEngine;

/// <summary>
/// Инструмент для тестирования физики урона БЕЗ второго игрока и без сети.
/// Это управляемый скриптом Rigidbody-снаряд ("манекен"), который с заданной
/// скоростью и под заданным углом врезается в твою машину.
///
/// ЗАЧЕМ: чтобы подобрать damageMultiplier / frontalDamageBonus / ejectImpulseThreshold
/// нужны ПОВТОРЯЕМЫЕ столкновения с известными параметрами. Ручная езда даёт
/// каждый раз разный удар — невозможно понять, помогла ли правка коэффициента
/// или тебе просто в этот раз показалось иначе.
///
/// КАК ИСПОЛЬЗОВАТЬ:
/// 1. Создай пустой GameObject "CrashDummy" рядом с ареной (не внутри машины).
/// 2. Добавь Rigidbody (масса ~1200, как у машины) и BoxCollider примерных
///    размеров кузова.
/// 3. Повесь этот скрипт, укажи target = твоя тестовая машина.
/// 4. Play -> нажми Space (или вызови Launch() из UI-кнопки/другого скрипта) —
///    манекен разгонится и вмажется в цель с заданной скоростью.
/// 5. Смотри консоль (включи logImpactsToConsole в CarCollisionDetector цели)
///    и меняй speed/angleOffset, чтобы проверить лобовой/боковой/задний удар.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CrashTestDummy : MonoBehaviour
{
    [Header("Цель")]
    public Transform target;

    [Header("Параметры теста")]
    [Tooltip("Скорость манекена в момент удара (м/с). ~10-15 = обычная авто-скорость, 20+ = сильный разгон")]
    public float speed = 15f;

    [Tooltip("Смещение точки прицеливания относительно центра цели (X = вбок, для теста бокового удара)")]
    public Vector3 aimOffset = Vector3.zero;

    [Tooltip("На каком расстоянии от цели стартовать манекен перед каждым запуском")]
    public float launchDistance = 10f;

    [Tooltip("Запускать по нажатию Space")]
    public bool launchOnSpaceKey = true;

    private Rigidbody rb;
    private Vector3 startLocalOffsetDir; // направление старта относительно цели, кэшируется при первом запуске

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = false;
    }

    private void Update()
    {
        if (launchOnSpaceKey && Input.GetKeyDown(KeyCode.Space))
            Launch();
    }

    /// <summary>Поставить манекен на дистанцию перед целью и разогнать точно в неё.</summary>
    [ContextMenu("Launch")]
    public void Launch()
    {
        if (target == null)
        {
            Debug.LogWarning("CrashTestDummy: target не назначен.");
            return;
        }

        Vector3 aimPoint = target.position + aimOffset;

        // Если это первый запуск — фиксируем направление, откуда стартуем,
        // чтобы повторные тесты били строго с той же стороны (повторяемость).
        if (startLocalOffsetDir == Vector3.zero)
            startLocalOffsetDir = -target.forward;

        transform.position = aimPoint + startLocalOffsetDir * launchDistance;

        Vector3 dir = (aimPoint - transform.position).normalized;
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        rb.linearVelocity = dir * speed;
        rb.angularVelocity = Vector3.zero;
    }

    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, target.position + aimOffset);
    }
}
