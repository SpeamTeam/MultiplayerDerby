using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class VolumeGradeUtility
{
    public static IEnumerator LerpFloatParam(VolumeParameter<float> param, float to, float duration)
    {
        param.overrideState = true;
        float from = param.value;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            param.value = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        param.value = to;
    }

    public static IEnumerator LerpColorParam(ColorParameter param, Color to, float duration)
    {
        param.overrideState = true;
        Color from = param.value;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            param.value = Color.Lerp(from, to, t / duration);
            yield return null;
        }
        param.value = to;
    }
}