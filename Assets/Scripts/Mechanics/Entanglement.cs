using System.Collections.Generic;
using UnityEngine;

public struct EntanglementPose
{
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;

    public static EntanglementPose Identity => new EntanglementPose
    {
        position = Vector3.zero,
        rotation = Quaternion.identity,
        scale = Vector3.one
    };

    public static float Difference(EntanglementPose a, EntanglementPose b)
    {
        return Vector3.Distance(a.position, b.position)
             + Quaternion.Angle(a.rotation, b.rotation) * Mathf.Deg2Rad
             + Vector3.Distance(a.scale, b.scale);
    }
}

public class EntanglementGroup
{
    private readonly List<Entanglement> members = new List<Entanglement>();
    private EntanglementPose agreed = EntanglementPose.Identity;
    private bool hasAgreed;
    private float lastSolvedTime = -1f;
    private Entanglement driver;
    private Entanglement lastHeld;

    private const float DriverTakeover = 2f;

    // Steps a partner must lag in a row before it holds a carried prop back. A crate pressed
    // into the floor dips for single steps, and reacting to those yanked it out of the hand.
    private const int StuckAfterSteps = 3;

    private const float TakeoverMargin = 0.05f;

    public List<Entanglement> Members => members;
    public bool IsEmpty => members.Count == 0;

    public void Add(Entanglement member)
    {
        if (!members.Contains(member)) members.Add(member);
    }

    public void Remove(Entanglement member)
    {
        members.Remove(member);
    }

    // Forgets the shared pose so the next step agrees on wherever the members now are. For a
    // checkpoint that has just put them all back, which would otherwise read as a huge jump.
    public void Resync()
    {
        hasAgreed = false;
        driver = null;
        lastHeld = null;

        foreach (Entanglement member in members)
        {
            if (member == null) continue;

            member.Stuck = false;
            member.StrainSteps = 0;
        }
    }

    public void Solve(float physicsTime)
    {

        if (lastSolvedTime == physicsTime) return;
        lastSolvedTime = physicsTime;

        members.RemoveAll(m => m == null);

        if (members.Count == 0) return;

        if (members.Count == 1 || !hasAgreed)
        {
            agreed = members[0].CurrentOffset;
            hasAgreed = true;

            if (members.Count == 1) return;
        }

        // The carried prop always leads. Otherwise the others would pin it to the shared pose
        // before it moved far enough to take over, and the grab would do nothing.
        foreach (Entanglement member in members)
        {
            if (!member.IsHeld) continue;

            driver = member;
            SolveHeld(member);
            return;
        }

        lastHeld = null;

        Entanglement best = null;
        float largest = 0f;
        float driverDifference = 0f;

        foreach (Entanglement member in members)
        {
            float difference = EntanglementPose.Difference(member.CurrentOffset, agreed);

            if (member == driver) driverDifference = difference;

            if (difference > largest)
            {
                largest = difference;
                best = member;
            }
        }

        bool rivalTakesOver = best != driver
                           && largest > driverDifference * DriverTakeover
                           && largest > TakeoverMargin;

        if (driver != null && members.Contains(driver) && !rivalTakesOver)
        {
            best = driver;
            largest = driverDifference;
        }

        driver = best;

        if (driver == null) return;

        if (largest < driver.MoveThreshold)
        {
            foreach (Entanglement member in members)
            {
                if (member != driver) member.DriveTo(agreed);
            }

            // At rest nothing is pushing against anything, so a carried prop that stopped at a
            // block must not stay stuck once its partner settles.
            driver.SetBlocked(false);
            return;
        }

        EntanglementPose candidate = driver.CurrentOffset;

        bool strained = false;

        foreach (Entanglement member in members)
        {
            if (member != driver && member.IsStrained) strained = true;
        }

        if (strained)
        {
            // Held at the last pose everyone reached. Zeroing velocity instead let gravity rebuild
            // a little speed each step, so a prop that fell off a ledge sank in slow motion.
            driver.DriveTo(agreed);
            driver.SetBlocked(true);

            foreach (Entanglement member in members)
            {
                if (member != driver) member.DriveTo(agreed);
            }

            return;
        }

        foreach (Entanglement member in members)
        {
            if (member != driver) member.DriveTo(candidate);
        }

        agreed = candidate;
        driver.SetBlocked(false);
    }

