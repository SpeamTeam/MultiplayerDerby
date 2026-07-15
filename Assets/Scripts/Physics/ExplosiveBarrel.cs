using Assets.Scripts.AI;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Взрывающаяся бочка.
///
/// РАЗДЕЛЕНИЕ «СТОЛКНОВЕНИЕ vs ВЗРЫВ» (важно для дерби):
/// прямой таран машины об бочку НЕ должен ломать машину. За это отвечает СЛОЙ:
/// бочка лежит на слое "Barrel", а системы урона/разрушения машины его игнорируют
/// (см. DriverEjection — не выбрасывает водителя от контакта с бочкой;
/// CarCollisionDetector — наносит урон только объектам с CarHealth, которого у
/// бочки нет). Физически бочка при этом со всем сталкивается (матрица коллизий
/// не тронута) — машина её толкает, бьёт, детонирует. Но HP машина теряет ТОЛЬКО
/// от взрыва (см. Explode ниже).
///
/// СЕТЬ: сама бочка пока НЕ networked (как и физика машин в текущем состоянии
/// проекта). Урон машине наносится через CarHealth.ApplyDamage, который реально
/// срабатывает только на сервере (на клиенте тихо игнорируется) — то есть при
/// хосте детонация авторитетна на хосте, а HP реплицируется всем. Когда бочку
/// будут переводить на server-authority, Explode() достаточно обернуть в IsServer.
/// </summary>
public class ExplosiveBarrel : NetworkBehaviour
{
    [Header("Здоровье")]
    public float maxHealth = 100f;
    private float currentHealth;

    [Header("Порог урона")]
    public float minImpactForce = 5f;   // ниже этого удары игнорируются (лёгкие касания)
    public float damageMultiplier = 2f; // сила удара → урон

    [Header("Взрыв")]
    public GameObject explosionEffect;
    [Tooltip("Радиус поражения взрыва (мировые единицы). Должен быть заметно больше самой бочки. Виден красной сферой-гизмо при выделении — удобно калибровать под масштаб арены.")]
    public float explosionRadius = 25f;

    [Tooltip("Сила, с которой продавливается mesh автомобиля при взрыве")]
    [SerializeField] private float maxExplosionDeformationForce = 5f;
    
    [Tooltip("Сила разлёта. Применяется как VelocityChange, т.е. это ~прирост скорости (м/с) в эпицентре — одинаково подбрасывает и лёгкие, и тяжёлые машины.")]
    public float explosionForce = 18f;
    [Tooltip("Насколько сильно взрыв поддаёт машины ВВЕРХ (подброс). Больше = выше полёт.")]
    public float upwardModifier = 2.5f;
    [Tooltip("Урон машине в эпицентре. С расстоянием слегка спадает.")]
    public float explosionDamage = 50f;
    public AudioClip explosionSound;

    [Header("Тряска камеры")]
    [Tooltip("Сила тряски камеры при взрыве. 0 — выключить.")]
    public float cameraShakeForce = 5f;

    [Header("Цепная реакция")]
    [Tooltip("Задержка перед детонацией соседней бочки, пойманной взрывом. Даёт эффект «волны» и разносит детонации по кадрам (никакой синхронной рекурсии).")]
    public float chainReactionDelay = 0.1f;

    // Уже взорвалась (эффект отыгран, объект уничтожается).
    private bool exploded = false;
    // Детонация запланирована по цепочке (Invoke висит) — не планируем повторно.
    private bool detonationScheduled = false;

    void Start()
    {
        currentHealth = maxHealth;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (exploded) return;

        // Сила удара = относительная скорость столкновения.
        float impact = collision.relativeVelocity.magnitude;

        if (impact < minImpactForce) return; // слабый контакт — игнор

        float damage = impact * damageMultiplier;
        TakeDamage(damage);
    }

    public void TakeDamage(float amount)
    {
        if (exploded) return;

        currentHealth -= amount;
        if (currentHealth <= 0f)
            Explode();
    }

