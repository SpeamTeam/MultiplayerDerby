using UnityEngine;

namespace Assets.Scripts.Network.Spawn
{

    // TODO: Probably need to change to NetworkBehavior and synchronize between clients
    public class SpawnPointsScript : MonoBehaviour
    {
        [SerializeField] private Transform[] spawnPoints;
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
        }
        public Transform[] getSpawnPoints() { return spawnPoints; }
    }
}