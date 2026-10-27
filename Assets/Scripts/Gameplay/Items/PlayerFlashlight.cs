using UnityEngine;
using UnityEngine.InputSystem;

// The player's flashlight. Nothing until a FlashlightPickup is used, then a spot light that follows
// the view and toggles with its key. Built at runtime under the camera, so no player prefab needs
// editing to give a level a flashlight.
[AddComponentMenu("Player/Player Flashlight")]
[DisallowMultipleComponent]
public class PlayerFlashlight : MonoBehaviour
{
    [Tooltip("F is grab, E interact, Q the photon emitters.")]
    [SerializeField] private Key toggleKey = Key.T;

    [Tooltip("Start the level already holding it. Off means it has to be picked up first.")]
    [SerializeField] private bool startWithFlashlight;

    [Header("Beam")]
    [SerializeField] private float range = 18f;

    [Range(10f, 120f)]
    [SerializeField] private float spotAngle = 55f;

    [Range(1f, 120f)]
    [SerializeField] private float innerSpotAngle = 28f;

    [SerializeField] private float intensity = 6f;
    [SerializeField] private Color color = new Color(1f, 0.96f, 0.88f);

    [Tooltip("Where the light sits relative to the camera: a little right and below, as if held.")]
    [SerializeField] private Vector3 offset = new Vector3(0.22f, -0.18f, 0.1f);

    [Tooltip("How far ahead the beam is aimed, so it lands on the crosshair rather than beside it.")]
    [SerializeField] private float aimDistance = 8f;

    [SerializeField] private LightShadows shadows = LightShadows.Soft;

    private Light beam;

    public bool HasFlashlight { get; private set; }
    public bool IsOn => beam != null && beam.enabled;

    void Start()
    {
        if (startWithFlashlight) Equip(true);
    }

    void Update()
    {
        if (!HasFlashlight || PauseMenu.IsPaused || DevConsole.BlocksInput) return;

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) beam.enabled = !beam.enabled;
    }

    public void Equip(bool switchOn)
    {
        HasFlashlight = true;

        if (beam == null) Build();

        beam.enabled = switchOn;
    }

    private void Build()
    {
        Camera view = GetComponentInChildren<Camera>(true);
        Transform holder = view != null ? view.transform : transform;

        GameObject go = new GameObject("Flashlight Beam");
        go.transform.SetParent(holder, false);
        go.transform.localPosition = offset;

        // Toed in towards a point straight ahead, so the bright centre sits on the crosshair.
        go.transform.localRotation = Quaternion.LookRotation(Vector3.forward * aimDistance - offset, Vector3.up);

        beam = go.AddComponent<Light>();
        beam.type = LightType.Spot;
        beam.range = range;
        beam.spotAngle = spotAngle;
        beam.innerSpotAngle = Mathf.Min(innerSpotAngle, spotAngle);
        beam.intensity = intensity;
        beam.color = color;
        beam.shadows = shadows;
    }
}
