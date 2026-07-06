using UnityEngine;

public class SpawnPointsScript : MonoBehaviour
{
    [SerializeField] private Transform[] spawnPoints;

    public Transform[] getSpawnPoints() { return spawnPoints; }
}
