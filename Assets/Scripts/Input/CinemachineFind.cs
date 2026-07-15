using Unity.Cinemachine;
using UnityEngine;

public class CinemachineFind : MonoBehaviour
{
    static public CinemachineFind Instance { get; private set; }

    public CinemachineCamera freeLookCamera;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Debug.Log("[CinemachineFind] instance assigned");
    }
}
