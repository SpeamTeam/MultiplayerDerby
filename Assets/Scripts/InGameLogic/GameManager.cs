using Assets.Scripts;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    static public GameManager Instance { get; private set; }
    public GameConfig Config;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // things we need on game start 
        BootstrapCamera();
    }


    void BootstrapCamera()
    {
        Instantiate(Config.cameraPrefab);

    }
}
