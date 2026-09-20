using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// Scene change entry point. Static, so the file name may differ from the type - do not rename.
public static class SceneTransition
{
    public static bool IsTransitioning => SceneTransitionFade.Instance.IsTransitioning;

    public static event Action<float> Progress
    {
        add { SceneTransitionFade.Instance.Progress += value; }
        remove { SceneTransitionFade.Instance.Progress -= value; }
    }

    public static void Load(string sceneName)
    {
        Load(new TransitionSettings(sceneName));
    }

    public static void Load(string sceneName, float fadeDuration)
    {
        Load(new TransitionSettings(sceneName, fadeDuration));
    }

    public static void Load(string sceneName, float fadeDuration, Color color)
    {
        TransitionSettings settings = new TransitionSettings(sceneName, fadeDuration)
        {
            color = color
        };

        Load(settings);
    }

    public static void Load(TransitionSettings settings)
    {
        SceneTransitionFade.Instance.Play(settings);
    }

    public static void Reload(float fadeDuration = 0.4f)
    {
        Load(new TransitionSettings(SceneManager.GetActiveScene().name, fadeDuration));
    }

    // Stops play mode in the editor, so a quit button works without building.
    public static void Quit(float fadeDuration = 0.4f)
    {
        SceneTransitionFade fade = SceneTransitionFade.Instance;
        TransitionSettings settings = new TransitionSettings(null, fadeDuration);

        fade.StartCoroutine(QuitRoutine(fade, settings));
    }

    private static System.Collections.IEnumerator QuitRoutine(
        SceneTransitionFade fade, TransitionSettings settings)
    {
        yield return fade.FadeOut(settings);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
