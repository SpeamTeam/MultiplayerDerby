using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[CreateAssetMenu(menuName = "Events/FogReveal")]
public class FogRevealEvent : GameEventBehaviour
{
    [Header("Спавн объектов")]
    [SerializeField] private List<GameObject> prefabsToSpawn; // префабы из Assets, позиция уже задана внутри префаба

    [Header("Материалы (плавное появление 0→255 за 3 сек)")]
    [SerializeField] private string colorPropertyName = "_BaseColor";
    [SerializeField] private float fadeDuration = 3f;

    [Header("Fog Density (через 1 сек после старта, за 2 сек)")]
    [SerializeField] private float fogStartDelay = 1f;
    [SerializeField] private float fogChangeDuration = 2f;
    [SerializeField] private float fogTargetDensity = 0.049f;
    [SerializeField] private float fogResetDensity = 0f;
    [Header("Общее")]
    [SerializeField] private float transitionDuration = 3f;

    [Header("Color Adjustments — целевые значения")]
    [SerializeField] private float targetPostExposure = 0.1f;
    [SerializeField] private float targetContrast = 11.3f;
    [SerializeField] private Color targetColorFilter = Color.white; // выставь HDR-цвет как на скрине
    [SerializeField] private float targetHueShift = 0f;
    [SerializeField] private float targetSaturation = 16.3f;

    [Header("Vignette — целевые значения")]
    [SerializeField] private Color targetVignetteColor = Color.black;
    [SerializeField] private float targetVignetteIntensity = 0.34f;
    [SerializeField] private float targetVignetteSmoothness = 0.58f;

    [Header("Chromatic Aberration — целевое значение")]
    [SerializeField] private float targetChromaticIntensity = 0.723f;

    // сохранённые исходные значения, чтобы вернуть при OnClientEnd
    private float _origPostExposure, _origContrast, _origHueShift, _origSaturation;
    private Color _origColorFilter;
    private Color _origVignetteColor;
    private float _origVignetteIntensity, _origVignetteSmoothness;
    private float _origChromaticIntensity;

    // храним заспавненные на этом клиенте инстансы, чтобы потом убрать
    private List<GameObject> _spawnedInstances = new List<GameObject>();

