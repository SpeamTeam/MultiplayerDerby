using System.Collections.Generic;
using Assets.Scripts.Network;
using Assets.Scripts.Network.Lobby;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Menu
{
    /// <summary>
    /// Ведёт панель лобби: показывает/прячет её относительно главного меню, рисует список
    /// слотов из LobbyManager.Slots, даёт хосту менять MaxPlayers/добавлять ботов/стартовать матч.
    /// Панель лобби живёт только пока не выгружена MenuScene — после LoadWorldScene() она (вместе
    /// со всем деревом MenuScene) исчезает сама, дополнительной очистки не требуется.
    /// </summary>
    public class LobbyUIManager : MonoBehaviour
    {
        [SerializeField] private GameObject mainMenuPanel;
        [SerializeField] private GameObject lobbyPanel;
        [SerializeField] private Transform slotListContainer;
        [SerializeField] private LobbySlotRow slotRowPrefab;
        [SerializeField] private TMP_Dropdown maxPlayersDropdown;
        [SerializeField] private Button startGameButton;
        [SerializeField] private Button backButton;

        private readonly List<LobbySlotRow> rows = new();
        private LobbyManager lobby;

        private void Awake()
        {
            startGameButton.onClick.AddListener(OnStartGamePressed);
            backButton.onClick.AddListener(OnBackPressed);
            maxPlayersDropdown.onValueChanged.AddListener(OnMaxPlayersDropdownChanged);
        }

        private void OnEnable()
        {
            LobbyManager.OnLobbyReady += HandleLobbyReady;
            if (LobbyManager.Instance != null)
                HandleLobbyReady();
        }

        private void OnDisable()
        {
            LobbyManager.OnLobbyReady -= HandleLobbyReady;
            Unbind();
        }

        private void HandleLobbyReady()
        {
            if (lobby == LobbyManager.Instance) return;
            Unbind();

            lobby = LobbyManager.Instance;
            if (lobby == null) return;

            lobby.Slots.OnListChanged += HandleSlotsChanged;
            lobby.MaxPlayers.OnValueChanged += HandleMaxPlayersChanged;

            mainMenuPanel.SetActive(false);
            lobbyPanel.SetActive(true);

            RefreshMaxPlayersControl();
        }

        private void Unbind()
        {
            if (lobby == null) return;
            lobby.Slots.OnListChanged -= HandleSlotsChanged;
            lobby.MaxPlayers.OnValueChanged -= HandleMaxPlayersChanged;
            lobby = null;
        }

        private void HandleSlotsChanged(NetworkListEvent<LobbySlotData> _) => RefreshSlots();
        private void HandleMaxPlayersChanged(int previous, int current) => RefreshMaxPlayersControl();

        private static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        private void RefreshMaxPlayersControl()
        {
            if (lobby == null) return;

            maxPlayersDropdown.interactable = IsHost;

            int cap = lobby.Slots.Count;
            if (maxPlayersDropdown.options.Count != cap)
            {
                var options = new List<string>(cap);
                for (int i = 1; i <= cap; i++)
                    options.Add(i.ToString());

                maxPlayersDropdown.ClearOptions();
                maxPlayersDropdown.AddOptions(options);
            }

            maxPlayersDropdown.SetValueWithoutNotify(lobby.MaxPlayers.Value - 1);

            startGameButton.gameObject.SetActive(IsHost);

            RefreshSlots();
        }

        private void RefreshSlots()
        {
            if (lobby == null) return;

            int visibleCount = Mathf.Min(lobby.MaxPlayers.Value, lobby.Slots.Count);

            while (rows.Count < visibleCount)
                rows.Add(Instantiate(slotRowPrefab, slotListContainer));

            for (int i = 0; i < rows.Count; i++)
                rows[i].gameObject.SetActive(i < visibleCount);

            for (int i = 0; i < visibleCount; i++)
                rows[i].Bind(lobby.Slots[i], IsHost, OnAddBotPressed, OnRemoveBotPressed);

            // ContentSizeFitter/VerticalLayoutGroup у SlotListContainer и LobbyPanel по умолчанию
            // пересчитываются с задержкой в один кадр после SetActive на строках — из-за этого размер
            // панели "плавал" через раз. Форсируем немедленный пересчёт всей ветки лобби-панели.
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)lobbyPanel.transform);
        }

        private void OnAddBotPressed(int slotIndex) => lobby?.AddBot(slotIndex);
        private void OnRemoveBotPressed(int slotIndex) => lobby?.RemoveBot(slotIndex);

        private void OnMaxPlayersDropdownChanged(int index)
        {
            if (lobby == null || !IsHost) return;
            lobby.SetMaxPlayers(index + 1);

            // Хост — сам себе сервер, поэтому MaxPlayers.Value уже обновлено синхронно к этому месту,
            // но не дожидаемся отдельного отклика через NetworkVariable.OnValueChanged, а сразу
            // пересчитываем панель здесь же, привязавшись прямо к dropdown.
            RefreshMaxPlayersControl();
        }

        private void OnStartGamePressed() => lobby?.TryStartGame();

        private void OnBackPressed() => NetworkHandler.Instance.Disconnect();
    }
}
