using System.Collections.Generic;
using UnityEngine;

// A flat mirror. The Photon Emitter's laser bounces off it and still lights slit gates, a spot
// light shone at it throws a reflected spot (MirrorBounceLight), a Which Path Detector can see slits
// in it, and a planar reflection makes it look like a real mirror.
// The mirror is its glass: a quad whose visible side (-Z, as on Unity's Quad) reflects. Put this on
// the wall the glass hangs on, so the wall's collider is what a beam meets, or on the quad itself.
// Tools > Puzzle > Make Selected a Mirror does either, laying glass on whichever face of a wall is
// toward the Scene view.
// Runs after PlayerCamera and the flashlight, so the reflection matches where the view ended up.
[AddComponentMenu("Puzzle/Mirror Surface")]
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public class MirrorSurface : MonoBehaviour
{
    // Enough for any puzzle, and a beam caught between two facing mirrors still ends.
    public const int MaxBounces = 8;

    [Header("Glass")]
    [Tooltip("Optional. The quad that is the mirror; its visible side reflects, and its size is " +
             "the mirror's. Left empty this object's own renderer is the glass.")]
    [SerializeField] private Renderer glass;

    [Range(0f, 1f)]
    [Tooltip("How much light the mirror sends back. A good silvered mirror is about 0.9. Dims the " +
             "reflection and any light bounced off it, but never the laser, which is a puzzle beam.")]
    [SerializeField] private float reflectivity = 0.9f;

    [Header("Reflection")]
    [Tooltip("Renders a true reflection from the player's view. Off, the glass shows only the " +
             "reflection probes, which is cheap but blurry and wrong close up.")]
    [SerializeField] private bool planarReflection = true;

    [Range(0.25f, 1f)]
    [Tooltip("Resolution of the reflection against the screen. Lower is cheaper and softer.")]
    [SerializeField] private float resolutionScale = 1f;

    [Tooltip("Whether shadows are drawn in the reflection. Without them lamps shine through props " +
             "in the mirror, but each mirror costs a little less.")]
    [SerializeField] private bool renderShadows = true;

    [Tooltip("Optional. Whose view the mirror reflects. Left empty the player camera is found.")]
    [SerializeField] private Camera viewCamera;

    // How far off the glass a bounced ray restarts, so it cannot meet the mirror it just left.
    private const float Lift = 0.001f;

    private static readonly int ReflectivityId = Shader.PropertyToID("_Reflectivity");
    private static readonly int ReflectionTexId = Shader.PropertyToID("_ReflectionTex");
    private static readonly int PlanarActiveId = Shader.PropertyToID("_PlanarActive");

    // Detectors sweep this instead of searching the scene, the same as SlitGate.Active.
    private static readonly List<MirrorSurface> active = new List<MirrorSurface>();

    // Shared by every trace, which is safe because Update never runs two at once.
    private static readonly RaycastHit[] hits = new RaycastHit[16];

    private MirrorReflection reflection;
    private MaterialPropertyBlock block;
    private float nextCameraSearch;

    private Vector3 center;
    private Vector3 normal;
    private Vector3 axisU;
    private Vector3 axisV;
    private float halfU;
    private float halfV;
    private int geometryFrame = -1;

    public static IReadOnlyList<MirrorSurface> Active => active;

    public Renderer Glass => glass != null ? glass : GetComponent<Renderer>();
    public bool HasGlass => Glass != null;
    public float Reflectivity => reflectivity;

    // Middle of the glass, and the way its reflecting side faces.
    public Vector3 Center { get { RefreshGeometry(); return center; } }
    public Vector3 Normal { get { RefreshGeometry(); return normal; } }

    // Centre to corner, for anything that needs to know how much of a view the glass fills.
    public float Radius { get { RefreshGeometry(); return Mathf.Sqrt(halfU * halfU + halfV * halfV); } }

    public string Status
    {
        get
        {
            if (!HasGlass) return "no glass";

            return reflection != null && reflection.Texture != null && planarReflection
                ? "reflecting " + (viewCamera != null ? viewCamera.name : "nothing")
                : "probe reflection only";
        }
    }

    // Statics survive play sessions when the domain reload is skipped.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
    }

    void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
    }

    void OnDisable()
    {
        active.Remove(this);
        reflection?.Pause();
        ShowReflection(false);
    }

    void OnDestroy()
    {
        reflection?.Dispose();
        reflection = null;
    }

    void LateUpdate()
    {
        Renderer surface = Glass;

        if (surface == null) return;

        bool showing = false;

        if (planarReflection && ResolveCamera())
        {
            if (reflection == null) reflection = new MirrorReflection();

            showing = reflection.Render(this, viewCamera, surface.bounds, resolutionScale, renderShadows);
        }
        else
        {
            reflection?.Pause();
        }

        ShowReflection(showing);
    }

    // Through a property block, so every mirror shares one material and keeps its own picture.
    private void ShowReflection(bool showing)
    {
        Renderer surface = Glass;

        if (surface == null) return;

        if (block == null) block = new MaterialPropertyBlock();

        surface.GetPropertyBlock(block);
        block.SetFloat(ReflectivityId, reflectivity);
        block.SetFloat(PlanarActiveId, showing ? 1f : 0f);

        if (showing) block.SetTexture(ReflectionTexId, reflection.Texture);

        surface.SetPropertyBlock(block);
    }

    // Looked up lazily and retried once a second, the project's usual self-heal for a player
    // that may load after this. Same fallback chain as SlitGate: PlayerCamera may not sit on the
    // Camera object.
    private bool ResolveCamera()
    {
        if (viewCamera != null) return true;
        if (Time.time < nextCameraSearch) return false;

        nextCameraSearch = Time.time + 1f;

        PlayerCamera player = FindAnyObjectByType<PlayerCamera>();

        if (player != null)
        {
            viewCamera = player.GetComponent<Camera>();

            if (viewCamera == null) viewCamera = player.GetComponentInChildren<Camera>(true);
        }

        if (viewCamera == null) viewCamera = Camera.main;

        return viewCamera != null;
    }

    // ---- Geometry ---------------------------------------------------------------------------

    // Read from the glass every frame rather than stored, so a mirror on a moving or rotating
    // mount stays right. Once a frame in play; every call in the editor, where frames stand still.
    private bool RefreshGeometry()
    {
        Renderer surface = Glass;

        if (surface == null) return false;
        if (Application.isPlaying && geometryFrame == Time.frameCount) return true;

        geometryFrame = Time.frameCount;

        Transform t = surface.transform;
        MeshFilter filter = surface.GetComponent<MeshFilter>();

        // A Quad's bounds by default, so a renderer with no mesh still gets a sensible mirror.
        Bounds bounds = filter != null && filter.sharedMesh != null
            ? filter.sharedMesh.bounds
            : new Bounds(Vector3.zero, new Vector3(1f, 1f, 0f));

        // The -Z face of the mesh: a Quad's visible side, or the front of a box used as glass.
        center = t.TransformPoint(new Vector3(bounds.center.x, bounds.center.y, bounds.min.z));
        normal = -t.forward;

        Vector3 u = t.TransformVector(new Vector3(bounds.extents.x, 0f, 0f));
        Vector3 v = t.TransformVector(new Vector3(0f, bounds.extents.y, 0f));

        halfU = u.magnitude;
        halfV = v.magnitude;
        axisU = halfU > 0f ? u / halfU : t.right;
        axisV = halfV > 0f ? v / halfV : t.up;

        return true;
    }

    // Whether a point on the mirror's plane lies on the glass rather than off its edge.
    public bool Contains(Vector3 point)
    {
        if (!RefreshGeometry()) return false;

        Vector3 offset = point - center;

        return Mathf.Abs(Vector3.Dot(offset, axisU)) <= halfU && Mathf.Abs(Vector3.Dot(offset, axisV)) <= halfV;
    }

    // Where a point appears to be, seen in the mirror: as far behind the glass as it is in front.
    public Vector3 ReflectPoint(Vector3 point)
    {
        RefreshGeometry();

        return point - 2f * Vector3.Dot(point - center, normal) * normal;
    }

    public Vector3 ReflectVector(Vector3 vector)
    {
        RefreshGeometry();

        return Vector3.Reflect(vector, normal);
    }

    // Where on the glass an eye sees a target's reflection, if it can. The reflection stands at
    // the target's mirror image, so it shows where the line from the eye to that image crosses the
    // glass: a point that splits the line in the ratio of the two distances from the plane.
    // Both must be in front, and the crossing must land on the glass, not beside it.
    public bool TryViewThrough(Vector3 eye, Vector3 target, out Vector3 onGlass)
    {
        onGlass = default;

        if (!RefreshGeometry()) return false;

        float eyeHeight = Vector3.Dot(eye - center, normal);
        float targetHeight = Vector3.Dot(target - center, normal);

        if (eyeHeight <= 0f || targetHeight <= 0f) return false;

        onGlass = Vector3.Lerp(eye, ReflectPoint(target), eyeHeight / (eyeHeight + targetHeight));

        return Contains(onGlass);
    }

    // ---- Beams ------------------------------------------------------------------------------

    // The mirror a ray just met, and which way it leaves, if it met the glass from the front. The
    // back and edges of a mirror's wall, or the wall around the glass, stop a beam like any wall.
    public static bool TryReflect(RaycastHit hit, Vector3 direction, out MirrorSurface mirror, out Vector3 reflected)
    {
        reflected = direction;
        mirror = hit.collider != null ? hit.collider.GetComponentInParent<MirrorSurface>() : null;

        if (mirror == null || !mirror.isActiveAndEnabled || !mirror.RefreshGeometry())
        {
            mirror = null;

            return false;
        }

        // Against the glass's own normal rather than the hit's, which a mesh collider interpolates.
        Vector3 facing = mirror.Normal;

        if (Vector3.Dot(hit.normal, facing) < 0.5f || Vector3.Dot(direction, facing) >= 0f || !mirror.Contains(hit.point))
        {
            mirror = null;

            return false;
        }

        reflected = Vector3.Reflect(direction, facing);

        return true;
    }

    // Nearest hit that is not part of skip, so a beam can start inside its source's own collider.
    // The rule PhotonEmitter and WhichPathDetector have always used. Glass is not a hit: light
    // carries on through a GlassPane.
    public static bool FirstHit(Vector3 origin, Vector3 direction, float range, LayerMask mask, Transform skip,
                                out RaycastHit nearest)
    {
        nearest = default;
        float best = float.PositiveInfinity;

        int count = Physics.RaycastNonAlloc(origin, direction, hits, range, mask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (hits[i].distance >= best) continue;
            if (skip != null && hits[i].collider.transform.IsChildOf(skip)) continue;
            if (GlassPane.IsClear(hits[i].collider)) continue;

            best = hits[i].distance;
            nearest = hits[i];
        }

        return best < float.PositiveInfinity;
    }

    // The path a ray takes through any number of mirrors: the origin, each bounce, then where it
    // ends, written into points. Range is the whole path's length, not each leg's. Skip is only
    // ignored on the first leg, so a beam bounced back at its own source is still stopped by it.
    // Returns how many points were written; last and landed say what the final leg ended on.
    public static int TracePath(Vector3 origin, Vector3 direction, float range, LayerMask mask, Transform skip,
                                Vector3[] points, out RaycastHit last, out bool landed)
    {
        int count = 0;
        float remaining = range;

        points[count++] = origin;

        while (true)
        {
            if (!FirstHit(origin, direction, remaining, mask, skip, out last))
            {
                landed = false;
                points[count++] = origin + direction * remaining;

                return count;
            }

            landed = true;
            points[count++] = last.point;
            remaining -= last.distance;

            if (count >= points.Length || remaining <= 0f) return count;
            if (!TryReflect(last, direction, out MirrorSurface mirror, out Vector3 reflected)) return count;

            origin = last.point + mirror.Normal * Lift;
            direction = reflected;
            skip = null;
        }
    }

    // Always drawn, so the reflecting side is never a guess while placing a mirror.
    void OnDrawGizmos()
    {
        if (!RefreshGeometry()) return;

        Vector3 u = axisU * halfU;
        Vector3 v = axisV * halfV;

        Gizmos.color = new Color(0.75f, 0.9f, 1f, 0.6f);
        Gizmos.DrawLine(center - u - v, center + u - v);
        Gizmos.DrawLine(center + u - v, center + u + v);
        Gizmos.DrawLine(center + u + v, center - u + v);
        Gizmos.DrawLine(center - u + v, center - u - v);

        // Which way it reflects, sized to the glass so it reads at any scale.
        float reach = Mathf.Clamp(Mathf.Min(halfU, halfV), 0.15f, 0.6f);

        Gizmos.DrawLine(center, center + normal * reach);
        Gizmos.DrawWireSphere(center + normal * reach, reach * 0.08f);
    }
}
