using UnityEngine;
using UnityEngine.InputSystem;

// Per-location scene change. Put on a collider at a doorway; walking in loads the scene.
[AddComponentMenu("Scene Management/Scene Transition Trigger")]
[RequireComponent(typeof(Collider))]
public class SceneTransitionTrigger : MonoBehaviour
{
    [SerializeField] private TransitionSettings transition = new TransitionSettings();

    [Header("Activation")]
    [Tooltip("Only objects with this tag trigger the change. Leave empty to accept anything.")]
    [SerializeField] private string requiredTag = "Player";

    [Tooltip("Wait for a key press while inside, instead of firing on entry.")]
    [SerializeField] private bool requireKeyPress;

    [SerializeField] private Key interactKey = Key.E;

    [Tooltip("Ignore the trigger once it has fired. Stops a re-entry queuing a second load.")]
    [SerializeField] private bool onlyOnce = true;

    [Header("Gizmo")]
    [SerializeField] private Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.25f);

    private Collider trigger;
    private bool occupied;
    private bool fired;

    public bool AwaitingKeyPress => occupied && requireKeyPress && !fired;

    void Reset()
    {
        Collider self = GetComponent<Collider>();

        if (self != null) self.isTrigger = true;
    }

    void Awake()
    {
        trigger = GetComponent<Collider>();

        if (trigger != null && !trigger.isTrigger)
        {
            Debug.LogWarning(
                "SceneTransitionTrigger needs its collider set to Is Trigger. Fixing it for this run.",
                this);

            trigger.isTrigger = true;
        }
    }

    void Update()
    {
        if (!AwaitingKeyPress || Keyboard.current == null) return;

        if (Keyboard.current[interactKey].wasPressedThisFrame) Fire();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!Accepts(other)) return;

        occupied = true;

        if (!requireKeyPress) Fire();
    }

    void OnTriggerExit(Collider other)
    {
        if (Accepts(other)) occupied = false;
    }

    private bool Accepts(Collider other)
    {
        if (fired && onlyOnce) return false;

        return string.IsNullOrEmpty(requiredTag) || other.CompareTag(requiredTag);
    }

    public void Fire()
    {
        if (fired && onlyOnce) return;

        fired = true;
        SceneTransition.Load(transition);
    }

    // Trigger volumes are invisible, so draw where this one sits.
    void OnDrawGizmos()
    {
        Collider self = GetComponent<Collider>();

        if (self == null) return;

        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(self.bounds.center, self.bounds.size);

        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
        Gizmos.DrawWireCube(self.bounds.center, self.bounds.size);
    }
}
