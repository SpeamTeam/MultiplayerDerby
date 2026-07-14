using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Тряска камеры на взрывах через Cinemachine Impulse.
///
/// ПОЧЕМУ ИМЕННО IMPULSE, А НЕ РУЧНОЙ СДВИГ TRANSFORM КАМЕРЫ:
/// живой vcam (CinemachineOrbitalFollow) КАЖДЫЙ кадр пересчитывает позицию
/// камеры, следя за машиной, и переписал бы любой наш ручной сдвиг Transform.
/// Impulse же добавляется как PositionCorrection ПОВЕРХ уже посчитанного
/// состояния vcam (см. CinemachineImpulseListener.PostPipelineStageCallback),
/// поэтому тряска и слежение за машиной НЕ конфликтуют — орбитальное вращение
/// (OrbitalCameraControl) и зум продолжают работать как раньше.
///
/// Компонент висит на объекте vcam (CameraObject, там же где CinemachineFind).
/// В Awake он сам добирает нужные Cinemachine-компоненты:
///   - Listener — чтобы камера РЕАГИРОВАЛА на импульсы;
///   - Source   — чтобы их ГЕНЕРИРОВАТЬ.
/// Так что в сцене/префабе руками ничего донастраивать не нужно (при добавлении
/// компонентов через AddComponent Reset() не вызывается, поэтому все поля явно
/// проставляются здесь).
/// </summary>
public class CameraImpulseShake : MonoBehaviour
{
    public static CameraImpulseShake Instance { get; private set; }

    [Tooltip("Общий множитель силы тряски (1 — как задаёт источник импульса).")]
    public float gain = 1f;

    [Tooltip("За пределами этой дистанции от взрыва тряска затухает до нуля.")]
    public float dissipationDistance = 120f;

    // Канал импульса. Source и Listener должны совпадать, иначе тряски не будет.
    private const int ImpulseChannel = 1;

    private CinemachineImpulseSource source;

    private void Awake()
    {
        // Singleton живёт вместе с камерой (её создаёт GameManager один раз).
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        // Слушатель: заставляет ЭТУ камеру двигаться в ответ на импульсы.
        var listener = GetComponent<CinemachineImpulseListener>();
        if (listener == null) listener = gameObject.AddComponent<CinemachineImpulseListener>();
        listener.ChannelMask = ImpulseChannel;
        listener.Gain = gain;
        listener.UseCameraSpace = true;
        listener.ApplyAfter = CinemachineCore.Stage.Noise;

        // Источник: генерирует импульсы «взрыва».
        source = GetComponent<CinemachineImpulseSource>();
        if (source == null) source = gameObject.AddComponent<CinemachineImpulseSource>();
        var def = source.ImpulseDefinition;
        def.ImpulseChannel = ImpulseChannel;
        def.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Explosion;
        // Dissipating: близкий взрыв бьёт сильнее далёкого (falloff по дистанции).
        def.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Dissipating;
        def.ImpulseDuration = 0.5f;
        def.DissipationDistance = dissipationDistance;
        def.DissipationRate = 0.25f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Тряхнуть камеру от взрыва в точке <paramref name="world"/> с силой <paramref name="force"/>.
    /// Затухание с расстоянием считает Cinemachine (dissipationDistance), поэтому
    /// близкий взрыв ощущается сильнее далёкого. Безопасно вызывать, даже если
    /// камеры в сцене нет (например, на выделенном сервере) — тогда это no-op.
    /// </summary>
    public static void ShakeAt(Vector3 world, float force)
    {
        if (Instance == null || Instance.source == null) return;
        // Немного случайности в направлении, чтобы толчок не был строго вертикальным.
        Vector3 velocity = (Vector3.down + Random.insideUnitSphere * 0.5f).normalized * force;
        Instance.source.GenerateImpulseAtPositionWithVelocity(world, velocity);
    }
}
