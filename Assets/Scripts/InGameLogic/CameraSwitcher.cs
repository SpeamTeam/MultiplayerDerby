using UnityEngine;
using UnityEngine.InputSystem;   // нова€ система

public class CameraSwitcher : MonoBehaviour
{
    public Camera[] cameras;
    private int currentIndex = 0;

    public bool isActive = false;

    [SerializeField] private AudioListener audioListener;

    void Start()
    {


        if (!isActive)
        {
            DeactivateAllCameras();
            audioListener.enabled = false;
            return;
        }
        ActivateCamera(currentIndex);
    }

    void Update()
    {
        if (Mouse.current == null || !isActive) return;

        // Ћ ћ Ч следующа€
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            currentIndex = (currentIndex + 1) % cameras.Length;
            ActivateCamera(currentIndex);
        }

        // ѕ ћ Ч предыдуща€
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            currentIndex = (currentIndex - 1 + cameras.Length) % cameras.Length;
            ActivateCamera(currentIndex);
        }
    }

    void ActivateCamera(int index)
    {
        for (int i = 0; i < cameras.Length; i++)
            cameras[i].gameObject.SetActive(i == index);
    }

    // Deactivates all cameras GAMEOBJECTS(!)
    void DeactivateAllCameras()
    {
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            cam.gameObject.SetActive(false);
        }
    }
}