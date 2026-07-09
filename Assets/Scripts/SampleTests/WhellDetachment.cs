using UnityEngine;
using System.Collections.Generic;

namespace Assets.Scripts.AI
{
    /// <summary>
    /// Добавьте этот компонент на тот же GameObject, что и CarCollision.
    /// Для каждого колеса укажите Transform колеса и порог деформации.
    /// </summary>
    public class WheelDetachment : MonoBehaviour
    {
        [System.Serializable]
        public class WheelData
        {
            [Tooltip("Transform самого колеса (визуальный объект)")]
            public Transform wheelTransform;

            [Tooltip("WheelCollider, связанный с этим колесом")]
            public WheelCollider wheelCollider;

            [Tooltip("Радиус зоны проверки деформации вокруг колеса (в локальных координатах кузова)")]
            public float checkRadius = 0.4f;

            [Tooltip("Насколько вершины должны сдвинуться, чтобы колесо отпало (в единицах Unity)")]
            public float deformThreshold = 0.15f;

            [Tooltip("Префаб для эффекта отпадания (опционально)")]
            public GameObject detachEffectPrefab;

            [HideInInspector] public bool isDetached = false;
            [HideInInspector] public float currentDeformation = 0f;
        }

        [Header("Wheel Settings")]
        public WheelData[] wheels;

        [Header("Physics After Detach")]
        [Tooltip("Сила, с которой колесо отлетает")]
        public float detachForce = 5f;
        [Tooltip("Случайный крутящий момент при отпадании")]
        public float detachTorque = 3f;

        private CarCollision carCollision;

        // Публичный метод, вызываемый из CarCollision после каждой деформации
        public void OnMeshDeformed(int meshIndex, Vector3[] originalVerts,
                                   Vector3[] deformedVerts, Transform meshTransform)
        {
            if (wheels == null) return;

            foreach (var wheel in wheels)
            {
                if (wheel == null || wheel.isDetached || wheel.wheelTransform == null)
                    continue;

                // Позиция колеса в локальном пространстве меша
                Vector3 wheelLocalPos = meshTransform.InverseTransformPoint(
                    wheel.wheelTransform.position
                );

                float maxDisplacement = CalculateMaxDisplacement(
                    originalVerts, deformedVerts, wheelLocalPos, wheel.checkRadius
                );

                wheel.currentDeformation = Mathf.Max(wheel.currentDeformation, maxDisplacement);

                if (wheel.currentDeformation >= wheel.deformThreshold)
                {
                    DetachWheel(wheel);
                }
            }
        }

        /// <summary>
        /// Считает максимальное смещение вершин в зоне колеса
        /// </summary>
        private float CalculateMaxDisplacement(Vector3[] original, Vector3[] deformed,
                                                Vector3 centerLocal, float radius)
        {
            float maxDisp = 0f;

            for (int i = 0; i < original.Length; i++)
            {
                float dist = Vector3.Distance(original[i], centerLocal);
                if (dist > radius) continue;

                float displacement = Vector3.Distance(original[i], deformed[i]);
                if (displacement > maxDisp)
                    maxDisp = displacement;
            }

            return maxDisp;
        }

        private void DetachWheel(WheelData wheel)
        {
            wheel.isDetached = true;

            // ── Отключаем WheelCollider ──────────────────────────────────────────
            if (wheel.wheelCollider != null)
            {
                wheel.wheelCollider.motorTorque = 0f;
                wheel.wheelCollider.brakeTorque = float.MaxValue;
                wheel.wheelCollider.enabled = false;
            }

            // ── Отсоединяем визуальное колесо от родителя ────────────────────────
            Transform wheelT = wheel.wheelTransform;
            Vector3 detachPosition = wheelT.position;
            Quaternion detachRotation = wheelT.rotation;

            wheelT.SetParent(null); // Отцепляем от машины

            // ── Добавляем Rigidbody для физики колеса ────────────────────────────
            Rigidbody wheelRb = wheelT.GetComponent<Rigidbody>();
            if (wheelRb == null)
                wheelRb = wheelT.gameObject.AddComponent<Rigidbody>();

            wheelRb.mass = 15f;

            // Наследуем скорость машины
            Rigidbody carRb = GetComponent<Rigidbody>();
            if (carRb != null)
                wheelRb.linearVelocity = carRb.linearVelocity;

            // Добавляем силу отпадания (от центра машины)
            Vector3 detachDir = (detachPosition - transform.position).normalized;
            detachDir.y += 0.3f; // Немного вверх
            wheelRb.AddForce(detachDir * detachForce, ForceMode.Impulse);
            wheelRb.AddTorque(Random.insideUnitSphere * detachTorque, ForceMode.Impulse);

            // ── Добавляем коллайдер колесу, если нет ─────────────────────────────
            if (wheelT.GetComponent<Collider>() == null)
            {
                SphereCollider sc = wheelT.gameObject.AddComponent<SphereCollider>();
                // Примерный радиус — настройте под ваши колёса
                sc.radius = 0.3f;
            }

            // ── Эффект отпадания ─────────────────────────────────────────────────
            if (wheel.detachEffectPrefab != null)
            {
                GameObject effect = Instantiate(
                    wheel.detachEffectPrefab, detachPosition, detachRotation
                );
                Destroy(effect, 3f);
            }

            // ── Уничтожаем колесо через N секунд ─────────────────────────────────
            Destroy(wheelT.gameObject, 10f);

            Debug.Log($"[WheelDetachment] Колесо '{wheelT.name}' отпало! " +
                      $"Деформация: {wheel.currentDeformation:F3}");
        }
    }
}