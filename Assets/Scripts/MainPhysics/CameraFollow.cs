using Unity.Cinemachine;
using UnityEngine;

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
    //     playerObject = player;

        Debug.Log("[CameraFollow] " + CinemachineFind.Instance != null ? "cinemachine found" : "cinemachine not found");
        var config = GameManager.Instance.Config;

        freeCamera = CinemachineFind.Instance.freeLookCamera;
        orbitalFollow = freeCamera.GetComponent<CinemachineOrbitalFollow>();

        freeCamera.Target.TrackingTarget = gameObject.transform;

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