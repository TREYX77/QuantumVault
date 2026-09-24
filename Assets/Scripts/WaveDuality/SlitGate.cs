using UnityEngine;

// The slits a photon beam passes through. While the player is looking at it the wave
// function collapses and the wall behind shows two plain bands instead of interference.
// Needs a collider so the emitter's raycast can find it.
[AddComponentMenu("Wave Duality/Slit Gate")]
[RequireComponent(typeof(Collider))]
public class SlitGate : MonoBehaviour
{
    [Header("Slits")]
    [Range(1, 2)]
    [SerializeField] private int slitCount = 2;

    [Tooltip("Distance between the two slits. Wider slits give tighter fringes.")]
    [SerializeField] private float slitSeparation = 0.4f;

    [SerializeField] private float slitWidth = 0.12f;

    [Tooltip("A game wavelength in metres, not real light. Fringe spacing is wavelength " +
             "times wall distance divided by slit separation, so nanometres would put the " +
             "fringes micrometres apart. Raise it to spread the pattern out.")]
    [SerializeField] private float wavelength = 0.15f;

    [Header("Wall")]
    [Tooltip("Optional. Left empty, a raycast forward finds the wall behind the slits.")]
    [SerializeField] private InterferenceWall wall;

    [SerializeField] private float wallSearchRange = 40f;

    [Header("Observation")]
    [Tooltip("Optional. Left empty, the player camera is resolved automatically.")]
    [SerializeField] private Camera viewCamera;

    [Tooltip("Blockers that count as breaking line of sight to the slits.")]
    [SerializeField] private LayerMask occluders = ~0;

    [Tooltip("How long the collapsed state lingers, so glancing past does not strobe the wall.")]
    [SerializeField] private float observationHoldTime = 0.35f;

    [Header("Beam")]
    [Tooltip("How long illumination lasts after the emitter stops refreshing it.")]
    [SerializeField] private float illuminationHoldTime = 0.15f;

    private Collider gateCollider;
    private float lastObservedTime = float.NegativeInfinity;
    private float lastIlluminatedTime = float.NegativeInfinity;
    private float nextCameraSearch;
    private bool lastApplied;
    private bool lastObservedState;

    public bool IsIlluminated => Time.time - lastIlluminatedTime <= illuminationHoldTime;
    public bool IsObserved => Time.time - lastObservedTime <= observationHoldTime;
    public int SlitCount => slitCount;

    public string Status => !IsIlluminated
        ? "no beam"
        : (IsObserved ? "observed - particle" : "unobserved - wave");

    void Awake()
    {
        gateCollider = GetComponent<Collider>();

        ResolveCamera();
    }

    // Called by PhotonEmitter every frame its beam lands on this gate.
    public void Illuminate()
    {
        lastIlluminatedTime = Time.time;
    }

    void Update()
    {
        if (LooksAtGate()) lastObservedTime = Time.time;

        bool illuminated = IsIlluminated;
        bool observed = IsObserved;

        if (!illuminated)
        {
            if (lastApplied) ResolveWall()?.BuildSolid();

            lastApplied = false;
            return;
        }

        // Only rebuild when the state actually flips, or on the first frame of a beam.
        if (lastApplied && observed == lastObservedState) return;

        lastApplied = true;
        lastObservedState = observed;

        ApplyPattern(observed);
    }

    private void ApplyPattern(bool observed)
    {
        InterferenceWall target = ResolveWall();

        if (target == null) return;

        float screenDistance = Vector3.Distance(transform.position, target.transform.position);
        float gateDistance = Mathf.Max(0.5f, screenDistance * 0.5f);

        float[] intensity = InterferencePattern.Sample(
            256, target.Width, observed, wavelength, slitSeparation, slitWidth,
            Mathf.Max(0.5f, screenDistance), gateDistance, slitCount);

        target.Apply(intensity);
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

        Bounds bounds = gateCollider != null ? gateCollider.bounds : new Bounds(transform.position, Vector3.one);

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(viewCamera);

        if (!GeometryUtility.TestPlanesAABB(planes, bounds)) return false;

        Vector3 eye = viewCamera.transform.position;

        if (!Physics.Linecast(eye, bounds.center, out RaycastHit hit, occluders, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return hit.transform.IsChildOf(transform);
    }

    private InterferenceWall ResolveWall()
    {
        if (wall != null) return wall;

        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit,
                            wallSearchRange, ~0, QueryTriggerInteraction.Ignore))
        {
            wall = hit.collider.GetComponentInParent<InterferenceWall>();
        }

        if (wall == null) wall = FindAnyObjectByType<InterferenceWall>();

        return wall;
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

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
        Gizmos.matrix = transform.localToWorldMatrix;

        float half = slitSeparation * 0.5f;

        if (slitCount < 2)
        {
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(slitWidth, 1f, 0.05f));
        }
        else
        {
            Gizmos.DrawWireCube(new Vector3(-half, 0f, 0f), new Vector3(slitWidth, 1f, 0.05f));
            Gizmos.DrawWireCube(new Vector3(half, 0f, 0f), new Vector3(slitWidth, 1f, 0.05f));
        }

        // Which way the pattern is thrown.
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * 3f);
    }
}
