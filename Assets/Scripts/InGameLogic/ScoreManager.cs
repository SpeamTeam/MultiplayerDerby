using Assets.Scripts.Network.Lobby;
using Assets.Scripts.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor.PackageManager;
using UnityEngine;
using static UnityEngine.LowLevelPhysics2D.PhysicsLayers;

namespace Assets.Scripts.InGameLogic
{
    /// <summary>
    /// Строка авторитативного табло очков. Реплицируется сервером всем клиентам через
    /// NetworkList — это даёт бесплатную полную синхронизацию для опоздавших клиентов
    /// (NGO шлёт им EventType.Full при спавне) и точечные дельты (Add/Value/RemoveAt)
    /// для всех остальных изменений.
    ///
    /// NetworkObjectId используется как идентификатор строки, а не OwnerClientId:
    /// у ботов OwnerClientId по умолчанию равен серверу, и на хосте это тот же ID,
    /// что и у машины самого хоста — по OwnerClientId их не различить (см. этот же
    /// приём в NetworkProvider.RespawnObject).
    /// </summary>
    public struct PlayerScoreEntry : IEquatable<PlayerScoreEntry>, INetworkSerializable
    {
        public ulong NetworkObjectId;
        public FixedString128Bytes Name;
        public int Score;
        public int Kills;

