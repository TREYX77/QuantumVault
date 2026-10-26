using UnityEngine;

// A hinged bridge lowered by weight. Props stacked on a sprung tray push it down, and the deck's
// hinge follows how far it sank: more weight, lower bridge. Goes on the deck's Rigidbody.
[AddComponentMenu("Puzzle/Weight Drawbridge")]
[RequireComponent(typeof(HingeJoint))]
public class WeightDrawbridge : MonoBehaviour
{
    [Header("Tray")]
    [Tooltip("The platform props are stacked on. It needs a Rigidbody; its joint is set up here.")]
    [SerializeField] private Rigidbody tray;

    [Tooltip("How far the tray sinks under the full load, in metres.")]
    [SerializeField] private float trayTravel = 0.5f;

    [Tooltip("Mass on the tray, in kg, that lowers the bridge all the way. Set weight, not spring " +
             "constants: the spring is worked out from this.")]
    [SerializeField] private float massToLower = 15f;

    [Tooltip("Gravity multiplier props fall with. Matches ObjectPhysics' default, so a 5 kg crate " +
             "presses the tray as hard as it actually falls.")]
    [SerializeField] private float propGravityScale = 2.24f;

    [Header("Bridge")]
    [Tooltip("Hinge angle with an empty tray, in degrees from the pose the deck was built in.")]
    [SerializeField] private float raisedAngle = 80f;

    [Tooltip("Hinge angle under the full load. 0 is the pose the deck was built in.")]
    [SerializeField] private float loweredAngle;

    [Tooltip("How hard the hinge drives toward its target. The deck's own weight is cancelled, " +
             "so this only resists what pushes on it, such as a crate dropped onto it.")]
    [SerializeField] private float spring = 12000f;

    [Tooltip("Stops the deck bouncing when it reaches its target.")]
    [SerializeField] private float damper = 1500f;

    private HingeJoint hinge;
    private Rigidbody deck;
    private Vector3 trayTop;

    // 0 with an empty tray, 1 once the tray has sunk its full travel.
    public float Load { get; private set; }

    void Awake()
    {
        hinge = GetComponent<HingeJoint>();
        deck = GetComponent<Rigidbody>();

        // The deck rests on the far ledge, so the limit sits just past lowered, not on it.
        hinge.useLimits = true;
        hinge.limits = new JointLimits
        {
            min = Mathf.Min(raisedAngle, loweredAngle) - 2f,
            max = Mathf.Max(raisedAngle, loweredAngle) + 2f
        };

        hinge.useSpring = true;
        SetTarget(raisedAngle);

        if (tray != null) SetUpTray();
    }

    void FixedUpdate()
    {
        // The deck's own weight would drag it well below every target but the flat one, so it
        // is cancelled around the hinge and the spring only has to hold the angle.
        if (deck != null && deck.useGravity)
        {
            Vector3 pivot = transform.TransformPoint(hinge.anchor);
            Vector3 weightTorque = Vector3.Cross(deck.worldCenterOfMass - pivot, Physics.gravity * deck.mass);

            deck.AddTorque(-weightTorque);
        }

        if (tray == null) return;

        Load = Mathf.Clamp01((trayTop.y - tray.position.y) / Mathf.Max(0.01f, trayTravel));
        SetTarget(Mathf.Lerp(raisedAngle, loweredAngle, Load));
    }

    private void SetTarget(float angle)
    {
        hinge.spring = new JointSpring { spring = spring, damper = damper, targetPosition = angle };
    }

    // Slides only up and down, sprung so massToLower sinks it exactly trayTravel.
    private void SetUpTray()
    {
        trayTop = tray.position;

        // The tray's own weight would read as load, so only what sits on it counts.
        tray.useGravity = false;

        ConfigurableJoint joint = tray.GetComponent<ConfigurableJoint>();
        if (joint == null) joint = tray.gameObject.AddComponent<ConfigurableJoint>();

        joint.connectedBody = null;
        joint.xMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Locked;
        joint.yMotion = ConfigurableJointMotion.Limited;
        joint.angularXMotion = ConfigurableJointMotion.Locked;
        joint.angularYMotion = ConfigurableJointMotion.Locked;
        joint.angularZMotion = ConfigurableJointMotion.Locked;
        joint.linearLimit = new SoftJointLimit { limit = trayTravel };

        float stiffness = massToLower * Physics.gravity.magnitude * propGravityScale / Mathf.Max(0.01f, trayTravel);
        float loadedMass = massToLower + tray.mass;

        joint.yDrive = new JointDrive
        {
            positionSpring = stiffness,
            positionDamper = 2f * Mathf.Sqrt(stiffness * loadedMass) * 0.7f,
            maximumForce = float.MaxValue
        };
        joint.targetPosition = Vector3.zero;
    }

    void OnDrawGizmosSelected()
    {
        if (tray == null) return;

        // The tray's travel, so the load range is visible while placing it.
        Vector3 top = Application.isPlaying ? trayTop : tray.transform.position;

        Gizmos.color = new Color(0.25f, 0.6f, 1f, 0.9f);
        Gizmos.DrawLine(top, top + Vector3.down * trayTravel);
        Gizmos.DrawWireSphere(top + Vector3.down * trayTravel, 0.1f);
    }
}
