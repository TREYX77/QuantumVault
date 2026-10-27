using UnityEngine;

// One of the doors an InterferenceDoorWall turns its bright fringes into. Dormant, it wears the
// wall's material and offers nothing, so it reads as plain wall. Once the wall shows the wave
// pattern it lights its frame and opens with the interact key, swinging on its hinge.
[AddComponentMenu("Wave Duality/Interference Door")]
[DisallowMultipleComponent]
public class InterferenceDoor : MonoBehaviour, IInteractable, IResettable
{
    [Header("Parts")]
    [Tooltip("What swings. The door leaf is its child, so the hinge sits at the leaf's edge.")]
    [SerializeField] private Transform hinge;

    [Tooltip("The door leaf, which swaps material between wall and door.")]
    [SerializeField] private Renderer leaf;

    [Tooltip("Glowing strips around the opening, shown only while the door exists.")]
    [SerializeField] private Renderer[] frameGlow = new Renderer[0];

    [Tooltip("The middle of the space behind the door. The wall puts its exit here if this is the " +
             "correct door.")]
    [SerializeField] private Transform exitAnchor;

    [Header("Look")]
    [Tooltip("Worn while dormant. Give it the wall's material so the door cannot be seen.")]
    [SerializeField] private Material dormantMaterial;

    [Tooltip("Worn once the photons have made it a door.")]
    [SerializeField] private Material activeMaterial;

    [Header("Swing")]
    [Tooltip("Degrees around the hinge when open. The sign picks the side it swings to.")]
    [SerializeField] private float openAngle = -100f;

    [SerializeField] private float swingTime = 0.6f;

    [Header("Prompt")]
    [SerializeField] private string openPrompt = "Open door";
    [SerializeField] private string closePrompt = "Close door";

    private Quaternion closedRotation;
    private float swing;
    private bool active;

    public bool IsActive => active;
    public bool IsOpen { get; private set; }
    public Transform ExitAnchor => exitAnchor != null ? exitAnchor : transform;

    void Awake()
    {
        if (hinge == null) hinge = transform;

        closedRotation = hinge.localRotation;
        ApplyLook();
    }

    void Update()
    {
        float target = IsOpen ? 1f : 0f;

        if (Mathf.Approximately(swing, target)) return;

        swing = swingTime > 0f ? Mathf.MoveTowards(swing, target, Time.deltaTime / swingTime) : target;
        ApplySwing();
    }

    // Called by the wall. An open door stays open when its wall goes dark, so nobody is shut in.
    public void Materialize(bool value)
    {
        if (active == value) return;

        active = value;
        ApplyLook();
    }

    public string Prompt => active ? (IsOpen ? closePrompt : openPrompt) : null;
    public bool CanInteract => active;

    public void Interact(PlayerInteractor interactor)
    {
        IsOpen = !IsOpen;
    }

    public void CaptureState()
    {
    }

    public void RestoreState()
    {
        IsOpen = false;
        swing = 0f;
        ApplySwing();
    }

    private void ApplySwing()
    {
        // Eased, so the door settles into place rather than stopping dead.
        float eased = swing * swing * (3f - 2f * swing);

        hinge.localRotation = closedRotation * Quaternion.Euler(0f, openAngle * eased, 0f);
    }

    private void ApplyLook()
    {
        if (leaf != null)
        {
            Material material = active ? activeMaterial : dormantMaterial;

            if (material != null) leaf.sharedMaterial = material;
        }

        foreach (Renderer glow in frameGlow)
        {
            if (glow != null) glow.enabled = active;
        }
    }
}
