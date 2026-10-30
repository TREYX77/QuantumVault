using UnityEngine;

// A which-path instrument. When it can see a slit gate it learns which slit each photon took,
// which destroys the interference term and collapses the wall to two plain bands. On the player,
// given the camera, seeing the slits is the act of observing. On a prop it aims with a cone.
// Seeing them in a MirrorSurface counts too: knowing which slit a photon took is the same whether
// the light from the slits reached the instrument straight or off a mirror.
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

    // How far in front of the glass a reflected line of sight is tested from, so the wall the
    // mirror hangs on never counts as being in the way of its own reflection.
    private const float MirrorClearance = 0.01f;

    // Shared by every detector, which is safe because Update never runs two at once.
    private static readonly Vector3[] slitPoints = new Vector3[6];
    private static readonly Vector3[] sightPath = new Vector3[MirrorSurface.MaxBounces + 2];

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

        if (InCone(eye, target, Vector3.Distance(eye.position, target))
            && (!requireLineOfSight || Unblocked(eye.position, target, gate))) return true;

        // Off a mirror the instrument has to aim at where the slits show in the glass, and they
        // must be in reach along the bounced path, not the straight one.
        foreach (MirrorSurface mirror in MirrorSurface.Active)
        {
            if (mirror == null || !mirror.TryViewThrough(eye.position, target, out Vector3 onGlass)) continue;

            float path = Vector3.Distance(eye.position, onGlass) + Vector3.Distance(onGlass, target);

            if (InCone(eye, onGlass, path)
                && (!requireLineOfSight || UnblockedVia(eye.position, onGlass, mirror, target, gate))) return true;
        }

        return false;
    }

    // Within reach, and inside the aiming cone toward the given point.
    private bool InCone(Transform eye, Vector3 toward, float distance)
    {
        if (distance > range) return false;

        // Half angle, so the serialized value reads as the full width of the cone.
        return Vector3.Angle(eye.forward, toward - eye.position) <= coneAngle * 0.5f;
    }

    // Seen means a point down either slit lands inside the camera's view with nothing solid in
    // front of it. Testing the slits themselves, not a cone or the pivot, stays exact at any range.
    private bool OnScreen(SlitGate gate)
    {
        Vector3 eye = viewCamera.transform.position;
        int count = gate.GetSlitPoints(slitPoints);

        for (int i = 0; i < count; i++)
        {
            if (InViewport(slitPoints[i]) && (!requireLineOfSight || Unblocked(eye, slitPoints[i], gate))) return true;
        }

        // In a mirror, the slit's reflection is what has to be on screen, and that is where the
        // line to it crosses the glass.
        foreach (MirrorSurface mirror in MirrorSurface.Active)
        {
            if (mirror == null) continue;

            for (int i = 0; i < count; i++)
            {
                if (!mirror.TryViewThrough(eye, slitPoints[i], out Vector3 onGlass) || !InViewport(onGlass)) continue;

                if (!requireLineOfSight || UnblockedVia(eye, onGlass, mirror, slitPoints[i], gate)) return true;
            }
        }

        return false;
    }

    private bool InViewport(Vector3 point)
    {
        Vector3 view = viewCamera.WorldToViewportPoint(point);

        return view.z > 0f && view.z <= viewCamera.farClipPlane
               && view.x >= 0f && view.x <= 1f && view.y >= 0f && view.y <= 1f;
    }

    // Through glass, as light goes, so slits seen through a window still count.
    private bool Unblocked(Vector3 from, Vector3 to, SlitGate gate)
    {
        if (!GlassPane.Linecast(from, to, blockers, out RaycastHit hit)) return true;

        // Hitting the gate itself, or our own body, is not something in the way.
        return hit.transform.IsChildOf(gate.transform) || hit.transform.IsChildOf(transform);
    }

    // Both legs of a reflected line of sight, eye to glass and glass to slit, each stopping just
    // in front of the glass.
    private bool UnblockedVia(Vector3 eye, Vector3 onGlass, MirrorSurface mirror, Vector3 target, SlitGate gate)
    {
        Vector3 front = onGlass + mirror.Normal * MirrorClearance;

        return Unblocked(eye, front, gate) && Unblocked(front, target, gate);
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

        // Along where it points and off any mirrors, never bent onto a gate. The colour says
        // whether it is reading slits, since the cone can catch a gate the line passes beside.
        // Its own body is skipped, so the line starts cleanly from inside the prop's collider.
        int points = MirrorSurface.TracePath(eye.position, eye.forward, range, blockers, transform, sightPath, out _, out _);

        line.enabled = true;
        line.positionCount = points;

        for (int i = 0; i < points; i++) line.SetPosition(i, sightPath[i]);

        Color color = nearest != null ? observingColor : idleColor;

        line.startColor = color;
        line.endColor = new Color(color.r, color.g, color.b, color.a * 0.25f);
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
