using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Централизованный "тикер" очков за выживание.
///
/// ПОЧЕМУ НЕ Update() НА КАЖДОЙ МАШИНЕ:
/// Если у каждой из N машин свой Update(), это N вызовов в КАЖДЫЙ кадр
/// (60 раз в секунду при 60 FPS) ради простого начисления очков, которое
/// физически достаточно делать раз в секунду — точность тут не нужна.
/// Вместо этого один менеджер на сцену запускает ОДНУ корутину, которая
/// просыпается раз в tickInterval секунд и обходит список машин — это
/// на два порядка меньше вызовов при том же результате для игрока.
///
/// Синглтон создаётся лениво — не нужно вручную класть его в сцену.
///
/// СЕТЬ (для коллег): начисление очков — серверная операция. Этот тикер
/// должен существовать и работать только на сервере; на клиентах просто
/// не создавайте PlayerScore.OnEnable-регистрацию (или гасите тикер),
/// т.к. итоговый счёт всё равно должен приходить с сервера как NetworkVariable.
/// </summary>
public class ScoreTickManager : MonoBehaviour
{
    private static ScoreTickManager _instance;

    public static ScoreTickManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("ScoreTickManager (auto)");
                _instance = go.AddComponent<ScoreTickManager>();
            }
            return _instance;
        }
    }

    /// <summary>Проверить существование без создания — чтобы не плодить менеджер при выгрузке сцены.</summary>
    public static bool Exists => _instance != null;

    [Tooltip("Как часто начисляются очки за выживание (сек). Не влияет на плавность игры — только на гранулярность начисления очков.")]
    public float tickInterval = 1f;

    private readonly List<PlayerScore> registered = new List<PlayerScore>();

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void OnEnable()
    {
        StartCoroutine(TickLoop());
    }

    private IEnumerator TickLoop()
    {
        var wait = new WaitForSeconds(tickInterval);
        while (true)
        {
            yield return wait;

            // Идём с конца — безопасно, если кто-то отпишется во время обхода.
            for (int i = registered.Count - 1; i >= 0; i--)
            {
                if (registered[i] == null)
                {
                    registered.RemoveAt(i);
                    continue;
                }
                registered[i].TickSurvival(tickInterval);
            }
        }
    }

    public void Register(PlayerScore score)
    {
        if (!registered.Contains(score))
            registered.Add(score);
    }

    public void Unregister(PlayerScore score)
    {
        registered.Remove(score);
    }
}
