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

    // Без этого статик переживает выгрузку сцены и указывает на уничтоженный объект
    // (WorldScene → MenuScene → WorldScene), а камера привязывается к трупу.
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
