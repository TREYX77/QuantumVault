
using UnityEngine;

public class MusicPauseMenu : MonoBehaviour
{
    [Header("Pause UI")]
    public GameObject pausePanel;

    [Header("Music")]
    public AudioSource levelMusic;
    public AudioSource pauseMusic;

    private bool isPaused = false;

    void Start()
    {
        pausePanel.SetActive(false);
        Time.timeScale = 1f;

        if (pauseMusic != null)
            pauseMusic.Stop();

        if (levelMusic != null && !levelMusic.isPlaying)
            levelMusic.Play();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused)
                ResumeGame();
            else
                PauseGame();
        }
    }

    public void PauseGame()
    {
        if (isPaused) return;

        isPaused = true;
        pausePanel.SetActive(true);
        Time.timeScale = 0f;

        if (levelMusic != null)
            levelMusic.Pause();

        if (pauseMusic != null)
            pauseMusic.Play();
    }

    public void ResumeGame()
    {
        if (!isPaused) return;

        isPaused = false;
        pausePanel.SetActive(false);
        Time.timeScale = 1f;

        if (pauseMusic != null)
            pauseMusic.Stop();

        if (levelMusic != null)
            levelMusic.UnPause();
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex
        );
    }
}

