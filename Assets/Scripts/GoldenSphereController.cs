using Assets.Scripts.AI;
using Assets.Scripts.InGameLogic;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class GoldenSphereController : NetworkBehaviour
{
    [Header("Настройки жизни")]
    [SerializeField] private float lifeTime = 15f;
    [SerializeField] private float blinkStartBeforeEnd = 3f;
    [SerializeField] private float blinkInterval = 0.15f;

    [Header("Визуал")]
    [SerializeField] private Renderer sphereRenderer;
    [SerializeField] private Color normalColor = Color.yellow;
    [SerializeField] private Color blinkColor = Color.red;
    [SerializeField] private GameObject explosionPrefab;
    [Tooltip("Сила тряски камеры при взрыве. 0 — выключить.")]
    [SerializeField] private float cameraShakeForce = 5f;

    [Header("Игрок")]
    [SerializeField] private string playerTag = "Player";

    [Header("Урон")]
    [SerializeField] private float explosionRadius = 25f;
    [Tooltip("Сила разлёта. Применяется как VelocityChange, т.е. это ~прирост скорости (м/с) в эпицентре — одинаково подбрасывает и лёгкие, и тяжёлые машины.")]
    [SerializeField] private float explosionForce = 18f;
    [Tooltip("Насколько сильно взрыв поддаёт машины ВВЕРХ (подброс). Больше = выше полёт.")]
    [SerializeField] private float upwardModifier = 2.5f;
    [Tooltip("Урон машине в эпицентре. С расстоянием слегка спадает.")]
    [SerializeField] private float explosionDamage = 50f;
    [SerializeField] private AudioClip explosionSound;
    [SerializeField] private float maxExplosionDeformationForce = 1f;

    private bool exploded = false; // Страховочный bool

    // Синхронизируем момент начала мигания на всех клиентах
    private readonly NetworkVariable<bool> _isBlinking = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Coroutine _lifeRoutine;
    private Coroutine _blinkRoutine;
    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        if (sphereRenderer == null)
            sphereRenderer = GetComponentInChildren<Renderer>();

        _mpb = new MaterialPropertyBlock();
    }

    public override void OnNetworkSpawn()
    {
        SetColor(normalColor);

        if (IsServer)
        {
            _lifeRoutine = StartCoroutine(LifeCycle());
        }

        _isBlinking.OnValueChanged += OnBlinkingChanged;
        if (_isBlinking.Value)
        {
            StartBlinkVisual();
        }
    }

    public override void OnNetworkDespawn()
    {
        _isBlinking.OnValueChanged -= OnBlinkingChanged;

        ExplodeEffectClientRpc();
        Explode();

        if (_lifeRoutine != null) StopCoroutine(_lifeRoutine);
        if (_blinkRoutine != null) StopCoroutine(_blinkRoutine);
    }

    void Explode()
    {
        if (exploded) return; // страховка: не взрываемся дважды
        exploded = true;

        Debug.Log($"[Barrel] {gameObject.name} exploded at {transform.position}", this);

        if (explosionSound != null)
            AudioSource.PlayClipAtPoint(explosionSound, transform.position);

        if (cameraShakeForce > 0f)
            CameraImpulseShake.ShakeAt(transform.position, cameraShakeForce);

        Collider[] colliders = Physics.OverlapSphere(transform.position, explosionRadius);
        var pushedBodies = new HashSet<Rigidbody>();
        var damagedCars = new HashSet<CarHealth>();

        foreach (Collider hit in colliders)
        {
            // Импульс разлёта — на любое физическое тело (ловит Rigidbody даже у дочерних коллайдеров).
            Rigidbody rb = hit.attachedRigidbody;
            if (rb != null && pushedBodies.Add(rb))
            {
                // VelocityChange: одинаковый подброс независимо от массы машины.
                rb.AddExplosionForce(explosionForce, transform.position,
                                     explosionRadius, upwardModifier, ForceMode.VelocityChange);
            }

            // Урон машине приходит ТОЛЬКО отсюда, от взрыва (не от прямого тарана бочки).
            CarHealth car = hit.GetComponentInParent<CarHealth>();
            if (car != null && damagedCars.Add(car))
            {
                float dist = Vector3.Distance(transform.position, car.transform.position);
                // Лёгкий спад урона к краю радиуса: в эпицентре — полный, на границе — половина.
                float falloff = Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(dist / explosionRadius));
                car.ApplyDamage(explosionDamage * falloff);

                if (IsServer)
                {
                    var carCollision = car.GetComponent<CarCollision>();

                    Vector3 closestPointOnCar = hit.ClosestPoint(transform.position);
                    Vector3 dir = (closestPointOnCar - transform.position).normalized;
                    float distance = Vector3.Distance(transform.position, closestPointOnCar);

                    Ray ray = new Ray(transform.position, dir);
                    if (hit.Raycast(ray, out RaycastHit rayHit, dist + 0.1f))
                    {
                        Vector3 worldNormal = rayHit.normal;

                        Vector3 localPoint = car.transform.InverseTransformPoint(closestPointOnCar);
                        Vector3 localNormal = car.transform.InverseTransformDirection(worldNormal);

                        carCollision.DeformViaForce(localPoint, localNormal, maxExplosionDeformationForce / dist);
                    }
                }
            }
        }

        Destroy(gameObject);
    }

    private IEnumerator LifeCycle()
    {
        float timeToBlink = Mathf.Max(0f, lifeTime - blinkStartBeforeEnd);
        yield return new WaitForSeconds(timeToBlink);

        _isBlinking.Value = true; // разошлёт визуал мигания всем клиентам

        yield return new WaitForSeconds(Mathf.Min(blinkStartBeforeEnd, lifeTime));

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(false);
        }
    }

    [ClientRpc]
    private void ExplodeEffectClientRpc()
    {
        Debug.Log("[GoldenSphereController] ExplodeEffectClientRpc invoked");
        Instantiate(explosionPrefab, transform.position, Quaternion.identity);
    }

    private void OnBlinkingChanged(bool previous, bool current)
    {
        if (current) StartBlinkVisual();
        else StopBlinkVisual();
    }

    private void StartBlinkVisual()
    {
        if (_blinkRoutine != null) StopCoroutine(_blinkRoutine);
        _blinkRoutine = StartCoroutine(BlinkRoutine());
    }

    private void StopBlinkVisual()
    {
        if (_blinkRoutine != null)
        {
            StopCoroutine(_blinkRoutine);
            _blinkRoutine = null;
        }
        SetColor(normalColor);
    }

    private IEnumerator BlinkRoutine()
    {
        bool toggle = false;
        while (true)
        {
            toggle = !toggle;
            SetColor(toggle ? blinkColor : normalColor);
            yield return new WaitForSeconds(blinkInterval);
        }
    }

    private void SetColor(Color color)
    {
        if (sphereRenderer == null) return;
        _mpb.SetColor("_BaseColor", color); // URP/Lit. Для Standard shader — "_Color"
        sphereRenderer.SetPropertyBlock(_mpb);
    }

    // === Обнаружение игрока ===
    private void OnCollisionEnter(Collision collision)
    {
        HandlePlayerTouch(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        HandlePlayerTouch(other);
    }


    private void HandlePlayerTouch(Collider other)
    {
        if (!IsServer) return;
        if (other.gameObject.layer != LayerMask.NameToLayer("Car")) return;

        if (ScoreManager.Instance == null)
        {
            Debug.LogWarning("[GoldenSphere] ScoreManager.Instance is null!");
            return;
        }

        var netObj = other.gameObject.GetComponentInParent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogWarning($"[GoldenSphere] На объекте {other.name} нет NetworkObject!");
            return;
        }

        ScoreManager.Instance.AddScoreServerRpc(netObj.NetworkObjectId, 20);
        Debug.Log($"[GoldenSphere] Игрок {other.name} коснулся Golden Sphere!");
    }
}