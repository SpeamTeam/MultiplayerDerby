using UnityEngine;

public abstract class GameEventBehaviour : ScriptableObject
{
    // Серверная авторитетная логика: спавн, NetworkVariable, баланс
    public virtual void OnServerStart(EventManager ctx) { }
    public virtual void OnServerEnd(EventManager ctx) { }

    // Чисто визуальная логика: выполняется на каждом клиенте через ClientRpc
    public virtual void OnClientStart(EventManager ctx) { }
    public virtual void OnClientEnd(EventManager ctx) { }
}