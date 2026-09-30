using System.Collections.Generic;
using UnityEngine;

// The slits a photon beam passes through. A WhichPathDetector that sees them collapses the
// wave function, so its walls show two plain bands instead of interference. Walls pick their
// gate and ask it for the pattern, so a gate can sit anywhere and feed any number of walls.
// Needs a collider so the emitter's raycast can find it.
// BoxCollider, not Collider: Collider is abstract, so Unity cannot satisfy the requirement
// and silently fails the whole AddComponent, leaving a gate the beam can never hit.
[AddComponentMenu("Wave Duality/Slit Gate")]
[RequireComponent(typeof(BoxCollider))]
public class SlitGate : MonoBehaviour
{
    [Header("Slits")]
    [Range(1, 2)]
    [Tooltip("Two slits interfere. One slit gives a single spread band with no fringes.")]
    [SerializeField] private int slitCount = 2;

    [Tooltip("Distance between the two slits. Wider slits give tighter fringes.")]
    [SerializeField] private float slitSeparation = 0.9f;

    [Tooltip("How wide each slit is cut. Widens the bands the collapsed state projects.")]
    [SerializeField] private float slitWidth = 0.2f;

    [Tooltip("A game wavelength in metres, not real light. Fringe spacing is wavelength " +
             "times wall distance divided by slit separation, so nanometres would put the " +
             "fringes micrometres apart. Raise it to spread the pattern out.")]
    [SerializeField] private float wavelength = 0.3f;

    [Header("Panels")]
    [Tooltip("Total width of the barrier the slits are cut into. The panels either side of " +
             "each slit are generated from this, so the geometry can never drift out of " +
             "step with the physics the way hand placed panels do.")]
    [SerializeField] private float panelWidth = 2.4f;

    [Tooltip("Height of the generated barrier panels.")]
    [SerializeField] private float panelHeight = 1.8f;

    [Tooltip("Depth of the generated barrier panels.")]
    [SerializeField] private float panelThickness = 0.15f;

    [Tooltip("Optional. Left empty the panels keep the primitive default material.")]
    [SerializeField] private Material panelMaterial;

    [Tooltip("Resizes the box collider to cover the panels, so dropping this component on " +
             "a bare GameObject gives something the beam can actually hit.")]
    [SerializeField] private bool fitColliderToPanels = true;

    [Header("Observation")]
    [Tooltip("How long the collapsed state lingers, so a detector sweeping past does not " +
             "strobe the wall.")]
    [SerializeField] private float observationHoldTime = 0.35f;

    [Tooltip("Off by default. The player's Which Path Detector already observes by seeing the " +
             "slits. Tick this for a rougher extra rule: collapse whenever the gate is on screen.")]
    [SerializeField] private bool gazeAlsoCollapses;

    [Tooltip("Optional. Only used for gaze collapse. Left empty the player camera is found.")]
    [SerializeField] private Camera viewCamera;

    [Tooltip("Blockers that count as breaking line of sight to the slits.")]
    [SerializeField] private LayerMask occluders = ~0;

    [Header("Beam")]
    [Tooltip("How long the gate stays lit after the laser leaves it, so a flicker in the beam " +
             "does not reseal the walls.")]
    [SerializeField] private float illuminationHoldTime = 0.5f;

    // Detectors sweep this instead of searching the scene, so aiming costs no allocation.
    private static readonly List<SlitGate> active = new List<SlitGate>();

    private readonly List<GameObject> panels = new List<GameObject>();

    private Collider gateCollider;
    private float lastObservedTime = float.NegativeInfinity;
    private float lastIlluminatedTime = float.NegativeInfinity;
    private float nextCameraSearch;

    private int builtSlitCount = -1;
    private float builtSeparation;
    private float builtSlitWidth;
    private float builtPanelWidth;
    private float builtPanelHeight;
    private float builtPanelThickness;

    public static IReadOnlyList<SlitGate> Active => active;

