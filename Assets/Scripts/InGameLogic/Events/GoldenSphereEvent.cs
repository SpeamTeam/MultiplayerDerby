using UnityEngine;
using Unity.Netcode;

[CreateAssetMenu(fileName = "GoldenSphereEvent", menuName = "Events/Golden Sphere Event")]
public class GoldenSphereEvent : GameEventBehaviour
{
    [Header("Префаб (должен быть добавлен в NetworkManager -> NetworkPrefabs)")]
    [SerializeField] private GameObject goldenSpherePrefab;

    [Header("Зона спавна")]
    [SerializeField] private Vector3 spawnAreaCenter = Vector3.zero;
    [SerializeField] private Vector3 spawnAreaSize = new Vector3(20f, 0f, 20f);
    [SerializeField] private float spawnHeight = 1f;

    private NetworkObject _spawnedInstance;

    public override void OnServerStart(EventManager ctx)
    {
        if (goldenSpherePrefab == null)
        {
            Debug.LogError("[GoldenSphereEvent] Префаб шарика не назначен!");
            return;
        }

        Vector3 randomOffset = new Vector3(
            Random.Range(-spawnAreaSize.x / 2f, spawnAreaSize.x / 2f),
            0f,
            Random.Range(-spawnAreaSize.z / 2f, spawnAreaSize.z / 2f)
        );

        Vector3 spawnPos = spawnAreaCenter + randomOffset + Vector3.up * spawnHeight;

        GameObject instance = Object.Instantiate(goldenSpherePrefab, spawnPos, Quaternion.identity);
        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[GoldenSphereEvent] На префабе отсутствует NetworkObject!");
            Object.Destroy(instance);
            return;
        }

        netObj.Spawn(true);
        _spawnedInstance = netObj;

        Debug.Log("[GoldenSphereEvent] Golden Sphere заспавнен на сервере.");
    }

    public override void OnServerEnd(EventManager ctx)
    {
        // На случай, если EventManager завершит ивент раньше,
        // чем шарик сам себя деспавнит по своему таймеру
        if (_spawnedInstance != null && _spawnedInstance.IsSpawned)
        {
            _spawnedInstance.Despawn(true);
        }
        _spawnedInstance = null;
    }
}