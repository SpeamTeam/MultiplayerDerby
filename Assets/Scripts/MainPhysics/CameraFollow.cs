using System.Collections;
using Assets.Scripts;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Привязка орбитальной камеры к машине. Компонент висит на самой машине.
///
/// СЕТЬ: камера — чисто клиентская вещь. InitializeCamera зовёт CarAgent.Start() и только
/// для локального игрока (там стоит проверка !IsOwner || IsBotControlled), поэтому ботам
/// и чужим машинам камера не достаётся.
///
/// Порядок инициализации: CinemachineFind живёт на префабе камеры, который создаёт
/// GameManager. Машина может стартовать раньше, чем этот префаб появится, поэтому привязка
/// ждёт готовности синглтона, а не падает и не сдаётся с первой попытки.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Tooltip("Сколько секунд ждать появления CinemachineFind, прежде чем сдаться и сообщить об ошибке.")]
    [SerializeField] private float bindTimeout = 5f;

    private CinemachineCamera freeCamera;
    private CinemachineOrbitalFollow orbitalFollow;

    public void InitializeCamera(GameObject player)
    {
        StartCoroutine(InitializeWhenReady());
    }

    private IEnumerator InitializeWhenReady()
    {
        // Ждём, а не падаем: камеру создаёт GameManager, и она может появиться позже нас.
        float waited = 0f;
        while (CinemachineFind.Instance == null)
        {
            if (waited >= bindTimeout)
            {
                Debug.LogError($"[CameraFollow] CinemachineFind.Instance не появился за {bindTimeout}s — камера не привязана к {name}. Проверь, что GameManager создаёт Config.cameraPrefab.");
                yield break;
            }
            waited += Time.deltaTime;
            yield return null;
        }

        freeCamera = CinemachineFind.Instance.freeLookCamera;
        if (freeCamera == null)
        {
            Debug.LogError("[CameraFollow] freeLookCamera не назначен в CinemachineFind на префабе CameraObject — камера не привязана.");
            yield break;
        }

        // Сначала главное — слежение за машиной. Всё остальное необязательно и не должно его сорвать.
        freeCamera.Target.TrackingTarget = transform;

        orbitalFollow = freeCamera.GetComponent<CinemachineOrbitalFollow>();
        if (orbitalFollow == null)
        {
            Debug.LogWarning("[CameraFollow] На freeLookCamera нет CinemachineOrbitalFollow — дистанция не задана, слежение работает.");
            yield break;
        }

        GameConfig config = GameManager.Instance != null ? GameManager.Instance.Config : null;
        if (config == null)
        {
            Debug.LogWarning("[CameraFollow] GameManager.Config недоступен — оставляю дистанцию камеры по умолчанию.");
            yield break;
        }

        orbitalFollow.RadialAxis.Value = config.distance;
    }
}