    // The hand moves the carried prop and the others follow. A partner that cannot keep up
    // limits the prop to where that partner got, but only in the direction it is stuck, so the
    // prop can still slide along or pull back from the obstacle instead of freezing.
    private void SolveHeld(Entanglement held)
    {
        EntanglementPose candidate = held.CurrentOffset;
        bool blocked = false;

        // A fresh grab starts free, whatever the last one ended on.
        if (held != lastHeld)
        {
            lastHeld = held;

            foreach (Entanglement member in members)
            {
                member.Stuck = false;
                member.StrainSteps = 0;
            }
        }

        foreach (Entanglement member in members)
        {
            if (member == held) continue;

            // Judged on last step's command, before this step's replaces it.
            member.StrainSteps = member.IsStrained ? member.StrainSteps + 1 : 0;

            if (member.StrainSteps >= StuckAfterSteps) member.Stuck = true;
            if (member.Stuck) member.Stuck = held.HoldBack(member);
            if (member.Stuck) blocked = true;

            // Always the hand's pose, so a stuck partner keeps pressing and stays stuck for as
            // long as the hand pushes, rather than flickering free every other step.
            member.DriveTo(candidate);
        }

        // Kept in step while carried, so letting go never snaps the prop back to an old pose.
        agreed = candidate;
        held.SetBlocked(blocked);
    }
}

[DefaultExecutionOrder(100)]
[AddComponentMenu("Mechanics/Entanglement")]
public class Entanglement : MonoBehaviour
{
    [Tooltip("Objects sharing this id are bound together. Case sensitive.")]
    [SerializeField] private string linkId = "Link A";

    [Tooltip("Optional. Offsets are measured from this pose. Left empty, the object's own " +
             "starting pose is used, which is what you want for props in separate rooms.")]
    [SerializeField] private Transform anchor;

    [Header("Sync")]
    [SerializeField] private bool syncPosition = true;
    [SerializeField] private bool syncRotation = true;
    [SerializeField] private bool syncScale = true;

    [Header("Following")]
    [Tooltip("Top speed a follower may use to keep up. Followers are driven by velocity, " +
             "never teleported, so walls still stop them.")]
    [SerializeField] private float followSpeed = 60f;

    [Range(0f, 0.95f)]
    [Tooltip("Smoothing on the follow. 0 means followers land exactly on target each step, " +
             "which is what identical motion needs. Raise it only for a deliberately soft link.")]
    [SerializeField] private float followDamping;

    [Tooltip("Angular cap while entangled. Unity defaults rigidbodies to 7 rad/s, far below " +
             "what matching a fast spin needs, and the clamp is silent.")]
    [SerializeField] private float maxAngularVelocity = 50f;

    [Tooltip("How far a follower may fall behind before the whole link counts as blocked. " +
             "Small is rigid, large is forgiving.")]
    [SerializeField] private float linkTolerance = 0.08f;

    [Tooltip("How far a follower's rotation may fall behind, in degrees, before the link counts " +
             "as blocked. Without it a partner wedged against the floor fights a rolling prop.")]
    [SerializeField] private float rotationTolerance = 10f;

    [Tooltip("Offset changes smaller than this are treated as noise.")]
    [SerializeField] private float moveThreshold = 0.001f;

    [Header("Gizmo")]
    [SerializeField] private Color gizmoColor = new Color(0.6f, 0.4f, 1f, 0.9f);

    private static readonly Dictionary<string, EntanglementGroup> groups =
        new Dictionary<string, EntanglementGroup>();

    private EntanglementGroup group;
    private string registeredId;

    private Rigidbody body;
    private ObjectPhysics objectPhysics;

