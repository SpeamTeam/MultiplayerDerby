using UnityEngine;

namespace Assets.Scripts.AI
{
    public class WheelDetachment : MonoBehaviour
    {
        [System.Serializable]
        public class WheelData
        {
            public string name = "Wheel";
            public Transform wheelMesh;
            public WheelCollider wheelCollider;

            [Header("Detection")]
            [Tooltip("Радиус проверки вершин вокруг колеса")]
            public float vertexCheckRadius = 0.8f;

            [Tooltip("Суммарная деформация для отпадания")]
            public float detachThreshold = 0.5f;

            [HideInInspector] public float accumulatedDeformation;
            [HideInInspector] public bool detached;
        }

        [Header("Wheels")]
        public WheelData[] wheels;

        [Header("Damage Settings")]
        [Tooltip("Множитель урона от деформации")]
        public float damageMultiplier = 8f;

        [Tooltip("Минимальное смещение вершины чтобы считаться")]
        public float minVertexDisplacement = 0.01f;

        [Header("Physics")]
        public float detachForce = 5f;
        public float detachTorque = 3f;

        [Header("Debug")]
        public bool enableLogs = true;
        public bool drawGizmos = true;

        private CarController carController;
        private Rigidbody carRb;

        private void Awake()
        {
            carController = GetComponent<CarController>();
            carRb = GetComponent<Rigidbody>();

            if (enableLogs)
                Debug.Log($"[WheelDetachment] Инициализация: {wheels?.Length ?? 0} колес");
        }

        /// <summary>
        /// Вызывается после деформации меша — проверяет все колёса
        /// </summary>
        public void OnMeshDeformedNearWheel(
            Vector3[] originalVerts,
            Vector3[] deformedVerts,
            Transform meshTransform,
            float impactForce)
        {
            if (wheels == null || wheels.Length == 0) return;

            foreach (WheelData wheel in wheels)
            {
                if (wheel == null || wheel.detached || wheel.wheelMesh == null)
                    continue;

                // Позиция колеса в локальных координатах меша
                Vector3 wheelLocalPos = meshTransform.InverseTransformPoint(wheel.wheelMesh.position);

                // Считаем деформацию вершин в зоне колеса
                DeformationData deform = CalculateDeformationNearWheel(
                    originalVerts,
                    deformedVerts,
                    wheelLocalPos,
                    wheel.vertexCheckRadius
                );

                if (deform.affectedVertices == 0)
                    continue;

                // Урон = средняя деформация * сила удара * множитель
                float damageAmount = deform.averageDisplacement * impactForce * damageMultiplier;
                wheel.accumulatedDeformation += damageAmount;

                if (enableLogs)
                {
                    Debug.Log($"[WheelDetachment] {wheel.name}: " +
                              $"вершин={deform.affectedVertices}, " +
                              $"avgDisp={deform.averageDisplacement:F4}, " +
                              $"maxDisp={deform.maxDisplacement:F4}, " +
                              $"damage+={damageAmount:F3}, " +
                              $"total={wheel.accumulatedDeformation:F3}/{wheel.detachThreshold:F3}");
                }

                if (wheel.accumulatedDeformation >= wheel.detachThreshold)
                    DetachWheel(wheel);
            }
        }

        private struct DeformationData
        {
            public int affectedVertices;
            public float averageDisplacement;
            public float maxDisplacement;
        }

        private DeformationData CalculateDeformationNearWheel(
            Vector3[] originalVerts,
            Vector3[] deformedVerts,
            Vector3 wheelLocalPos,
            float radius)
        {
            DeformationData result = new DeformationData();
            float sumDisplacement = 0f;
            float maxDisplacement = 0f;
            int count = 0;

            float radiusSqr = radius * radius;

            for (int i = 0; i < originalVerts.Length; i++)
            {
                // Проверяем расстояние от вершины до центра колеса
                float distSqr = (originalVerts[i] - wheelLocalPos).sqrMagnitude;

                if (distSqr > radiusSqr)
                    continue;

                // Считаем смещение этой вершины
                float displacement = Vector3.Distance(originalVerts[i], deformedVerts[i]);

                if (displacement < minVertexDisplacement)
                    continue;

                sumDisplacement += displacement;
                count++;

                if (displacement > maxDisplacement)
                    maxDisplacement = displacement;
            }

            result.affectedVertices = count;
            result.maxDisplacement = maxDisplacement;
            result.averageDisplacement = count > 0 ? sumDisplacement / count : 0f;

            return result;
        }

