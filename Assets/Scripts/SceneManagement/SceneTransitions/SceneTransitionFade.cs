using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drives the fade overlay and the scene load behind it. Nothing needs to be placed in a
/// scene: the first transition creates this object, its canvas and its image, and it then
/// survives every following load. Drop it into a scene by hand only if you want to assign
/// your own canvas to <see cref="canvasGroup"/> and <see cref="overlayImage"/>.
///
/// Call it through <see cref="SceneTransition"/> rather than talking to it directly.
/// </summary>
[DisallowMultipleComponent]
public class SceneTransitionFade : MonoBehaviour
{
    [Tooltip("Optional. Left empty, a full-screen canvas is built at runtime.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("Optional. Left empty, a full-screen image is built at runtime.")]
    [SerializeField] private Image overlayImage;

    [Tooltip("Drawn above every other canvas in the project.")]
    [SerializeField] private int sortingOrder = short.MaxValue;

    private static SceneTransitionFade instance;
    private GameObject activeOverlay;

    /// <summary>Reports load progress from 0 to 1 while a transition is running.</summary>
    public event Action<float> Progress;

    public bool IsTransitioning { get; private set; }

    /// <summary>The live instance, created on first use.</summary>
    public static SceneTransitionFade Instance
    {
        get
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindAnyObjectByType<SceneTransitionFade>();

            if (instance == null)
            {
                GameObject host = new GameObject("Scene Transition Fade");
                instance = host.AddComponent<SceneTransitionFade>();
            }

            return instance;
        }
    }

    // Statics survive play sessions when Enter Play Mode Options skip the domain reload,
    // which would otherwise leave a stale instance pointing at a destroyed object.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        // DontDestroyOnLoad only accepts root objects.
        if (transform.parent != null)
        {
            transform.SetParent(null, false);
        }

        DontDestroyOnLoad(gameObject);
        BuildOverlayIfMissing();

        canvasGroup.alpha = 0f;
        SetBlocking(false);
    }

    /// <summary>
    /// Runs the full transition. A second call while one is already running is ignored, so
    /// a double-clicked button or a re-entered trigger cannot stack two loads.
    /// </summary>
    public void Play(TransitionSettings settings)
    {
        if (settings == null)
        {
            Debug.LogError("Scene transition has no settings.", this);
            return;
        }

        if (IsTransitioning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.sceneName))
        {
            Debug.LogError("Scene transition has no scene name set.", this);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(settings.sceneName))
        {
            Debug.LogError(
                "Scene \"" + settings.sceneName + "\" cannot be loaded. Add it to " +
                "File > Build Profiles > Scene List, and check the name matches the scene asset.",
                this);
            return;
        }

        StartCoroutine(Run(settings));
    }

    /// <summary>Fades the screen out and leaves it covered. Useful before a cutscene or a quit.</summary>
    public Coroutine FadeOut(TransitionSettings settings)
    {
        ApplyVisuals(settings);
        SetBlocking(true);
        return StartCoroutine(FadeTo(1f, settings.fadeOutDuration, settings.curve));
    }

    /// <summary>Fades a covered screen back in.</summary>
    public Coroutine FadeIn(TransitionSettings settings)
    {
        return StartCoroutine(FadeInRoutine(settings));
    }

    private IEnumerator FadeInRoutine(TransitionSettings settings)
    {
        yield return FadeTo(0f, settings.fadeInDuration, settings.curve);
        SetBlocking(false);
        ClearOverlay();
    }

    private IEnumerator Run(TransitionSettings settings)
    {
        IsTransitioning = true;
        ApplyVisuals(settings);
        SetBlocking(true);

        yield return FadeTo(1f, settings.fadeOutDuration, settings.curve);

        SpawnOverlay(settings);

        AsyncOperation load = SceneManager.LoadSceneAsync(settings.sceneName);

        // Hold activation back so the new scene cannot pop in before the hold has played.
        load.allowSceneActivation = false;

        // Progress stalls at 0.9 while activation is blocked, so rescale it to read 0..1.
        while (load.progress < 0.9f)
        {
            Progress?.Invoke(Mathf.Clamp01(load.progress / 0.9f));
            yield return null;
        }

        Progress?.Invoke(1f);

        if (settings.holdDuration > 0f)
        {
            // Unscaled, so a hold still runs when the outgoing scene paused the game.
            yield return new WaitForSecondsRealtime(settings.holdDuration);
        }

        load.allowSceneActivation = true;

        while (!load.isDone)
        {
            yield return null;
        }

        yield return FadeTo(0f, settings.fadeInDuration, settings.curve);

        SetBlocking(false);
        ClearOverlay();

        IsTransitioning = false;
    }

    private IEnumerator FadeTo(float targetAlpha, float duration, AnimationCurve curve)
    {
        float startAlpha = canvasGroup.alpha;

        if (duration <= 0f)
        {
            canvasGroup.alpha = targetAlpha;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Unscaled time, or a pause menu setting timeScale to 0 would freeze the fade.
            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            float eased = curve != null && curve.length > 0 ? curve.Evaluate(t) : t;

            canvasGroup.alpha = Mathf.LerpUnclamped(startAlpha, targetAlpha, eased);
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
    }

    private void ApplyVisuals(TransitionSettings settings)
    {
        BuildOverlayIfMissing();

        overlayImage.color = settings.color;
        overlayImage.sprite = settings.image;
        overlayImage.type = Image.Type.Simple;
        overlayImage.preserveAspect = settings.image != null;
    }

    private void SpawnOverlay(TransitionSettings settings)
    {
        ClearOverlay();

        if (settings.overlayPrefab == null)
        {
            return;
        }

        activeOverlay = Instantiate(settings.overlayPrefab, canvasGroup.transform);
    }

    private void ClearOverlay()
    {
        if (activeOverlay != null)
        {
            Destroy(activeOverlay);
            activeOverlay = null;
        }
    }

    // Swallows clicks while the screen is covered, so buttons underneath cannot be hit mid-fade.
    private void SetBlocking(bool blocking)
    {
        canvasGroup.blocksRaycasts = blocking;
        canvasGroup.interactable = blocking;

        if (overlayImage != null)
        {
            overlayImage.raycastTarget = blocking;
        }
    }

    private void BuildOverlayIfMissing()
    {
        if (canvasGroup == null)
        {
            Canvas canvas = gameObject.GetComponent<Canvas>();

            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = sortingOrder;
                gameObject.AddComponent<CanvasScaler>();
                gameObject.AddComponent<GraphicRaycaster>();
            }

            canvasGroup = gameObject.GetComponent<CanvasGroup>();

            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        if (overlayImage == null)
        {
            GameObject imageHost = new GameObject("Overlay Image", typeof(RectTransform));
            imageHost.transform.SetParent(canvasGroup.transform, false);

            overlayImage = imageHost.AddComponent<Image>();

            // Stretch to every corner so it covers any aspect ratio.
            RectTransform rect = overlayImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
