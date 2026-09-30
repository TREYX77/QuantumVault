using UnityEngine;

// A which-path instrument. When it can see a slit gate it learns which slit each photon took,
// which destroys the interference term and collapses the wall to two plain bands. On the player,
// given the camera, seeing the slits is the act of observing. On a prop it aims with a cone.
[AddComponentMenu("Wave Duality/Which Path Detector")]
public class WhichPathDetector : MonoBehaviour
{
    [Header("Screen")]
    [Tooltip("Optional. With a camera here, observing means the slits themselves are visible on " +
             "its screen with nothing solid in front of them, at any distance, and the cone and " +
             "range below are ignored. Give it the player's camera so seeing the slits observes.")]
    [SerializeField] private Camera viewCamera;

    [Header("Reach")]
    [Tooltip("Optional. Where the instrument looks from and which way it faces. Defaults to " +
             "this object. A lens child lets a floor standing prop look out at slit height, " +
             "so aiming stays a flat sweep instead of needing to tilt up.")]
    [SerializeField] private Transform sightOrigin;

    [Tooltip("How far the instrument can resolve a set of slits.")]
    [SerializeField] private float range = 12f;

    [Tooltip("Full width of the aiming cone in degrees. The slits must fall inside it, so " +
             "turning the instrument on the spot is enough to stop it reading them.")]
    [Range(1f, 180f)]
    [SerializeField] private float coneAngle = 35f;

    [Tooltip("Stops the instrument reading through solid geometry, which would look like a " +
             "bug. Untick for pure aim only behaviour, where nothing can block it.")]
    [SerializeField] private bool requireLineOfSight = true;

    [Tooltip("What counts as blocking the view of the slits.")]
    [SerializeField] private LayerMask blockers = ~0;

    [Header("Look")]
    [Tooltip("Draws where the instrument is pointing. Aiming is invisible without it.")]
    [SerializeField] private bool showSightLine = true;

    [SerializeField] private Color idleColor = new Color(0.55f, 0.6f, 0.7f, 0.5f);
    [SerializeField] private Color observingColor = new Color(1f, 0.45f, 0.35f);
    [SerializeField] private float lineThickness = 0.02f;

    // Shared by every detector, which is safe because Update never runs two at once.
    private static readonly RaycastHit[] hits = new RaycastHit[16];
    private static readonly Vector3[] slitPoints = new Vector3[6];

    private LineRenderer line;
    private SlitGate nearest;
    private int observedCount;

    // Lets a switch or trigger cut the instrument without moving it, the same way
    // PlayerCamera.LookEnabled is borrowed and handed back.
    public bool Powered { get; set; } = true;

    public SlitGate Nearest => nearest;

    private Transform Eye => sightOrigin != null ? sightOrigin : transform;

    public string Status
    {
        get
        {
            if (!Powered) return "unpowered";

            return observedCount == 0
                ? "aimed at nothing"
                : "observing " + nearest.name + " (" + observedCount + ")";
        }
    }

    void OnDisable()
    {
        nearest = null;
        observedCount = 0;

        if (line != null) line.enabled = false;
    }

    void Update()
    {
        nearest = null;
        observedCount = 0;

        if (Powered)
        {
            float nearestSqr = float.PositiveInfinity;

            // Swept from the gates' own registry, so no scene search and no allocation.
            foreach (SlitGate gate in SlitGate.Active)
            {
                if (gate == null || !CanResolve(gate)) continue;

                gate.Observe();
                observedCount++;

                float sqr = (gate.AimPoint - Eye.position).sqrMagnitude;

                if (sqr >= nearestSqr) continue;

                nearestSqr = sqr;
                nearest = gate;
            }
        }

        DrawSightLine();
    }

    private bool CanResolve(SlitGate gate)
    {
        if (viewCamera != null) return OnScreen(gate);

        Transform eye = Eye;
        Vector3 target = gate.AimPoint;
        Vector3 toGate = target - eye.position;

        if (toGate.sqrMagnitude > range * range) return false;

        // Half angle, so the serialized value reads as the full width of the cone.
        if (Vector3.Angle(eye.forward, toGate) > coneAngle * 0.5f) return false;

        return !requireLineOfSight || Unblocked(eye.position, target, gate);
    }

