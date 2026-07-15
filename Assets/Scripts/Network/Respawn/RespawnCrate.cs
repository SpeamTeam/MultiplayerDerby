using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Assets.Scripts.Network.Respawn
{
    /// <summary>
    /// Ящик, который дрон несёт и сбрасывает над точкой респавна.
    ///
    /// СЕТЬ (server-authoritative):
    ///   • Спавн/деспавн и вся физика — ТОЛЬКО на сервере. Пока ящик «в руках» дрона,
    ///     он висит под ним на SpringJoint и инерционно раскачивается при полёте:
    ///     Rigidbody на сервере НЕ кинематический, на клиентах кинематический —
    ///     позицию им даёт NetworkTransform.
    ///   • На сбросе (ServerDrop) сервер удаляет joint — ящик отцепляется и падает
    ///     с текущей инерцией, падение реплицируется всем через NetworkTransform.
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

        [Header("Подвес (SpringJoint)")]
        [Tooltip("Жёсткость пружины. Больше — жёстче подвес и слабее раскачка.")]
        [SerializeField] private float springForce = 200f;

        [Tooltip("Затухание пружины. Больше — быстрее гаснет раскачка.")]
        [SerializeField] private float springDamper = 8f;

        [Tooltip("Длина «верёвки»: на этом расстоянии от точки подвеса пружина начинает тянуть вверх.")]
        [SerializeField] private float maxDistance = 3f;

        [Tooltip("Минимальное расстояние до точки подвеса, ближе которого пружина отталкивает.")]
        [SerializeField] private float minDistance = 0f;

        private Rigidbody rb;
        private SpringJoint joint;
        private bool dropped;

        // Готов к деспавну (растворение завершено) — читает дрон-владелец на сервере.
        public bool IsFinished { get; private set; }

        // Длительность растворения приходит из GameConfig через дрон.
        private float settleTime = 1.5f;
        private float dissolveDuration = 1.5f;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);
        }

        public override void OnNetworkSpawn()
        {
            if (rb == null) return;

            if (IsServer)
                rb.isKinematic = false;   // на сервере физика активна — ящик качается
            else
                rb.isKinematic = true;    // на клиентах позицию даёт NetworkTransform
        }

        /// <summary>Тайминги из конфига (задаёт дрон при спавне, только на сервере).</summary>
        public void ServerConfigure(float crateFallSettleTime, float crateDissolveDuration)
        {
            if (!IsServer) return;
            settleTime = crateFallSettleTime;
            dissolveDuration = crateDissolveDuration;
        }

        /// <summary>
        /// Сервер: подвесить ящик под дрон на пружине. Вызывается один раз при спавне —
        /// дальше положение ящика ведёт физика, а не покадровое позиционирование.
        /// </summary>
        /// <param name="droneRb">Rigidbody дрона — тело, к которому крепится подвес.</param>
        /// <param name="anchorUnderDrone">Точка подвеса в ЛОКАЛЬНЫХ координатах дрона.</param>
        public void ServerAttach(Rigidbody droneRb, Vector3 anchorUnderDrone)
        {
            if (!IsServer || droneRb == null) return;

            joint = gameObject.AddComponent<SpringJoint>();
            joint.connectedBody = droneRb;
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedAnchor = anchorUnderDrone;  // точка под дроном (локально к дрону)
            joint.anchor = Vector3.zero;               // центр ящика
            joint.spring = springForce;
            joint.damper = springDamper;
            joint.maxDistance = maxDistance;
            joint.minDistance = minDistance;
        }

        /// <summary>
        /// Сервер: сбросить ящик. Удаляет подвес — ящик отцепляется и падает с той инерцией,
        /// которую набрал на раскачке. Падение authoritative на сервере и реплицируется
        /// NetworkTransform'ом. Через settleTime запускает растворение.
        /// </summary>
        public void ServerDrop()
        {
            if (!IsServer || dropped) return;
            dropped = true;

            if (joint != null)
                Destroy(joint);   // отцепляем от дрона — ящик падает свободно

            if (rb != null)
                rb.useGravity = true;

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