    public override void OnClientStart(EventManager ctx)
    {
        SpawnInstances();
        ctx.RunCoroutine(FadeMaterials(0f, 1f, fadeDuration));
        ctx.RunCoroutine(DelayedFog(fogStartDelay, fogTargetDensity, fogChangeDuration));
        var volume = GlobalVolumeProvider.Instance?.GlobalVolume;
        if (volume == null || volume.profile == null)
        {
            Debug.LogWarning("GlobalVolumeGradeEvent: Global Volume не найден");
            return;
        }

        if (volume.profile.TryGet(out ColorAdjustments ca))
        {
            _origPostExposure = ca.postExposure.value;
            _origContrast = ca.contrast.value;
            _origColorFilter = ca.colorFilter.value;
            _origHueShift = ca.hueShift.value;
            _origSaturation = ca.saturation.value;

            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(ca.postExposure, targetPostExposure, transitionDuration));
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(ca.contrast, targetContrast, transitionDuration));
            ctx.RunCoroutine(VolumeGradeUtility.LerpColorParam(ca.colorFilter, targetColorFilter, transitionDuration));
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(ca.hueShift, targetHueShift, transitionDuration));
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(ca.saturation, targetSaturation, transitionDuration));
        }

        if (volume.profile.TryGet(out Vignette vg))
        {
            _origVignetteColor = vg.color.value;
            _origVignetteIntensity = vg.intensity.value;
            _origVignetteSmoothness = vg.smoothness.value;

            ctx.RunCoroutine(VolumeGradeUtility.LerpColorParam(vg.color, targetVignetteColor, transitionDuration));
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(vg.intensity, targetVignetteIntensity, transitionDuration));
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(vg.smoothness, targetVignetteSmoothness, transitionDuration));
        }

        if (volume.profile.TryGet(out ChromaticAberration chroma))
        {
            _origChromaticIntensity = chroma.intensity.value;
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(chroma.intensity, targetChromaticIntensity, transitionDuration));
        }
    }

    public override void OnClientEnd(EventManager ctx)
    {
        ctx.RunCoroutine(FadeOutThenDestroy(fadeDuration));
        ctx.RunCoroutine(LerpFogDensity(fogResetDensity, fogChangeDuration));
        var volume = GlobalVolumeProvider.Instance?.GlobalVolume;
        if (volume == null || volume.profile == null) return;

        if (volume.profile.TryGet(out ColorAdjustments ca))
        {
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(ca.postExposure, _origPostExposure, transitionDuration));
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(ca.contrast, _origContrast, transitionDuration));
            ctx.RunCoroutine(VolumeGradeUtility.LerpColorParam(ca.colorFilter, _origColorFilter, transitionDuration));
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(ca.hueShift, _origHueShift, transitionDuration));
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(ca.saturation, _origSaturation, transitionDuration));
        }

        if (volume.profile.TryGet(out Vignette vg))
        {
            ctx.RunCoroutine(VolumeGradeUtility.LerpColorParam(vg.color, _origVignetteColor, transitionDuration));
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(vg.intensity, _origVignetteIntensity, transitionDuration));
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(vg.smoothness, _origVignetteSmoothness, transitionDuration));
        }

        if (volume.profile.TryGet(out ChromaticAberration chroma))
        {
            ctx.RunCoroutine(VolumeGradeUtility.LerpFloatParam(chroma.intensity, _origChromaticIntensity, transitionDuration));
        }
    }

    private void SpawnInstances()
    {
        _spawnedInstances.Clear();
        foreach (var prefab in prefabsToSpawn)
        {
            if (prefab == null) continue;
            var instance = Instantiate(prefab); // позиция берётся из самого префаба
            _spawnedInstances.Add(instance);
        }
    }
    public static IEnumerator LerpFogDensity(float toDensity, float duration)
    {
        RenderSettings.fog = true; // на случай если выключен
        float fromDensity = RenderSettings.fogDensity;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            RenderSettings.fogDensity = Mathf.Lerp(fromDensity, toDensity, t / duration);
            yield return null;
        }
        RenderSettings.fogDensity = toDensity;
    }

    private IEnumerator FadeMaterials(float fromAlpha01, float toAlpha01, float duration)
    {
        var renderers = CollectRenderers();
        var blocks = new MaterialPropertyBlock[renderers.Count];
        for (int i = 0; i < renderers.Count; i++)
            blocks[i] = new MaterialPropertyBlock();

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Lerp(fromAlpha01, toAlpha01, t / duration);
            ApplyAlpha(renderers, blocks, alpha);
            yield return null;
        }
        ApplyAlpha(renderers, blocks, toAlpha01);
    }

    private IEnumerator FadeOutThenDestroy(float duration)
    {
        var renderers = CollectRenderers();
        var blocks = new MaterialPropertyBlock[renderers.Count];
        for (int i = 0; i < renderers.Count; i++)
            blocks[i] = new MaterialPropertyBlock();

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, t / duration);
            ApplyAlpha(renderers, blocks, alpha);
            yield return null;
        }

        foreach (var instance in _spawnedInstances)
            if (instance != null) Destroy(instance);
        _spawnedInstances.Clear();
    }

    private List<Renderer> CollectRenderers()
    {
        var list = new List<Renderer>();
        foreach (var instance in _spawnedInstances)
        {
            if (instance == null) continue;
            list.AddRange(instance.GetComponentsInChildren<Renderer>());
        }
        return list;
    }

    private void ApplyAlpha(List<Renderer> renderers, MaterialPropertyBlock[] blocks, float alpha01)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            var r = renderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(blocks[i]);
            Color c = r.sharedMaterial.HasProperty(colorPropertyName)
                ? r.sharedMaterial.GetColor(colorPropertyName)
                : Color.white;
            c.a = alpha01;
            blocks[i].SetColor(colorPropertyName, c);
            r.SetPropertyBlock(blocks[i]);
        }
    }

    private IEnumerator DelayedFog(float delay, float toDensity, float duration)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        yield return LerpFogDensity(toDensity, duration);
    }
}