using System.Collections.Generic;
using UnityEngine;

// A wall whose solid sections are built at runtime from an interference pattern. Bright
// fringes become gaps you can walk through. Local X runs across it, Y up, Z is the normal.
[AddComponentMenu("Wave Duality/Interference Wall")]
public class InterferenceWall : MonoBehaviour
{
    [Header("Size")]
    [SerializeField] private float width = 12f;
    [SerializeField] private float height = 4f;
    [SerializeField] private float thickness = 0.4f;

    [Header("Pattern")]
    [Tooltip("How finely the pattern is sampled across the wall. Higher is more exact.")]
    [SerializeField] private int samples = 256;

    [Range(0.01f, 0.99f)]
    [Tooltip("Brightness at which the wall opens up. Lower means more, wider gaps.")]
    [SerializeField] private float brightnessThreshold = 0.35f;

    [Tooltip("Gaps narrower than this are filled back in, since the player cannot fit.")]
    [SerializeField] private float minOpeningWidth = 0.9f;

    [Header("Look")]
    [SerializeField] private Material solidMaterial;

    [Tooltip("Optional. A renderer that displays the intensity profile, so the player can " +
             "read the fringes before walking into them.")]
    [SerializeField] private Renderer patternDisplay;

    [SerializeField] private Color brightColor = new Color(0.4f, 0.8f, 1f);
    [SerializeField] private Color darkColor = new Color(0.02f, 0.03f, 0.06f);

    private readonly List<GameObject> slabs = new List<GameObject>();
    private Texture2D patternTexture;
    private string lastSignature;

    public float Width => width;
    public bool IsSolid { get; private set; } = true;

    void Start()
    {
        BuildSolid();
    }

    void OnDestroy()
    {
        if (patternTexture != null) Destroy(patternTexture);
    }

    // Called by the slit gate whenever the pattern changes.
    public void Apply(float[] intensity)
    {
        if (intensity == null || intensity.Length < 2)
        {
            BuildSolid();
            return;
        }

        List<Band> openings = InterferencePattern.ToOpenings(intensity, width, brightnessThreshold, minOpeningWidth);
        List<Band> solids = InterferencePattern.ToSolids(openings, width);

        // Rebuilding every frame would destroy and recreate colliders under the player's
        // feet, so only touch the geometry when the layout actually changed.
        string signature = Signature(solids);

        if (signature != lastSignature)
        {
            lastSignature = signature;
            Rebuild(solids);
        }

        IsSolid = openings.Count == 0;

        UpdateDisplay(intensity);
    }

    // No beam, no pattern: the wall is just a wall.
    public void BuildSolid()
    {
        string signature = "solid";

        if (signature != lastSignature)
        {
            lastSignature = signature;
            Rebuild(new List<Band> { new Band(-width * 0.5f, width * 0.5f) });
        }

        IsSolid = true;

        UpdateDisplay(null);
    }

    private void Rebuild(List<Band> solidBands)
    {
        foreach (GameObject slab in slabs)
        {
            if (slab != null) Destroy(slab);
        }

        slabs.Clear();

        foreach (Band band in solidBands)
        {
            if (band.Width <= 0.001f) continue;

            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab";
            slab.transform.SetParent(transform, false);
            slab.transform.localPosition = new Vector3(band.Centre, 0f, 0f);
            slab.transform.localRotation = Quaternion.identity;
            slab.transform.localScale = new Vector3(band.Width, height, thickness);

            if (solidMaterial != null)
            {
                slab.GetComponent<MeshRenderer>().sharedMaterial = solidMaterial;
            }

            slabs.Add(slab);
        }
    }

    // A one-pixel-tall strip of the intensity profile, stretched across the display quad.
    private void UpdateDisplay(float[] intensity)
    {
        if (patternDisplay == null) return;

        if (intensity == null)
        {
            patternDisplay.enabled = false;
            return;
        }

        patternDisplay.enabled = true;

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

        patternDisplay.material.mainTexture = patternTexture;
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

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(width, height, thickness));
    }
}
