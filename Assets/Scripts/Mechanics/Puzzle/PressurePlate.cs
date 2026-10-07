using System;
using UnityEngine;
using UnityEngine.Events;

// A floor button the player or a physics prop holds down. Goes on the plate's parent, which
// has a Base and a Button child. Gates and doors read IsPressed or listen to PressedChanged.
// It looks at what sits on the button every physics step instead of counting trigger
// enters and exits: a crate that is destroyed, or that vanishes because it was looked at,
// never sends an exit, and a counting plate would stay pressed forever.
[AddComponentMenu("Puzzle/Pressure Plate")]
[DisallowMultipleComponent]
public class PressurePlate : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("The part that sinks. Left empty, the child called Button is used.")]
    [SerializeField] private Transform button;

    [Header("What Presses It")]
    [Tooltip("The player standing on it.")]
    [SerializeField] private bool acceptPlayer = true;

    [Tooltip("Physics props, anything with Object Physics, such as the crate.")]
    [SerializeField] private bool acceptProps = true;

    [Tooltip("Any moving rigidbody at all, even ones that are not props.")]
    [SerializeField] private bool acceptAnyRigidbody;

    [Tooltip("A prop the player is carrying does not count, so hovering it over the plate " +
             "does not open anything. It has to be put down.")]
    [SerializeField] private bool ignoreCarriedProps = true;

    [Header("Sensor")]
    [Tooltip("How far above the button something still counts as on it, in metres.")]
    [SerializeField] private float sensorHeight = 0.3f;

    [Tooltip("How much of the button's width counts, so something leaning on the rim from " +
             "beside the plate does not press it.")]
    [Range(0.3f, 1f)]
    [SerializeField] private float sensorInset = 0.85f;

    [Header("Feel")]
    [Tooltip("How far the button sinks when pressed, in metres.")]
    [SerializeField] private float pressDepth = 0.08f;

    [Tooltip("How fast the button moves, in metres per second.")]
    [SerializeField] private float pressSpeed = 0.6f;

    [Tooltip("How long it stays pressed after the weight leaves, so a crate bouncing as it " +
             "lands does not flicker the gate open and shut.")]
    [SerializeField] private float releaseDelay = 0.15f;

    [Header("Glow")]
    [SerializeField] private Color idleGlow = new Color(0.6f, 0.08f, 0.06f);
    [SerializeField] private Color pressedGlow = new Color(0.25f, 0.6f, 1f);

    [Tooltip("Brightness of the glow. Above 1 blooms.")]
    [SerializeField] private float glowIntensity = 2f;

    [Header("Events")]
    [SerializeField] private UnityEvent onPressed;
    [SerializeField] private UnityEvent onReleased;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private readonly Collider[] overlapBuffer = new Collider[16];

    private Rigidbody buttonBody;
    private Renderer[] buttonRenderers;
    private MaterialPropertyBlock block;
    private Vector3 restLocal;
    private Bounds buttonLocalBounds;
    private float travel;
    private float lastOccupiedTime = float.NegativeInfinity;

    private GrabInteract grabber;
    private float nextGrabberSearch;

    public bool IsPressed { get; private set; }

    // The plate, then whether it is now pressed.
    public event Action<PressurePlate, bool> PressedChanged;

    void Reset()
    {
        button = transform.Find("Button");
    }

    void Awake()
    {
        if (button == null) button = transform.Find("Button");

        if (button == null)
        {
            Debug.LogError("Pressure Plate needs a child called Button, or one assigned in the inspector.", this);
            enabled = false;

            return;
        }

        restLocal = button.localPosition;
        buttonBody = button.GetComponent<Rigidbody>();
        buttonRenderers = button.GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();

        MeshFilter filter = button.GetComponent<MeshFilter>();
        buttonLocalBounds = filter != null && filter.sharedMesh != null
            ? filter.sharedMesh.bounds
            : new Bounds(Vector3.zero, Vector3.one);

        ApplyGlow();
    }

    void FixedUpdate()
    {
        if (SenseOccupant()) lastOccupiedTime = Time.time;

        bool held = Time.time - lastOccupiedTime <= releaseDelay;

        if (held != IsPressed) SetPressed(held);

        MoveButton();
    }

    private void SetPressed(bool value)
    {
        IsPressed = value;
        ApplyGlow();

        if (value) onPressed?.Invoke();
        else onReleased?.Invoke();

        PressedChanged?.Invoke(this, value);
    }

    // ---- Sensing -----------------------------------------------------------------------

    private bool SenseOccupant()
    {
        GetSensor(out Vector3 centre, out Vector3 halfExtents, out Quaternion rotation);

        int count = Physics.OverlapBoxNonAlloc(centre, halfExtents, overlapBuffer, rotation,
                                               Physics.AllLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (Accepts(overlapBuffer[i])) return true;
        }

        return false;
    }

    // A slab sitting on the button's top face, following the button down as it sinks, so
    // whatever is riding it down stays inside.
    private void GetSensor(out Vector3 centre, out Vector3 halfExtents, out Quaternion rotation)
    {
        Vector3 scale = button.lossyScale;
        Vector3 top = new Vector3(buttonLocalBounds.center.x, buttonLocalBounds.max.y, buttonLocalBounds.center.z);

        rotation = button.rotation;

        // Starts a little inside the button, so a prop resting exactly on it is caught.
        float bottomInside = 0.02f;
        centre = button.TransformPoint(top) + button.up * (sensorHeight - bottomInside) * 0.5f;

        halfExtents = new Vector3(
            Mathf.Abs(buttonLocalBounds.extents.x * scale.x) * sensorInset,
            (sensorHeight + bottomInside) * 0.5f,
            Mathf.Abs(buttonLocalBounds.extents.z * scale.z) * sensorInset);
    }

    private bool Accepts(Collider other)
    {
        // The plate's own base and button.
        if (other.transform.IsChildOf(transform)) return false;

        if (acceptPlayer && other.GetComponentInParent<CharacterController>() != null) return true;

        Rigidbody body = other.attachedRigidbody;

        if (body == null) return false;

        if (ignoreCarriedProps && IsCarried(body)) return false;

        if (acceptProps && body.GetComponent<ObjectPhysics>() != null) return true;

        return acceptAnyRigidbody && !body.isKinematic;
    }

    private bool IsCarried(Rigidbody body)
    {
        if (grabber == null && Time.time >= nextGrabberSearch)
        {
            nextGrabberSearch = Time.time + 1f;
            grabber = FindAnyObjectByType<GrabInteract>();
        }

        return grabber != null && grabber.Held != null && grabber.Held.Body == body;
    }

    // ---- Button ------------------------------------------------------------------------

    // Kinematic MovePosition rather than setting the transform, so a crate resting on the
    // button is carried down with it instead of hovering where the button used to be.
    private void MoveButton()
    {
        float target = IsPressed ? 1f : 0f;

        if (Mathf.Approximately(travel, target)) return;

        float step = pressDepth > 0f ? pressSpeed / pressDepth * Time.fixedDeltaTime : 1f;
        travel = Mathf.MoveTowards(travel, target, step);

        Transform parent = button.parent;
        float parentScaleY = parent != null ? Mathf.Abs(parent.lossyScale.y) : 1f;
        Vector3 local = restLocal + Vector3.down * (pressDepth / Mathf.Max(0.0001f, parentScaleY)) * travel;
        Vector3 world = parent != null ? parent.TransformPoint(local) : local;

        if (buttonBody != null && buttonBody.isKinematic) buttonBody.MovePosition(world);
        else button.position = world;
    }

    // A property block, so every plate can share one material and still glow on its own.
    private void ApplyGlow()
    {
        if (buttonRenderers == null) return;

        Color glow = IsPressed ? pressedGlow : idleGlow;

        foreach (Renderer renderer in buttonRenderers)
        {
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, Color.Lerp(Color.gray, glow, 0.6f));
            block.SetColor(EmissionColorId, glow * glowIntensity);
            renderer.SetPropertyBlock(block);
        }
    }

    // ---- Gizmos ------------------------------------------------------------------------

    void OnDrawGizmosSelected()
    {
        Transform part = button != null ? button : transform.Find("Button");

        if (part == null) return;

        if (!Application.isPlaying)
        {
            MeshFilter filter = part.GetComponent<MeshFilter>();
            buttonLocalBounds = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds
                : new Bounds(Vector3.zero, Vector3.one);
        }

        Transform saved = button;
        button = part;
        GetSensor(out Vector3 centre, out Vector3 halfExtents, out Quaternion rotation);
        button = saved;

        Gizmos.matrix = Matrix4x4.TRS(centre, rotation, Vector3.one);
        Gizmos.color = IsPressed ? new Color(0.25f, 0.6f, 1f, 0.35f) : new Color(1f, 0.3f, 0.2f, 0.25f);
        Gizmos.DrawCube(Vector3.zero, halfExtents * 2f);
        Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2f);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
