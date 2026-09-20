using UnityEngine;

// One transition, authored in the inspector. Only sceneName is required.
[System.Serializable]
public class TransitionSettings
{
    [Header("Destination")]
    [Tooltip("Scene to load. It must be added to File > Build Profiles > Scene List.")]
    public string sceneName;

    [Header("Timing")]
    [Tooltip("Seconds to fade the screen out before loading.")]
    [Min(0f)] public float fadeOutDuration = 0.4f;

    [Tooltip("Seconds to hold on a fully covered screen. Raise it to give a logo time to read.")]
    [Min(0f)] public float holdDuration = 0.1f;

    [Tooltip("Seconds to fade back in once the new scene is live.")]
    [Min(0f)] public float fadeInDuration = 0.4f;

    [Tooltip("Shape of the fade. The default eases in and out; a straight line is linear.")]
    public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Visuals")]
    [Tooltip("Colour the screen fades to.")]
    public Color color = Color.black;

    [Tooltip("Optional full-screen image drawn in place of a flat colour. Tinted by Colour.")]
    public Sprite image;

    [Tooltip("Optional UI prefab shown while the screen is covered: a spinner, logo or hint card. " +
             "It is spawned under the transition canvas and destroyed when the fade finishes.")]
    public GameObject overlayPrefab;

    public float TotalDuration => fadeOutDuration + holdDuration + fadeInDuration;

    public TransitionSettings() { }

    public TransitionSettings(string sceneName, float fadeDuration = 0.4f)
    {
        this.sceneName = sceneName;
        fadeOutDuration = fadeDuration;
        fadeInDuration = fadeDuration;
    }

    public TransitionSettings Clone()
    {
        return (TransitionSettings)MemberwiseClone();
    }
}
