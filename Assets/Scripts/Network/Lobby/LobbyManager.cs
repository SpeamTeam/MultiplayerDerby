using System;
using Assets.Scripts.Network;
using Unity.Netcode;
using UnityEngine;

namespace Assets.Scripts.Network.Lobby
{
    /// <summary>
    /// Авторитативное состояние лобби: список слотов, лимит игроков, признак старта матча.
    /// Живёт весь сеанс (спавнится NetworkHandler.MakeHost вместе с DestroyWithScene = false),
    /// поэтому подключения продолжают резервировать свободные слоты и после перехода в WorldScene —
    /// именно это позволяет донабирать игроков во время уже идущего матча.
    ///
    /// Мутирующие методы (SetMaxPlayers/AddBot/RemoveBot/TryStartGame) вызываются напрямую с
    /// хостовского UI без ServerRpc — тот же паттерн доверия, что уже используется в
    /// GameManager.SpawnBot(): интерфейс лобби физически показывается только хосту, а IsServer-guard
    /// делает вызов с клиента безопасным no-op.
    /// </summary>
    public class LobbyManager : NetworkBehaviour
    {
        public static LobbyManager Instance { get; private set; }

        public static event Action OnLobbyReady;

        public readonly NetworkList<LobbySlotData> Slots = new(
            writePerm: NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> MaxPlayers = new(
            writePerm: NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> GameStarted = new(
            writePerm: NetworkVariableWritePermission.Server);


        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                var gameConfig = NetworkHandler.Instance.gameConfig;
                int cap = Mathf.Max(1, gameConfig.lobbySlotCap);

                for (int i = 0; i < cap; i++)
                    Slots.Add(new LobbySlotData { SlotIndex = i });

                MaxPlayers.Value = Mathf.Clamp(gameConfig.defaultMaxPlayers, gameConfig.minPlayers, cap);

                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;

                // Хост сам себя не проводит через ConnectionApprovalCallback с полезной нагрузкой,
                // поэтому первый слот резервируем ему напрямую.
                TryReserveSlotFor(NetworkManager.ServerClientId, NetworkHandler.Instance.localPlayerName);
            }

            OnLobbyReady?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Резервирует первый свободный слот с индексом меньше MaxPlayers. Вызывается из
        /// ConnectionApprovalCallback (NetworkHandler) — единственное условие отказа — отсутствие
        /// свободного слота, факт того что матч уже идёт (GameStarted) сам по себе не блокирует.
        /// </summary>
        public bool TryReserveSlotFor(ulong clientId, string nickName)
        {
            if (!IsServer) return false;

            int max = Mathf.Min(MaxPlayers.Value, Slots.Count);
            for (int i = 0; i < max; i++)
            {
                var slot = Slots[i];
                if (slot.IsOccupied) continue;

                slot.IsOccupied = true;
                slot.IsBot = false;
                slot.ClientId = clientId;
                slot.NickName = string.IsNullOrEmpty(nickName) ? $"Player_{clientId}" : nickName;
                Slots[i] = slot;
                return true;
            }

            return false;
        }

        public bool HasFreeSlot()
        {
            int max = Mathf.Min(MaxPlayers.Value, Slots.Count);
            for (int i = 0; i < max; i++)
                if (!Slots[i].IsOccupied) return true;
            return false;
        }

        public void SetMaxPlayers(int value)
        {
            if (!IsServer || GameStarted.Value) return;

            int highestOccupied = -1;
            for (int i = 0; i < Slots.Count; i++)
                if (Slots[i].IsOccupied) highestOccupied = i;

            var gameConfig = NetworkHandler.Instance.gameConfig;
            int min = Mathf.Max(gameConfig.minPlayers, highestOccupied + 1);
            MaxPlayers.Value = Mathf.Clamp(value, min, Slots.Count);
        }

        public void AddBot(int slotIndex)
        {
            if (!IsServer || GameStarted.Value) return;
            if (slotIndex < 0 || slotIndex >= MaxPlayers.Value) return;

            var slot = Slots[slotIndex];
            if (slot.IsOccupied) return;

            slot.IsOccupied = true;
            slot.IsBot = true;
            slot.ClientId = NetworkManager.ServerClientId;
            slot.NickName = "Bot";
            Slots[slotIndex] = slot;
        }

        public void RemoveBot(int slotIndex)
        {
            if (!IsServer || GameStarted.Value) return;
            if (slotIndex < 0 || slotIndex >= Slots.Count) return;

            var slot = Slots[slotIndex];
            if (!slot.IsOccupied || !slot.IsBot) return;

            Slots[slotIndex] = new LobbySlotData { SlotIndex = slotIndex };
        }

        public void TryStartGame()
        {
            if (!IsServer || GameStarted.Value) return;

            GameStarted.Value = true;
            NetworkHandler.Instance.LoadWorldScene();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            for (int i = 0; i < Slots.Count; i++)
            {
                var slot = Slots[i];
                if (slot.IsOccupied && !slot.IsBot && slot.ClientId == clientId)
                {
                    Slots[i] = new LobbySlotData { SlotIndex = i };
                    break;
                }
            }
        }

        public string GetNicknameFor(ulong clientId)
        {
            foreach( var slot in Slots) {
               if (slot.ClientId == clientId) return slot.NickName.ToString(); 
            }
            return "";
        }
    }
}
