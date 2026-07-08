using System;
using UnityEngine;

/// <summary>
/// Стабилизация машины: anti-roll (уменьшает крен в поворотах) и авто-flip
/// (переворачивает обратно, если машина застряла на крыше/боку).
///
/// В дерби машину часто переворачивают тараном — без auto-flip игрок застревает.
///
/// СЕТЬ: как и вся физика — работает только на сервере. На клиентах отключить.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarStabilizer : MonoBehaviour
{
    [Header("Anti-roll (виртуальный стабилизатор)")]
    [Tooltip("Сила выравнивания кузова к горизонту. 0 = выключено.")]
    public float uprightTorque = 8f;

    [Tooltip("Гашение вращения при выравнивании")]
    public float uprightDamping = 3f;

    [Tooltip("Ниже этого |dot(up, worldUp)| считаем машину сильно накрененной")]
    [Range(0f, 1f)] public float activateBelowDot = 0.6f;

    [Header("Авто-flip при перевороте")]
    [Tooltip("Включить авто-переворот, если машина долго лежит")]
    public bool autoFlip = true;

    [Tooltip("Сколько секунд машина должна лежать перевёрнутой, прежде чем её вернут")]
    public float flipAfterSeconds = 3f;

    [Tooltip("Через сколько секунд лежания на боку сообщить об этом наружу (например, DriverEjection) — должно быть МЕНЬШЕ flipAfterSeconds, иначе водитель не успеет вылететь до авто-выравнивания")]
    public float notifyFlippedAfterSeconds = 1.2f;

    [Tooltip("dot(up, worldUp) ниже этого = машина считается перевёрнутой")]
    [Range(-1f, 1f)] public float flippedThreshold = 0.1f;

    /// <summary>Срабатывает один раз, когда машина лежит перевёрнутой дольше notifyFlippedAfterSeconds.</summary>
    public event Action OnFlippedTooLong;

    private bool hasNotifiedFlip;

    private Rigidbody rb;
    private float flippedTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        float upDot = Vector3.Dot(transform.up, Vector3.up);

        // --- Мягкое выравнивание к горизонту (только когда прилично накренились,
        //     чтобы не мешать нормальной езде и прыжкам) ---
        if (uprightTorque > 0f && upDot < activateBelowDot && upDot > flippedThreshold)
        {
            Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
            rb.AddTorque(axis * uprightTorque - rb.angularVelocity * uprightDamping,
                         ForceMode.Acceleration);
        }

        // --- Полный переворот, если машина легла ---
        if (autoFlip)
        {
            if (upDot < flippedThreshold)
            {
                flippedTimer += Time.fixedDeltaTime;

                if (!hasNotifiedFlip && flippedTimer >= notifyFlippedAfterSeconds)
                {
                    hasNotifiedFlip = true;
                    OnFlippedTooLong?.Invoke();
                }

                if (flippedTimer >= flipAfterSeconds)
                {
                    FlipUpright();
                    flippedTimer = 0f;
                    hasNotifiedFlip = false;
                }
            }
            else
            {
                flippedTimer = 0f;
                hasNotifiedFlip = false;
            }
        }
    }

    private void FlipUpright()
    {
        // Сохраняем yaw (направление), обнуляем крен/тангаж, приподнимаем.
        float yaw = transform.eulerAngles.y;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        transform.position += Vector3.up * 1.5f;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }
}
