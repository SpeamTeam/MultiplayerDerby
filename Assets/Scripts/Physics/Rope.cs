using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class RopeLine : MonoBehaviour
{
    [Header("Точки крепления")]
    public Transform dronePoint;   // фиксированная точка на дроне (CrateAttachPoint)
    public Transform cratePoint;   // фиксированная точка в центре ящика

    private LineRenderer line;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
    }

    void LateUpdate()
    {
        if (dronePoint == null || cratePoint == null) return;

        line.SetPosition(0, dronePoint.position);  // верх — дрон
        line.SetPosition(1, cratePoint.position);  // низ — ящик
    }
}