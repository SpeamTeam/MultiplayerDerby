using UnityEngine;

public class RagdollSolverSettings : MonoBehaviour
{
    [Header("Solver Iterations")]
    [Tooltip("Position solver iterations (Unity default = 6)")]
    [SerializeField] private int solverIterations = 20;

    [Tooltip("Velocity solver iterations (Unity default = 1)")]
    [SerializeField] private int solverVelocityIterations = 8;

    [Header("Доп. настройки стабильности (опционально)")]
    [SerializeField] private bool overrideMaxAngularVelocity = true;
    [SerializeField] private float maxAngularVelocity = 25f;

    [SerializeField] private bool overrideSleepThreshold = false;
    [SerializeField] private float sleepThreshold = 0.005f;

    [Header("Область поиска")]
    [Tooltip("Включать неактивные дочерние объекты в поиск")]
    [SerializeField] private bool includeInactive = true;

    [ContextMenu("Apply Solver Settings Now")]
    public void ApplySolverSettings()
    {
        Rigidbody[] rigidbodies = GetComponentsInChildren<Rigidbody>(includeInactive);

        foreach (Rigidbody rb in rigidbodies)
        {
            rb.solverIterations = solverIterations;
            rb.solverVelocityIterations = solverVelocityIterations;

            if (overrideMaxAngularVelocity)
                rb.maxAngularVelocity = maxAngularVelocity;

            if (overrideSleepThreshold)
                rb.sleepThreshold = sleepThreshold;
        }

        Debug.Log($"[RagdollSolverSettings] Применено к {rigidbodies.Length} Rigidbody на объекте '{gameObject.name}'.");
    }

    private void Awake()
    {
        ApplySolverSettings();
    }
}