using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Turns any box shaped wall into one whose solid sections follow a slit gate's interference
// pattern, so bright fringes become doorways. Add it to a wall or place the prefab, then link a
// gate from anywhere on the map. Local X runs across the wall, Y up, Z through it.
[AddComponentMenu("Wave Duality/Interference Wall")]
[RequireComponent(typeof(BoxCollider))]
public class InterferenceWall : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("The slit gate whose light this wall shows. It can be anywhere on the map, and one " +
             "gate can feed several walls. Left empty, the nearest gate facing this wall is used.")]
    [SerializeField] private SlitGate gate;

    [Tooltip("Fixes this wall's layout as if its gate stood this many metres away, so moving or " +
             "re-linking the gate never changes the doorways. Larger spreads them further apart " +
             "and widens them. 0 uses the real distance to the gate.")]
    [SerializeField] private float patternDistance = 7f;

    [Header("Doorways")]
    [Tooltip("How finely the pattern is sampled across the wall. Higher is more exact.")]
    [SerializeField] private int samples = 256;

    [Range(0.01f, 0.99f)]
    [Tooltip("Brightness at which the wall opens up. Lower means more, wider gaps.")]
    [SerializeField] private float brightnessThreshold = 0.25f;

    [Tooltip("Gaps narrower than this are filled back in. The player capsule is 1m across, " +
             "so anything under that reads as a doorway and is not one. Leave margin.")]
    [SerializeField] private float minOpeningWidth = 1.4f;

    [Header("Look")]
    [Tooltip("Optional. Left empty, the slabs copy this wall's own material, so it keeps the " +
             "look of whatever wall it was added to.")]
    [SerializeField] private Material solidMaterial;

    [Tooltip("Shows the pattern as a strip above the wall, so the player can read the fringes " +
             "before walking into them.")]
    [SerializeField] private bool showPattern = true;

    [Tooltip("Optional. An unlit material for the strip. Left empty, a plain unlit one is made.")]
    [SerializeField] private Material patternMaterial;

    [Tooltip("Height of the strip.")]
    [SerializeField] private float patternHeight = 1.2f;

    [Tooltip("Space between the top of the wall and the strip.")]
    [SerializeField] private float patternGap = 0.35f;

    [Tooltip("Strip colour where the pattern is brightest.")]
    [SerializeField] private Color brightColor = new Color(0.4f, 0.8f, 1f);

    [Tooltip("Strip colour where the pattern is dark.")]
    [SerializeField] private Color darkColor = new Color(0.02f, 0.03f, 0.06f);

    // Anything thinner reads as a glitch rather than a piece of wall, so it is left out.
    private const float MinSlabWidth = 0.3f;

    private enum Shown { Nothing, Solid, Wave, Observed }

    private readonly List<GameObject> slabs = new List<GameObject>();

    private BoxCollider box;
    private Renderer ownRenderer;
    private Collider[] ownColliders;
    private bool[] collidersWereEnabled;
    private bool rendererWasEnabled;

    private Transform generated;
    private Renderer strip;
    private Material stripMaterial;
    private Texture2D patternTexture;

    private SlitGate autoGate;
    private float nextGateSearch;
    private bool warnedNoGate;
    private Shown shown;
    private string lastSignature;

    public bool IsSolid { get; private set; } = true;

    // The gate actually in use: the linked one, or the nearest facing one when left empty.
    public SlitGate Gate
    {
        get
        {
            if (gate != null) return gate;

            // Edit mode has no registry yet, so the preview searches the scene the same way.
            return Application.isPlaying ? autoGate : NearestFacingGate(FindObjectsByType<SlitGate>());
        }
    }

    public Vector3 Centre => transform.TransformPoint(Box != null ? Box.center : Vector3.zero);

    // In metres, whatever mix of collider size and transform scale made the wall this big.
    public Vector3 Size
    {
        get
        {
            Vector3 size = Box != null ? Box.size : Vector3.one;
            Vector3 scale = transform.lossyScale;

            return new Vector3(Mathf.Abs(size.x * scale.x), Mathf.Abs(size.y * scale.y), Mathf.Abs(size.z * scale.z));
        }
    }

    // Looked up on demand because the Scene view preview runs before Awake ever has.
    private BoxCollider Box
    {
        get
        {
            if (box == null) box = GetComponent<BoxCollider>();

            return box;
        }
    }

    private Material SlabMaterial
    {
        get
        {
            if (solidMaterial != null) return solidMaterial;

            return ownRenderer != null ? ownRenderer.sharedMaterial : null;
        }
    }

    // Links the nearest facing gate the moment the component is added to a wall.
    void Reset()
    {
        gate = NearestFacingGate(FindObjectsByType<SlitGate>());
    }

    // A value changed in the inspector during play shows on the next frame.
    void OnValidate()
    {
        shown = Shown.Nothing;
    }

    void OnEnable()
    {
        TakeOver();

        // Solid straight away, so nothing can slip through before the first Update.
        BuildSolid();
        shown = Shown.Solid;
    }

    void OnDisable()
    {
        HandBack();
    }

    void OnDestroy()
    {
        if (stripMaterial != null) Destroy(stripMaterial);
        if (patternTexture != null) Destroy(patternTexture);
    }

    void Update()
    {
        SlitGate source = ResolveGate();

        Shown next = Shown.Solid;

        if (source != null && source.IsIlluminated) next = source.IsObserved ? Shown.Observed : Shown.Wave;

        // Only rebuild when the state actually flips, not every frame the beam stays on.
        if (next == shown) return;

        shown = next;

        if (next == Shown.Solid) BuildSolid();
        else Apply(Intensity(source, next == Shown.Observed));
    }

    private SlitGate ResolveGate()
    {
        if (gate != null) return gate;
        if (autoGate != null) return autoGate;

        // The project's usual self-heal: look again once a second rather than every frame.
        if (Time.time < nextGateSearch) return null;

        nextGateSearch = Time.time + 1f;
        autoGate = NearestFacingGate(SlitGate.Active);

        if (autoGate == null && !warnedNoGate)
        {
            warnedNoGate = true;
            Debug.LogWarning(name + " has no slit gate. Link one in its Gate field, or face a gate at it.", this);
        }

        return autoGate;
    }

    // Only gates with this wall in front of them, so a gate aimed into another module is not taken.
    private SlitGate NearestFacingGate(IReadOnlyList<SlitGate> gates)
    {
        Vector3 centre = Centre;
        SlitGate nearest = null;
        float nearestSqr = float.PositiveInfinity;

        for (int i = 0; i < gates.Count; i++)
        {
            SlitGate candidate = gates[i];

            if (candidate == null) continue;

            Vector3 toWall = centre - candidate.transform.position;

            if (Vector3.Dot(candidate.transform.forward, toWall) <= 0f) continue;
            if (toWall.sqrMagnitude >= nearestSqr) continue;

            nearestSqr = toWall.sqrMagnitude;
            nearest = candidate;
        }

        return nearest;
    }

    // The one path both the game and the Scene view preview take, so the preview cannot drift.
    private float[] Intensity(SlitGate source, bool observed)
    {
        return source.SamplePattern(samples, Size.x, DistanceTo(source), observed);
    }

    private List<Band> Openings(float[] intensity)
    {
        return InterferencePattern.ToOpenings(intensity, Size.x, brightnessThreshold, minOpeningWidth);
    }

    private float DistanceTo(SlitGate source)
    {
        return patternDistance > 0f ? patternDistance : Vector3.Distance(source.transform.position, Centre);
    }

    private void Apply(float[] intensity)
    {
        List<Band> openings = Openings(intensity);

        Rebuild(InterferencePattern.ToSolids(openings, Size.x));

        IsSolid = openings.Count == 0;

        UpdateStrip(intensity);
    }

    // No beam, no pattern: the wall is just a wall.
    private void BuildSolid()
    {
        float half = Size.x * 0.5f;

        Rebuild(new List<Band> { new Band(-half, half) });

        IsSolid = true;

        UpdateStrip(null);
    }

    // The wall's own mesh and collider would block every doorway, so they step aside while the
    // slabs stand in for them, and come back if this component is ever switched off.
    private void TakeOver()
    {
        ownRenderer = GetComponent<Renderer>();
        ownColliders = GetComponents<Collider>();
        collidersWereEnabled = new bool[ownColliders.Length];

        if (ownRenderer != null)
        {
            rendererWasEnabled = ownRenderer.enabled;
            ownRenderer.enabled = false;
        }

        for (int i = 0; i < ownColliders.Length; i++)
        {
            collidersWereEnabled[i] = ownColliders[i].enabled;
            ownColliders[i].enabled = false;
        }

        // Everything built lives under one child whose scale cancels the wall's, so slabs are laid
        // out in metres and a cube scaled into a wall does not stretch them a second time.
        generated = new GameObject("Generated").transform;
        generated.SetParent(transform, false);
        generated.localPosition = Box != null ? Box.center : Vector3.zero;
        generated.localRotation = Quaternion.identity;

        Vector3 scale = transform.lossyScale;
        generated.localScale = new Vector3(Inverse(scale.x), Inverse(scale.y), Inverse(scale.z));
    }

    private void HandBack()
    {
        if (generated != null) Destroy(generated.gameObject);

        generated = null;
        strip = null;
        slabs.Clear();
        lastSignature = null;

        if (ownRenderer != null) ownRenderer.enabled = rendererWasEnabled;

        if (ownColliders == null) return;

        for (int i = 0; i < ownColliders.Length; i++)
        {
            if (ownColliders[i] != null) ownColliders[i].enabled = collidersWereEnabled[i];
        }
    }

    private static float Inverse(float value)
    {
        return 1f / Mathf.Max(0.0001f, Mathf.Abs(value));
    }

    // Rebuilding every frame would destroy and recreate colliders under the player's feet,
    // so the geometry is only touched when the layout actually changed.
    private void Rebuild(List<Band> solidBands)
    {
        string signature = Signature(solidBands);

        if (signature == lastSignature) return;

        lastSignature = signature;

        foreach (GameObject slab in slabs)
        {
            if (slab != null) Destroy(slab);
        }

        slabs.Clear();

        Vector3 size = Size;
        Material material = SlabMaterial;

        foreach (Band band in solidBands)
        {
            if (band.Width < MinSlabWidth) continue;

            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab";

            // Same layer and tag as the wall, so anything filtering on them treats slabs as the wall.
            slab.layer = gameObject.layer;
            slab.tag = gameObject.tag;

            slab.transform.SetParent(generated, false);
            slab.transform.localPosition = new Vector3(band.Centre, 0f, 0f);
            slab.transform.localRotation = Quaternion.identity;
            slab.transform.localScale = new Vector3(band.Width, size.y, size.z);

            if (material != null) slab.GetComponent<MeshRenderer>().sharedMaterial = material;

            slabs.Add(slab);
        }
    }

    // A one pixel tall strip of the intensity profile, stretched across the top of the wall.
    private void UpdateStrip(float[] intensity)
    {
        if (!showPattern || intensity == null)
        {
            if (strip != null) strip.enabled = false;
            return;
        }

        if (strip == null) BuildStrip();

        if (patternTexture == null || patternTexture.width != intensity.Length)
        {
            if (patternTexture != null) Destroy(patternTexture);

            patternTexture = new Texture2D(intensity.Length, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp
            };
        }

        for (int i = 0; i < intensity.Length; i++)
        {
            patternTexture.SetPixel(i, 0, Color.Lerp(darkColor, brightColor, intensity[i]));
        }

        patternTexture.Apply();

        stripMaterial.mainTexture = patternTexture;
        strip.enabled = true;
    }

    // A thin box rather than a quad, so the strip reads from either side of the wall.
    private void BuildStrip()
    {
        GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name = "Pattern Strip";
        piece.layer = gameObject.layer;

        // Purely a display, so nothing should bump into it.
        Destroy(piece.GetComponent<Collider>());

        Vector3 size = Size;

        piece.transform.SetParent(generated, false);
        piece.transform.localPosition = new Vector3(0f, size.y * 0.5f + patternGap + patternHeight * 0.5f, 0f);
        piece.transform.localRotation = Quaternion.identity;
        piece.transform.localScale = new Vector3(size.x, patternHeight, 0.02f);

        // Each wall gets its own copy, so one wall's pattern never paints another's strip.
        if (stripMaterial == null)
        {
            stripMaterial = patternMaterial != null
                ? new Material(patternMaterial)
                : new Material(Shader.Find("Sprites/Default"));
        }

        strip = piece.GetComponent<Renderer>();
        strip.sharedMaterial = stripMaterial;
        strip.shadowCastingMode = ShadowCastingMode.Off;
        strip.receiveShadows = false;
    }

    private static string Signature(List<Band> bands)
    {
        string signature = "";

        foreach (Band band in bands)
        {
            signature += band.start.ToString("0.00") + ":" + band.end.ToString("0.00") + "|";
        }

        return signature;
    }

    // Shows the doorways before play, so a wall can be tuned per module without pressing Play.
    void OnDrawGizmosSelected()
    {
        SlitGate source = Gate;

        if (source == null)
        {
            DrawLabel("No slit gate faces this wall. Link one in its Gate field.");
            return;
        }

        Vector3 size = Size;
        List<Band> wave = Openings(Intensity(source, false));
        List<Band> particle = Openings(Intensity(source, true));

        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
        Gizmos.DrawLine(Centre, source.AimPoint);

        // Laid out in metres around the wall's centre, the same space the slabs are built in.
        Gizmos.matrix = Matrix4x4.TRS(Centre, transform.rotation, Vector3.one);
        DrawDoorways(wave, new Color(0.4f, 0.8f, 1f, 0.9f), size.y, size.z + 0.05f);
        DrawDoorways(particle, new Color(1f, 0.45f, 0.35f, 0.9f), size.y * 0.8f, size.z + 0.1f);
        Gizmos.matrix = Matrix4x4.identity;

        string link = gate != null ? source.name : source.name + " (nearest, not linked)";

        DrawLabel(link + "  ·  " + DistanceTo(source).ToString("0.##") + " m\n" +
                  wave.Count + " doorways, " + particle.Count + " when observed");
    }

    private static void DrawDoorways(List<Band> doorways, Color color, float height, float depth)
    {
        Gizmos.color = color;

        foreach (Band doorway in doorways)
        {
            Gizmos.DrawWireCube(new Vector3(doorway.Centre, 0f, 0f), new Vector3(doorway.Width, height, depth));
        }
    }

    private void DrawLabel(string text)
    {
#if UNITY_EDITOR
        Vector3 above = Centre + transform.up * (Size.y * 0.5f + patternGap + patternHeight + 0.4f);

        UnityEditor.Handles.Label(above, text);
#endif
    }
}
