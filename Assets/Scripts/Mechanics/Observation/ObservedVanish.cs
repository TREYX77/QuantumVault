using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

// Something that stops existing while the player watches it, like a quantum object collapsing
// under observation. Drop it on anything, a cube, a wall, a crate, a bridge, and that object
// and its children vanish together: renderers, colliders, lights, audio, particles and scripts.
// Pieces are switched off one by one rather than the GameObject itself, so this component keeps
// running on the vanished object to notice when the player looks away again.
// Seen means on screen and not behind a wall, not just under the crosshair, so the only way
// across a vanishing bridge is to cross it without looking at it.
[AddComponentMenu("Observation/Observed Vanish")]
[DisallowMultipleComponent]
public class ObservedVanish : MonoBehaviour
{
    public enum VanishMode
    {
        VanishWhenSeen,
        ExistOnlyWhenSeen
    }

    [Header("Rule")]
    [Tooltip("VanishWhenSeen: gone while watched. ExistOnlyWhenSeen: only there while watched.")]
    [SerializeField] private VanishMode mode = VanishMode.VanishWhenSeen;

    [Tooltip("Optional. What vanishes. Left empty, this object and all its children vanish, " +
             "which is what you want almost every time.")]
    [SerializeField] private Transform content;

    [Tooltip("Also switch off scripts, lights and sounds while vanished. Untick to only take " +
             "away what you can see and bump into.")]
    [SerializeField] private bool disableScripts = true;

    [Header("Timing")]
    [Tooltip("How long it must be wrongly watched (or unwatched) before it goes. A short grace " +
             "stops a glance sweeping past from dropping the player.")]
    [SerializeField] private float vanishDelay = 0.1f;

    [Tooltip("How long it must stay unwatched (or watched) before it comes back. Longer than the " +
             "vanish delay so it does not strobe at the edge of the screen.")]
    [SerializeField] private float appearDelay = 0.3f;

    [Tooltip("Fade length in seconds. Zero switches instantly. Only URP Lit and Simple Lit " +
             "materials fade; anything else pops at the end of the fade.")]
    [SerializeField] private float fadeDuration = 0.2f;

    [Header("Line Of Sight")]
    [Tooltip("Optional. Left empty the player camera is found.")]
    [SerializeField] private Camera viewCamera;

    [Tooltip("What blocks the player's view. Ignore Raycast is left out by default.")]
    [SerializeField] private LayerMask occluders = Physics.DefaultRaycastLayers;

    [Tooltip("Points are scattered over the object's colliders about this far apart, so the deck " +
             "under the player's feet counts as seen, not just the middle of its bounding box. " +
             "Smaller catches thinner glimpses but checks more points.")]
    [SerializeField] private float sampleSpacing = 0.75f;

    [Tooltip("Cap on the points scattered over the colliders. Only points on screen are ray " +
             "checked, and the first one in view ends the search, so the cost stays small.")]
    [Range(8, 1024)]
    [SerializeField] private int maxSamplePoints = 512;

    [Tooltip("Fallback when no collider shape can be read: points along the longest side of " +
             "the bounds. A long bridge can hide its middle while both ends are in plain view.")]
    [Range(1, 16)]
    [SerializeField] private int samplesAlongLength = 7;

    [Header("Events")]
    [SerializeField] private UnityEvent onVanish;
    [SerializeField] private UnityEvent onReappear;

    // Shared by every instance: the frustum only changes once a frame, so it is built once.
    private static readonly Plane[] frustum = new Plane[6];
    private static Camera frustumCamera;
    private static int frustumFrame = -1;

    private static readonly List<ObservedVanish> active = new List<ObservedVanish>();

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private const float RetryInterval = 0.1f;

    private readonly Collider[] overlapBuffer = new Collider[32];

    // Each piece remembers how it started, so restoring never switches on something a
    // designer had switched off on purpose.
    private Renderer[] renderers;
    private bool[] rendererWasEnabled;
    private Collider[] colliders;
    private bool[] colliderWasEnabled;
    private Behaviour[] behaviours;
    private bool[] behaviourWasEnabled;
    private Rigidbody[] bodies;
    private bool[] bodyWasKinematic;
    private ParticleSystem[] particles;
    private bool[] particleWasPlaying;

