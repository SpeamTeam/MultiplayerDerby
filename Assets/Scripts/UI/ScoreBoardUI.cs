using Assets.Scripts.InGameLogic;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.UI
{
    /// <summary>
    /// Табло очков в левом верхнем углу экрана. Живёт на префабе Score
    /// (Assets/Prefabs/Menu/Score.prefab), который кладётся в сцену-синглтон
    /// и используется через ScoreBoardUI.Instance из GameManager/NetworkProvider.
    ///
    /// Строки идентифицируются произвольным ulong id — обычно это OwnerClientId
    /// или NetworkObjectId машины, но менеджер сам ничего про сеть не знает:
    /// всё начисление и авторитетность — забота вызывающей стороны.
    /// </summary>
    public class ScoreBoardUI : MonoBehaviour
    {
        public static ScoreBoardUI Instance { get; private set; }

        [SerializeField] private RectTransform rowsContainer;
        [SerializeField] private PlayerScoreRow rowPrefab;

        private readonly Dictionary<ulong, PlayerScoreRow> rows = new Dictionary<ulong, PlayerScoreRow>();

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            // ScoreManager могло ещё не заспавниться на этом клиенте — тогда именно
            // оно подхватит нас, как только само поднимется (см. ScoreManager.OnNetworkSpawn).
            if (ScoreManager.Instance != null)
                ScoreManager.Instance.SetScoreBoard(this);
        }

        public bool HasRow(ulong id) => rows.ContainsKey(id);

        /// <summary>Создаёт строку для id, если её ещё нет, иначе просто обновляет значения существующей.</summary>
        public PlayerScoreRow AddOrUpdateRow(ulong id, string playerName, int score = 0)
        {
            if (rows.TryGetValue(id, out var existingRow))
            {
                existingRow.SetValues(playerName, score);
                return existingRow;
            }

            var row = Instantiate(rowPrefab, rowsContainer);
            row.SetValues(playerName, score);
            rows[id] = row;
            return row;
        }

        public void SetScore(ulong id, int score)
        {
            if (rows.TryGetValue(id, out var row))
                row.SetScore(score);
        }

        public void SetPlayerName(ulong id, string playerName)
        {
            if (rows.TryGetValue(id, out var row))
                row.SetPlayerName(playerName);
        }

        public void RemoveRow(ulong id)
        {
            if (!rows.TryGetValue(id, out var row)) return;

            if (row != null)
                Destroy(row.gameObject);
            rows.Remove(id);
        }

        public void Clear()
        {
            foreach (var row in rows.Values)
            {
                if (row != null)
                    Destroy(row.gameObject);
            }
            rows.Clear();
        }
    }
}
