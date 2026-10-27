using UnityEngine;
using UnityEngine.InputSystem;

// Looks down the crosshair for something interactable within reach, shows its prompt, and uses it
// when the interact key is pressed. Pickups also answer to the grab key, and the prompt names the
// grab key for loose props too, so every way to pick something up is on screen. Goes on the player.
// Runs before GrabInteract, so the grab-key press that drops a prop is not also read as a pickup.
[AddComponentMenu("Player/Player Interactor")]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10)]
public class PlayerInteractor : MonoBehaviour
{
    [Tooltip("F is grab, R rotate, Q the photon emitters, T the flashlight.")]
    [SerializeField] private Key interactKey = Key.E;

    [Tooltip("How far away something can be used, in metres. Matches the grab range, so the " +
             "prompt never vanishes while the grab key would still work.")]
    [SerializeField] private float reach = 3f;

    [Tooltip("Thickness of the look ray. A little forgiveness, so a flashlight lying on a table " +
             "does not need a pixel-perfect aim. 0 is an exact ray.")]
    [SerializeField] private float aimRadius = 0.1f;

    [Tooltip("What the look ray can hit. Triggers are always ignored.")]
    [SerializeField] private LayerMask mask = Physics.DefaultRaycastLayers;

    [Tooltip("Optional. Left empty, the camera under the player is used.")]
    [SerializeField] private Camera viewCamera;

    [Header("Prompt")]
    [SerializeField] private bool showPrompt = true;
    [SerializeField] private int promptSize = 22;
    [SerializeField] private Color promptColor = new Color(0.92f, 0.95f, 1f);

    [Tooltip("Shown with the grab key while aimed at a loose prop that can be carried.")]
    [SerializeField] private string grabPrompt = "Pick up";

    // Shared by every interactor, which is safe because Update never runs two at once. Two
    // buffers, because the sight check runs while the probe's hits are still being read.
    private static readonly RaycastHit[] hits = new RaycastHit[16];
    private static readonly RaycastHit[] sightHits = new RaycastHit[16];

    private GrabInteract grab;
    private GUIStyle style;
    private GUIStyle shadow;

    public IInteractable Target { get; private set; }
    public Key InteractKey => interactKey;

    // A pickup takes the grab key only when the grabber has nothing of its own to use it for.
    private bool PickupTakesGrabKey => Target is IPickup && grab != null && !grab.IsHolding && grab.Aimed == null;

    void Awake()
    {
        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>(true);
        if (viewCamera == null) viewCamera = Camera.main;

        grab = GetComponentInChildren<GrabInteract>(true);
    }

    void Update()
    {
        Target = FindTarget();

        if (PauseMenu.IsPaused || DevConsole.BlocksInput) return;

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null || Target == null || !Target.CanInteract) return;

        bool pressed = keyboard[interactKey].wasPressedThisFrame
                    || (PickupTakesGrabKey && keyboard[grab.GrabKey].wasPressedThisFrame);

        if (pressed) Target.Interact(this);
    }

    // The nearest interactable the look probe touches that the eye can actually see. The fat
    // probe forgives a rough aim; the sight line stops it reaching through walls, and stops the
    // table under a small item from taking the hit instead of the item.
    private IInteractable FindTarget()
    {
        if (viewCamera == null) return null;

        Transform eye = viewCamera.transform;
        Transform carried = grab != null && grab.Held != null ? grab.Held.transform : null;

        int count = aimRadius > 0f
            ? Physics.SphereCastNonAlloc(eye.position, aimRadius, eye.forward, hits, reach, mask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(eye.position, eye.forward, hits, reach, mask, QueryTriggerInteraction.Ignore);

        IInteractable best = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];

            if (hit.distance >= bestDistance || IsIgnored(hit.collider.transform, carried)) continue;

            IInteractable candidate = hit.collider.GetComponentInParent<IInteractable>();

            if (candidate == null) continue;

            // A probe that starts inside a collider reports no point.
            Vector3 point = hit.distance > 0f ? hit.point : hit.collider.ClosestPoint(eye.position);

            if (!InSight(eye.position, point, candidate, carried)) continue;

            bestDistance = hit.distance;
            best = candidate;
        }

        return best;
    }

    // Nothing solid between the eye and the point except the player and the target itself.
    private bool InSight(Vector3 from, Vector3 to, IInteractable target, Transform carried)
    {
        Vector3 line = to - from;
        float length = line.magnitude;

        if (length < 0.001f) return true;

        // Stops just short, so the surface the point lies on is not counted against it.
        int count = Physics.RaycastNonAlloc(from, line / length, sightHits, length - 0.02f, mask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider blocker = sightHits[i].collider;

            if (IsIgnored(blocker.transform, carried)) continue;
            if (blocker.GetComponentInParent<IInteractable>() == target) continue;

            return false;
        }

        return true;
    }

    private bool IsIgnored(Transform other, Transform carried)
    {
        return other.IsChildOf(transform) || (carried != null && other.IsChildOf(carried));
    }

    // Every action open to the player at once, each with its key: "[F] Pick up    [E] Fire emitter".
    private string PromptText()
    {
        string carry = grab != null && !grab.IsHolding && grab.Aimed != null
            ? $"[{grab.GrabKey}]  {grabPrompt}"
            : null;

        string use = null;
        string prompt = Target?.Prompt;

        if (!string.IsNullOrEmpty(prompt))
        {
            Key key = PickupTakesGrabKey ? grab.GrabKey : interactKey;

            use = Target.CanInteract ? $"[{key}]  {prompt}" : prompt;
        }

        if (carry == null) return use;

        return use == null ? carry : carry + "        " + use;
    }

    void OnGUI()
    {
        if (!showPrompt || PauseMenu.IsPaused) return;

        string text = PromptText();

        if (string.IsNullOrEmpty(text)) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            shadow = new GUIStyle(style);
        }

        style.fontSize = promptSize;
        shadow.fontSize = promptSize;
        style.normal.textColor = promptColor;
        shadow.normal.textColor = new Color(0f, 0f, 0f, 0.8f);

        Rect rect = new Rect(0f, Screen.height * 0.58f, Screen.width, promptSize * 2f);

        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, shadow);
        GUI.Label(rect, text, style);
    }
}
