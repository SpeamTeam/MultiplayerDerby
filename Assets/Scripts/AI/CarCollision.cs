using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;

namespace Assets.Scripts.AI
{
    [RequireComponent(typeof(Rigidbody))]
    //[RequireComponent(typeof(CarController))]
    public class CarCollision : NetworkBehaviour
    {
        [Header("Impact Thresholds")]
        public float minImpactSpeed = 3f;
        public float maxImpactSpeed = 25f;

        [Header("Damage")]
        public float maxDamage = 40f;

        [Header("Deformation")]
        public MeshFilter[] deformableMeshes;
        public float deformRadius = 0.6f;
        public float maxDeformDepth = 0.25f;

        [Header("Collider Update")]
        [Tooltip("Обновлять MeshCollider после каждой деформации")]
        public bool updateMeshColliders = true;
        [Tooltip("Если MeshCollider не найден на том же объекте, что и MeshFilter — искать на дочерних объектах")]
        public bool searchInChildren = true;

        [Header("Effects")]
        public ParticleSystem sparksPrefab;
        public AudioSource audioSource;
        public AudioClip[] impactClips;

        private CarController carController;
        private Rigidbody rb;

        private float lastHitTime;
        private const float hitCooldown = 0.15f;

        // Текущие (деформированные) вершины
        private Vector3[][] deformedVerts;
        // Оригинальные (неизменяемые) вершины — для ключей словаря
        private Vector3[][] originalVerts;
        // Группы дублированных вершин по оригинальной позиции
        private Dictionary<Vector3Int, List<int>>[] vertexGroups;
        // MeshCollider, привязанный к каждому MeshFilter
        private MeshCollider[] linkedColliders;

        private WheelDetachment wheelDetachment;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            //carController = GetComponent<CarController>();

            // Клон меша и группы вершин нужны КАЖДОМУ инстансу (сервер и все клиенты) —
            // деформация чисто визуальная и применяется локально на каждом peer'е отдельно.
            InitDeformation();

            wheelDetachment = GetComponent<WheelDetachment>();
        }

        private void InitDeformation()
        {
            if (deformableMeshes == null || deformableMeshes.Length == 0) return;

            deformedVerts = new Vector3[deformableMeshes.Length][];
            originalVerts = new Vector3[deformableMeshes.Length][];
            vertexGroups = new Dictionary<Vector3Int, List<int>>[deformableMeshes.Length];
            linkedColliders = new MeshCollider[deformableMeshes.Length];

            for (int m = 0; m < deformableMeshes.Length; m++)
            {
                if (deformableMeshes[m] == null) continue;

                // ── Клонируем меш, чтобы не портить оригинальный ассет ──────────
                Mesh clone = Instantiate(deformableMeshes[m].sharedMesh);
                clone.name = deformableMeshes[m].sharedMesh.name + "_deformed";
                deformableMeshes[m].mesh = clone;

                deformedVerts[m] = clone.vertices;
                originalVerts[m] = (Vector3[])deformedVerts[m].Clone();

                // ── Строим группы по оригинальным позициям ──────────────────────
                vertexGroups[m] = new Dictionary<Vector3Int, List<int>>();
                for (int i = 0; i < originalVerts[m].Length; i++)
                {
                    Vector3Int key = RoundVertex(originalVerts[m][i]);

                    if (!vertexGroups[m].ContainsKey(key))
                        vertexGroups[m][key] = new List<int>();

                    vertexGroups[m][key].Add(i);
                }

                // ── Ищем MeshCollider, связанный с этим MeshFilter ──────────────
                linkedColliders[m] = FindLinkedCollider(deformableMeshes[m]);

                if (linkedColliders[m] != null)
                {
                    ApplyMeshToCollider(linkedColliders[m], clone);
                    Debug.Log($"[CarCollision] MeshCollider найден для '{deformableMeshes[m].name}'.");
                }
                else
                {
                    Debug.LogWarning($"[CarCollision] MeshCollider не найден для '{deformableMeshes[m].name}'. " +
                                     "Коллайдер этого меша обновляться не будет.");
                }
            }
        }

