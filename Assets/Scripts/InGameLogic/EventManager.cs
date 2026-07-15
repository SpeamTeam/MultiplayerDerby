using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// ќписание одного случайного ивента
[Serializable]
public class GameEventData
{
    public string eventId;
    [Range(0f, 1f)] public float chance = 0.1f;
    public float duration = 15f;
    public float cooldown = 30f;

    // —сылка на ScriptableObject, который реализует IGameEvent
    public GameEventBehaviour behaviour;

    [NonSerialized] public bool isActive;
    [NonSerialized] public float cooldownTimer;
}

public class EventManager : NetworkBehaviour
{
    public static EventManager Instance { get; private set; }

    [SerializeField] private float checkInterval = 10f;
    [SerializeField] private List<GameEventData> events = new List<GameEventData>();

    private Coroutine _checkRoutine;
    private System.Random _rng = new System.Random();

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
        if (!IsServer) return; // логика только на сервере
        _checkRoutine = StartCoroutine(EventCheckLoop());
    }

    public override void OnNetworkDespawn()
    {
        if (_checkRoutine != null)
        {
            StopCoroutine(_checkRoutine);
            _checkRoutine = null;
        }
    }

    private IEnumerator EventCheckLoop()
    {
        var wait = new WaitForSeconds(checkInterval);

        while (true)
        {
            yield return wait;

            foreach (var evt in events)
            {
                // тикаем кулдаун
                if (evt.cooldownTimer > 0f)
                {
                    evt.cooldownTimer -= checkInterval;
                    continue;
                }

                if (evt.isActive) continue; // уже идЄт Ч не роллим повторно

                float roll = (float)_rng.NextDouble();
                if (roll <= evt.chance)
                {
                    StartCoroutine(RunEvent(evt));
                }
            }
        }
    }

    private IEnumerator RunEvent(GameEventData evt)
    {
        evt.isActive = true;
        Debug.Log($"[Server] —обытие запущено: {evt.eventId}");

        // сообщаем клиентам, что ивент началс€
        NotifyEventStartedClientRpc(evt.eventId);

        // здесь же можно вызвать реальную серверную логику ивента
        ApplyServerSideEventLogic(evt);

        yield return new WaitForSeconds(evt.duration);

        evt.isActive = false;
        evt.cooldownTimer = evt.cooldown;
        evt.behaviour?.OnServerEnd(this);
        NotifyEventEndedClientRpc(evt.eventId);
        Debug.Log($"[Server] —обытие завершено: {evt.eventId}");
    }
    public Coroutine RunCoroutine(IEnumerator routine) => StartCoroutine(routine);

    private void ApplyServerSideEventLogic(GameEventData evt)
    {
        evt.behaviour?.OnServerStart(this);
    }

    [ClientRpc]
    private void NotifyEventStartedClientRpc(string eventId)
    {
        var evt = events.Find(e => e.eventId == eventId);
        evt?.behaviour?.OnClientStart(this);
    }

    [ClientRpc]
    private void NotifyEventEndedClientRpc(string eventId)
    {
        var evt = events.Find(e => e.eventId == eventId);
        evt?.behaviour?.OnClientEnd(this);
    }
}