    private Vector3 startPosition;
    private Quaternion startRotation;
    private Vector3 startScale;

    private Vector3 commandedPosition;
    private Quaternion commandedRotation = Quaternion.identity;
    private bool hasCommand;
    private float commandTime;

    // How fast a partner that cannot follow may draw a carried prop back. Gentle, so the
    // correction never pulls the prop out of the player's hand.
    private const float HoldBackSpeed = 4f;

    // Everything bound to this object, itself included. Empty while it is not in a link.
    public IReadOnlyList<Entanglement> Linked =>
        group != null ? group.Members : (IReadOnlyList<Entanglement>)System.Array.Empty<Entanglement>();

    public void Resync()
    {
        group?.Resync();
    }

    public float MoveThreshold => moveThreshold;
    public float LinkTolerance => linkTolerance;
    public Rigidbody Body => body;
    public bool IsBlocked { get; private set; }

    // Set by GrabInteract while the player carries this prop, so it leads the link.
    public bool IsHeld { get; set; }

    // Latched by the group while this partner cannot follow a carried prop.
    public bool Stuck { get; set; }

    // How many steps in a row this partner has lagged behind a carried prop.
    public int StrainSteps { get; set; }

    // Fell behind its last command in place or in angle, so something is stopping it.
    public bool IsStrained
    {
        get
        {
            // Only last step's command counts. An old one left over from a pose the link has
            // since given up on would read as lag the moment a new grab started.
            if (!hasCommand || Time.fixedTime - commandTime > Time.fixedDeltaTime * 1.5f) return false;
            if (CommandResidual > linkTolerance) return true;

            return syncRotation && Quaternion.Angle(transform.rotation, commandedRotation) > rotationTolerance;
        }
    }

    private Vector3 AnchorPosition => anchor != null ? anchor.position : startPosition;
    private Quaternion AnchorRotation => anchor != null ? anchor.rotation : startRotation;

    public EntanglementPose CurrentOffset
    {
        get
        {
            Quaternion inverse = Quaternion.Inverse(AnchorRotation);

            return new EntanglementPose
            {
                position = inverse * (transform.position - AnchorPosition),
                rotation = inverse * transform.rotation,
                scale = SafeDivide(transform.localScale, startScale)
            };
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        groups.Clear();
    }

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        objectPhysics = GetComponent<ObjectPhysics>();

        startPosition = transform.position;
        startRotation = transform.rotation;
        startScale = transform.localScale;

        if (body != null) body.maxAngularVelocity = Mathf.Max(body.maxAngularVelocity, maxAngularVelocity);
    }

    void OnEnable()
    {
        Register();
    }

    void OnDisable()
    {
        Unregister();
    }

    void FixedUpdate()
    {

        if (registeredId != linkId)
        {
            Unregister();
            Register();
        }

        group?.Solve(Time.fixedTime);
    }

    public float CommandResidual =>
        hasCommand && syncPosition ? Vector3.Distance(transform.position, commandedPosition) : 0f;

    public void DriveTo(EntanglementPose offset)
    {
        if (syncScale) transform.localScale = Vector3.Scale(startScale, offset.scale);

        Vector3 targetPosition = AnchorPosition + AnchorRotation * offset.position;
        Quaternion targetRotation = AnchorRotation * offset.rotation;

        commandedPosition = targetPosition;
        commandedRotation = targetRotation;
        commandTime = Time.fixedTime;
        hasCommand = true;

        if (body == null)
        {
            if (syncPosition) transform.position = targetPosition;
            if (syncRotation) transform.rotation = targetRotation;
            return;
        }

        if (syncPosition)
        {
            Vector3 delta = targetPosition - body.position;
            Vector3 desired = Vector3.ClampMagnitude(delta / Time.fixedDeltaTime, followSpeed);

           
            if (body.useGravity)
            {
                float scale = objectPhysics != null ? objectPhysics.GravityScale : 1f;

                desired -= Physics.gravity * (scale * Time.fixedDeltaTime);
            }

            body.linearVelocity = Vector3.Lerp(body.linearVelocity, desired, 1f - followDamping);
        }

        if (syncRotation) DriveRotation(targetRotation);
    }

