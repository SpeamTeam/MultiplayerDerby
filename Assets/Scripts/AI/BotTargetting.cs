using UnityEngine;

namespace Assets.Scripts.AI
{
    [RequireComponent(typeof(CarAgent))]
    public class BotTargeting : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CarNavMeshAgent botController; 

        [Header("Timing")]
        [SerializeField] private float reevaluateInterval = 2.5f;

        [Tooltip("не бросать цель раньше этого времени")]
        [SerializeField] private float minChaseTime = 4f;

        [Tooltip("новая цель должна быть в X раз лучше")]
        [SerializeField] private float switchThreshold = 1.25f;

        [Header("Scoring weights")]
        [SerializeField] private float distanceWeight = 1.0f;
        [SerializeField] private float randomJitter = 0.3f;     // непредсказуемость
        [SerializeField] private float maxUsefulDistance = 60f;

        private Transform currentTarget;
        private float timeSinceEval;
        private float timeOnCurrentTarget;

        private CarAgent carAgent;

        private void Awake()
        {
            carAgent = GetComponent<CarAgent>();
        }

        void FixedUpdate()
        {
            if (!carAgent.IsServer) return;
            timeSinceEval += Time.fixedDeltaTime;
            timeOnCurrentTarget += Time.fixedDeltaTime;

            if (timeSinceEval >= reevaluateInterval)
            {
                timeSinceEval = 0f;
                EvaluateTargets();
            }
        }

        void EvaluateTargets()
        {
            var candidates = AITargetManager.Instance.targets;
            if (candidates == null || candidates.Count == 0) return;

            Transform best = null;
            float bestScore = float.NegativeInfinity;

            foreach (var go in candidates)
            {
                if (go == null || go.transform == transform) continue;

                float score = ScoreTarget(go.transform);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = go.transform;
                }
            }

            if (best == null) return;

            // если текущей цели нет — просто берём лучшую
            if (currentTarget == null)
            {
                SetTarget(best);
                return;
            }

            // не переключаемся, пока не прошёл минимальный кулдаун
            if (timeOnCurrentTarget < minChaseTime) return;

            float currentScore = ScoreTarget(currentTarget);

            // переключаемся только если новая цель заметно лучше
            if (best != currentTarget && bestScore > currentScore * switchThreshold)
            {
                SetTarget(best);
            }
        }

        float ScoreTarget(Transform target)
        {
            float dist = Vector3.Distance(transform.position, target.position);

            // чем ближе — тем выше очки (инвертируем и нормализуем)
            float distScore = Mathf.Clamp01(1f - dist / maxUsefulDistance) * distanceWeight;

            // немного случайности, чтобы боты не были предсказуемы
            float noise = Random.Range(-randomJitter, randomJitter);

            return distScore + noise;
        }

        void SetTarget(Transform target)
        {
            currentTarget = target;
            timeOnCurrentTarget = 0f;
            botController.SetTarget(target);
        }
    }
}
