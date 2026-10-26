
using UnityEngine;

[AddComponentMenu("Audio/Music Pause Menu")]
[DisallowMultipleComponent]
public class MusicPauseMenu : MonoBehaviour
{
    [Header("Music")]
    [SerializeField] private AudioSource levelMusic;
    [SerializeField] private AudioSource pauseMusic;

    private bool wasPaused;

    private void Start()
    {
        // The PauseMenu script controls the actual pause state.
        wasPaused = PauseMenu.IsPaused;

        if (pauseMusic != null)
        {
            pauseMusic.Stop();
            pauseMusic.ignoreListenerPause = true;
        }

        if (levelMusic != null)
        {
            levelMusic.ignoreListenerPause = true;

            if (!levelMusic.isPlaying)
                levelMusic.Play();
        }

        UpdateMusic(PauseMenu.IsPaused);
    }

    private void Update()
    {
        // Detect when the existing PauseMenu opens or closes.
        bool isPaused = PauseMenu.IsPaused;

        if (isPaused == wasPaused)
            return;

        wasPaused = isPaused;
        UpdateMusic(isPaused);
    }

    private void UpdateMusic(bool isPaused)
    {
        if (isPaused)
        {
            if (levelMusic != null)
                levelMusic.Pause();

            if (pauseMusic != null && !pauseMusic.isPlaying)
                pauseMusic.Play();
        }
        else
        {
            if (pauseMusic != null)
                pauseMusic.Stop();

            if (levelMusic != null)
                levelMusic.UnPause();
        }
    }

    private void OnDisable()
    {
        // Avoid leaving music paused if this component is disabled.
        if (pauseMusic != null)
            pauseMusic.Stop();

        if (levelMusic != null && PauseMenu.IsPaused == false)
            levelMusic.UnPause();
    }
}