    private Material[][] originalMaterials;
    private Material[][] fadeMaterials;
    private Color[][] fadeBaseColors;
    private bool fadeMaterialsApplied;

    // A point on the object's surface, kept in its own transform's space so items that move
    // carry their points with them.
    private struct SurfacePoint
    {
        public Transform Space;
        public Vector3 Local;
    }

    private readonly List<SurfacePoint> surfacePoints = new List<SurfacePoint>();

    private Vector3[] samples;
    private Bounds bounds;
    private bool hasBounds;

    private CharacterController player;
    private float nextCameraSearch;
    private float nextAppearAttempt;

    private bool present = true;
    private bool visualsShown = true;
    private float alpha = 1f;
    private float pendingTime;
    private bool observed;

    public static IReadOnlyList<ObservedVanish> Active => active;

    public bool IsObserved => observed;
    public bool IsHidden => !visualsShown;
    public bool IsPresent => present;
    public VanishMode Mode => mode;

    // Statics survive play sessions when the domain reload is skipped.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
        frustumCamera = null;
        frustumFrame = -1;
    }

    void Awake()
    {
        CollectParts();
        CollectSurfacePoints();

        samples = new Vector3[surfacePoints.Count + 1 + samplesAlongLength + 8];

        RefreshBounds();
        ResolveCamera();

        // An object that only exists while watched starts gone, or the first frame would show it.
        if (mode == VanishMode.ExistOnlyWhenSeen) HideImmediately();
    }

    void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
    }

    void OnDisable()
    {
        active.Remove(this);
    }

    void OnDestroy()
    {
        if (fadeMaterials == null) return;

        foreach (Material[] slots in fadeMaterials)
        {
            if (slots == null) continue;

            foreach (Material material in slots)
            {
                if (material != null) Destroy(material);
            }
        }
    }

    void Update()
    {
        if (visualsShown) RefreshBounds();

        observed = IsSeen();

        bool wantPresent = mode == VanishMode.VanishWhenSeen ? !observed : observed;

        if (wantPresent == present)
        {
            pendingTime = 0f;
        }
        else
        {
            pendingTime += Time.deltaTime;

            float delay = wantPresent ? appearDelay : vanishDelay;

            if (pendingTime >= delay)
            {
                if (wantPresent) TryAppear();
                else Vanish();
            }
        }

        UpdateFade();
    }

    // Switches it off this frame, with no fade and no event. For scripted puzzle resets.
    public void HideImmediately()
    {
        present = false;
        pendingTime = 0f;
        alpha = 0f;
        SetCollidersEnabled(false);
        SetBodiesFrozen(true);
        RestoreMaterials();
        SetVisualsShown(false);
    }

    private void CollectParts()
    {
        Transform root = content != null ? content : transform;

        renderers = root.GetComponentsInChildren<Renderer>(true);
        colliders = root.GetComponentsInChildren<Collider>(true);
        bodies = root.GetComponentsInChildren<Rigidbody>(true);
        particles = root.GetComponentsInChildren<ParticleSystem>(true);

        // Lights, audio, animators and gameplay scripts are all Behaviours. This one is left
        // out, or it would switch itself off and never bring the object back.
        List<Behaviour> foundBehaviours = new List<Behaviour>();

        if (disableScripts)
        {
            foreach (Behaviour behaviour in root.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour != this) foundBehaviours.Add(behaviour);
            }
        }

        behaviours = foundBehaviours.ToArray();

        rendererWasEnabled = new bool[renderers.Length];
        colliderWasEnabled = new bool[colliders.Length];
        behaviourWasEnabled = new bool[behaviours.Length];
        bodyWasKinematic = new bool[bodies.Length];
        particleWasPlaying = new bool[particles.Length];
        originalMaterials = new Material[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            rendererWasEnabled[i] = renderers[i].enabled;
            originalMaterials[i] = renderers[i].sharedMaterials;
        }

        for (int i = 0; i < colliders.Length; i++) colliderWasEnabled[i] = colliders[i].enabled;
        for (int i = 0; i < behaviours.Length; i++) behaviourWasEnabled[i] = behaviours[i].enabled;
        for (int i = 0; i < bodies.Length; i++) bodyWasKinematic[i] = bodies[i].isKinematic;
        for (int i = 0; i < particles.Length; i++) particleWasPlaying[i] = particles[i].isPlaying;
    }

    // Collider off the moment it is seen, so the player drops straight away; the visual is
    // allowed to fade out after it. Physics bodies freeze at the same moment, or a crate with
    // no collider would fall through the floor while it fades.
    private void Vanish()
    {
        present = false;
        pendingTime = 0f;
        SetCollidersEnabled(false);
        SetBodiesFrozen(true);
        onVanish?.Invoke();
    }

    // Disabled colliders are invisible to physics queries, so the only way to ask whether it
    // would come back inside the player is to switch them on and look. Doing it inside one
    // Update means a failed attempt is never simulated or drawn.
    private void TryAppear()
    {
        if (Time.time < nextAppearAttempt) return;

        SetCollidersEnabled(true);

        if (OverlapsPlayer())
        {
            SetCollidersEnabled(false);
            nextAppearAttempt = Time.time + RetryInterval;

            return;
        }

        SetBodiesFrozen(false);
        SetVisualsShown(true);

        present = true;
        pendingTime = 0f;
        onReappear?.Invoke();
    }

    private void UpdateFade()
    {
        float target = present ? 1f : 0f;

        if (Mathf.Approximately(alpha, target))
        {
            if (!present && visualsShown) SetVisualsShown(false);

            return;
        }

        alpha = fadeDuration > 0f
            ? Mathf.MoveTowards(alpha, target, Time.deltaTime / fadeDuration)
            : target;

        if (alpha >= 1f)
        {
            RestoreMaterials();
        }
        else if (alpha <= 0f)
        {
            RestoreMaterials();
            SetVisualsShown(false);
        }
        else
        {
            ApplyFade(alpha);
        }
    }

    // Everything except colliders and bodies, which switch on their own schedule above.
    private void SetVisualsShown(bool value)
    {
        if (visualsShown == value) return;

        visualsShown = value;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].enabled = value && rendererWasEnabled[i];
        }

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null) behaviours[i].enabled = value && behaviourWasEnabled[i];
        }

        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] == null) continue;

            if (!value) particles[i].Pause(false);
            else if (particleWasPlaying[i]) particles[i].Play(false);
        }
    }

    // Kinematic rather than asleep: a sleeping body wakes the moment anything touches it.
    private void SetBodiesFrozen(bool frozen)
    {
        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody body = bodies[i];

            if (body == null) continue;

            if (frozen)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }
            else
            {
                body.isKinematic = bodyWasKinematic[i];
            }
        }
    }

    // Puts back each collider's own setting rather than forcing them all on, so a collider
    // someone switched off on purpose stays off.
    private void SetCollidersEnabled(bool value)
    {
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) colliders[i].enabled = value && colliderWasEnabled[i];
        }
    }

    private bool OverlapsPlayer()
    {
        if (player == null) ResolvePlayer();
        if (player == null) return false;

        Transform body = player.transform;
        Vector3 scale = body.lossyScale;

        float radius = player.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) + player.skinWidth;
        float half = Mathf.Max(0f, player.height * Mathf.Abs(scale.y) * 0.5f - radius);

        Vector3 centre = body.TransformPoint(player.center);
        Vector3 up = body.up * half;

        int count = Physics.OverlapCapsuleNonAlloc(centre + up, centre - up, radius, overlapBuffer,
                                                   Physics.AllLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (overlapBuffer[i].transform.IsChildOf(transform)) return true;
        }

        return false;
    }

    // ---- Observation -------------------------------------------------------------------

    // A frustum test first, which is cheap and rejects everything off screen, then line of
    // sight to a handful of points so a wall in between does not count as looking.
    private bool IsSeen()
    {
        if (!hasBounds) return false;

        if (viewCamera == null)
        {
            if (Time.time < nextCameraSearch) return false;

            nextCameraSearch = Time.time + 1f;
            ResolveCamera();

            if (viewCamera == null) return false;
        }

        if (!viewCamera.isActiveAndEnabled) return false;

        Plane[] planes = FrustumFor(viewCamera);

        if (!GeometryUtility.TestPlanesAABB(planes, bounds)) return false;

        Vector3 eye = viewCamera.transform.position;
        int count = BuildSamples();

        for (int i = 0; i < count; i++)
        {
            Vector3 point = samples[i];

            if (!InsideFrustum(planes, point)) continue;

            if (!Physics.Linecast(eye, point, out RaycastHit hit, occluders, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            // While it is present its own colliders are in the way of its own points. The
            // collider's transform, not hit.transform, which is the Rigidbody's if there is one.
            if (hit.collider.transform.IsChildOf(transform)) return true;
        }

        return false;
    }

    private static Plane[] FrustumFor(Camera cam)
    {
        if (frustumFrame != Time.frameCount || frustumCamera != cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            frustumCamera = cam;
            frustumFrame = Time.frameCount;
        }

        return frustum;
    }

    private static bool InsideFrustum(Plane[] planes, Vector3 point)
    {
        for (int i = 0; i < planes.Length; i++)
        {
            if (planes[i].GetDistanceToPoint(point) < 0f) return false;
        }

        return true;
    }

    // Points on the colliders when there are any. The bounds are only a fallback: a rope
    // bridge's box is centred in the empty air over the deck, so looking straight up from the
    // middle of the bridge would count as looking at it.
    private int BuildSamples()
    {
        if (surfacePoints.Count == 0) return BuildBoundsSamples(0);

        for (int i = 0; i < surfacePoints.Count; i++)
        {
            SurfacePoint point = surfacePoints[i];

            samples[i] = point.Space != null ? point.Space.TransformPoint(point.Local) : bounds.center;
        }

        return surfacePoints.Count;
    }

    // Points on the real shape, read once. Bounding box points are not enough: a rope bridge's
    // box is centred at rope height, so standing on the deck and looking at your feet would
    // never see any of them. Mesh vertices are not enough either: a baked walkway only has
    // them along its two edges, so the middle of the deck would have none.
    private void CollectSurfacePoints()
    {
        surfacePoints.Clear();

        List<SurfacePoint> found = new List<SurfacePoint>();

        // Fixed seed, so the same object always checks the same points.
        System.Random random = new System.Random(1234);

        foreach (Collider collider in colliders)
        {
            if (collider == null) continue;

            Transform space = collider.transform;

            switch (collider)
            {
                case BoxCollider box:
                    ScatterOverBox(space, box.center, box.size, found);

                    break;

                case SphereCollider sphere:
                    ScatterOverBox(space, sphere.center, Vector3.one * sphere.radius * 2f * 0.7f, found);

                    break;

                case CapsuleCollider capsule:
                    Vector3 size = Vector3.one * capsule.radius * 2f * 0.7f;
                    size[capsule.direction] = Mathf.Max(capsule.height * 0.85f, size[capsule.direction]);
                    ScatterOverBox(space, capsule.center, size, found);

                    break;

                case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                    // An imported mesh is usually not readable outside the editor, so its box
                    // stands in for it. A baked walkway is readable and gets the real shape.
                    if (meshCollider.sharedMesh.isReadable) ScatterOverMesh(meshCollider.sharedMesh, space, random, found);
                    else ScatterOverBox(space, meshCollider.sharedMesh.bounds.center, meshCollider.sharedMesh.bounds.size, found);

                    break;

                default:
                    found.Add(new SurfacePoint { Space = space, Local = space.InverseTransformPoint(collider.bounds.center) });

                    break;
            }
        }

        // Nothing to collide with, a decoration or a light fitting, so the look is all there is.
        if (found.Count == 0)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;

                Bounds local = renderer.localBounds;
                ScatterOverBox(renderer.transform, local.center, local.size, found);
            }
        }

        // Evenly thinned rather than cut off, so points still cover the whole length.
        float stride = Mathf.Max(1f, found.Count / (float)maxSamplePoints);

        for (float i = 0f; i < found.Count && surfacePoints.Count < maxSamplePoints; i += stride)
        {
            surfacePoints.Add(found[(int)i]);
        }
    }

    // A grid on each of the six faces, about sampleSpacing apart in world metres, so a long
    // wall seen only at one end still has points there. Pulled in a touch from each face so
    // a point never sits exactly on the surface where a ray could slip past it.
    private void ScatterOverBox(Transform space, Vector3 centre, Vector3 size, List<SurfacePoint> found)
    {
        Vector3 scale = space.lossyScale;
        Vector3 world = new Vector3(Mathf.Abs(size.x * scale.x), Mathf.Abs(size.y * scale.y), Mathf.Abs(size.z * scale.z));
        float spacing = Mathf.Max(0.05f, sampleSpacing);

        found.Add(new SurfacePoint { Space = space, Local = centre });

        for (int axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;

            int countU = Mathf.Clamp(Mathf.CeilToInt(world[u] / spacing), 1, 64);
            int countV = Mathf.Clamp(Mathf.CeilToInt(world[v] / spacing), 1, 64);

            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < countU; i++)
                {
                    for (int j = 0; j < countV; j++)
                    {
                        Vector3 local = centre;
                        local[axis] += side * size[axis] * 0.5f * 0.98f;
                        local[u] += ((i + 0.5f) / countU - 0.5f) * size[u];
                        local[v] += ((j + 0.5f) / countV - 0.5f) * size[v];

                        found.Add(new SurfacePoint { Space = space, Local = local });
                    }
                }
            }
        }
    }

    // One point per sampleSpacing squared of surface, in world sized area, so a long thin
    // triangle gets as many points as its size deserves and a tiny one may get none.
    private void ScatterOverMesh(Mesh mesh, Transform space, System.Random random, List<SurfacePoint> found)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        float areaPerPoint = Mathf.Max(0.01f, sampleSpacing * sampleSpacing);
        float carried = (float)random.NextDouble() * areaPerPoint;

        for (int t = 0; t < triangles.Length; t += 3)
        {
            Vector3 a = vertices[triangles[t]];
            Vector3 b = vertices[triangles[t + 1]];
            Vector3 c = vertices[triangles[t + 2]];

            Vector3 worldA = space.TransformPoint(a);
            float area = Vector3.Cross(space.TransformPoint(b) - worldA, space.TransformPoint(c) - worldA).magnitude * 0.5f;

            carried += area;

            while (carried >= areaPerPoint)
            {
                carried -= areaPerPoint;

                // Uniform over the triangle: fold the unit square's far half back in.
                float u = (float)random.NextDouble();
                float v = (float)random.NextDouble();

                if (u + v > 1f)
                {
                    u = 1f - u;
                    v = 1f - v;
                }

                found.Add(new SurfacePoint { Space = space, Local = a + (b - a) * u + (c - a) * v });
            }
        }
    }

    // Centre, a line down the longest side, and the corners pulled in a little so they sit
    // nearer the actual geometry than the empty air at the box's tips. Written from start on,
    // and returns where the samples now end.
    private int BuildBoundsSamples(int start)
    {
        Vector3 centre = bounds.center;
        Vector3 extents = bounds.extents;

        int count = start;
        samples[count++] = centre;

        Vector3 axis = extents.x >= extents.y && extents.x >= extents.z
            ? new Vector3(extents.x, 0f, 0f)
            : (extents.z >= extents.y ? new Vector3(0f, 0f, extents.z) : new Vector3(0f, extents.y, 0f));

        for (int i = 0; i < samplesAlongLength; i++)
        {
            float t = samplesAlongLength == 1 ? 0f : Mathf.Lerp(-0.95f, 0.95f, i / (float)(samplesAlongLength - 1));

            samples[count++] = centre + axis * t;
        }

        Vector3 corner = extents * 0.75f;

        for (int i = 0; i < 8; i++)
        {
            samples[count++] = centre + new Vector3(
                (i & 1) == 0 ? -corner.x : corner.x,
                (i & 2) == 0 ? -corner.y : corner.y,
                (i & 4) == 0 ? -corner.z : corner.z);
        }

        return count;
    }

    // Cached rather than read on demand: a hidden object has no renderer bounds, and it still
    // has to know where it is to notice the player looking there.
    private void RefreshBounds()
    {
        bool found = false;
        Bounds union = default;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

            if (!found)
            {
                union = renderer.bounds;
                found = true;
            }
            else
            {
                union.Encapsulate(renderer.bounds);
            }
        }

        if (!found)
        {
            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;

                if (!found)
                {
                    union = collider.bounds;
                    found = true;
                }
                else
                {
                    union.Encapsulate(collider.bounds);
                }
            }
        }

        if (!found) return;

        bounds = union;
        hasBounds = true;
    }

    // Same fallback chain SlitGate uses: PlayerCamera may not sit on the Camera object.
    private void ResolveCamera()
    {
        if (viewCamera != null) return;

        PlayerCamera playerCamera = FindAnyObjectByType<PlayerCamera>();

        if (playerCamera != null)
        {
            viewCamera = playerCamera.GetComponent<Camera>();

            if (viewCamera == null) viewCamera = playerCamera.GetComponentInChildren<Camera>(true);
        }

        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) viewCamera = FindAnyObjectByType<Camera>();
    }

    private void ResolvePlayer()
    {
        if (viewCamera != null) player = viewCamera.GetComponentInParent<CharacterController>();

        if (player == null)
        {
            Movement movement = FindAnyObjectByType<Movement>();

            if (movement != null) player = movement.GetComponent<CharacterController>();
        }
    }

    // ---- Fade --------------------------------------------------------------------------

    private void ApplyFade(float value)
    {
        if (fadeMaterials == null) BuildFadeMaterials();

        for (int r = 0; r < renderers.Length; r++)
        {
            Material[] slots = fadeMaterials[r];

            if (slots == null || renderers[r] == null) continue;

            if (!fadeMaterialsApplied)
            {
                Material[] swapped = (Material[])originalMaterials[r].Clone();

                for (int s = 0; s < slots.Length; s++)
                {
                    if (slots[s] != null) swapped[s] = slots[s];
                }

                renderers[r].sharedMaterials = swapped;
            }

            for (int s = 0; s < slots.Length; s++)
            {
                if (slots[s] == null) continue;

                Color colour = fadeBaseColors[r][s];
                colour.a *= value;
                slots[s].SetColor(BaseColorId, colour);
            }
        }

        fadeMaterialsApplied = true;
    }

    private void RestoreMaterials()
    {
        if (!fadeMaterialsApplied) return;

        for (int r = 0; r < renderers.Length; r++)
        {
            if (renderers[r] != null) renderers[r].sharedMaterials = originalMaterials[r];
        }

        fadeMaterialsApplied = false;
    }

    // Copies, made once, so fading never touches the shared material assets other objects use.
    private void BuildFadeMaterials()
    {
        fadeMaterials = new Material[renderers.Length][];
        fadeBaseColors = new Color[renderers.Length][];

        for (int r = 0; r < renderers.Length; r++)
        {
            Material[] originals = originalMaterials[r];

            fadeMaterials[r] = new Material[originals.Length];
            fadeBaseColors[r] = new Color[originals.Length];

            for (int s = 0; s < originals.Length; s++)
            {
                Material original = originals[s];

                if (!CanFade(original)) continue;

                Material copy = new Material(original) { name = original.name + " (Fade)" };
                MakeTransparent(copy);

                fadeMaterials[r][s] = copy;
                fadeBaseColors[r][s] = original.GetColor(BaseColorId);
            }
        }
    }

    private static bool CanFade(Material material)
    {
        if (material == null || !material.HasProperty(BaseColorId) || !material.HasProperty("_Surface")) return false;

        string shader = material.shader.name;

        return shader == "Universal Render Pipeline/Lit" || shader == "Universal Render Pipeline/Simple Lit";
    }

    // What the URP material inspector does when Surface Type is set to Transparent.
    private static void MakeTransparent(Material material)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;

        // A half faded bridge casting a solid shadow gives the trick away.
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.SetShaderPassEnabled("DepthOnly", false);
    }

    // ---- Gizmos ------------------------------------------------------------------------

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || !hasBounds) return;

        Gizmos.color = !present
            ? new Color(1f, 0.3f, 0.3f, 0.6f)
            : (observed ? new Color(1f, 0.85f, 0.3f, 0.6f) : new Color(0.4f, 1f, 0.5f, 0.4f));

        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !hasBounds || samples == null) return;

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);

        int count = BuildSamples();

        for (int i = 0; i < count; i++) Gizmos.DrawSphere(samples[i], 0.15f);
    }
}
