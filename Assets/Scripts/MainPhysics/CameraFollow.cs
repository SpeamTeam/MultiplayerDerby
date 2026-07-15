using NUnit.Framework;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace Assets.Scripts.MainPhysics
{
    public class CameraFollow : MonoBehaviour
    {

        // [Header("Settings")]
        // [SerializeField] private float positionSmooth = 10f;
        // [SerializeField] private float rotationSmooth = 3f;

        // private GameObject playerObject;

        private CinemachineCamera freeCamera;
        private CinemachineOrbitalFollow orbitalFollow;

        public void InitializeCamera(GameObject player)
        {
            StartCoroutine(InitializeCameraWhenReady(player));
        }
        private IEnumerator InitializeCameraWhenReady(GameObject player)
        {
            // ждём, пока синглтон реально появится 
            while (CinemachineFind.Instance == null)
                yield return null;

            var instance = CinemachineFind.Instance;
            var config = GameManager.Instance.Config;

            freeCamera = instance.freeLookCamera;
            orbitalFollow = freeCamera.GetComponent<CinemachineOrbitalFollow>();

            freeCamera.Target.TrackingTarget = transform;
            orbitalFollow.RadialAxis.Value = config.distance;
        }

        // private void LateUpdate()
        // {
        //     if (playerObject == null) return;

        //     transform.SetPositionAndRotation(
        //         Vector3.Lerp(transform.position, playerObject.transform.position, positionSmooth * Time.deltaTime),
        //         Quaternion.Lerp(transform.rotation, playerObject.transform.rotation, rotationSmooth * Time.deltaTime)
        //     );
        // }

        private void LateUpdate()
        {
        }
    }
}