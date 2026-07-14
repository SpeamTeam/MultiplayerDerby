using System.Collections;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.AI;
using Assets.Scripts.Network;
using Assets.Scripts.Network.Spawn;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Does things only for local player. Does not really affect the multiplayer and in-game interactions
/// </summary>
public class GameManager : MonoBehaviour
{
    static public GameManager Instance { get; private set; }
    public GameConfig Config;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // things we need on game start
        BootstrapCamera();
        BootstrapPauseMenu();
        BootstrapScoreMenu();
    }

    // Bootstrapping  methods {
    void BootstrapCamera()
    {
        Instantiate(Config.cameraPrefab);
    }

    void BootstrapPauseMenu()
    {
        var obj = Instantiate(Config.pauseMenuPrefab, Vector3.zero, Quaternion.identity);
        obj.SetActive(false);
    }

    void BootstrapScoreMenu()
    {
        Instantiate(Config.scoreMenuPrefab);
    }
    // Bootstrapping methods }

    /// <summary>
    /// Спавнит бота на сервере в любой свободной точке спавна (та же логика, что у
    /// CarAgent.GetRespawnPose — первая незанятая SpawnPoint, иначе первая по списку).
    /// Ставит боту фиксированную цель (другая точка спавна) — только чтобы было видно,
    /// что он вообще едет; полноценный выбор цели (ближайший игрок и т.п.) — отдельная задача.
    /// </summary>
    // TODO: Remove this and make bots spawn via in-game interactions
    [ContextMenu("Spawn Bot")]
    public void SpawnBot()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[GameManager] SpawnBot: спавнить бота можно только на сервере.");
            return;
        }

        if (Config == null || Config.botPrefab == null)
        {
            Debug.LogWarning("[GameManager] SpawnBot: botPrefab не назначен в GameConfig.");
            return;
        }

        var spawnPoints = SpawnPointsScript.Instance != null ? SpawnPointsScript.Instance.getSpawnPoints() : null;
        SpawnPoint spawnPoint = PickFreeSpawnPoint(spawnPoints);

        Vector3 spawnPos = spawnPoint != null ? spawnPoint.transform.position : Vector3.up;
        Quaternion spawnRot = spawnPoint != null ? spawnPoint.transform.rotation : Quaternion.identity;

        GameObject botInstance = Instantiate(Config.botPrefab, spawnPos, spawnRot);
        NetworkObject netObj = botInstance.GetComponent<NetworkObject>();
        netObj.Spawn(); // без clientId => владелец — сервер (см. PlayerCarController.ConfigureAsBot)

        CarNavMeshAgent navAgent = botInstance.GetComponent<CarNavMeshAgent>();
        if (navAgent != null && spawnPoints != null && spawnPoints.Count > 0)
        {
            SpawnPoint targetPoint = spawnPoints.Find(p => p != spawnPoint) ?? spawnPoints[0];
            navAgent.SetTarget(targetPoint.transform);
        }
    }

    private SpawnPoint PickFreeSpawnPoint(List<SpawnPoint> spawnPoints)
    {
        if (spawnPoints == null || spawnPoints.Count == 0) return null;

        foreach (var point in spawnPoints)
        {
            if (point != null && !point.IsOccupied)
                return point;
        }

        // Все точки заняты — берём любую, лишь бы не блокировать спавн намертво.
        return spawnPoints[0];
    }

}
