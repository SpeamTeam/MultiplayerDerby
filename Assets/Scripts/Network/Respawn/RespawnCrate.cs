using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Assets.Scripts.Network.Respawn
{
    /// <summary>
    /// Ящик, который дрон несёт и сбрасывает над точкой респавна.
    ///
    /// СЕТЬ (server-authoritative):
    ///   • Пока ящик «в руках», он ПРИКЛЕЕН к CrateAttachPoint дрона настоящим парентингом:
    ///     становится частью иерархии дрона и повторяет его движение кадр в кадр на КАЖДОМ
    ///     пире — без физики, joint'ов и сетевой синхронизации позы. Рассинхрона «ящик
    ///     отстаёт от дрона» быть не может в принципе: у них общий transform-родитель.
    ///   • Парентинг делает каждый пир САМ (по NetworkVariable carrier), а не NGO: NGO умеет
    ///     реплицировать родителя только если на нём есть NetworkObject, а CrateAttachPoint —
    ///     обычный дочерний объект дрона. Поэтому на префабе AutoObjectParentSync = 0 —
    ///     штатный способ сказать NGO «парентинг мой, не вмешивайся».
    ///   • Пока ящик на подвесе, NetworkTransform переведён в локальное пространство: локальная
    ///     поза постоянна, поэтому по сети не уходит ничего и мировые координаты не перетирают
    ///     парентинг. На сбросе — обратно в мировое.
    ///   • Физика и деспавн — ТОЛЬКО на сервере. На сбросе (ServerDrop) ящик отцепляется от
    ///     дрона, сервер включает ему физику, и падение реплицируется всем NetworkTransform'ом.
    ///     На клиентах Rigidbody всегда кинематический — они ничего не симулируют.
    ///   • Растворение (fade материала + сжатие + звук) запускается ClientRpc'ом, чтобы
    ///     исчезновение и звук были синхронны у ВСЕХ клиентов, включая хост. Сервер
    ///     параллельно отсчитывает ту же длительность и деспавнит ящик.
    ///
    /// Готовностью к деспавну управляет сервер (IsFinished) — дрон ждёт этого флага,
    /// после чего вызывает свой onComplete и запускает ServerRespawn машины.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RespawnCrate : NetworkBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("3D-звук приземления/растворения (spatialBlend = 1). Проигрывается у всех клиентов через ClientRpc.")]
        [SerializeField] private AudioSource dissolveAudio;

        [Tooltip("Рендереры ящика, которые нужно растворять. Если пусто — берутся все дочерние при спавне.")]
        [SerializeField] private Renderer[] renderers;

        private Rigidbody rb;
        private NetworkTransform netTransform;
        private bool dropped;
        private Coroutine attachWait;

        /// <summary>
        /// Дрон, который несёт ящик. Пишет сервер, читают все: по нему каждый пир сам цепляет
        /// ящик под CrateAttachPoint дрона. default — ящик уже сброшен (или ещё не подхвачен).
        /// Именно NetworkVariable, а не ClientRpc: он же синхронизирует и того, кто подключился
        /// посреди доставки.
        /// </summary>
        private readonly NetworkVariable<NetworkBehaviourReference> carrier =
            new NetworkVariable<NetworkBehaviourReference>();

        private bool CarrierIsSet => !carrier.Value.Equals(default(NetworkBehaviourReference));

        // Готов к деспавну (растворение завершено) — читает дрон-владелец на сервере.
        public bool IsFinished { get; private set; }

        // Длительность растворения приходит из GameConfig через дрон.
        private float settleTime = 1.5f;
        private float dissolveDuration = 1.5f;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            netTransform = GetComponent<NetworkTransform>();
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);
        }

        public override void OnNetworkSpawn()
        {
            // Ящик рождается «в руках» дрона и до сброса никуда не падает. На клиентах тело
            // кинематическое всегда: там его ведёт парентинг/NetworkTransform, а не физика.
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            carrier.OnValueChanged += OnCarrierChanged;
            ApplyCarrierState();
        }

        public override void OnNetworkDespawn()
        {
            carrier.OnValueChanged -= OnCarrierChanged;

            if (attachWait != null)
            {
                StopCoroutine(attachWait);
                attachWait = null;
            }
        }

        private void OnCarrierChanged(NetworkBehaviourReference previous, NetworkBehaviourReference current)
        {
            ApplyCarrierState();
        }

        /// <summary>
        /// Приводит ящик в состояние, описанное <see cref="carrier"/>: подвешен под дрон или
        /// отцеплен. Идемпотентно и выполняется на КАЖДОМ пире — и по OnValueChanged, и явным
        /// вызовом с сервера, чтобы не зависеть от порядка срабатывания колбэка у автора записи.
        /// </summary>
        private void ApplyCarrierState()
        {
            if (attachWait != null)
            {
                StopCoroutine(attachWait);
                attachWait = null;
            }

            if (!CarrierIsSet)
            {
                Detach();
                return;
            }

            if (carrier.Value.TryGet(out RespawnDrone drone) && drone != null)
            {
                AttachTo(drone);
                return;
            }

            // Дрон ещё не заспавнился на этом пире: порядок спавна NGO не гарантирует, что
            // дрон приедет раньше ящика (ловится при подключении посреди доставки). Ждём его.
            attachWait = StartCoroutine(AttachWhenCarrierSpawns());
        }

        private IEnumerator AttachWhenCarrierSpawns()
        {
            while (CarrierIsSet)
            {
                yield return null;

                if (carrier.Value.TryGet(out RespawnDrone drone) && drone != null)
                {
                    AttachTo(drone);
                    break;
                }
            }
            attachWait = null;
        }

        /// <summary>
        /// Приклеить ящик к точке подвеса дрона: дальше он часть его иерархии и следует за ним
        /// сам, без физики и без сети.
        /// </summary>
        private void AttachTo(RespawnDrone drone)
        {
            transform.SetParent(drone.CrateAttachPoint, false);
            transform.SetLocalPositionAndRotation(drone.CrateAttachLocalOffset, Quaternion.identity);

            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                // Интерполирует уже дрон — своя интерполяция поверх родителя даёт дрожание.
                rb.interpolation = RigidbodyInterpolation.None;
            }

            // Локальная поза константна → NetworkTransform не шлёт ничего и, главное, не
            // перетирает парентинг мировыми координатами. Флаг реплицирует авторитет (сервер).
            if (netTransform != null && IsServer)
                netTransform.InLocalSpace = true;
        }

        /// <summary>Снять ящик с подвеса, оставив его ровно там, где он висел.</summary>
        private void Detach()
        {
            if (transform.parent != null)
                transform.SetParent(null, true);

            if (rb != null)
                rb.interpolation = RigidbodyInterpolation.Interpolate;

            if (netTransform != null && IsServer)
                netTransform.InLocalSpace = false;
        }

        /// <summary>Тайминги из конфига (задаёт дрон при спавне, только на сервере).</summary>
        public void ServerConfigure(float crateFallSettleTime, float crateDissolveDuration)
        {
            if (!IsServer) return;
            settleTime = crateFallSettleTime;
            dissolveDuration = crateDissolveDuration;
        }

        /// <summary>
        /// Сервер: подвесить ящик под дрон. Само приклеивание сделает каждый пир у себя,
        /// получив carrier — включая тех, кто подключится уже в процессе доставки.
        /// </summary>
        public void ServerBeginCarry(RespawnDrone drone)
        {
            if (!IsServer || drone == null) return;

            carrier.Value = new NetworkBehaviourReference(drone);
            ApplyCarrierState();
        }

        /// <summary>
        /// Сервер: сбросить ящик. Снимает его с подвеса у всех пиров и включает физику — ящик
        /// падает вертикально вниз из точки сброса. Падение authoritative на сервере и
        /// реплицируется NetworkTransform'ом. Через settleTime запускает растворение.
        /// </summary>
        public void ServerDrop()
        {
            if (!IsServer || dropped) return;
            dropped = true;

            // Отцепляет и здесь, и у клиентов (через OnValueChanged).
            carrier.Value = default;
            ApplyCarrierState();

            if (rb != null)
            {
                // Порядок важен: скорости задаются только не-кинематическому телу.
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            StartCoroutine(SettleThenDissolve());
        }

        private IEnumerator SettleThenDissolve()
        {
            // Даём ящику упасть и улечься (детерминированно по таймеру — не гоняем
            // покадровую проверку скорости по сети).
            yield return new WaitForSeconds(settleTime);

            // Фиксируем ящик на месте, чтобы во время растворения он не подпрыгивал/катился.
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            // Синхронный визуал+звук у всех клиентов (в т.ч. хоста).
            DissolveClientRpc(dissolveDuration);

            // Сервер отсчитывает ту же длительность и деспавнит.
            yield return new WaitForSeconds(dissolveDuration);

            IsFinished = true;
            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }

        [ClientRpc]
        private void DissolveClientRpc(float duration)
        {
            if (dissolveAudio != null)
            {
                dissolveAudio.spatialBlend = 1f; // страховка: гарантированно 3D-звук
                dissolveAudio.Play();
            }
            StartCoroutine(DissolveVisual(duration));
        }

        /// <summary>
        /// Плавное растворение: fade альфы материала (URP Lit _BaseColor и/или _Dissolve,
        /// если шейдер их поддерживает) + сжатие как универсальный запасной эффект, который
        /// работает с любым материалом. Идёт на каждом клиенте независимо, но одинаковой
        /// длительности — визуально синхронно.
        /// </summary>
        private IEnumerator DissolveVisual(float duration)
        {
            if (duration <= 0f) duration = 0.01f;

            Vector3 startScale = transform.localScale;
            var block = new MaterialPropertyBlock();
            float t = 0f;

            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration); // 0 → 1
                float alpha = 1f - k;

                transform.localScale = startScale * Mathf.Max(0.001f, alpha);
                ApplyFade(block, alpha, k);
                yield return null;
            }

            // По завершении прячем рендереры — сервер деспавнит объект чуть позже.
            foreach (var r in renderers)
                if (r != null) r.enabled = false;
        }

        private void ApplyFade(MaterialPropertyBlock block, float alpha, float dissolveAmount)
        {
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                if (r.sharedMaterial != null)
                {
                    if (r.sharedMaterial.HasProperty("_BaseColor"))
                    {
                        Color c = r.sharedMaterial.GetColor("_BaseColor");
                        c.a = alpha;
                        block.SetColor("_BaseColor", c);
                    }
                    if (r.sharedMaterial.HasProperty("_Dissolve"))
                        block.SetFloat("_Dissolve", dissolveAmount);
                    if (r.sharedMaterial.HasProperty("_DissolveAmount"))
                        block.SetFloat("_DissolveAmount", dissolveAmount);
                }
                r.SetPropertyBlock(block);
            }
        }
    }
}
