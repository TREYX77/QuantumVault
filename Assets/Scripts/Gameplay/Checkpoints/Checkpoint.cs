using UnityEngine;

// A save point at a room's entrance. Walking through its trigger makes it the current
// checkpoint, and a reset puts the player back here, facing the way the arrow points.
[AddComponentMenu("Puzzle/Checkpoint")]
[RequireComponent(typeof(BoxCollider))]
[DisallowMultipleComponent]
public class Checkpoint : MonoBehaviour
{
    [Tooltip("Shown in the Scene view and the console, so rooms are easy to tell apart.")]
    [SerializeField] private string roomName = "Room";

    [Tooltip("The checkpoint used before the player has walked through any other. Tick it on " +
             "the one at the spawn. Without one, the player's starting spot is used.")]
    [SerializeField] private bool isLevelStart;

    private BoxCollider volume;

    public string RoomName => roomName;
    public bool IsLevelStart => isLevelStart;

    // Where the player stands after a reset: the bottom of the volume, so they land on the floor.
    public Vector3 SpawnPosition
    {
        get
        {
            BoxCollider box = volume != null ? volume : GetComponent<BoxCollider>();

            if (box == null) return transform.position;

            Vector3 bottom = box.center - Vector3.up * box.size.y * 0.5f;

            return transform.TransformPoint(bottom) + Vector3.up * 0.05f;
        }
    }

    public float SpawnYaw => Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized).eulerAngles.y;

    void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(4f, 3f, 1f);
        box.center = new Vector3(0f, 1.5f, 0f);
    }

    void Awake()
    {
        volume = GetComponent<BoxCollider>();

        if (!volume.isTrigger)
        {
            Debug.LogWarning($"Checkpoint '{roomName}' needs its collider set to Is Trigger. Fixing it for this run.", this);
            volume.isTrigger = true;
        }
    }

    void Start()
    {
        CheckpointManager.Instance.Register(this);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;

        CheckpointManager.Instance.Reach(this);
    }

    private static bool IsPlayer(Collider other)
    {
        return other.GetComponentInParent<CharacterController>() != null || other.CompareTag("Player");
    }

    // Always drawn: the volume, and an arrow for the way a reset player will face.
    void OnDrawGizmos()
    {
        BoxCollider box = GetComponent<BoxCollider>();

        if (box == null) return;

        bool current = Application.isPlaying && CheckpointManager.HasInstance && CheckpointManager.Instance.Current == this;
        Color colour = current ? new Color(0.3f, 1f, 0.45f) : (isLevelStart ? new Color(1f, 0.85f, 0.3f) : new Color(0.4f, 0.8f, 1f));

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(colour.r, colour.g, colour.b, 0.15f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = colour;
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.matrix = Matrix4x4.identity;

        Vector3 from = SpawnPosition + Vector3.up * 0.1f;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 to = from + forward * 1.5f;
        Vector3 side = Vector3.Cross(Vector3.up, forward) * 0.3f;

        Gizmos.DrawLine(from, to);
        Gizmos.DrawLine(to, to - forward * 0.4f + side);
        Gizmos.DrawLine(to, to - forward * 0.4f - side);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * (box.center.y + box.size.y * 0.5f + 0.3f),
                                  (isLevelStart ? "Start: " : "Checkpoint: ") + roomName);
#endif
    }
}
