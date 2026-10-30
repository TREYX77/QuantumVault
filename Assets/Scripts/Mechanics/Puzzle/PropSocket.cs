using UnityEngine;
using UnityEngine.Events;

// A slot one prop clicks into, such as a plank laid across a gap. Let go inside this object's
// trigger box, the prop snaps to this transform's pose and turns kinematic, so walking on it can
// never shove it off the ledges, and it can no longer be picked up. Either end may face either
// way: the nearer of the two flips is used. A checkpoint reset frees it again.
// The trigger box is the catch zone. Let it reach below the ledges, so a prop dropped a little
// short is still caught on its way into the gap.
[AddComponentMenu("Puzzle/Prop Socket")]
[RequireComponent(typeof(BoxCollider))]
[DisallowMultipleComponent]
public class PropSocket : MonoBehaviour, IResettable
{
    [Tooltip("The prop that fits. Its centre has to be inside this object's trigger box when it " +
             "is let go.")]
    [SerializeField] private Rigidbody prop;

    [Tooltip("Optional. A faint outline of the prop, shown while the slot is empty so the player " +
             "can see where it goes.")]
    [SerializeField] private Renderer ghost;

    [SerializeField] private UnityEvent onFilled;

    private BoxCollider zone;
    private ObjectPhysics propPhysics;
    private GrabInteract grabber;
    private float nextGrabberSearch;

    public bool IsFilled { get; private set; }

    // Leaves the size alone, so adding this to a box already fitted over a gap keeps its fit.
    void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    void Awake()
    {
        zone = GetComponent<BoxCollider>();
        zone.isTrigger = true;

        if (prop != null) propPhysics = prop.GetComponent<ObjectPhysics>();
    }

    void FixedUpdate()
    {
        if (IsFilled || prop == null || IsHeld || !Contains(prop.worldCenterOfMass)) return;

        Fill();
    }

    private void Fill()
    {
        // The turn that moves it least, so a plank laid either way round goes in without spinning.
        Quaternion target = transform.rotation;
        Quaternion flipped = target * Quaternion.Euler(0f, 180f, 0f);

        if (Quaternion.Angle(prop.rotation, flipped) < Quaternion.Angle(prop.rotation, target)) target = flipped;

        // Stilled before it turns kinematic, since a kinematic body's velocity cannot be set.
        prop.linearVelocity = Vector3.zero;
        prop.angularVelocity = Vector3.zero;
        prop.isKinematic = true;
        prop.transform.SetPositionAndRotation(transform.position, target);

        IsFilled = true;

        if (ghost != null) ghost.enabled = false;

        onFilled?.Invoke();
    }

    private bool Contains(Vector3 point)
    {
        Vector3 local = transform.InverseTransformPoint(point) - zone.center;
        Vector3 half = zone.size * 0.5f;

        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    // Looked up lazily and retried once a second, the project's usual self-heal for a player
    // that may load after this.
    private bool IsHeld
    {
        get
        {
            if (grabber == null)
            {
                if (Time.time < nextGrabberSearch) return false;

                nextGrabberSearch = Time.time + 1f;
                grabber = FindAnyObjectByType<GrabInteract>();

                if (grabber == null) return false;
            }

            return propPhysics != null && grabber.Held == propPhysics;
        }
    }

    // ---- Checkpoint reset -------------------------------------------------------------------

    public void CaptureState()
    {
    }

    // Free again, so the checkpoint can put the prop back where it was found. Mechanics reset
    // before props, so the prop is dynamic by the time it is moved home.
    public void RestoreState()
    {
        if (!IsFilled) return;

        IsFilled = false;

        if (prop != null) prop.isKinematic = false;
        if (ghost != null) ghost.enabled = true;
    }

    // Always drawn, so the catch zone is visible while laying out the gap.
    void OnDrawGizmos()
    {
        BoxCollider box = GetComponent<BoxCollider>();

        if (box == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.5f);
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.DrawLine(Vector3.back * 0.5f, Vector3.forward * 0.5f);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
