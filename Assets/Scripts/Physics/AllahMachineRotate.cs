using UnityEngine;
using System.Collections;

public class CrateSpawner : MonoBehaviour
{
    [Header("Крышка")]
    public Transform lid;               // сюда перетащи Pivot
    public Vector3 lidOpenAngle = new Vector3(-100f, 0f, 0f); // угол откидывания
    public float lidOpenSpeed = 2f;

    [Header("Бочки")]
    public GameObject barrelPrefab;          // префаб бочки (ExplosiveBarrel)
    public Transform spawnPoint;             // откуда появляются
    public int barrelCount = 4;
    public float spawnInterval = 0.3f;       // задержка между бочками
    public float pushForce = 3f;             // сила выкатывания наружу
    public Vector3 pushDirection = Vector3.forward; // направление выкатывания (локальное)

    [Header("Триггер")]
    public bool openOnStart = false;         // открыть сразу при старте
    public float minImpactToOpen = 5f;       // сила удара машины для открытия

    private bool opened = false;

    void Start()
    {
        if (openOnStart)
            Open();
    }

    // Открытие от удара машины (если ящик должна вскрывать машина)
    void OnCollisionEnter(Collision collision)
    {
        if (opened) return;
        if (collision.relativeVelocity.magnitude < minImpactToOpen) return;
        Open();
    }

    public void Open()
    {
        if (opened) return;
        opened = true;
        StartCoroutine(OpenSequence());
    }

    private IEnumerator OpenSequence()
    {
        // 1. Отодвигаем крышку
      // вместо сдвига — откидывание крышки поворотом
if (lid != null)
{
    Quaternion start = lid.localRotation;
            Quaternion target = start * Quaternion.Euler(lidOpenAngle); // угол откидывания
            float t = 0f;
    while (t < 1f)
    {
        t += Time.deltaTime * lidOpenSpeed;
        lid.localRotation = Quaternion.Slerp(start, target, t);
        yield return null;
    }
}

        // 2. Спавним бочки по одной
        for (int i = 0; i < barrelCount; i++)
        {
            SpawnBarrel();
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    private void SpawnBarrel()
    {
        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;

        GameObject barrel = Instantiate(barrelPrefab, pos, Quaternion.identity);

        // толкаем бочку наружу
        Rigidbody rb = barrel.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // направление в мировых координатах относительно ящика
            Vector3 worldDir = transform.TransformDirection(pushDirection.normalized);
            rb.AddForce(worldDir * pushForce, ForceMode.Impulse);
        }
    }

    // визуализация в редакторе
    void OnDrawGizmosSelected()
    {
        if (spawnPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(spawnPoint.position, 0.3f);
            Gizmos.color = Color.cyan;
            Vector3 dir = transform.TransformDirection(pushDirection.normalized);
            Gizmos.DrawLine(spawnPoint.position, spawnPoint.position + dir * 2f);
        }
    }
}