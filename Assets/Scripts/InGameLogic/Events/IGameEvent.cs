public interface IGameEvent
{
    void OnStart(EventManager ctx);
    void OnEnd(EventManager ctx);
}