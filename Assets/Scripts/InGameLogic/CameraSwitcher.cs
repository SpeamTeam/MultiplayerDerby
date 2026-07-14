using UnityEngine;
using UnityEngine.InputSystem;   // нова€ система

public class CameraSwitcher : MonoBehaviour
{
    public Camera[] cameras;
    private int currentIndex = 0;

    void Start()
    {
        ActivateCamera(currentIndex);
    }

    void Update()
    {
        if (Mouse.current == null) return;

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
}