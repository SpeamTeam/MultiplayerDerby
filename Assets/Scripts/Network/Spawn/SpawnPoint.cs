using UnityEngine;
using System.Collections.Generic;
using System;

namespace Assets.Scripts.Network.Spawn
{
    public class SpawnPoint : MonoBehaviour
    {
        [SerializeField] private LayerMask carLayer;

        // TODO: Need to rethink this approach. I like it
        // public static event Action<SpawnPoint> OnSpawnPointCreated;
        private readonly HashSet<Collider> occupants = new HashSet<Collider>();

        public bool IsOccupied => occupants.Count > 0;


        // private void Awake()
        // {
        //     OnSpawnPointCreated?.Invoke(this);
        // }
        private void OnTriggerEnter(Collider other)
        {
            if (((1 << other.gameObject.layer) & carLayer) == 0)
                return;
            occupants.Add(other);
        }

        private void OnTriggerExit(Collider other)
        {
            occupants.Remove(other);
        }
    }
}
