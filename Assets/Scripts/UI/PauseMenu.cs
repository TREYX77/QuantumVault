using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The Escape menu: resume, reset to the last checkpoint, restart the level, main menu, quit.
// Freezes time and frees the cursor while open. Player scripts check IsPaused and ignore input
// rather than being disabled, since disabling GrabInteract drops whatever is being carried.
[AddComponentMenu("UI/Pause Menu")]
[DisallowMultipleComponent]
public class PauseMenu : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("Everything shown while paused. Hidden otherwise.")]
    [SerializeField] private GameObject panel;

    [SerializeField] private Button resumeButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button quitButton;

    [Header("Behaviour")]
    [SerializeField] private Key toggleKey = Key.Escape;

    [Tooltip("Scene the Main Menu button loads. It must be in the build scene list.")]
    [SerializeField] private string mainMenuScene = "MainMenu";

    [Tooltip("Fade length for Restart Level and Main Menu.")]
    [SerializeField] private float fadeDuration = 0.4f;

    private static PauseMenu current;

    private float timeScaleBeforePause = 1f;

    // True while any pause menu is open. Player scripts read this to ignore input.
    public static bool IsPaused { get; private set; }

    // True when the scene has a pause menu, so PlayerCamera leaves Escape to it.
    public static bool Present => current != null && current.isActiveAndEnabled;

    // Statics survive play sessions when the domain reload is skipped.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        current = null;
        IsPaused = false;
    }

    void Awake()
    {
        current = this;

        if (panel != null) panel.SetActive(false);

        EnsureEventSystem();

        Hook(resumeButton, Resume);
        Hook(resetButton, ResetToCheckpoint);
        Hook(restartButton, RestartLevel);
        Hook(mainMenuButton, GoToMainMenu);
        Hook(quitButton, Quit);
    }

    void OnDestroy()
    {
        if (current != this) return;

        // Leaving a scene while paused must not leave the next one frozen.
        if (IsPaused)
        {
            Time.timeScale = timeScaleBeforePause;
            AudioListener.pause = false;
        }

        IsPaused = false;
        current = null;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;

        // Escape closes the console first; the same press must not also open this menu.
        if (keyboard == null || !keyboard[toggleKey].wasPressedThisFrame || DevConsole.BlocksInput) return;

        if (IsPaused) Resume();
        else if (!SceneTransition.IsTransitioning) Pause();
    }

    public void Pause()
    {
        if (IsPaused) return;

        IsPaused = true;
        timeScaleBeforePause = Time.timeScale;
        Time.timeScale = 0f;
        AudioListener.pause = true;

        if (panel != null) panel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Keyboard and gamepad can navigate straight away.
        if (resumeButton != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
    }

    public void Resume()
    {
        Unpause(true);
    }

    // relockCursor is false when leaving for a menu scene, which has no player camera to unlock
    // it again and would otherwise open with an invisible, locked cursor.
    private void Unpause(bool relockCursor)
    {
        if (!IsPaused) return;

        IsPaused = false;
        Time.timeScale = timeScaleBeforePause;
        AudioListener.pause = false;

        if (panel != null) panel.SetActive(false);

        if (!relockCursor) return;

        PlayerCamera look = FindAnyObjectByType<PlayerCamera>();

        if (look != null) look.RelockCursor();
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void ResetToCheckpoint()
    {
        Resume();
        CheckpointManager.Instance.ResetToCheckpoint();
        ActivateVoiceInstance.RestartPlaying();
    }

    public void RestartLevel()
    {
        Resume();
        SceneTransition.Load(new TransitionSettings(SceneManager.GetActiveScene().name, fadeDuration));
    }

    public void GoToMainMenu()
    {
        Unpause(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneTransition.Load(new TransitionSettings(mainMenuScene, fadeDuration));
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void Hook(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) button.onClick.AddListener(action);
    }

    // Buttons need an EventSystem to be clickable, and none of the levels has one.
    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }
}
