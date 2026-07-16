using UnityEngine;

public class MatchMusicStarter : MonoBehaviour
{
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private float delayBeforeMusic = 9f;

    private void Start()
    {
        // ћузыка не должна играть сама по себе Ч отключаем Play On Awake в инспекторе
        Invoke(nameof(PlayMusic), delayBeforeMusic);
    }

    private void PlayMusic()
    {
        if (musicSource != null && !musicSource.isPlaying)
        {
            musicSource.Play();
        }
    }
}