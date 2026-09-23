using UnityEngine;

// Rigid: locked to view. RigidWithSag: heavy props lag. Dangle: hangs from the grip.
public enum HoldOrientation
{
    Rigid,
    RigidWithSag,
    Dangle
}

// Marks a rigidbody as a pickupable prop and describes how heavy it should feel.
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
    public float GravityScale => gravityScale;
    public bool OverridesOrientation => overrideOrientation;
    public HoldOrientation Orientation => orientation;
    public float CarrySpeedMultiplier => WeightLerp(minSpeedMultiplier);
    public float CarryJumpMultiplier => WeightLerp(minJumpMultiplier);

    void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        // useGravity is off while held, so this skips during a carry.
        if (body == null || !body.useGravity || Mathf.Approximately(gravityScale, 1f)) return;

        // Acceleration, not Force: gravity must not depend on mass.
        body.AddForce(Physics.gravity * (gravityScale - 1f), ForceMode.Acceleration);
    }

    private float WeightLerp(float heaviestValue)
    {
        if (body == null) return 1f;

        // InverseLerp clamps, so anything heavier sits at the worst penalty.
        return Mathf.Lerp(1f, heaviestValue, Mathf.InverseLerp(lightMass, heavyMass, body.mass));
    }
}
