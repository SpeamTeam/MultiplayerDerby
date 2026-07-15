using Assets.Scripts.AI;
using Assets.Scripts.Network;
using Assets.Scripts.Network.Spawn;
using Assets.Scripts.UI;
using Unity.Collections;
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
// [RequireComponent(typeof(DriverEjection))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerCarController))]
[RequireComponent(typeof(CameraFollow))]
// [RequireComponent (typeof(PlayerInput))]
public class CarAgent : NetworkBehaviour
{
    [Header("Ссылки")]
    [SerializeField] private PlayerCarController controller;      // чтобы отключать управление при смерти
    [SerializeField] private GameObject RagDollPrefab;
    public Rigidbody rb;

    private CarHealth health;
    private PlayerInput input;
    private CarCollision carCollision; // может отсутствовать — деформация/отпадение колёс необязательны
    // private DriverEjection driverEjection;

    public WheelSetupScript wheelSetup;

    public NetworkVariable<FixedString128Bytes> nickName = new NetworkVariable<FixedString128Bytes>();

    private void Awake()
    {
        health = GetComponent<CarHealth>();
        carCollision = GetComponent<CarCollision>();
        health.OnDied += DropRagdoll;
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (controller == null) controller = GetComponent<PlayerCarController>();
    }
    private void DropRagdoll(CarHealth useless)
    {
        GameObject RagDollInstance = Instantiate(RagDollPrefab, transform.position + new Vector3(-0.25f, -0.473f, -0.038f), Quaternion.identity);
    }
    public override void OnNetworkSpawn()
    {
        // Список только для настоящих игроков (таргетинг ботов, статистика и т.п.) —
        // боты туда не попадают. Респавн по смерти теперь не завязан на этот список
        // (см. CarHealth.Die → GameManager.HandleCarDeath → NetworkProvider.RespawnObject),
        // поэтому регистрация тут не критична для респавна, но семантика "playersList"
        // должна оставаться честной.
        if (IsServer && nickName.Value.IsEmpty)
        {
            var substr = "Player_" + (OwnerClientId.ToString().Length >= 5 ? OwnerClientId.ToString()[..5] : OwnerClientId.ToString());
            nickName.Value = substr.Length <= 20 ? substr : substr.Substring(0, 20);
            Debug.Log($"Nick was empty so I chosed {nickName.Value}");
        }

        if (IsServer && !IsBotControlled && NetworkProvider.Instance != null)
            NetworkProvider.Instance.RegisterPlayer(this);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && !IsBotControlled && NetworkProvider.Instance != null)
            NetworkProvider.Instance.UnregisterPlayer(this);
    }

    /// <summary>
    /// Машина под управлением бота (флаг ставит CarNavMeshAgent через ConfigureAsBot).
    /// Единственный надёжный признак: по OwnerClientId бот и хост неразличимы — у ботов
    /// владелец сервер, а на хосте это тот же ID, что и у машины самого игрока-хоста.
    /// Нужен NetworkProvider'у, чтобы выбрать ветку высадки (ручная у игрока / авто у бота).
    /// </summary>
    public bool IsBotControlled => controller != null && controller.IsBotControlled;

    private void Start()
    {
        // Ботов CarNavMeshAgent помечает через PlayerCarController.ConfigureAsBot().
        // Без этой проверки бот (у которого по умолчанию OwnerClientId == серверу)
        // на хосте прошёл бы IsOwner-проверку и увёл бы камеру/паузу с реального игрока.
        if (!IsOwner || IsBotControlled) return;

        CameraFollow cameraFollow = GetComponent<CameraFollow>();

        /// Initializing object camera interpolates to
        // var camTarget = Instantiate(GameManager.Instance.Config.cameraTargetPrefab, gameObject.transform.position, Quaternion.identity);
        // camTarget.GetComponent<CameraFollow>().InitializeCamera(gameObject);

        if (cameraFollow != null) cameraFollow.InitializeCamera(gameObject);

        input = GetComponent<PlayerInput>();

        PauseMenuScript.Instance.MenuDeactivated += SetActiveInput;
    }

    private void OnEnable()
    {
        health.OnHealthChanged += HandleHealthChanged;
        health.OnDied += HandleDied;
        health.OnRespawned += HandleRespawned;
    }

    private void OnDisable()
    {
        health.OnHealthChanged -= HandleHealthChanged;
        health.OnDied -= HandleDied;
        health.OnRespawned -= HandleRespawned;
    }

    private void HandleHealthChanged(float current, float normalized)
    {
        // Хук для UI/звука получения урона. Начисление очков за урон
        // делает детектор атакующего через RegisterDamageDealt.
    }


    private void HandleDied(CarHealth attacker)
    {
        // Отключаем управление и глушим машину. Респавн CarHealth закажет отдельно
        // (см. комментарий класса) — здесь его планировать больше не нужно.
        if (controller != null) controller.enabled = false;
    }

    // Симметрично HandleDied — срабатывает на КАЖДОМ пире (не только на сервере),
    // т.к. без этого controller.enabled навсегда остаётся false на клиентах:
    // ServerRespawn() выполняется только на сервере и его локальное
    // "controller.enabled = true" до клиентов не доходит, а других обновлений
    // компонента не было — визуальные NetworkVariable колёс переставали
    // применяться, т.к. клиентский Update() (единственное место их чтения) не тикал.
    private void HandleRespawned()
    {
        if (controller != null) controller.enabled = true;
    }

    /// <summary>
    /// Респавн в плановую точку из SpawnPointsScript. Авторитативно — вызывать
    /// только на сервере (только NetworkProvider.Respawn должен его звать).
    /// Этим путём идут fallback-ветки (кинематик выключен / нет префаба дрона) и Suicide;
    /// при кинематике машина встаёт в точку падения ящика — см. ServerRespawnAt.
    /// </summary>
    public void ServerRespawn()
    {
        if (!IsServer) return;

        GetRespawnPose(out Vector3 spawnPos, out Quaternion spawnRot);
        ServerRespawnAt(spawnPos, spawnRot);
    }

    /// <summary>
    /// Фактически перемещает и оживляет машину в ЗАДАННОЙ позе. Авторитативно — только сервер.
    /// Точку задаёт вызывающий: при кинематическом респавне это точка падения ящика,
    /// которую дрон отдаёт NetworkProvider'у в onComplete.
    /// </summary>
    public void ServerRespawnAt(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        controller.wheelFL.motorTorque = 0f;
        controller.wheelFR.motorTorque = 0f;

        transform.SetPositionAndRotation(position, rotation);

        health.ResetState(); // это же вызовет HandleRespawned на всех пирах и включит controller
        // driverEjection?.ResetState();
        carCollision?.ServerNotifyRespawn(); // откатывает деформацию меша и возвращает отпавшие колёса

        Debug.Log("SOME PLAYER RESPAWNED!");
    }

    /// <summary>
    /// Публичный доступ к будущей респавн-позе — нужен NetworkProvider'у на сервере,
    /// чтобы дрон/ящик доставлялись ровно в ту точку, где появится машина.
    /// Это только ЧТЕНИЕ выбранной точки, оно не занимает её и не меняет состояние.
    /// </summary>
    public void GetPlannedRespawnPose(out Vector3 position, out Quaternion rotation)
    {
        GetRespawnPose(out position, out rotation);
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
            pauseMenu.OnContinue();
        }
    }
    public void SetActiveInput(bool flag)
    {
        // if (input != null)
        //     input.enabled = flag;
        if (controller != null)
        {
            controller.enabled = flag;
        }
    }
}
