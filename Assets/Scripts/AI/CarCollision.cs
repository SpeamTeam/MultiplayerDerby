using UnityEngine;
using System.Collections.Generic;

namespace Assets.Scripts.AI
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CarController))]
    public class CarCollision : MonoBehaviour
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

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            carController = GetComponent<CarController>();
            InitDeformation();
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
                    // Назначаем клонированный меш коллайдеру сразу при старте
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
            // Сначала ищем на том же объекте
            MeshCollider col = filter.GetComponent<MeshCollider>();
            if (col != null) return col;

            // Затем ищем на родительском объекте (сам CarCollision)
            col = GetComponent<MeshCollider>();
            if (col != null) return col;

            // Наконец — в дочерних объектах корневого объекта
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
            // Назначение null перед новым мешем сбрасывает внутренний кэш физического движка
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

        private void OnCollisionEnter(Collision collision)
        {
            if (Time.time - lastHitTime < hitCooldown) return;
            if (carController.IsDead) return;

            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed < minImpactSpeed) return;

            lastHitTime = Time.time;

            float force = Mathf.Clamp01(
                (impactSpeed - minImpactSpeed) / (maxImpactSpeed - minImpactSpeed)
            );

            ContactPoint contact = collision.GetContact(0);

            carController.TakeDamage(force * maxDamage);
            DeformMeshes(contact.point, contact.normal, force);
            SpawnEffects(contact.point, contact.normal, force);
            PlayImpactSound(force);
        }

        private void DeformMeshes(Vector3 worldPoint, Vector3 worldNormal, float force)
        {
            if (deformableMeshes == null) return;

            for (int m = 0; m < deformableMeshes.Length; m++)
            {
                if (deformableMeshes[m] == null) continue;

                Transform t = deformableMeshes[m].transform;
                Mesh mesh = deformableMeshes[m].mesh;
                Vector3[] verts = deformedVerts[m];
                Vector3[] origVerts = originalVerts[m];

                Vector3 localPoint = t.InverseTransformPoint(worldPoint);
                Vector3 localCenter = t.InverseTransformPoint(transform.position);

                // Направление вдавливания: от точки удара к центру машины
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

                    // ── Обновляем MeshCollider ───────────────────────────────────
                    if (updateMeshColliders && linkedColliders != null && linkedColliders[m] != null)
                        ApplyMeshToCollider(linkedColliders[m], mesh);
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