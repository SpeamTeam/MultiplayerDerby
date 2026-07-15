using System;
using Assets.Scripts.Network.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Menu
{
    /// <summary>
    /// Одна строка списка слотов в лобби. Заполняется через Bind() при каждом изменении
    /// LobbyManager.Slots (см. LobbyUIManager).
    /// </summary>
    public class LobbySlotRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Button addBotButton;
        [SerializeField] private Button removeBotButton;

        private int slotIndex;
        private Action<int> onAddBot;
        private Action<int> onRemoveBot;

        private void Awake()
        {
            addBotButton.onClick.AddListener(() => onAddBot?.Invoke(slotIndex));
            removeBotButton.onClick.AddListener(() => onRemoveBot?.Invoke(slotIndex));
        }

        public void Bind(LobbySlotData slot, bool isHost, Action<int> addBotHandler, Action<int> removeBotHandler)
        {
            slotIndex = slot.SlotIndex;
            onAddBot = addBotHandler;
            onRemoveBot = removeBotHandler;

            if (!slot.IsOccupied)
                label.text = $"{slot.SlotIndex + 1}. Пусто";
            else if (slot.IsBot)
                label.text = $"{slot.SlotIndex + 1}. [Бот]";
            else
                label.text = $"{slot.SlotIndex + 1}. {slot.NickName}";

            addBotButton.gameObject.SetActive(isHost && !slot.IsOccupied);
            removeBotButton.gameObject.SetActive(isHost && slot.IsOccupied && slot.IsBot);
        }
    }
}
