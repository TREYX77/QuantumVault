using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// A photon source placed in the world. Its laser runs straight out of the muzzle and lights a
// slit gate only if it actually hits it. Works on any model: aim a Muzzle child down the barrel.
// Fired from anywhere with its toggle key, or by using it (E), which charges for a moment first so
// the player has time to look away. While carried it shows a faint aim line that fires nothing.
[AddComponentMenu("Wave Duality/Photon Emitter")]
public class PhotonEmitter : MonoBehaviour, IInteractable, IResettable
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

    [Tooltip("How much brighter than white the laser is drawn. Above 1 it blooms when the camera " +
             "has post-processing, which is what makes it read as light rather than a green line.")]
    [SerializeField] private float laserGlow = 2.5f;

    [Header("Aiming")]
    [Tooltip("While carried, and while charging, a faint line shows where the laser will go. It " +
             "fires nothing and lights no gate, so aiming never counts as firing.")]
    [SerializeField] private bool showAimPreview = true;

    [Tooltip("Seconds between using the emitter and it firing, so there is time to look away. " +
             "Using it means looking at it, and it points at the slits, so this must cover a " +
             "full turn: the slits count as seen anywhere on screen, and for a moment after.")]
    [SerializeField] private float chargeTime = 2f;

    [Tooltip("Thickness of the aim line.")]
    [SerializeField] private float previewWidth = 0.008f;

    [Range(0f, 1f)]
    [Tooltip("How see-through the aim line is, against the full laser.")]
    [SerializeField] private float previewAlpha = 0.35f;

    // Shared by every emitter, which is safe because Update never runs two at once.
    private static readonly RaycastHit[] hits = new RaycastHit[16];

    private LineRenderer line;
    private Material lineMaterial;
    private SlitGate litGate;

    private ObjectPhysics prop;
    private GrabInteract grabber;
    private float nextGrabberSearch;
    private float chargeEnd = -1f;

    // Settable so a switch or trigger can drive the laser as well as the key.
    public bool IsOn { get; set; }
    public bool IsCharging => chargeEnd >= 0f;
    public SlitGate LitGate => litGate;

    public string Status
    {
        get
        {
            if (IsCharging) return "charging";
            if (!IsOn) return "laser off";

            return litGate == null ? "laser on - no gate in its path" : litGate.name + ": " + litGate.Status;
        }
    }

    private Transform Muzzle => muzzle != null ? muzzle : transform;

    void Awake()
    {
        IsOn = startOn;
        prop = GetComponent<ObjectPhysics>();

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

        if (IsCharging && Time.time >= chargeEnd)
        {
            chargeEnd = -1f;
            IsOn = true;
        }

        litGate = null;

        if (!IsOn)
        {
            if (showAimPreview && (IsCharging || IsCarried)) DrawPreview();
            else line.enabled = false;

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
        if (litGate != null) litGate.Illuminate(from.position);

        SetLineLook(laserWidth, laserColor);
        line.enabled = true;
        line.SetPosition(0, from.position);
        line.SetPosition(1, end);
    }

    // The same straight line the laser will take, but faint, and it lights nothing.
    private void DrawPreview()
    {
        Transform from = Muzzle;
        Vector3 end = from.position + from.forward * range;

        if (FirstHit(from.position, from.forward, out RaycastHit hit)) end = hit.point;

        // Blinks while charging, so it reads as "about to fire" rather than "aiming".
        float alpha = previewAlpha;

        if (IsCharging) alpha *= Mathf.PingPong(Time.time * 6f, 1f) > 0.5f ? 1.6f : 0.4f;

        SetLineLook(previewWidth, new Color(laserColor.r, laserColor.g, laserColor.b, Mathf.Clamp01(alpha)));
        line.enabled = true;
        line.SetPosition(0, from.position);
        line.SetPosition(1, end);
    }

    private void SetLineLook(float width, Color color)
    {
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
    }

    // Looked up lazily and retried once a second, the project's usual self-heal for a player
    // that may load after this.
    private bool IsCarried
    {
        get
        {
            if (prop == null) return false;

            if (grabber == null)
            {
                if (Time.time < nextGrabberSearch) return false;

                nextGrabberSearch = Time.time + 1f;
                grabber = FindAnyObjectByType<GrabInteract>();

                if (grabber == null) return false;
            }

            return grabber.Held == prop;
        }
    }

    private void ReadToggle()
    {
        Keyboard keyboard = Keyboard.current;

        // Typing a q into the console must not toggle every laser, and neither must a paused game.
        if (keyboard == null || toggleKey == Key.None || DevConsole.BlocksInput || PauseMenu.IsPaused) return;

        if (toggleMode)
        {
            if (keyboard[toggleKey].wasPressedThisFrame)
            {
                chargeEnd = -1f;
                IsOn = !IsOn;
            }
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

    // ---- Interact (E) -----------------------------------------------------------------------

    // While charging the player is still looking at it, and so at the slits it points to.
    public string Prompt => IsOn ? "Stop emitter" : (IsCharging ? "Cancel    -    look away before it fires!" : "Fire emitter");

    // Not while carried: the player would be looking straight down its barrel at whatever it hits.
    public bool CanInteract => !IsCarried;

    public void Interact(PlayerInteractor interactor)
    {
        if (IsOn)
        {
            IsOn = false;
            return;
        }

        chargeEnd = IsCharging ? -1f : Time.time + Mathf.Max(0f, chargeTime);
    }

    // ---- Checkpoint reset -------------------------------------------------------------------

    public void CaptureState()
    {
    }

    // Off, whatever it was doing, so a reset never leaves photons running into a fresh puzzle.
    public void RestoreState()
    {
        IsOn = false;
        chargeEnd = -1f;
        litGate = null;

        if (line != null) line.enabled = false;
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

        // Tint, not vertex colour, carries the extra brightness: vertex colours are clamped to 1.
        // Alpha stays 1 so the faint aim line keeps its own transparency.
        float glow = Mathf.Max(1f, laserGlow);
        lineMaterial.color = new Color(glow, glow, glow, 1f);

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
