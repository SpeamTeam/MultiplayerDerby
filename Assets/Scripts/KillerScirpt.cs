using UnityEngine;

public class KillerScirpt : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if(other.gameObject.layer != LayerMask.NameToLayer("Car")) return;
        other.gameObject.GetComponentInParent<CarHealth>().ApplyDamage(100);
        Debug.Log("player flew out");
    }
}
