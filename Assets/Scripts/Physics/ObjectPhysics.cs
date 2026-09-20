using UnityEngine;

/// <summary>
/// How a carried prop is oriented. Declared here rather than inside a MonoBehaviour so both
/// the grabber and the prop can refer to it; a plain enum has no file-name requirement.
/// </summary>
public enum HoldOrientation
{
    /// <summary>Locked to the view and snappy. Best for anything that must be placed precisely.</summary>
    Rigid,

    /// <summary>Locked to the view, but heavy props turn sluggishly and settle behind you.</summary>
    RigidWithSag,

    /// <summary>Rotation left to physics. Hangs and swings from wherever it was grabbed.</summary>
    Dangle
}

/// <summary>
/// Marks a rigidbody as a physics prop and describes how its weight should feel. It keeps
/// no state about being carried: it only answers questions that <see cref="GrabInteract"/>
/// asks, so the same component works whether the prop is held, thrown or just sat there.
///
/// Add it to anything with a Rigidbody that the player should be able to pick up.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[AddComponentMenu("Physics/Object Physics")]
public class ObjectPhysics : MonoBehaviour
{
    [Tooltip("Untick for props that are physical but must not be picked up.")]
    [SerializeField] private bool grabbable = true;

    [Header("Weight")]
    [Tooltip("Mass at or below this carries with no penalty at all.")]
    [SerializeField] private float lightMass = 2f;

    [Tooltip("Mass at which the carry penalty is at its worst. Anything heavier feels the same.")]
    [SerializeField] private float heavyMass = 50f;

    [Range(0.05f, 1f)]
    [Tooltip("Player walk speed while carrying something at Heavy Mass.")]
    [SerializeField] private float minSpeedMultiplier = 0.35f;

    [Range(0.05f, 1f)]
    [Tooltip("Player jump height while carrying something at Heavy Mass.")]
    [SerializeField] private float minJumpMultiplier = 0.85f;

    [Header("Carrying")]
    [Tooltip("Use this prop's own orientation mode instead of the grabber's default. " +
             "A ladder wants Dangle while a puzzle block wants Rigid.")]
    [SerializeField] private bool overrideOrientation;

    [SerializeField] private HoldOrientation orientation = HoldOrientation.Rigid;

    [Header("Throwing")]
    [Tooltip("Impulse applied on throw. It is divided by mass, so one value gives a light " +
             "prop a fast exit and a heavy one a short lob without any extra tuning.")]
    [SerializeField] private float throwImpulse = 12f;

    [Header("Gravity")]
    [Tooltip("Multiplier on Unity's gravity. The project falls at -9.81 while the player " +
             "controller uses -22, so 2.24 makes this prop drop at the same rate the " +
             "player does. Lower it for floaty props, raise it for dense ones.")]
    [SerializeField] private float gravityScale = 2.24f;

    private Rigidbody body;

    public Rigidbody Body => body;
    public bool Grabbable => grabbable;
    public float ThrowImpulse => throwImpulse;
    public float Mass => body != null ? body.mass : 1f;
    public bool OverridesOrientation => overrideOrientation;
    public HoldOrientation Orientation => orientation;

    /// <summary>Walk speed multiplier to hand to Movement while this is being carried.</summary>
    public float CarrySpeedMultiplier => WeightLerp(minSpeedMultiplier);

    /// <summary>Jump height multiplier to hand to Movement while this is being carried.</summary>
    public float CarryJumpMultiplier => WeightLerp(minJumpMultiplier);

    void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        // useGravity is switched off while the prop is held, which doubles as the signal
        // to stop adding to it. Nothing here runs during a carry.
        if (body == null || !body.useGravity || Mathf.Approximately(gravityScale, 1f))
        {
            return;
        }

        // Acceleration, not Force: gravity has to be mass independent or a heavy crate
        // and a light one would stop falling at the same rate.
        body.AddForce(Physics.gravity * (gravityScale - 1f), ForceMode.Acceleration);
    }

    // InverseLerp clamps to 0..1, so masses beyond heavyMass sit at the worst penalty
    // rather than running away into negative multipliers.
    private float WeightLerp(float heaviestValue)
    {
        if (body == null)
        {
            return 1f;
        }

        return Mathf.Lerp(1f, heaviestValue, Mathf.InverseLerp(lightMass, heavyMass, body.mass));
    }
}
