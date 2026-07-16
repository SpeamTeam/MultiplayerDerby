using Assets.Scripts.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TMPro;
using UnityEngine.SocialPlatforms.Impl;
using Assets.Scripts.InGameLogic;

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
    private Coroutine countdownCoroutine;
    private TextMeshProUGUI timerText;

    [SerializeField] private float checkInterval = 10f;
    [SerializeField] private List<GameEventData> events = new List<GameEventData>();

    private Coroutine _checkRoutine;
    private System.Random _rng = new System.Random();

    private int timerDuration = 15;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    [ClientRpc]
    void GetTimerClientRpc()
    {
        GameObject CurTimer = ScoreBoardUI.Instance.gameObject.transform.Find("Timer").gameObject;
        CurTimer.SetActive(true);
        timerText = CurTimer.GetComponent<TextMeshProUGUI>();
        return ;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return; // логика только на сервере
        GetTimerClientRpc();
        //CurTimer = ScoreBoardUI.Instance.gameObject.transform.Find("Timer").gameObject;
        //CurTimer.SetActive(true);
        //timerText = CurTimer.GetComponent<TextMeshProUGUI>();
        StartCountdown();
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
    public void StartCountdown()
    {
        if (countdownCoroutine != null)
            StopCoroutine(countdownCoroutine);

        countdownCoroutine = StartCoroutine(CountdownRoutine(timerDuration));
    }

    public void StopCountdown()
    {
        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }
    }

    private IEnumerator CountdownRoutine(int totalSeconds)
    {
        int remaining = totalSeconds;

        UpdateTextClientRpc(remaining);

        while (remaining > 0)
        {
            yield return new WaitForSeconds(1f);
            remaining--;
            UpdateTextClientRpc(remaining);
        }

        countdownCoroutine = null;
        OnFinished();
    }
    [ClientRpc]
    private void UpdateTextClientRpc(int seconds)
    {
        int minutes = seconds / 60;
        int secs = seconds % 60;
        timerText.text = $"{minutes:00}:{secs:00}";
    }

    private void OnFinished()
    {
        Debug.Log("Countdown finished!");

        ScoreManager.Instance.PostCombat();
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
