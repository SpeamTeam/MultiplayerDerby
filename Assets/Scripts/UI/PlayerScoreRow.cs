using TMPro;
using UnityEngine;

namespace Assets.Scripts.UI
{
    /// <summary>
    /// Одна строка таблицы очков (ник + текущий счёт). Спавнится и обновляется
    /// исключительно через ScoreBoardUI — сама по себе состояния не хранит,
    /// только отображает переданные значения.
    /// </summary>
    public class PlayerScoreRow : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI playerNameText;
        [SerializeField] private TextMeshProUGUI scoreText;

        public void SetPlayerName(string playerName)
        {
            if (playerNameText != null)
                playerNameText.text = playerName;
        }

        public void SetScore(int score)
        {
            if (scoreText != null)
                scoreText.text = score.ToString();
        }

        public void SetValues(string playerName, int score)
        {
            SetPlayerName(playerName);
            SetScore(score);
        }
    }
}
