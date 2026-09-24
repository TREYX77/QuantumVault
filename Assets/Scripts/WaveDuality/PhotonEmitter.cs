using UnityEngine;
using UnityEngine.InputSystem;

// Handheld photon beam. Toggle it on and aim; whenever it lands on a SlitGate that gate
// projects its pattern onto the wall behind. Goes on the player, alongside GrabInteract.
[AddComponentMenu("Wave Duality/Photon Emitter")]
public class PhotonEmitter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("All optional. Left empty they are resolved from the scene.")]
    [SerializeField] private PlayerCamera playerCamera;
    [SerializeField] private Camera viewCamera;

    [Tooltip("Optional. Where the beam starts visually. Defaults to the camera.")]
    [SerializeField] private Transform muzzle;

    [Header("Input")]
    [SerializeField] private Key toggleKey = Key.F;

    [Tooltip("Beam stays on until toggled off. Untick to require holding the key.")]
    [SerializeField] private bool toggleMode = true;

    [Header("Beam")]
    [SerializeField] private float range = 60f;
    [SerializeField] private LayerMask beamMask = ~0;

    [Tooltip("Thickness of the probe, so aiming at a slit gate is not pixel exact.")]
    [SerializeField] private float beamRadius = 0.05f;

    [Header("Look")]
    [SerializeField] private Color beamColor = new Color(0.45f, 0.85f, 1f);
    [SerializeField] private float beamThickness = 0.03f;

    private LineRenderer line;
    private SlitGate litGate;
    private float nextResolveTime;

    public bool IsOn { get; private set; }
    public SlitGate LitGate => litGate;

    public string Status
    {
        get
        {
            if (!IsOn) return "beam off";

            return litGate == null ? "beam on - no gate" : litGate.name + ": " + litGate.Status;
        }
    }

    void Awake()
    {
        ResolveReferences();
        BuildLine();
    }

    void OnDisable()
    {
        IsOn = false;
        litGate = null;

        if (line != null) line.enabled = false;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null) return;

        if (toggleMode)
        {
            if (keyboard[toggleKey].wasPressedThisFrame) IsOn = !IsOn;
        }
        else
        {
            IsOn = keyboard[toggleKey].isPressed;
        }

        if (!IsOn)
        {
            litGate = null;
            line.enabled = false;
            return;
        }

        FireBeam();
    }

    private void FireBeam()
    {
        if (viewCamera == null)
        {
            // Self-heals if the rig is spawned after this component woke up.
            if (Time.time >= nextResolveTime)
            {
                nextResolveTime = Time.time + 1f;
                ResolveReferences();
            }

            if (viewCamera == null) return;
        }

        Transform view = viewCamera.transform;
        Vector3 origin = muzzle != null ? muzzle.position : view.position;
        Vector3 end = view.position + view.forward * range;

        litGate = null;

        // A sphere rather than a hairline ray, so hitting a narrow gate is forgiving.
        if (Physics.SphereCast(view.position, beamRadius, view.forward, out RaycastHit hit,
                               range, beamMask, QueryTriggerInteraction.Ignore))
        {
            // Skip the player's own colliders, which the camera sits inside.
            if (!hit.collider.transform.IsChildOf(transform))
            {
                end = hit.point;

                litGate = hit.collider.GetComponentInParent<SlitGate>();
            }
        }

        // Refreshed every frame, so the gate's illumination lapses when the beam moves away.
        litGate?.Illuminate();

        line.enabled = true;
        line.SetPosition(0, origin);
        line.SetPosition(1, end);
    }

    private void BuildLine()
    {
        line = GetComponent<LineRenderer>();

        if (line == null) line = gameObject.AddComponent<LineRenderer>();

        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = beamThickness;
        line.endWidth = beamThickness;
        line.enabled = false;

        // Sprites/Default tints from vertex colour with no lighting, which suits a beam.
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = beamColor;
        line.endColor = new Color(beamColor.r, beamColor.g, beamColor.b, 0.25f);
    }

    // Same fallback chain GrabInteract uses: PlayerCamera may not sit on the Camera object.
    private void ResolveReferences()
    {
        if (playerCamera == null) playerCamera = GetComponentInChildren<PlayerCamera>(true);
        if (playerCamera == null) playerCamera = FindAnyObjectByType<PlayerCamera>();

        if (viewCamera != null) return;

        if (playerCamera != null)
        {
            viewCamera = playerCamera.GetComponent<Camera>();

            if (viewCamera == null) viewCamera = playerCamera.GetComponentInChildren<Camera>(true);
        }

        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>(true);
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) viewCamera = FindAnyObjectByType<Camera>();
    }
}
