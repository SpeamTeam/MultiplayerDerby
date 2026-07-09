using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public class WheelSetupScript : MonoBehaviour
{
    [Range(0f, 1f)]
    [SerializeField] private float suspensionDistance = .3f;

    [Range(0f, 1f)]
    [SerializeField] private float forwardFrictionRear = .5f;
    [Range(0f, 1f)]
    [SerializeField] private float forwardFrictionFront = .5f;

    [Range(0f, 1f)]
    [SerializeField] private float sidewaysFrictionRear = .75f;

    [Range(0f, 1f)]
    [SerializeField] private float sidewaysFrictionFront = .75f;

    [Header("Wheels Colliders")]
    [SerializeField] private WheelCollider wheelFL;
    [SerializeField] private WheelCollider wheelFR;
    [SerializeField] private WheelCollider wheelRL;
    [SerializeField] private WheelCollider wheelRR;

    public List<WheelCollider> wheelColliders;
    public List<Transform> wheelTransforms;

    [Header("Wheel Transforms")]
    [SerializeField] private Transform visualFL;
    [SerializeField] private Transform visualFR;
    [SerializeField] private Transform visualRL;
    [SerializeField] private Transform visualRR;

    void Start()
    {
        wheelColliders.Add(wheelFL);
        wheelColliders.Add(wheelFR);
        wheelColliders.Add(wheelRL);
        wheelColliders.Add(wheelRR);

        wheelTransforms.Add(visualFL);
        wheelTransforms.Add(visualFR);
        wheelTransforms.Add(visualRL);
        wheelTransforms.Add(visualRR);

        ApplySettingsFront(wheelFL); 
        ApplySettingsFront(wheelFR); 
        ApplySettingsRear(wheelRL); 
        ApplySettingsRear(wheelRR); 
    }

    void ApplySettingsRear(WheelCollider wheelCollider)
    {
        var forwardCurve = wheelCollider.forwardFriction;
        var sidewaysCurve = wheelCollider.sidewaysFriction;

        forwardCurve.asymptoteValue = forwardFrictionRear;
        sidewaysCurve.asymptoteValue = sidewaysFrictionRear;

        wheelCollider.forwardFriction = forwardCurve;
        wheelCollider.sidewaysFriction = sidewaysCurve;

        wheelCollider.suspensionDistance = suspensionDistance;
    }
    void ApplySettingsFront(WheelCollider wheelCollider)
    {
        var forwardCurve = wheelCollider.forwardFriction;
        var sidewaysCurve = wheelCollider.sidewaysFriction;

        forwardCurve.asymptoteValue = forwardFrictionFront;
        sidewaysCurve.asymptoteValue = sidewaysFrictionFront;

        wheelCollider.forwardFriction = forwardCurve;
        wheelCollider.sidewaysFriction = sidewaysCurve;

        wheelCollider.suspensionDistance = suspensionDistance;
    }
    
}
