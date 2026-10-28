using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// The player's flashlight. Nothing until a FlashlightPickup is used, then the picked-up model held
// out in the right hand with a spot light at its lens, aimed at the crosshair and trailing the view
// a touch like a hand would. Toggles with its key. Built at runtime under the camera, so no player
// prefab needs editing to give a level a flashlight.
// Runs after PlayerCamera, so the beam follows where the view ended up this frame.
[AddComponentMenu("Player/Player Flashlight")]
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class PlayerFlashlight : MonoBehaviour
{
    [Tooltip("F is grab, E interact, Q the photon emitters.")]
    [SerializeField] private Key toggleKey = Key.T;

    [Tooltip("Start the level already holding it. Off means it has to be picked up first.")]
    [SerializeField] private bool startWithFlashlight;

    [Header("Beam")]
    [SerializeField] private float range = 22f;

    [Range(10f, 120f)]
    [Tooltip("Edge of the spill light.")]
    [SerializeField] private float spotAngle = 60f;

    [Range(1f, 120f)]
    [Tooltip("Edge of the bright centre. Ignored with a cookie, which draws the hotspot itself.")]
    [SerializeField] private float innerSpotAngle = 16f;

    [Tooltip("Light falls off with the square of distance, so this is set for the far wall to " +
             "show a spot; close up the hotspot is meant to blow out, like a real one.")]
    [SerializeField] private float intensity = 18f;
    [SerializeField] private Color color = new Color(1f, 0.96f, 0.88f);

    [Tooltip("Optional. The pattern a reflector throws: a hotspot, a faint ring, soft spill. " +
             "Left empty the beam is a plain cone.")]
    [SerializeField] private Texture cookie;

    [SerializeField] private LightShadows shadows = LightShadows.Soft;

    [Tooltip("Nothing closer to the lamp than this casts a shadow from it, so the player's own " +
             "head, shoulder and arm do not black out half the beam.")]
    [SerializeField] private float shadowNearPlane = 0.45f;

    [Header("Holding")]
    [Tooltip("Where the lens sits relative to the camera: right, below and ahead of the eyes, as if " +
             "held out in the right hand. Keep it past the camera's near clip plane, or the model " +
             "is sliced off.")]
    [SerializeField] private Vector3 offset = new Vector3(0.24f, -0.21f, 0.5f);

    [Tooltip("How far ahead the beam is aimed, so it lands on the crosshair rather than beside it.")]
    [SerializeField] private float aimDistance = 8f;

    [Tooltip("How quickly the beam catches up when the view turns, like a hand following the " +
             "eyes. 0 locks it rigidly to the view.")]
    [SerializeField] private float followSharpness = 20f;

    [Tooltip("Size of the picked-up model in hand, against its size lying in the level.")]
    [SerializeField] private float heldScale = 0.32f;

    [Tooltip("A tiny shadowless light on the camera side of the held model, so it reads as a " +
             "flashlight instead of a black hole in a dark room. Its range only reaches the hand. " +
             "0 turns it off.")]
    [SerializeField] private float handFill = 0.4f;

    private Light beam;
    private Light fill;
    private Transform view;
    private Transform held;
    private Quaternion aim;
    private Quaternion smoothed;

    public bool HasFlashlight { get; private set; }
    public bool IsOn => beam != null && beam.enabled;

    private Quaternion Target => view.rotation * aim;

    void Start()
    {
        if (startWithFlashlight) Equip(true);
    }

    void Update()
    {
        if (!HasFlashlight || PauseMenu.IsPaused || DevConsole.BlocksInput) return;

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) SetOn(!beam.enabled);
    }

    // After the camera has turned, so the beam trails the view instead of leading it.
    void LateUpdate()
    {
        if (beam == null) return;

        smoothed = followSharpness > 0f
            ? Quaternion.Slerp(smoothed, Target, 1f - Mathf.Exp(-followSharpness * Time.deltaTime))
            : Target;

        beam.transform.rotation = smoothed;
    }

    // The model is optional: it is the pickup's own, moved into the hand.
    public void Equip(bool switchOn, Transform heldModel = null)
    {
        HasFlashlight = true;

        if (beam == null) Build();

        smoothed = Target;
        beam.transform.rotation = smoothed;

        if (heldModel != null) Hold(heldModel);

        SetOn(switchOn);
    }

    private void SetOn(bool on)
    {
        beam.enabled = on;

        if (fill != null) fill.enabled = on;
    }

    private void Build()
    {
        Camera camera = GetComponentInChildren<Camera>(true);
        view = camera != null ? camera.transform : transform;

        GameObject go = new GameObject("Flashlight Beam");
        go.transform.SetParent(view, false);
        go.transform.localPosition = offset;

        // Toed in towards a point straight ahead, so the bright centre sits on the crosshair.
        aim = Quaternion.LookRotation(Vector3.forward * aimDistance - offset, Vector3.up);

        beam = go.AddComponent<Light>();
        beam.type = LightType.Spot;
        beam.range = range;
        beam.spotAngle = spotAngle;
        beam.innerSpotAngle = cookie != null ? spotAngle * 0.85f : Mathf.Min(innerSpotAngle, spotAngle);
        beam.intensity = intensity;
        beam.color = color;
        beam.cookie = cookie;
        beam.shadows = shadows;
        beam.shadowNearPlane = shadowNearPlane;
    }

    // Lens on the light and pointing down the beam. The model's own spot light marks its lens,
    // then is switched off; the beam above replaces it. Colliders go, so the hand cannot shove
    // props or block rays, and shadows go, so the head it wraps around cannot black out the beam.
    private void Hold(Transform model)
    {
        if (held != null) Destroy(held.gameObject);

        held = model;

        Transform lensPoint = null;

        foreach (Light light in model.GetComponentsInChildren<Light>(true))
        {
            if (lensPoint == null && light.type == LightType.Spot) lensPoint = light.transform;

            light.enabled = false;
        }

        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) Destroy(collider);

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        Quaternion lensTurn = lensPoint != null ? Quaternion.Inverse(model.rotation) * lensPoint.rotation : Quaternion.identity;

        model.SetParent(beam.transform, true);
        model.localScale *= heldScale;
        model.rotation = beam.transform.rotation * Quaternion.Inverse(lensTurn);

        Vector3 lensOffset = lensPoint != null ? lensPoint.position - model.position : Vector3.zero;

        model.position = beam.transform.position - lensOffset;

        if (fill == null && handFill > 0f) BuildFill();
    }

    // Behind and above the model, on the side the player sees, and too short to light the room.
    private void BuildFill()
    {
        GameObject go = new GameObject("Hand Fill");
        go.transform.SetParent(beam.transform, false);
        go.transform.localPosition = new Vector3(0.04f, 0.08f, -0.25f);

        fill = go.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.range = 0.45f;
        fill.intensity = handFill;
        fill.color = color;
        fill.shadows = LightShadows.None;
        fill.enabled = beam.enabled;
    }
}