        public bool Equals(PlayerScoreEntry other) =>
            NetworkObjectId == other.NetworkObjectId &&
            Name.Equals(other.Name) &&
            Score == other.Score &&
            Kills == other.Kills;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref NetworkObjectId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Score);
            serializer.SerializeValue(ref Kills);
        }
    }

    /// <summary>
    /// Авторитативный подсчёт очков: урон, киллы, очки за выживание (растущий коэффициент).
    /// Вся арифметика — только на сервере (см. IsServer-проверки), клиентам достаётся
    /// только чтение реплицированного <see cref="scores"/> и локальный рендер в
    /// <see cref="ScoreBoardUI"/>. Живёт на NetworkProviderObject (см. NetworkProvider),
    /// регистрация машин идёт через NetworkProvider.RegisterPlayer/UnregisterPlayer.
    /// </summary>
    public class ScoreManager : NetworkBehaviour
    {
        public static ScoreManager Instance { get; private set; }

        [Header("Урон и киллы")]
        [Tooltip("Очков за 1 единицу нанесённого урона")]
        [SerializeField] private float pointsPerDamage = 1f;

        [Tooltip("Бонус за уничтожение чужой машины")]
        [SerializeField] private int pointsPerKill = 250;

        [Header("Очки за выживание")]
        [Tooltip("Как часто начисляются очки за выживание (сек)")]
        [SerializeField] private float tickInterval = 1f;

        [Tooltip("Базовые очки за один тик, ДО применения коэффициента")]
        [SerializeField] private float basePointsPerTick = 0f;

        [Tooltip("Насколько растёт коэффициент за каждую секунду жизни без смерти")]
        [SerializeField] private float survivalCoefficientGrowthPerSecond = 0.05f;

        [Tooltip("Потолок коэффициента — чтобы очень долгая жизнь не давала бесконечно растущие очки")]
        [SerializeField] private float maxSurvivalCoefficient = 3f;

        // Авторитативное реплицируемое табло. Пишет только сервер, читают все пиры.
        private readonly NetworkList<PlayerScoreEntry> scores = new(
            writePerm: NetworkVariableWritePermission.Server);

        // Серверная бухгалтерия, не реплицируется: кому из зарегистрированных игроков
        // сколько секунд накопилось без смерти + ссылка на CarHealth, чтобы тикер
        // мог проверять IsDead. Ключ — тот же NetworkObjectId, что и в scores.
        private class ServerPlayerState
        {
            public CarHealth health;
            public float timeAliveThisLife;
            public Action onRespawned;
        }
        private readonly Dictionary<ulong, ServerPlayerState> serverPlayers = new();

        private ScoreBoardUI scoreboardUI;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            scores.OnListChanged += HandleScoresChanged;

            if (IsServer)
                StartCoroutine(SurvivalTickLoop());

            // Локальная UI-панель могла успеть заспавниться раньше нас (или позже —
            // см. симметричный вызов в ScoreBoardUI.Start()). Что бы ни случилось
            // первым, именно оно и подхватит уже готовую сторону.
            if (ScoreBoardUI.Instance != null)
                SetScoreBoard(ScoreBoardUI.Instance);
        }

        public override void OnNetworkDespawn()
        {
            scores.OnListChanged -= HandleScoresChanged;
            StopAllCoroutines();
        }

        /// <summary>Подключает локальную UI-панель табло. Вызывается с обеих сторон — см. OnNetworkSpawn.</summary>
        public void SetScoreBoard(ScoreBoardUI newBoard)
        {
            scoreboardUI = newBoard;
            RebuildScoreboard();
        }

        private void RebuildScoreboard()
        {
            if (scoreboardUI == null) return;

            scoreboardUI.Clear();
            foreach (var entry in scores)
                scoreboardUI.AddOrUpdateRow(entry.NetworkObjectId, entry.Name.ToString(), entry.Score);
        }

        private void HandleScoresChanged(NetworkListEvent<PlayerScoreEntry> change)
        {
            if (scoreboardUI == null) return;

            switch (change.Type)
            {
                case NetworkListEvent<PlayerScoreEntry>.EventType.Add:
                case NetworkListEvent<PlayerScoreEntry>.EventType.Insert:
                case NetworkListEvent<PlayerScoreEntry>.EventType.Value:
                    scoreboardUI.AddOrUpdateRow(change.Value.NetworkObjectId, change.Value.Name.ToString(), change.Value.Score);
                    break;

                case NetworkListEvent<PlayerScoreEntry>.EventType.Remove:
                case NetworkListEvent<PlayerScoreEntry>.EventType.RemoveAt:
                    scoreboardUI.RemoveRow(change.Value.NetworkObjectId);
                    break;

                case NetworkListEvent<PlayerScoreEntry>.EventType.Clear:
                    scoreboardUI.Clear();
                    break;

                case NetworkListEvent<PlayerScoreEntry>.EventType.Full:
                    // Полная синхронизация — так приходит начальное состояние опоздавшему клиенту.
                    RebuildScoreboard();
                    break;
            }
        }

        // ==================== Регистрация игроков (только сервер) ====================
        // Вызывается из NetworkProvider.RegisterPlayer/UnregisterPlayer — боты туда
        // не попадают (см. CarAgent.OnNetworkSpawn), поэтому здесь заведомо только
        // настоящие игроки.

        public void RegisterPlayer(CarAgent agent)
        {
            if (!IsServer || agent == null) return;

            var health = agent.GetComponent<CarHealth>();
            if (health == null) return;

            ulong id = agent.NetworkObjectId;
            if (serverPlayers.ContainsKey(id)) return;

            var state = new ServerPlayerState { health = health };
            state.onRespawned = () => state.timeAliveThisLife = 0f;

            health.OnGettingDamage += HandleDamageDealt;
            health.OnDied += HandleKill;
            health.OnRespawned += state.onRespawned;

            serverPlayers[id] = state;

            scores.Add(new PlayerScoreEntry
            {
                NetworkObjectId = id,
                Name = LobbyManager.Instance.GetNicknameFor(agent.OwnerClientId),
                Score = 0,
                Kills = 0
            });
        }

        public void UnregisterPlayer(CarAgent agent)
        {
            if (!IsServer || agent == null) return;

            ulong id = agent.NetworkObjectId;
            if (!serverPlayers.TryGetValue(id, out var state)) return;

            if (state.health != null)
            {
                state.health.OnGettingDamage -= HandleDamageDealt;
                state.health.OnDied -= HandleKill;
                state.health.OnRespawned -= state.onRespawned;
            }
            serverPlayers.Remove(id);

            for (int i = 0; i < scores.Count; i++)
            {
                if (scores[i].NetworkObjectId != id) continue;
                scores.RemoveAt(i);
                break;
            }
        }

        // ==================== Начисление очков (только сервер) ====================

        private void HandleDamageDealt(float amount, CarHealth attacker)
        {
            if (!IsServer || attacker == null) return;
            AddScore(attacker.NetworkObjectId, Mathf.RoundToInt(amount * pointsPerDamage));
        }

        private void HandleKill(CarHealth attacker)
        {
            if (!IsServer || attacker == null) return;
            AddScore(attacker.NetworkObjectId, pointsPerKill, addKill: true);
        }

        private IEnumerator SurvivalTickLoop()
        {
            var wait = new WaitForSeconds(tickInterval);
            while (true)
            {
                yield return wait;

                foreach (var kv in serverPlayers)
                {
                    var state = kv.Value;
                    if (state.health == null || state.health.IsDead) continue;

                    state.timeAliveThisLife += tickInterval;
                    float coefficient = Mathf.Min(
                        1f + survivalCoefficientGrowthPerSecond * state.timeAliveThisLife,
                        maxSurvivalCoefficient);

                    AddScore(kv.Key, Mathf.RoundToInt(basePointsPerTick * coefficient));
                }
            }
        }
        
        public void AddScore(ulong networkObjectId, int amount, bool addKill = false)
        {
            if (!IsServer || (amount == 0 && !addKill)) return;

            for (int i = 0; i < scores.Count; i++)
            {
                if (scores[i].NetworkObjectId != networkObjectId) continue;

                var entry = scores[i];
                entry.Score += amount;
                if (addKill) entry.Kills++;
                scores[i] = entry;
                return;
            }
        }
        [ServerRpc]
        public void AddScoreServerRpc(ulong networkObjectId, int amount, bool addKill = false)
        {
            if (!IsServer || (amount == 0 && !addKill)) return;

            for (int i = 0; i < scores.Count; i++)
            {
                if (scores[i].NetworkObjectId != networkObjectId) continue;

                var entry = scores[i];
                entry.Score += amount;
                if (addKill) entry.Kills++;
                scores[i] = entry;
                return;
            }
        }
    }
}
