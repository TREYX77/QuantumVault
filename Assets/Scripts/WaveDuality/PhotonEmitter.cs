using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// A photon source placed in the world. Its laser runs straight out of the muzzle and lights a
// slit gate only if it actually hits it. Works on any model: aim a Muzzle child down the barrel.
[AddComponentMenu("Wave Duality/Photon Emitter")]
public class PhotonEmitter : MonoBehaviour
{
    [Header("Muzzle")]
    [Tooltip("Optional. Where the laser starts, and it fires along this transform's blue Z " +
             "axis. Left empty the laser leaves this object's pivot along its own +Z, so on a " +
             "model whose barrel faces another way, add a Muzzle child and rotate it to match.")]
    [SerializeField] private Transform muzzle;

    [Header("Input")]
    [Tooltip("Toggles every emitter in the level, from anywhere. F is left for grabbing. " +
             "Set to None for an emitter driven only by switches or triggers through IsOn.")]
    [SerializeField] private Key toggleKey = Key.Q;

    [Tooltip("Laser stays on until toggled off. Untick to require holding the key.")]
    [SerializeField] private bool toggleMode = true;

    [Tooltip("Whether the laser is already firing when the level starts.")]
    [SerializeField] private bool startOn;

    [Header("Beam")]
    [Tooltip("How far the laser reaches when it hits nothing.")]
    [SerializeField] private float range = 60f;

    [Tooltip("What stops the laser. It ends on the first of these it meets, so the player or " +
             "a carried prop standing in the way cuts the gate off.")]
    [SerializeField] private LayerMask beamMask = ~0;

    [Header("Look")]
    [Tooltip("Colour of the visible laser.")]
    [SerializeField] private Color laserColor = new Color(0.3f, 1f, 0.35f);

    [Tooltip("Thickness of the visible laser.")]
    [SerializeField] private float laserWidth = 0.02f;

    // Shared by every emitter, which is safe because Update never runs two at once.
    private static readonly RaycastHit[] hits = new RaycastHit[16];

    private LineRenderer line;
    private Material lineMaterial;
    private SlitGate litGate;

    // Settable so a switch or trigger can drive the laser as well as the key.
    public bool IsOn { get; set; }
    public SlitGate LitGate => litGate;

    public string Status
    {
        get
        {
            if (!IsOn) return "laser off";

            return litGate == null ? "laser on - no gate in its path" : litGate.name + ": " + litGate.Status;
        }
    }

    private Transform Muzzle => muzzle != null ? muzzle : transform;

    void Awake()
    {
        IsOn = startOn;

        BuildLine();
    }

    void OnDisable()
    {
        litGate = null;

        if (line != null) line.enabled = false;
    }

    void OnDestroy()
    {
        if (lineMaterial != null) Destroy(lineMaterial);
    }

    void Update()
    {
        ReadToggle();

        litGate = null;

        if (!IsOn)
        {
            line.enabled = false;
            return;
        }

        Transform from = Muzzle;
        Vector3 end = from.position + from.forward * range;

        // Straight along the barrel, never bent toward a gate: a gate is lit only if this
        // exact line reaches it first.
        if (FirstHit(from.position, from.forward, out RaycastHit hit))
        {
            end = hit.point;
            litGate = hit.collider.GetComponentInParent<SlitGate>();
        }

        // Refreshed every frame, so the gate's illumination lapses once the laser leaves it.
        if (litGate != null) litGate.Illuminate();

        line.enabled = true;
        line.SetPosition(0, from.position);
        line.SetPosition(1, end);
    }

    private void ReadToggle()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null || toggleKey == Key.None) return;

        if (toggleMode)
        {
            if (keyboard[toggleKey].wasPressedThisFrame) IsOn = !IsOn;
        }
        else
        {
            IsOn = keyboard[toggleKey].isPressed;
        }
    }

    // Nearest hit that is not part of this emitter, so it can fire from inside its own
    // collider or a carried prop's. Same rule as WhichPathDetector.FirstHit.
    private bool FirstHit(Vector3 origin, Vector3 direction, out RaycastHit nearest)
    {
        nearest = default;
        float best = float.PositiveInfinity;

        int count = Physics.RaycastNonAlloc(origin, direction, hits, range, beamMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (hits[i].distance >= best || hits[i].collider.transform.IsChildOf(transform)) continue;

            best = hits[i].distance;
            nearest = hits[i];
        }

        return best < float.PositiveInfinity;
    }

    private void BuildLine()
    {
        line = GetComponent<LineRenderer>();

        if (line == null) line = gameObject.AddComponent<LineRenderer>();

        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = laserWidth;
        line.endWidth = laserWidth;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;

        // Sprites/Default tints from vertex colour with no lighting, which suits a beam.
        lineMaterial = new Material(Shader.Find("Sprites/Default"));
        line.sharedMaterial = lineMaterial;
        line.startColor = laserColor;
        line.endColor = laserColor;
    }

    void OnDrawGizmosSelected()
    {
        Transform from = Muzzle;
        Vector3 end = from.position + from.forward * range;

        // Physics answers in edit mode too, so this shows what the laser will hit while placing it.
        if (FirstHit(from.position, from.forward, out RaycastHit hit)) end = hit.point;

        Gizmos.color = laserColor;
        Gizmos.DrawLine(from.position, end);
        Gizmos.DrawWireSphere(end, 0.06f);
    }
}
