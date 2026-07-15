using System.Collections.Generic;
using UnityEngine;

public class AITargetManager : MonoBehaviour
{
    public static AITargetManager Instance;
    
    public List<GameObject> targets;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }
    
    public void AddTarget(GameObject target)
    {
        targets.Add(target);
    }

}
