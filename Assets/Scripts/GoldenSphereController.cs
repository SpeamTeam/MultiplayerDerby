using System.Collections;
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

    [Header("Игрок")]
    [SerializeField] private string playerTag = "Player";

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

        if (_lifeRoutine != null) StopCoroutine(_lifeRoutine);
        if (_blinkRoutine != null) StopCoroutine(_blinkRoutine);
    }

    private IEnumerator LifeCycle()
    {
        float timeToBlink = Mathf.Max(0f, lifeTime - blinkStartBeforeEnd);
        yield return new WaitForSeconds(timeToBlink);

        _isBlinking.Value = true; // разошлёт визуал мигания всем клиентам

        yield return new WaitForSeconds(Mathf.Min(blinkStartBeforeEnd, lifeTime));

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
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
        if (!IsServer) return; // касание — авторитетная логика на сервере
        if (!other.CompareTag(playerTag)) return;

        Debug.Log($"[GoldenSphere] Игрок {other.name} коснулся Golden Sphere!");
    }
}