using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerSpawnManager : NetworkBehaviour
{
    public static PlayerSpawnManager Instance { get; private set; }

    [Header("Spawn Settings")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private SpawnPointsScript spawnPointsScript;
    private Transform[] spawnPoints;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        spawnPoints = spawnPointsScript.getSpawnPoints();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // Подписываемся на событие завершения загрузки сцены
        NetworkManager.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.SceneManager != null)
        {
            NetworkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
        }
    }

    private void OnSceneLoadCompleted(
        string sceneName, 
        UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, 
        List<ulong> clientsCompleted, 
        List<ulong> clientsTimedOut)
    {
        // Спавним игрока только для тех, кто загрузил нужную сцену
        if (sceneName != "WorldScene") return;

        foreach (ulong clientId in clientsCompleted)
        {
            SpawnPlayer(clientId);
        }
    }

    private void SpawnPlayer(ulong clientId)
    {
        Vector3 spawnPos = GetSpawnPoint();
        
        GameObject playerInstance = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
        
        NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
        networkObject.SpawnAsPlayerObject(clientId);
        
        Debug.Log($"Player spawned for client {clientId} at {spawnPos}");
    }

    private Vector3 GetSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            return Vector3.zero;
        }

        int randomIndex = Random.Range(0, spawnPoints.Length);
        return spawnPoints[randomIndex].position;
    }
}
