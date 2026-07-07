using UnityEngine;

// TODO: Probably need to change to NetworkBehavior and synchronize between clients
public class SpawnPointsScript : MonoBehaviour
{
    [SerializeField] private Transform[] spawnPoints;
    public static SpawnPointsScript Instance { get; private set;  }


    private void Awake()
    {
        if (Instance != this)
        {
            Destroy(gameObject);
        }
        Instance = this;
    }
    public Transform[] getSpawnPoints() { return spawnPoints; }
}
