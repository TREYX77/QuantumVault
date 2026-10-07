using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// A portal that only works while its pressure plates are held down. Goes on the portal's root.
// Closed, the vortex glows red and its box collider is a solid wall. Open, it glows blue and the
// same box becomes a trigger that sends the player to the destination scene. Living on the root
// with the collider means the root gets the trigger message itself, with no relay script.
[AddComponentMenu("Puzzle/Portal Gate")]
[RequireComponent(typeof(BoxCollider))]
[DisallowMultipleComponent]
public class PortalGate : MonoBehaviour
{
    public enum PlateRule
    {
        All,
        Any
    }

    [Header("Pressure Plates")]
    [Tooltip("Drag the plates that power this gate in here. Right click the component header " +
             "and pick Add Pressure Plate to make a new one already linked.")]
    [SerializeField] private List<PressurePlate> plates = new List<PressurePlate>();

    [Tooltip("All: every plate must be held down. Any: one is enough.")]
    [SerializeField] private PlateRule rule = PlateRule.All;

    [Tooltip("Once opened, stay open even if the plates are let go.")]
    [SerializeField] private bool stayOpenOnceOpened;

    [Header("Destination")]
    [SerializeField] private TransitionSettings transition = new TransitionSettings();

    [Tooltip("Only objects with this tag are sent through. Thrown crates are not.")]
    [SerializeField] private string requiredTag = "Player";

    [Header("Vortex Look")]
    [Tooltip("The glowing disc. Left empty, the first renderer under this object whose " +
             "material has the colour property below is used.")]
    [SerializeField] private Renderer vortexRenderer;

    [SerializeField] private Color openColor = new Color(0.35f, 0.7f, 1f);
    [SerializeField] private float openRingGlow = 484.64f;
    [SerializeField] private Color closedColor = new Color(1f, 0.2f, 0.15f);
    [SerializeField] private float closedRingGlow = 4148f;

    [Tooltip("Seconds the colour takes to change.")]
    [SerializeField] private float colorFadeTime = 0.4f;

    [Tooltip("The vortex shader's colour property. VortexPortal names it Color_D9AD4C99.")]
    [SerializeField] private string colorProperty = "Color_D9AD4C99";

    [Tooltip("The vortex shader's ring glow property, EmissiveRing in the material. Left " +
             "empty, only the colour changes.")]
    [SerializeField] private string ringGlowProperty = "Vector1_51108891";

    [Header("Events")]
    [SerializeField] private UnityEvent onOpened;
    [SerializeField] private UnityEvent onClosed;

    // How thick the wall is, in metres, however flat the disc mesh is.
    private const float MinBarrierThickness = 0.4f;

    private BoxCollider barrier;
    private MaterialPropertyBlock block;
    private int colorId;
    private int ringId;
    private bool hasRing;
    private float blend;
    private bool fired;
    private bool warnedNoDestination;

    public bool IsOpen { get; private set; }
    public IReadOnlyList<PressurePlate> Plates => plates;

    void Reset()
    {
        FitBarrierToVortex();
    }

    void Awake()
    {
        barrier = GetComponent<BoxCollider>();
        block = new MaterialPropertyBlock();

        if (vortexRenderer == null) vortexRenderer = FindVortex();

        colorId = Shader.PropertyToID(colorProperty);
        hasRing = !string.IsNullOrEmpty(ringGlowProperty);
        ringId = hasRing ? Shader.PropertyToID(ringGlowProperty) : 0;

        // Settle on the starting state with no fade, so a closed gate never flashes blue.
        IsOpen = PlatesSatisfied();
        blend = IsOpen ? 1f : 0f;
        barrier.isTrigger = IsOpen;
        ApplyLook();
    }

    void Update()
    {
        bool open = PlatesSatisfied() || (stayOpenOnceOpened && IsOpen);

        if (open != IsOpen) SetOpen(open);

        float target = IsOpen ? 1f : 0f;

        if (!Mathf.Approximately(blend, target))
        {
            blend = colorFadeTime > 0f ? Mathf.MoveTowards(blend, target, Time.deltaTime / colorFadeTime) : target;
            ApplyLook();
        }
    }

    // Gates with no plates are simply open, so a portal can be placed before its puzzle.
    private bool PlatesSatisfied()
    {
        int counted = 0;
        int pressed = 0;

        foreach (PressurePlate plate in plates)
        {
            if (plate == null) continue;

            counted++;

            if (plate.IsPressed) pressed++;
        }

        if (counted == 0) return true;

        return rule == PlateRule.All ? pressed == counted : pressed > 0;
    }

