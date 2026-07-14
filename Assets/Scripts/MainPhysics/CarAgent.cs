using Assets.Scripts.AI;
using Assets.Scripts.Network;
using Assets.Scripts.Network.Spawn;
using Assets.Scripts.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// "Мозг" одной машины: связывает CarHealth + PlayerScore + управление + респавн.
/// Подписывается на события здоровья и раздаёт очки нужным сторонам.
///
/// Почему отдельный класс, а не всё в CarHealth:
/// CarHealth должен оставаться тупым хранилищем HP. Вся ГЕЙМПЛЕЙНАЯ РЕАКЦИЯ
/// (начислить очки, отключить управление) живёт здесь.
///
/// СЕТЬ: сам факт респавна теперь заказывает CarHealth — он на смерти (на сервере)
/// зовёт GameManager.HandleCarDeath, тот через задержку из GameConfig зовёт
/// NetworkProvider.Respawn(clientId), а NetworkProvider уже находит нужный CarAgent
/// (через регистрацию ниже) и вызывает ServerRespawn(). HandleDied здесь больше
/// НЕ планирует респавн сам — иначе он выполнялся бы независимо на каждом пире.
/// </summary>
[RequireComponent(typeof(CarHealth))]
[RequireComponent(typeof(PlayerScore))]
// [RequireComponent(typeof(DriverEjection))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerCarController))]
[RequireComponent(typeof(CameraFollow))]
// [RequireComponent (typeof(PlayerInput))]
public class CarAgent : NetworkBehaviour
{
    [Header("Ссылки")]
    [SerializeField] private PlayerCarController controller;      // чтобы отключать управление при смерти
    public Rigidbody rb;

    private CarHealth health;
    private PlayerScore score;
    private PlayerInput input;
    private CarCollision carCollision; // может отсутствовать — деформация/отпадение колёс необязательны
    // private DriverEjection driverEjection;

    // TODO: delete after debug hud become useless
    public WheelSetupScript wheelSetup;

    private void Awake()
    {
        health = GetComponent<CarHealth>();
        score = GetComponent<PlayerScore>();
        carCollision = GetComponent<CarCollision>();
        // driverEjection = GetComponent<DriverEjection>(); // может отсутствовать — необязательный компонент
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (controller == null) controller = GetComponent<PlayerCarController>();
    }

    public override void OnNetworkSpawn()
    {
        // Список только для настоящих игроков (таргетинг ботов, статистика и т.п.) —
        // боты туда не попадают. Респавн по смерти теперь не завязан на этот список
        // (см. CarHealth.Die → GameManager.HandleCarDeath → NetworkProvider.RespawnObject),
        // поэтому регистрация тут не критична для респавна, но семантика "playersList"
        // должна оставаться честной.
        if (IsServer && !IsBotControlled && NetworkProvider.Instance != null)
            NetworkProvider.Instance.RegisterPlayer(this);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && !IsBotControlled && NetworkProvider.Instance != null)
            NetworkProvider.Instance.UnregisterPlayer(this);
    }

    private bool IsBotControlled => controller != null && controller.IsBotControlled;

    private void Start()
    {
        // Ботов CarNavMeshAgent помечает через PlayerCarController.ConfigureAsBot().
        // Без этой проверки бот (у которого по умолчанию OwnerClientId == серверу)
        // на хосте прошёл бы IsOwner-проверку и увёл бы камеру/паузу с реального игрока.
        if (!IsOwner || IsBotControlled) return;

        CameraFollow cameraFollow = GetComponent<CameraFollow>();
        if (cameraFollow != null) cameraFollow.InitializeCamera();

        input = GetComponent<PlayerInput>();

        PauseMenuScript.Instance.MenuDeactivated += SetActiveInput;
    }

    private void OnEnable()
    {
        health.OnHealthChanged += HandleHealthChanged;
        health.OnDied += HandleDied;
    }

    private void OnDisable()
    {
        health.OnHealthChanged -= HandleHealthChanged;
        health.OnDied -= HandleDied;
    }

    private void HandleHealthChanged(float current, float normalized)
    {
        // Хук для UI/звука получения урона. Начисление очков за урон
        // делает детектор атакующего через RegisterDamageDealt.
    }

    /// <summary>
    /// Вызывается на машине АТАКУЮЩЕГО, когда она нанесла урон.
    /// Детектор столкновений вызывает это на своей стороне.
    /// </summary>
    public void RegisterDamageDealt(float amount)
    {
        score.AddDamageScore(amount);
    }

    private void HandleDied(CarHealth attacker)
    {
        // Отключаем управление и глушим машину. Респавн CarHealth закажет отдельно
        // (см. комментарий класса) — здесь его планировать больше не нужно.
        if (controller != null) controller.enabled = false;

        // Начисляем килл атакующему (если это не самоуничтожение).
        if (attacker != null && attacker != health)
        {
            var killerAgent = attacker.GetComponent<CarAgent>();
            if (killerAgent != null)
                killerAgent.score.AddKill();
        }
    }

    /// <summary>
    /// Фактически перемещает и оживляет машину. Авторитативно — вызывать
    /// только на сервере (только NetworkProvider.Respawn должен его звать).
    /// </summary>
    public void ServerRespawn()
    {
        if (!IsServer) return;

        GetRespawnPose(out Vector3 spawnPos, out Quaternion spawnRot);

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(spawnPos, spawnRot);

        health.ResetState();
        // driverEjection?.ResetState();
        score.OnRespawn(); // сбрасывает коэффициент за время жизни, totalScore не трогает
        carCollision?.ServerNotifyRespawn(); // откатывает деформацию меша и возвращает отпавшие колёса

        if (controller != null) controller.enabled = true;
        Debug.Log("SOME PLAYER RESPAWNED!");
    }

    private void GetRespawnPose(out Vector3 position, out Quaternion rotation)
    {
        var spawnPoints = SpawnPointsScript.Instance != null ? SpawnPointsScript.Instance.getSpawnPoints() : null;

        if (spawnPoints != null && spawnPoints.Count > 0)
        {
            foreach (var point in spawnPoints)
            {
                if (point == null || point.IsOccupied) continue;
                position = point.transform.position;
                rotation = point.transform.rotation;
                return;
            }

            // Все точки заняты — используем первую, лишь бы не блокировать респавн намертво.
            position = spawnPoints[0].transform.position;
            rotation = spawnPoints[0].transform.rotation;
            return;
        }

        // Заглушка на случай отсутствия точек спавна в сцене (например, в тестовой сцене).
        position = transform.position + Vector3.up * 1f;
        rotation = Quaternion.identity;
    }

    public void ActivatePauseMenu()
    {
        var pauseMenu = PauseMenuScript.Instance;
        if (!pauseMenu.gameObject.activeSelf)
        {
            SetActiveInput(false);
            pauseMenu.gameObject.SetActive(true);
        }
        else
        {
            SetActiveInput(true);
            pauseMenu.gameObject.SetActive(false);
        }
    }
    public void SetActiveInput(bool flag)
    {
        if (input != null)
            input.enabled = flag;
    }
}