    // Called on the carried prop after the hand set its velocity. Strips only the motion that
    // would carry it further from where the partner got, and draws it back to within half the
    // tolerance, so it stops at the obstacle but stays free to slide or back away. Returns
    // whether the partner still holds it back.
    public bool HoldBack(Entanglement partner)
    {
        if (body == null) return false;

        EntanglementPose reached = partner.CurrentOffset;
        bool pushing = false;

        if (syncPosition)
        {
            Vector3 gap = body.position - (AnchorPosition + AnchorRotation * reached.position);
            float distance = gap.magnitude;

            if (distance > 0.0001f)
            {
                Vector3 away = gap / distance;
                Vector3 velocity = body.linearVelocity;
                float outward = Vector3.Dot(velocity, away);

                if (outward > 0f)
                {
                    velocity -= away * outward;
                    pushing = true;
                }

                float excess = distance - linkTolerance * 0.5f;

                if (excess > 0f) velocity -= away * Mathf.Min(excess / Time.fixedDeltaTime, HoldBackSpeed);

                body.linearVelocity = velocity;
            }
        }

        if (syncRotation)
        {
            Quaternion reachedRotation = AnchorRotation * reached.rotation;

            // A hard stop: the prop turns no further than the partner managed.
            if (Quaternion.Angle(body.rotation, reachedRotation) > rotationTolerance * 0.5f)
            {
                DriveRotation(Quaternion.RotateTowards(reachedRotation, body.rotation, rotationTolerance * 0.5f));
                pushing = true;
            }
        }

        // Free again once the hand eases off and the partner has caught up.
        return pushing || partner.IsStrained;
    }

    public void SetBlocked(bool blocked)
    {
        IsBlocked = blocked;
    }

    private void DriveRotation(Quaternion targetRotation)
    {
        Quaternion difference = targetRotation * Quaternion.Inverse(body.rotation);

        difference.ToAngleAxis(out float angle, out Vector3 axis);

        if (axis.sqrMagnitude < 0.0001f || float.IsNaN(axis.x) || float.IsInfinity(axis.x))
        {
            body.angularVelocity = Vector3.zero;
            return;
        }

        if (angle > 180f) angle -= 360f;

        if (Mathf.Abs(angle) < 0.01f)
        {
            body.angularVelocity = Vector3.zero;
            return;
        }

        Vector3 angular = axis.normalized * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime);

        body.angularVelocity = Vector3.Lerp(body.angularVelocity, angular, 1f - followDamping);
    }

    private void Register()
    {
        registeredId = linkId;

        if (string.IsNullOrWhiteSpace(linkId)) return;

        if (!groups.TryGetValue(linkId, out group))
        {
            group = new EntanglementGroup();
            groups[linkId] = group;
        }

        group.Add(this);
    }

    private void Unregister()
    {
        if (group == null) return;

        group.Remove(this);

        if (group.IsEmpty && registeredId != null) groups.Remove(registeredId);

        group = null;
    }

    private static Vector3 SafeDivide(Vector3 value, Vector3 divisor)
    {
        return new Vector3(
            Mathf.Approximately(divisor.x, 0f) ? 1f : value.x / divisor.x,
            Mathf.Approximately(divisor.y, 0f) ? 1f : value.y / divisor.y,
            Mathf.Approximately(divisor.z, 0f) ? 1f : value.z / divisor.z);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = gizmoColor;

        Vector3 origin = anchor != null
            ? anchor.position
            : (Application.isPlaying ? startPosition : transform.position);

        Gizmos.DrawLine(origin, transform.position);
        Gizmos.DrawWireSphere(origin, 0.12f);

        if (!Application.isPlaying || group == null) return;

        foreach (Entanglement member in group.Members)
        {
            if (member != null && member != this) Gizmos.DrawLine(transform.position, member.transform.position);
        }
    }
}
