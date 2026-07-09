using UnityEngine;

public class ForceAdding : MonoBehaviour
{
    [Header("Настройки силы")]
    [SerializeField] private Vector3 direction = Vector3.forward;
    [SerializeField] private float strength = 10f;
    [SerializeField] private ForceMode forceMode = ForceMode.Force;

    [Header("Управление")]
    [SerializeField] private bool applyOnStart = false;
    [SerializeField] private bool applyOnKey = true;
    [SerializeField] private KeyCode applyKey = KeyCode.Space;
    [SerializeField] private bool normalizeDirection = true;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (rb == null)
            Debug.LogError($"[ForceApplier] Rigidbody не найден на объекте {gameObject.name}!");
    }

    private void Start()
    {
        if (applyOnStart)
            ApplyForce();
    }

    private void Update()
    {
        if (applyOnKey && Input.GetKeyDown(applyKey))
            ApplyForce();
    }

    public void ApplyForce()
    {
        if (rb == null) return;

        Vector3 forceVector = normalizeDirection
            ? direction.normalized * strength
            : direction * strength;

        rb.AddForce(forceVector, forceMode);

        Debug.Log($"[ForceApplier] Применена сила: {forceVector}, режим: {forceMode}");
    }

    // Визуализация направления силы в редакторе
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 forceDir = normalizeDirection ? direction.normalized : direction;
        Gizmos.DrawRay(transform.position, forceDir * 2f);
    }
}