    // Seen means a point down either slit lands inside the camera's view with nothing solid in
    // front of it. Testing the slits themselves, not a cone or the pivot, stays exact at any range.
    private bool OnScreen(SlitGate gate)
    {
        Vector3 eye = viewCamera.transform.position;
        int count = gate.GetSlitPoints(slitPoints);

        for (int i = 0; i < count; i++)
        {
            Vector3 view = viewCamera.WorldToViewportPoint(slitPoints[i]);

            if (view.z <= 0f || view.z > viewCamera.farClipPlane) continue;
            if (view.x < 0f || view.x > 1f || view.y < 0f || view.y > 1f) continue;

            if (!requireLineOfSight || Unblocked(eye, slitPoints[i], gate)) return true;
        }

        return false;
    }

    private bool Unblocked(Vector3 from, Vector3 to, SlitGate gate)
    {
        if (!Physics.Linecast(from, to, out RaycastHit hit, blockers, QueryTriggerInteraction.Ignore)) return true;

        // Hitting the gate itself, or our own body, is not something in the way.
        return hit.transform.IsChildOf(gate.transform) || hit.transform.IsChildOf(transform);
    }

    private void DrawSightLine()
    {
        if (!showSightLine || !Powered)
        {
            if (line != null) line.enabled = false;
            return;
        }

        // Built on first use, so an instrument with its line off, like the player, never carries one.
        if (line == null) BuildLine();

        Transform eye = Eye;
        Vector3 end = eye.position + eye.forward * range;

        // Straight along where it points, never bent onto a gate. The colour says whether it
        // is reading slits, since the cone can catch a gate the line itself passes beside.
        if (FirstHit(eye.position, eye.forward, out RaycastHit hit)) end = hit.point;

        line.enabled = true;
        line.SetPosition(0, eye.position);
        line.SetPosition(1, end);

        Color color = nearest != null ? observingColor : idleColor;

        line.startColor = color;
        line.endColor = new Color(color.r, color.g, color.b, color.a * 0.25f);
    }

    // Nearest hit that is not part of this prop, so the line starts cleanly from inside its
    // own collider. Same rule as PhotonEmitter.FirstHit.
    private bool FirstHit(Vector3 origin, Vector3 direction, out RaycastHit first)
    {
        first = default;
        float best = float.PositiveInfinity;

        int count = Physics.RaycastNonAlloc(origin, direction, hits, range, blockers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (hits[i].distance >= best || hits[i].collider.transform.IsChildOf(transform)) continue;

            best = hits[i].distance;
            first = hits[i];
        }

        return best < float.PositiveInfinity;
    }

    private void BuildLine()
    {
        line = GetComponent<LineRenderer>();

        if (line == null) line = gameObject.AddComponent<LineRenderer>();

        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = lineThickness;
        line.endWidth = lineThickness;
        line.enabled = false;

        // Sprites/Default tints from vertex colour with no lighting, which suits a beam.
        line.material = new Material(Shader.Find("Sprites/Default"));
    }

    void OnDrawGizmosSelected()
    {
        // With a view camera the screen is the rule, so a cone would only mislead.
        if (viewCamera == null)
        {
            Gizmos.color = new Color(1f, 0.45f, 0.35f, 0.7f);
            Gizmos.matrix = Eye.localToWorldMatrix;

            // Four edges of the cone, so the aim is readable while placing the prop.
            float half = coneAngle * 0.5f;
            float spread = Mathf.Tan(half * Mathf.Deg2Rad) * range;

            Gizmos.DrawLine(Vector3.zero, new Vector3(spread, 0f, range));
            Gizmos.DrawLine(Vector3.zero, new Vector3(-spread, 0f, range));
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, spread, range));
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, -spread, range));

            Gizmos.matrix = Matrix4x4.identity;
        }

        if (Application.isPlaying && nearest != null)
        {
            Gizmos.color = observingColor;
            Gizmos.DrawLine(Eye.position, nearest.AimPoint);
        }
    }
}