    private void SetOpen(bool open)
    {
        IsOpen = open;

        // Solid wall while closed, walk-in trigger while open.
        barrier.isTrigger = open;

        if (open)
        {
            if (string.IsNullOrEmpty(transition.sceneName) && !warnedNoDestination)
            {
                warnedNoDestination = true;
                Debug.LogWarning($"Portal Gate '{name}' opened but has no destination scene set.", this);
            }

            onOpened?.Invoke();
        }
        else
        {
            onClosed?.Invoke();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsOpen || fired) return;

        bool tagged = string.IsNullOrEmpty(requiredTag)
                      || other.CompareTag(requiredTag)
                      || (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag(requiredTag));

        if (!tagged || string.IsNullOrEmpty(transition.sceneName)) return;

        fired = true;
        SceneTransition.Load(transition);
    }

    // A property block, so the shared portal material asset is never edited and every gate
    // keeps its own colour.
    private void ApplyLook()
    {
        if (vortexRenderer == null) return;

        vortexRenderer.GetPropertyBlock(block);
        block.SetColor(colorId, Color.Lerp(closedColor, openColor, blend));

        if (hasRing) block.SetFloat(ringId, Mathf.Lerp(closedRingGlow, openRingGlow, blend));

        vortexRenderer.SetPropertyBlock(block);
    }

    private Renderer FindVortex()
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            Material material = renderer.sharedMaterial;

            if (material != null && material.HasProperty(colorProperty)) return renderer;
        }

        return null;
    }

    // ---- Editor helpers ----------------------------------------------------------------

    public void AddPlate(PressurePlate plate)
    {
        if (plate != null && !plates.Contains(plate)) plates.Add(plate);
    }

    // Sizes the box to the vortex disc, so the wall and the warp cover exactly the glowing
    // part. Returns false when there is no vortex to fit to.
    public bool FitBarrierToVortex()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        Renderer vortex = vortexRenderer != null ? vortexRenderer : FindVortex();

        if (box == null || vortex == null) return false;

        MeshFilter filter = vortex.GetComponent<MeshFilter>();
        Bounds meshBounds = filter != null && filter.sharedMesh != null
            ? filter.sharedMesh.bounds
            : new Bounds(vortex.transform.InverseTransformPoint(vortex.bounds.center), Vector3.one);

        // The disc's corners in this object's space, boxed up.
        Bounds local = new Bounds(transform.InverseTransformPoint(vortex.transform.TransformPoint(meshBounds.center)), Vector3.zero);

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(
                (i & 1) == 0 ? -1f : 1f,
                (i & 2) == 0 ? -1f : 1f,
                (i & 4) == 0 ? -1f : 1f));

            local.Encapsulate(transform.InverseTransformPoint(vortex.transform.TransformPoint(corner)));
        }

        // The disc is flat, so give its thin side a real thickness in metres.
        Vector3 size = local.size;
        Vector3 scale = transform.lossyScale;
        int thin = size.x <= size.y && size.x <= size.z ? 0 : (size.y <= size.z ? 1 : 2);
        size[thin] = Mathf.Max(size[thin], MinBarrierThickness / Mathf.Max(0.0001f, Mathf.Abs(scale[thin])));

        box.center = local.center;
        box.size = size;
        box.isTrigger = false;

        return true;
    }

    // The way the disc faces, for placing a plate in front of the portal.
    public Vector3 Facing
    {
        get
        {
            BoxCollider box = GetComponent<BoxCollider>();

            if (box == null) return transform.forward;

            Vector3 size = box.size;
            Vector3 axis = size.x <= size.y && size.x <= size.z ? Vector3.right : (size.y <= size.z ? Vector3.up : Vector3.forward);

            return transform.TransformDirection(axis).normalized;
        }
    }

    public Vector3 BarrierCentre
    {
        get
        {
            BoxCollider box = GetComponent<BoxCollider>();

            return box != null ? transform.TransformPoint(box.center) : transform.position;
        }
    }

    // ---- Gizmos ------------------------------------------------------------------------

    // Always drawn, so which plates feed which gate is visible across the whole level.
    void OnDrawGizmos()
    {
        Vector3 from = BarrierCentre;

        foreach (PressurePlate plate in plates)
        {
            if (plate == null) continue;

            Gizmos.color = !Application.isPlaying
                ? new Color(1f, 0.85f, 0.3f, 0.8f)
                : (plate.IsPressed ? new Color(0.25f, 0.6f, 1f, 0.9f) : new Color(1f, 0.25f, 0.2f, 0.9f));

            Gizmos.DrawLine(from, plate.transform.position);
            Gizmos.DrawWireSphere(plate.transform.position, 0.2f);
        }
    }
}
