using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public void InitializeCamera()
    {
        Debug.Log(CinemachineFind.Instance != null ? "cinemachine found" : "cinemachine not found");
        CinemachineFind.Instance.freeLookCamera.Target.TrackingTarget = gameObject.transform;
    }
}