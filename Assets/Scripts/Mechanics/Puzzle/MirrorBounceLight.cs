using UnityEngine;

// Throws the reflection of a spot light off any MirrorSurface it shines on. The light a flat mirror
// sends back is exactly what a copy of the lamp would give standing at its mirror image behind the
// glass, so that is what this places: the same cone, colour and cookie, dimmed by the mirror, with
// shadows that only start at the glass so the wall the mirror hangs on does not block them.
// The player's flashlight gets one automatically; add it to any other spot light by hand.
// One bounce, following where the middle of the beam lands.
// Runs after PlayerFlashlight, so it bounces the beam from where the hand has just aimed it.
[AddComponentMenu("Puzzle/Mirror Bounce Light")]
[RequireComponent(typeof(Light))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(110)]
public class MirrorBounceLight : MonoBehaviour
{
    [Tooltip("What the beam can land on. It bounces only if the first thing it meets is a mirror's glass.")]
    [SerializeField] private LayerMask surfaces = ~0;

    [Tooltip("Seconds the reflected spot takes to fade in or out, so sweeping off a mirror's edge " +
             "does not pop.")]
    [SerializeField] private float fadeTime = 0.08f;

    // Light.shadowNearPlane cannot go further than this.
    private const float MaxShadowNearPlane = 10f;

    private Light source;
    private Light bounce;
    private MirrorSurface mirror;
    private float strength;

    public bool IsBouncing => bounce != null && bounce.enabled;

    void Awake()
    {
        source = GetComponent<Light>();
    }

    void OnDisable()
    {
        strength = 0f;

        if (bounce != null) bounce.enabled = false;
    }

    void OnDestroy()
    {
        if (bounce != null) Destroy(bounce.gameObject);
    }

    void LateUpdate()
    {
        MirrorSurface hitMirror = null;
        bool lit = source.enabled && source.type == LightType.Spot && FindMirror(out hitMirror);

        // The last mirror is kept while fading out, so the spot dims where it was, not nowhere.
        if (lit) mirror = hitMirror;

        float step = fadeTime > 0f ? Time.deltaTime / fadeTime : 1f;
        strength = Mathf.MoveTowards(strength, lit ? 1f : 0f, step);

        if (strength <= 0f || mirror == null)
        {
            if (bounce != null) bounce.enabled = false;

            return;
        }

        if (bounce == null) Build();

        Place(mirror);
        bounce.enabled = true;
    }

    private bool FindMirror(out MirrorSurface hitMirror)
    {
        hitMirror = null;
        Transform from = source.transform;

        return MirrorSurface.FirstHit(from.position, from.forward, source.range, surfaces, transform, out RaycastHit hit)
               && MirrorSurface.TryReflect(hit, from.forward, out hitMirror, out _);
    }

    private void Place(MirrorSurface glass)
    {
        Transform from = source.transform;
        Vector3 image = glass.ReflectPoint(from.position);

        bounce.transform.SetPositionAndRotation(
            image,
            Quaternion.LookRotation(glass.ReflectVector(from.forward), glass.ReflectVector(from.up)));

        // How far the image stands behind the glass. Everything nearer the image than that is
        // behind the mirror, so it must cast no shadow, or the wall would black the light out.
        float behind = Vector3.Dot(glass.Center - image, glass.Normal);

        // No wider than the glass looks from the image, or light would spill past its edges.
        float glassAngle = 2f * Mathf.Atan2(glass.Radius, Mathf.Max(0.01f, behind)) * Mathf.Rad2Deg;

        bounce.spotAngle = Mathf.Min(source.spotAngle, glassAngle);
        bounce.innerSpotAngle = Mathf.Min(source.innerSpotAngle, bounce.spotAngle);

        // Measured from the image, the distance to anything lit is the whole path the light took,
        // lamp to mirror to there, so the source's own range and falloff are already right.
        bounce.range = source.range;
        bounce.intensity = source.intensity * glass.Reflectivity * strength;
        bounce.color = source.color;
        bounce.useColorTemperature = source.useColorTemperature;
        bounce.colorTemperature = source.colorTemperature;
        bounce.cookie = source.cookie;
        bounce.cullingMask = source.cullingMask;
        bounce.renderingLayerMask = source.renderingLayerMask;

        bounce.shadows = source.shadows;
        bounce.shadowStrength = source.shadowStrength;
        bounce.shadowBias = source.shadowBias;
        bounce.shadowNormalBias = source.shadowNormalBias;
        bounce.shadowNearPlane = Mathf.Clamp(behind + 0.05f, 0.1f, MaxShadowNearPlane);
    }

    private void Build()
    {
        GameObject go = new GameObject("Mirror Bounce");
        go.transform.SetParent(transform, false);

        bounce = go.AddComponent<Light>();
        bounce.type = LightType.Spot;
        bounce.enabled = false;
    }
}