    public bool IsIlluminated => Time.time - lastIlluminatedTime <= illuminationHoldTime;
    public bool IsObserved => Time.time - lastObservedTime <= observationHoldTime;
    public int SlitCount => slitCount;

    // What a detector or the beam should aim at, rather than the pivot.
    public Vector3 AimPoint => GateBounds.center;

    private Bounds GateBounds => gateCollider != null
        ? gateCollider.bounds
        : new Bounds(transform.position, Vector3.one);

    public string Status => !IsIlluminated
        ? "no beam"
        : (IsObserved ? "observed - particle" : "unobserved - wave");

    // Statics survive play sessions when the domain reload is skipped.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
    }

    void Awake()
    {
        gateCollider = GetComponent<Collider>();

        RebuildPanels();

        if (gazeAlsoCollapses) ResolveCamera();
    }

    void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
    }

    void OnDisable()
    {
        active.Remove(this);
    }

    // Called by PhotonEmitter every frame its beam lands on this gate.
    public void Illuminate()
    {
        lastIlluminatedTime = Time.time;
    }

    // Called by a WhichPathDetector every frame it is aimed at these slits. Knowing which
    // slit the photon took is what destroys the interference term.
    public void Observe()
    {
        lastObservedTime = Time.time;
    }

    // Points down the middle of each slit, so an observer can ask whether the slits themselves
    // are visible rather than the pivot. Fills the caller's buffer, so it never allocates.
    public int GetSlitPoints(Vector3[] points)
    {
        int count = 0;
        float reach = panelHeight * 0.35f;

        for (int slit = 0; slit < Mathf.Min(slitCount, 2); slit++)
        {
            float x = slitCount < 2 ? 0f : (slit == 0 ? -0.5f : 0.5f) * slitSeparation;

            for (int row = -1; row <= 1 && count < points.Length; row++)
            {
                points[count++] = transform.TransformPoint(new Vector3(x, row * reach, 0f));
            }
        }

        return count;
    }

    void Update()
    {
        // Compared field by field rather than by signature string, since this runs every
        // frame and building a string here would allocate every frame.
        if (!PanelsMatchSettings) RebuildPanels();

        if (gazeAlsoCollapses && LooksAtGate()) lastObservedTime = Time.time;
    }

    // The intensity these slits throw across a wall of the given width at the given distance.
    // Each wall asks for its own, so the gate keeps no list of walls and can feed any number.
    public float[] SamplePattern(int samples, float width, float distance, bool observed)
    {
        float gateDistance = Mathf.Max(0.5f, distance * 0.5f);

        return InterferencePattern.Sample(samples, width, observed, wavelength, slitSeparation,
                                          slitWidth, Mathf.Max(0.5f, distance), gateDistance, slitCount);
    }

    private bool PanelsMatchSettings =>
        builtSlitCount == slitCount
        && Mathf.Approximately(builtSeparation, slitSeparation)
        && Mathf.Approximately(builtSlitWidth, slitWidth)
        && Mathf.Approximately(builtPanelWidth, panelWidth)
        && Mathf.Approximately(builtPanelHeight, panelHeight)
        && Mathf.Approximately(builtPanelThickness, panelThickness);

    // The barrier is whatever the slits leave behind, which is the same question the wall
    // asks of its fringes, so it is the same function.
    private void RebuildPanels()
    {
        builtSlitCount = slitCount;
        builtSeparation = slitSeparation;
        builtSlitWidth = slitWidth;
        builtPanelWidth = panelWidth;
        builtPanelHeight = panelHeight;
        builtPanelThickness = panelThickness;

        foreach (GameObject panel in panels)
        {
            if (panel != null) Destroy(panel);
        }

        panels.Clear();

        foreach (Band band in InterferencePattern.ToSolids(SlitBands(), panelWidth))
        {
            if (band.Width <= 0.001f) continue;

            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Slit Panel";
            panel.transform.SetParent(transform, false);
            panel.transform.localPosition = new Vector3(band.Centre, 0f, 0f);
            panel.transform.localRotation = Quaternion.identity;
            panel.transform.localScale = new Vector3(band.Width, panelHeight, panelThickness);

            // The gate's own collider is the beam target, so these must not shadow it.
            Destroy(panel.GetComponent<Collider>());

            if (panelMaterial != null) panel.GetComponent<MeshRenderer>().sharedMaterial = panelMaterial;

            panels.Add(panel);
        }

        FitCollider();
    }

    // The openings, in metres from the centre, left to right.
    private List<Band> SlitBands()
    {
        List<Band> bands = new List<Band>();
        float half = panelWidth * 0.5f;
        float reach = slitWidth * 0.5f;

        if (slitCount < 2)
        {
            bands.Add(new Band(Mathf.Max(-half, -reach), Mathf.Min(half, reach)));

            return bands;
        }

        float offset = slitSeparation * 0.5f;

        bands.Add(new Band(Mathf.Max(-half, -offset - reach), Mathf.Clamp(-offset + reach, -half, half)));
        bands.Add(new Band(Mathf.Clamp(offset - reach, -half, half), Mathf.Min(half, offset + reach)));

        return bands;
    }

    private void FitCollider()
    {
        if (!fitColliderToPanels) return;

        if (gateCollider is BoxCollider box)
        {
            box.center = Vector3.zero;
            box.size = new Vector3(panelWidth, panelHeight, panelThickness);
        }
    }

    // A proper frustum test, not a dot product, so the gate counts as seen only when it is
    // genuinely on screen. The line of sight check stops a wall in between counting.
    private bool LooksAtGate()
    {
        if (viewCamera == null)
        {
            if (Time.time < nextCameraSearch) return false;

            nextCameraSearch = Time.time + 1f;
            ResolveCamera();

            if (viewCamera == null) return false;
        }

        Bounds bounds = GateBounds;

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(viewCamera);

        if (!GeometryUtility.TestPlanesAABB(planes, bounds)) return false;

        Vector3 eye = viewCamera.transform.position;

        if (!Physics.Linecast(eye, bounds.center, out RaycastHit hit, occluders, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return hit.transform.IsChildOf(transform);
    }

    // Same fallback chain GrabInteract uses: PlayerCamera may not sit on the Camera object.
    private void ResolveCamera()
    {
        PlayerCamera player = FindAnyObjectByType<PlayerCamera>();

        if (player != null)
        {
            viewCamera = player.GetComponent<Camera>();

            if (viewCamera == null) viewCamera = player.GetComponentInChildren<Camera>(true);
        }

        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) viewCamera = FindAnyObjectByType<Camera>();
    }

    // Always drawn, and so clickable, because the panels only exist in play mode and an
    // unselected gate would otherwise be invisible in the Scene view.
    void OnDrawGizmos()
    {
        Vector3 barrier = new Vector3(panelWidth, panelHeight, panelThickness);

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.12f);
        Gizmos.DrawCube(Vector3.zero, barrier);
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.35f);
        Gizmos.DrawWireCube(Vector3.zero, barrier);
        Gizmos.matrix = Matrix4x4.identity;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
        Gizmos.matrix = transform.localToWorldMatrix;

        float half = slitSeparation * 0.5f;

        if (slitCount < 2)
        {
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(slitWidth, panelHeight, 0.05f));
        }
        else
        {
            Gizmos.DrawWireCube(new Vector3(-half, 0f, 0f), new Vector3(slitWidth, panelHeight, 0.05f));
            Gizmos.DrawWireCube(new Vector3(half, 0f, 0f), new Vector3(slitWidth, panelHeight, 0.05f));
        }

        // Which way the pattern is thrown.
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * 3f);
        Gizmos.matrix = Matrix4x4.identity;

        // Every wall this gate feeds, wherever it is on the map, so a link is never a guess.
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);

        foreach (InterferenceWall wall in FindObjectsByType<InterferenceWall>())
        {
            if (wall.Gate == this) Gizmos.DrawLine(AimPoint, wall.Centre);
        }
    }
}