        /// <summary>Возвращает все отпавшие колёса на место — вызывать при респавне.</summary>
        public void ResetWheels()
        {
            if (wheels == null) return;

            foreach (WheelData wheel in wheels)
            {
                if (wheel == null || !wheel.detached) continue;

                wheel.detached = false;
                wheel.accumulatedDeformation = 0f;

                if (wheel.wheelMesh != null)
                    wheel.wheelMesh.gameObject.SetActive(true);

                if (wheel.wheelCollider != null)
                {
                    wheel.wheelCollider.gameObject.SetActive(true);
                    wheel.wheelCollider.enabled = true;
                    wheel.wheelCollider.motorTorque = 0f;
                    wheel.wheelCollider.brakeTorque = 0f;
                }
            }
        }

        private void DetachWheel(WheelData wheel)
        {
            if (wheel.detached) return;
            wheel.detached = true;

            Debug.Log($"[WheelDetachment] ═══ КОЛЕСО ОТПАЛО: {wheel.name} ═══");

            Transform originalWheel = wheel.wheelMesh;
            Vector3 pos = originalWheel.position;
            Quaternion rot = originalWheel.rotation;

            // 1. Уведомляем CarController
            if (carController != null)
                carController.NotifyWheelDetached(wheel.wheelCollider, originalWheel);

            // 2. Создаём физический объект
            GameObject detached = new GameObject("Detached_" + wheel.name);
            detached.transform.position = pos;
            detached.transform.rotation = rot;

            // Копируем mesh
            MeshFilter srcMF = originalWheel.GetComponent<MeshFilter>() ?? originalWheel.GetComponentInChildren<MeshFilter>();
            MeshRenderer srcMR = originalWheel.GetComponent<MeshRenderer>() ?? originalWheel.GetComponentInChildren<MeshRenderer>();

            if (srcMF != null && srcMR != null)
            {
                MeshFilter newMF = detached.AddComponent<MeshFilter>();
                MeshRenderer newMR = detached.AddComponent<MeshRenderer>();
                newMF.sharedMesh = srcMF.sharedMesh;
                newMR.sharedMaterials = srcMR.sharedMaterials;
            }

            // 3. Скрываем оригинал
            originalWheel.gameObject.SetActive(false);
            wheel.wheelCollider.gameObject.SetActive(false);

            // 4. Физика
            SphereCollider col = detached.AddComponent<SphereCollider>();
            col.radius = 0.35f;

            Rigidbody rb = detached.AddComponent<Rigidbody>();
            rb.mass = 20f;

            if (carRb != null)
                rb.linearVelocity = carRb.linearVelocity;

            Vector3 dir = (pos - transform.position).normalized + Vector3.up * 0.5f;
            rb.AddForce(dir.normalized * detachForce, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * detachTorque, ForceMode.Impulse);

            Destroy(detached, 10f);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || wheels == null) return;

            foreach (var wheel in wheels)
            {
                if (wheel?.wheelMesh == null) continue;

                float t = wheel.detachThreshold > 0f
                    ? Mathf.Clamp01(wheel.accumulatedDeformation / wheel.detachThreshold)
                    : 0f;

                Gizmos.color = wheel.detached
                    ? Color.red
                    : Color.Lerp(Color.green, Color.yellow, t);

                Gizmos.DrawWireSphere(wheel.wheelMesh.position, wheel.vertexCheckRadius);

                // Прогресс бар
                Gizmos.color = Color.Lerp(Color.green, Color.red, t);
                Vector3 start = wheel.wheelMesh.position + Vector3.up * (wheel.vertexCheckRadius + 0.1f);
                Vector3 end = start + Vector3.right * t * 0.5f;
                Gizmos.DrawLine(start, end);

#if UNITY_EDITOR
                UnityEditor.Handles.Label(
                    start + Vector3.up * 0.1f,
                    $"{wheel.name}\n{wheel.accumulatedDeformation:F2}/{wheel.detachThreshold:F2}"
                );
#endif
            }
        }
    }
}