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
    [Range(0f, 1f)] public float chance = 0.1f; // шанс срабатывани€ за один тик проверки
    public float duration = 15f;                // сколько длитс€ ивент
    public float cooldown = 30f;                // минимальна€ пауза после окончани€ перед новым роллом

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

        NotifyEventEndedClientRpc(evt.eventId);
        Debug.Log($"[Server] —обытие завершено: {evt.eventId}");
    }

    private void ApplyServerSideEventLogic(GameEventData evt)
    {
        // TODO: спавн мобов, баффы, изменение погоды и т.д.
        switch (evt.eventId)
        {
            case "meteor_shower":
                // ...
                break;
            case "double_loot":
                // ...
                break;
        }
    }

    [ClientRpc]
    private void NotifyEventStartedClientRpc(string eventId)
    {
        Debug.Log($"[Client] —обытие началось: {eventId}");
        // тут включаешь VFX/UI на клиенте
    }

    [ClientRpc]
    private void NotifyEventEndedClientRpc(string eventId)
    {
        Debug.Log($"[Client] —обытие закончилось: {eventId}");
        // выключаешь VFX/UI
    }
}