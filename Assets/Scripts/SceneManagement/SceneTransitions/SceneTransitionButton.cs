using UnityEngine;
using UnityEngine.UI;

// Per-button scene change. Drop on a Button and set the scene name; onClick is automatic.
[AddComponentMenu("Scene Management/Scene Transition Button")]
public class SceneTransitionButton : MonoBehaviour
{
    [SerializeField] private TransitionSettings transition = new TransitionSettings();

    [Tooltip("Hook this object's Button automatically. Turn off if you would rather wire " +
             "Go() into the Button's On Click list yourself.")]
    [SerializeField] private bool autoHookButton = true;

    [Tooltip("Optional. Left empty, a Button on this object is used.")]
    [SerializeField] private Button button;

    public TransitionSettings Transition => transition;

    void Awake()
    {
        if (!autoHookButton) return;

        if (button == null) button = GetComponent<Button>();
        if (button != null) button.onClick.AddListener(Go);
    }

    void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(Go);
    }

    public void Go()
    {
        SceneTransition.Load(transition);
    }

    public void Go(string sceneName)
    {
        TransitionSettings overridden = transition.Clone();
        overridden.sceneName = sceneName;
        SceneTransition.Load(overridden);
    }
}