    /// <summary>
    /// Запланировать детонацию по цепочке (вызывается из чужого взрыва).
    /// Через Invoke, поэтому взрывы соседей разносятся по кадрам — это и «волна»,
    /// и защита от бесконечной синхронной рекурсии. Флаги exploded/detonationScheduled
    /// гарантируют, что каждая бочка взорвётся ровно один раз.
    /// </summary>
    public void DetonateFromChain(float delay)
    {
        if (exploded || detonationScheduled) return;

        if (delay <= 0f)
        {
            Explode();
            return;
        }

        detonationScheduled = true;
        Invoke(nameof(Explode), delay);
    }

    // [ClientRpc]
    // private void InitExplosionParticlesClientRPC()
    // {
    //     if (explosionEffect != null)
    //         Instantiate(explosionEffect, transform.position, Quaternion.identity);
    //     else
    //         Debug.LogWarning("[ExplosiveBarrel] There's no explosionEffect assigned");
    // }

    void Explode()
    {
        if (exploded) return; // страховка: не взрываемся дважды
        exploded = true;

        Debug.Log($"[Barrel] {gameObject.name} exploded at {transform.position}", this);
        // 1. Эффект
        if (explosionEffect != null)
        {
            Instantiate(explosionEffect, transform.position, Quaternion.identity);
        }
        else
            Debug.LogWarning("[ExplosiveBarrel] There's no explosionEffect assigned");

        // 2. Звук
        if (explosionSound != null)
            AudioSource.PlayClipAtPoint(explosionSound, transform.position);

        // 3. Тряска камеры (через Cinemachine Impulse — не конфликтует со слежением
        //    камеры за машиной, см. CameraImpulseShake).
        if (cameraShakeForce > 0f)
            CameraImpulseShake.ShakeAt(transform.position, cameraShakeForce);

        // 4. Раскидываем машины/объекты и раздаём урон машинам.
        // OverlapSphere может вернуть НЕСКОЛЬКО коллайдеров одной машины (кузов,
        // колёса, триггеры) — дедупим и импульс, и урон по её Rigidbody/CarHealth,
        // чтобы одну машину не «ударило» и не «ранило» многократно за один взрыв.
        Collider[] colliders = Physics.OverlapSphere(transform.position, explosionRadius);
        var pushedBodies = new HashSet<Rigidbody>();
        var damagedCars = new HashSet<CarHealth>();

        foreach (Collider hit in colliders)
        {
            // Импульс разлёта — на любое физическое тело (ловит Rigidbody даже у дочерних коллайдеров).
            Rigidbody rb = hit.attachedRigidbody;
            if (rb != null && pushedBodies.Add(rb))
            {
                // VelocityChange: одинаковый подброс независимо от массы машины.
                rb.AddExplosionForce(explosionForce, transform.position,
                                     explosionRadius, upwardModifier, ForceMode.VelocityChange);
            }

            // Цепная реакция с другими бочками (с задержкой — «волна»).
            ExplosiveBarrel barrel = hit.GetComponentInParent<ExplosiveBarrel>();
            if (barrel != null && barrel != this)
                barrel.DetonateFromChain(chainReactionDelay);

            // Урон машине приходит ТОЛЬКО отсюда, от взрыва (не от прямого тарана бочки).
            CarHealth car = hit.GetComponentInParent<CarHealth>();
            if (car != null && damagedCars.Add(car))
            {
                float dist = Vector3.Distance(transform.position, car.transform.position);
                // Лёгкий спад урона к краю радиуса: в эпицентре — полный, на границе — половина.
                float falloff = Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(dist / explosionRadius));
                car.ApplyDamage(explosionDamage * falloff);

                if (IsServer)
                {
                    var carCollision = car.GetComponent<CarCollision>();

                    Vector3 closestPointOnCar = hit.ClosestPoint(transform.position);
                    Vector3 dir = (closestPointOnCar - transform.position).normalized;
                    float distance = Vector3.Distance(transform.position, closestPointOnCar);

                    Ray ray = new Ray(transform.position, dir);
                    if (hit.Raycast(ray, out RaycastHit rayHit, dist + 0.1f))
                    {
                        Vector3 worldNormal = rayHit.normal;

                        Vector3 localPoint = car.transform.InverseTransformPoint(closestPointOnCar);
                        Vector3 localNormal = car.transform.InverseTransformDirection(worldNormal);

                        carCollision.DeformViaForce(localPoint, localNormal, maxExplosionDeformationForce / dist);
                    }
                }
            }
        }

        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}
