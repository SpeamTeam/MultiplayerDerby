using UnityEngine;

public class ExplosiveBarrel : MonoBehaviour
{
    [Header("Здоровье")]
    public float maxHealth = 100f;
    private float currentHealth;

    [Header("Порог урона")]
    public float minImpactForce = 5f;   // ниже этого удары игнорируются (лёгкие касания)
    public float damageMultiplier = 2f; // сила удара → урон

    [Header("Взрыв")]
    public GameObject explosionEffect;
    public float explosionRadius = 6f;
    public float explosionForce = 1500f;   // для машин нужна большая сила
    public float upwardModifier = 1.5f;    // подбрасывание вверх
    public float explosionDamage = 50f;
    public AudioClip explosionSound;

    private bool exploded = false;

    void Start()
    {
        currentHealth = maxHealth;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (exploded) return;

        // Сила удара = импульс столкновения
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

    void Explode()
    {
        exploded = true;

        // 1. Эффект
        if (explosionEffect != null)
            Instantiate(explosionEffect, transform.position, Quaternion.identity);

        // 2. Звук
        if (explosionSound != null)
            AudioSource.PlayClipAtPoint(explosionSound, transform.position);

        // 3. Раскидываем машины и объекты вокруг
        Collider[] colliders = Physics.OverlapSphere(transform.position, explosionRadius);
        foreach (Collider hit in colliders)
        {
            Rigidbody rb = hit.attachedRigidbody;   // ловит Rigidbody даже у дочерних коллайдеров
            if (rb != null)
            {
                rb.AddExplosionForce(explosionForce, transform.position,
                                     explosionRadius, upwardModifier, ForceMode.Impulse);
            }

            // цепная реакция с другими бочками
            ExplosiveBarrel barrel = hit.GetComponent<ExplosiveBarrel>();
            if (barrel != null && barrel != this)
                barrel.TakeDamage(explosionDamage);

            // урон машине, если есть своя система здоровья
            // hit.GetComponentInParent<CarHealth>()?.TakeDamage(explosionDamage);
        }

        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}