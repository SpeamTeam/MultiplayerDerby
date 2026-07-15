using System;
using Unity.Collections;
using Unity.Netcode;

namespace Assets.Scripts.Network.Lobby
{
    /// <summary>
    /// Одна строка лобби. Реплицируется сервером всем клиентам через NetworkList в LobbyManager.
    /// </summary>
    public struct LobbySlotData : IEquatable<LobbySlotData>, INetworkSerializable
    {
        public int SlotIndex;
        public bool IsOccupied;
        public bool IsBot;
        public ulong ClientId;
        public FixedString64Bytes NickName;

        public bool Equals(LobbySlotData other) =>
            SlotIndex == other.SlotIndex &&
            IsOccupied == other.IsOccupied &&
            IsBot == other.IsBot &&
            ClientId == other.ClientId &&
            NickName.Equals(other.NickName);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref SlotIndex);
            serializer.SerializeValue(ref IsOccupied);
            serializer.SerializeValue(ref IsBot);
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref NickName);
        }
    }
}
