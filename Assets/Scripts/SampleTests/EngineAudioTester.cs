using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class EngineAudioTester : MonoBehaviour
{
    [Header("=== AUDIO SOURCE ===")]
    [SerializeField] private AudioSource audioSource;

    [Header("=== TEMP AUDIO CLIP ===")]
    [Tooltip("Перетащи сюда клип для теста — он применится автоматически")]
    [SerializeField] private AudioClip tempClip;
    private AudioClip _lastTempClip;

    [Header("=== СИМУЛЯЦИЯ СКОРОСТИ ===")]
    [Tooltip("Текущая скорость авто (км/ч)")]
    [SerializeField, Range(0f, 300f)] private float simulatedSpeed = 0f;
    [Tooltip("Максимальная скорость авто (км/ч)")]
    [SerializeField] private float maxSpeed = 200f;

    [Header("=== PITCH НАСТРОЙКИ ===")]
    [Tooltip("Pitch на холостых оборотах")]
    [SerializeField] private float minPitch = 0.8f;
    [Tooltip("Pitch на максимальной скорости")]
    [SerializeField] private float maxPitch = 2.5f;

    [Header("=== VOLUME НАСТРОЙКИ ===")]
    [Tooltip("Громкость на холостых")]
    [SerializeField, Range(0f, 1f)] private float minVolume = 0.3f;
    [Tooltip("Громкость на максимальной скорости")]
    [SerializeField, Range(0f, 1f)] private float maxVolume = 1f;

    [Header("=== ПЛАВНОСТЬ ===")]
    [Tooltip("Скорость изменения pitch и volume")]
    [SerializeField] private float smoothSpeed = 5f;

    [Header("=== УПРАВЛЕНИЕ ===")]
    [SerializeField] private bool isPlaying = false;
    [SerializeField] private bool useSmoothing = true;

    [Header("=== ДЕБАГ (только чтение) ===")]
    [SerializeField] private float currentPitch;
    [SerializeField] private float currentVolume;
    [SerializeField] private float speedRatio;

    // -------------------------------------------------------

    private void Reset()
    {
        audioSource = GetComponent<AudioSource>();
        SetupAudioSource();
    }

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        SetupAudioSource();
    }

    private void SetupAudioSource()
    {
        if (audioSource == null) return;

        audioSource.loop = true;
        audioSource.playOnAwake = false;
    }

    private void Update()
    {
        HandleClipChange();
        HandlePlayback();
        UpdateEngineSound();
    }

    /// <summary>
    /// Автоматически применяет новый клип при его смене в инспекторе
    /// </summary>
    private void HandleClipChange()
    {
        if (tempClip == _lastTempClip) return;

        _lastTempClip = tempClip;

        if (audioSource == null) return;

        bool wasPlaying = audioSource.isPlaying;

        audioSource.Stop();
        audioSource.clip = tempClip;

        if (wasPlaying && tempClip != null)
            audioSource.Play();

        Debug.Log(tempClip != null
            ? $"[EngineAudioTester] Клип применён: <b>{tempClip.name}</b>"
            : "[EngineAudioTester] Клип удалён");
    }

    /// <summary>
    /// Следит за флагом isPlaying для старта/стопа через инспектор
    /// </summary>
    private void HandlePlayback()
    {
        if (audioSource == null || audioSource.clip == null) return;

        if (isPlaying && !audioSource.isPlaying)
        {
            audioSource.Play();
            Debug.Log("[EngineAudioTester] ▶ Воспроизведение начато");
        }
        else if (!isPlaying && audioSource.isPlaying)
        {
            audioSource.Stop();
            Debug.Log("[EngineAudioTester] ■ Воспроизведение остановлено");
        }
    }

    /// <summary>
    /// Обновляет pitch и volume в зависимости от скорости
    /// </summary>
    private void UpdateEngineSound()
    {
        if (audioSource == null || !audioSource.isPlaying) return;

        speedRatio = Mathf.Clamp01(simulatedSpeed / maxSpeed);

        float targetPitch = Mathf.Lerp(minPitch, maxPitch, speedRatio);
        float targetVolume = Mathf.Lerp(minVolume, maxVolume, speedRatio);

        if (useSmoothing)
        {
            currentPitch = Mathf.Lerp(audioSource.pitch, targetPitch, Time.deltaTime * smoothSpeed);
            currentVolume = Mathf.Lerp(audioSource.volume, targetVolume, Time.deltaTime * smoothSpeed);
        }
        else
        {
            currentPitch = targetPitch;
            currentVolume = targetVolume;
        }

        audioSource.pitch = currentPitch;
        audioSource.volume = currentVolume;
    }

    // -------------------------------------------------------
    // Публичные методы для вызова из других скриптов
    // -------------------------------------------------------

    public void SetSpeed(float speed) => simulatedSpeed = Mathf.Clamp(speed, 0f, maxSpeed);
    public void Play() => isPlaying = true;
    public void Stop() => isPlaying = false;
}