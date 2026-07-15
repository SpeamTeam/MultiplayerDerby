using UnityEngine;
using UnityEngine.Rendering;

public class GlobalVolumeProvider : MonoBehaviour
{
    public static GlobalVolumeProvider Instance { get; private set; }

    [SerializeField] private Volume globalVolume;
    public Volume GlobalVolume => globalVolume;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
}