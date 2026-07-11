using UnityEngine;
using System.Collections.Generic;

namespace Assets.Scripts.Network.Spawn
{

    // TODO: Probably need to change to NetworkBehavior and synchronize between clients
    public class SpawnPointsScript : MonoBehaviour
    {
        [SerializeField] private List<SpawnPoint> spawnPoints;
        public static SpawnPointsScript Instance { get; private set; }


        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.Log(gameObject + "is being destroyd");
                Destroy(gameObject);
                return;
            }
            Instance = this;


            // SpawnPoint.OnSpawnPointCreated += AddSpawnPoint;
        }
        public List<SpawnPoint> getSpawnPoints() { return spawnPoints; }

        public void AddSpawnPoint(SpawnPoint point)
        {
            spawnPoints.Add(point);
        }
    }
}
