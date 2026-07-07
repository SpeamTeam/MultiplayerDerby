using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NavMeshBot : MonoBehaviour
{
    [Header("Настройки цели")]
    [SerializeField] private Transform targetPoint;
    [SerializeField] private bool moveOnStart = true;

    [Header("Настройки агента")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float stoppingDistance = 0.5f;
    [SerializeField] private float angularSpeed = 120f;
    [SerializeField] private float acceleration = 8f;

    [Header("Визуализация")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private Color gizmoColor = Color.green;

    private NavMeshAgent agent;
    private bool isMoving = false;

    // Публичные свойства для отслеживания состояния
    public bool IsMoving => isMoving;
    public bool HasReachedTarget => !agent.pathPending
                                    && agent.remainingDistance <= agent.stoppingDistance;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        SetupAgent();
    }

    private void Start()
    {
        if (moveOnStart && targetPoint != null)
        {
            MoveToTarget(targetPoint.position);
        }
    }

    private void Update()
    {
        CheckArrival();
    }

    /// <summary>
    /// Настройка параметров NavMeshAgent
    /// </summary>
    private void SetupAgent()
    {
        agent.speed = moveSpeed;
        agent.stoppingDistance = stoppingDistance;
        agent.angularSpeed = angularSpeed;
        agent.acceleration = acceleration;
    }

    /// <summary>
    /// Отправить бота к целевой точке
    /// </summary>
    public void MoveToTarget(Vector3 destination)
    {
        if (agent == null) return;

        bool pathFound = agent.SetDestination(destination);

        if (pathFound)
        {
            isMoving = true;
            Debug.Log($"[NavMeshBot] Движение к точке: {destination}");
        }
        else
        {
            Debug.LogWarning("[NavMeshBot] Путь не найден!");
        }
    }

    /// <summary>
    /// Установить новую целевую точку через Inspector и начать движение
    /// </summary>
    public void MoveToTargetPoint()
    {
        if (targetPoint != null)
        {
            MoveToTarget(targetPoint.position);
        }
        else
        {
            Debug.LogWarning("[NavMeshBot] Target Point не назначен!");
        }
    }

    /// <summary>
    /// Остановить бота
    /// </summary>
    public void StopMovement()
    {
        if (agent != null)
        {
            agent.ResetPath();
            isMoving = false;
            Debug.Log("[NavMeshBot] Бот остановлен");
        }
    }

    /// <summary>
    /// Проверка достижения цели
    /// </summary>
    private void CheckArrival()
    {
        if (!isMoving) return;

        if (HasReachedTarget)
        {
            isMoving = false;
            Debug.Log("[NavMeshBot] Цель достигнута!");
            OnTargetReached();
        }
    }

    /// <summary>
    /// Вызывается при достижении цели
    /// </summary>
    private void OnTargetReached()
    {
        // Здесь можно добавить свою логику (анимации, события и т.д.)
    }

    /// <summary>
    /// Обновить цель если она движется
    /// </summary>
    public void UpdateTargetPosition()
    {
        if (targetPoint != null && isMoving)
        {
            agent.SetDestination(targetPoint.position);
        }
    }

    // ==================== GIZMOS ====================

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        DrawTargetGizmo();
        DrawPathGizmo();
    }

    private void DrawTargetGizmo()
    {
        if (targetPoint == null) return;

        Gizmos.color = gizmoColor;

        // Сфера на цели
        Gizmos.DrawWireSphere(targetPoint.position, 0.5f);

        // Линия от бота до цели
        Gizmos.DrawLine(transform.position, targetPoint.position);

        // Крест на цели
        float crossSize = 0.3f;
        Gizmos.DrawLine(
            targetPoint.position + Vector3.left * crossSize,
            targetPoint.position + Vector3.right * crossSize
        );
        Gizmos.DrawLine(
            targetPoint.position + Vector3.forward * crossSize,
            targetPoint.position + Vector3.back * crossSize
        );
    }

    private void DrawPathGizmo()
    {
        if (agent == null || !agent.hasPath) return;

        Gizmos.color = Color.yellow;

        Vector3[] corners = agent.path.corners;

        for (int i = 0; i < corners.Length - 1; i++)
        {
            Gizmos.DrawLine(corners[i], corners[i + 1]);
            Gizmos.DrawWireSphere(corners[i], 0.1f);
        }
    }
}