        /// <summary>
        /// Ищет MeshCollider на том же GameObject, что и переданный MeshFilter.
        /// Если не нашёл и разрешён поиск в дочерних — ищет там.
        /// </summary>
        private MeshCollider FindLinkedCollider(MeshFilter filter)
        {
            MeshCollider col = filter.GetComponent<MeshCollider>();
            if (col != null) return col;

            col = GetComponent<MeshCollider>();
            if (col != null) return col;

            if (searchInChildren)
            {
                col = GetComponentInChildren<MeshCollider>();
                if (col != null) return col;
            }

            return null;
        }

        /// <summary>
        /// Присваивает меш коллайдеру. Временно переключает convex,
        /// чтобы Unity принял обновлённую геометрию корректно.
        /// </summary>
        private static void ApplyMeshToCollider(MeshCollider collider, Mesh mesh)
        {
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
        }

        private Vector3Int RoundVertex(Vector3 v)
        {
            return new Vector3Int(
                Mathf.RoundToInt(v.x * 1000f),
                Mathf.RoundToInt(v.y * 1000f),
                Mathf.RoundToInt(v.z * 1000f)
            );
        }

        // Физическое столкновение с реальным impulse существует ТОЛЬКО там, где Rigidbody
        // не kinematic — то есть только на сервере (см. PlayerCarController: клиенты
        // держат kinematic Rigidbody + отключённые WheelCollider'ы). На клиентах этот
        // метод физически не вызовется для машина-машина/машина-стена столкновений.
        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServer) return;
            if (Time.time - lastHitTime < hitCooldown) return;
            //if (carController.IsDead) return;

            ContactPoint contact = collision.GetContact(0);

            // Меряем силу удара по компоненту скорости ВДОЛЬ НОРМАЛИ контакта, а не по
            // полной collision.relativeVelocity. При обычной езде машина касается земли
            // почти по касательной (скорость параллельна поверхности) — этот компонент
            // маленький, даже если машина едет быстро. При падении и ударе о землю (или
            // лобовом столкновении со стеной/другой машиной) скорость направлена именно
            // по нормали — компонент большой. Так отличаем "зацепил кочку" от реального удара.
            float normalImpactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (normalImpactSpeed < minImpactSpeed) return;

            lastHitTime = Time.time;

            float force = Mathf.Clamp01(
                (normalImpactSpeed - minImpactSpeed) / (maxImpactSpeed - minImpactSpeed)
            );

            // Если врезались в другого игрока (а не в статичное окружение — стену, землю),
            // перераспределяем force по "вине": тот, чья собственная скорость вдоль нормали
            // внесла больше в удар (активно въехал), получает меньше; тот, кто почти не
            // двигался (в него въехали), получает больше. Сумма на двоих не меняется — это
            // просто честное распределение исходного force, а не дополнительный урон.
            Rigidbody otherRb = collision.rigidbody;
            CarCollision otherCar = otherRb != null ? otherRb.GetComponent<CarCollision>() : null;

            if (otherCar != null)
            {
                float myImpactSpeed = Mathf.Abs(Vector3.Dot(rb.linearVelocity, contact.normal));
                float otherImpactSpeed = Mathf.Abs(Vector3.Dot(otherRb.linearVelocity, contact.normal));
                float totalSpeed = myImpactSpeed + otherImpactSpeed;

                if (totalSpeed > 0.01f)
                {
                    float otherContribution = otherImpactSpeed / totalSpeed;
                    force *= otherContribution;
                }
            }

            // Авторитативный урон (если понадобится) должен жить только здесь, на сервере,
            // и реплицироваться отдельно как NetworkVariable<float> health — НЕ через
            // визуальный ClientRpc ниже. Использовать нужно уже пересчитанный force выше.
            //carController.TakeDamage(force * maxDamage);

            // ВАЖНО: переводим точку/нормаль в ЛОКАЛЬНЫЕ координаты корня машины ДО отправки.
            // NetworkTransform на клиенте почти всегда на кадр-другой "позади" сервера
            // (интерполяция). Если слать мировую точку и переводить её в локаль уже НА
            // КЛИЕНТЕ через его текущий (чуть другой) transform, точка удара "уезжает"
            // и не попадает в deformRadius — деформация тихо не происходит.
            // Локальные координаты относительно машины от сетевой задержки не зависят.
            Vector3 localHitPoint = transform.InverseTransformPoint(contact.point);
            Vector3 localHitNormal = transform.InverseTransformDirection(contact.normal);

            PlayImpactEffectsClientRpc(localHitPoint, localHitNormal, force);
        }

        [ClientRpc]
        private void PlayImpactEffectsClientRpc(Vector3 localPoint, Vector3 localNormal, float force)
        {
            // Разворачиваем обратно в мировые координаты уже ТЕКУЩИМ transform'ом
            // этого конкретного peer'а — рассинхрона по времени тут не возникает.
            Vector3 worldPoint = transform.TransformPoint(localPoint);
            Vector3 worldNormal = transform.TransformDirection(localNormal).normalized;

            DeformMeshes(localPoint, force);
            SpawnEffects(worldPoint, worldNormal, force);
            PlayImpactSound(force);
        }

        private void DeformMeshes(Vector3 rootLocalPoint, float force)
        {
            if (deformableMeshes == null) return;

            for (int m = 0; m < deformableMeshes.Length; m++)
            {
                if (deformableMeshes[m] == null) continue;

                Transform t = deformableMeshes[m].transform;
                Mesh mesh = deformableMeshes[m].mesh;
                Vector3[] verts = deformedVerts[m];
                Vector3[] origVerts = originalVerts[m];

                // rootLocalPoint уже в локальных координатах корня машины — переводим
                // в мир и обратно в локаль конкретного меша ТЕКУЩИМ transform'ом этого
                // peer'а (см. комментарий в PlayImpactEffectsClientRpc выше).
                Vector3 localPoint = t.InverseTransformPoint(transform.TransformPoint(rootLocalPoint));
                Vector3 localCenter = t.InverseTransformPoint(transform.position);

                Vector3 inwardDir = (localCenter - localPoint).normalized;

                var processedGroups = new HashSet<Vector3Int>();
                bool changed = false;

                for (int i = 0; i < verts.Length; i++)
                {
                    float dist = Vector3.Distance(verts[i], localPoint);
                    if (dist >= deformRadius) continue;

                    Vector3Int key = RoundVertex(origVerts[i]);
                    if (!processedGroups.Add(key)) continue;

                    float falloff = 1f - (dist / deformRadius);
                    falloff *= falloff;

                    Vector3 randomOffset = Vector3.ProjectOnPlane(
                        Random.insideUnitSphere * 0.08f,
                        inwardDir
                    );
                    Vector3 deformDir = (inwardDir + randomOffset).normalized;

                    float depth = force * maxDeformDepth * falloff;
                    Vector3 offset = deformDir * depth;

                    if (vertexGroups[m].TryGetValue(key, out List<int> group))
                    {
                        foreach (int idx in group)
                            verts[idx] += offset;
                    }

                    changed = true;
                }

                if (changed)
                {
                    mesh.vertices = verts;
                    RecalculateFlatNormals(mesh);
                    mesh.RecalculateBounds();

                    if (updateMeshColliders && linkedColliders != null && linkedColliders[m] != null)
                        ApplyMeshToCollider(linkedColliders[m], mesh);

                    if (wheelDetachment != null)
                    {
                        wheelDetachment.OnMeshDeformedNearWheel(
                            originalVerts[m],
                            verts,
                            t,
                            force
                        );
                    }
                }
            }
        }

        private void RecalculateFlatNormals(Mesh mesh)
        {
            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            Vector3[] normals = new Vector3[verts.Length];

            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                Vector3 normal = Vector3.Cross(
                    verts[b] - verts[a],
                    verts[c] - verts[a]
                ).normalized;

                normals[a] = normal;
                normals[b] = normal;
                normals[c] = normal;
            }

            mesh.normals = normals;
        }

        private void SpawnEffects(Vector3 point, Vector3 normal, float force)
        {
            if (sparksPrefab == null) return;

            ParticleSystem sparks = Instantiate(
                sparksPrefab, point, Quaternion.LookRotation(normal)
            );

            var emission = sparks.emission;
            emission.SetBurst(0, new ParticleSystem.Burst(
                0f, Mathf.RoundToInt(Mathf.Lerp(5f, 30f, force))
            ));

            sparks.Play();
            Destroy(sparks.gameObject, 2f);
        }

        private void PlayImpactSound(float force)
        {
            if (audioSource == null || impactClips == null || impactClips.Length == 0) return;

            AudioClip clip = impactClips[Random.Range(0, impactClips.Length)];
            audioSource.PlayOneShot(clip, Mathf.Lerp(0.2f, 1f, force));
        }
    }
}