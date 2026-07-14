using UnityEngine;
using System.Collections;

public class DroneAnimator : MonoBehaviour {
	Transform[] Props;
	public float propSpeed = 0;
	Transform trans;
	public float bobSpeed;
	public float bobHeight;
	public float wobble;
	public float wobbleSpeed;

    // Use this for initialization
    void Start()
    {
        trans = transform;
        var found = new System.Collections.Generic.List<Transform>();
        foreach (Transform child in transform)
        {
            if (child.name.ToLower().Contains("motor") && child.childCount > 0)
            {
                found.Add(child.GetChild(0));   // это Prop
            }
        }
        Props = found.ToArray();
    }

    void Update()
    {
        foreach (Transform prop in Props)
        {
            if (prop == null) continue;
            prop.Rotate(0, 0, propSpeed * Time.deltaTime);
        }
        Vector3 pos = trans.localPosition;
		pos.y = Mathf.Sin (Time.time * bobSpeed) * bobHeight;
		trans.localPosition = pos;

		Vector3 rot = trans.localEulerAngles;
		rot.x = 270 +  Mathf.Sin (Time.time * wobbleSpeed) * wobble;
		rot.z = Mathf.Cos (Time.time * wobbleSpeed) * wobble;
		rot.y = 0;
		trans.localEulerAngles = rot;
	}
}
