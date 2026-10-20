using System;
using System.Collections.Generic;
using UnityEngine;

// Remembers the last checkpoint and how the level looked when the player reached it, and puts
// both back on a reset. One per scene, built on first use, so a level only needs checkpoints.
// The state is what the level looked like on arrival, not at level start, so resetting in a
// later room never undoes puzzles already solved in the rooms before it.
[DisallowMultipleComponent]
public class CheckpointManager : MonoBehaviour
{
    private struct PropState
    {
        public Rigidbody Body;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    private static CheckpointManager instance;

    private readonly List<Checkpoint> checkpoints = new List<Checkpoint>();
    private readonly List<PropState> props = new List<PropState>();
    private readonly List<IResettable> resettables = new List<IResettable>();

    private Movement player;
    private Vector3 startPosition;
    private float startYaw;
    private bool started;

    public Checkpoint Current { get; private set; }

    // The checkpoint just reached, for HUD messages or sounds.
    public event Action<Checkpoint> Reached;
    public event Action ResetPerformed;

    public static bool HasInstance => instance != null;

    public static CheckpointManager Instance
    {
        get
        {
            if (instance != null) return instance;

            instance = FindAnyObjectByType<CheckpointManager>();

            if (instance == null) instance = new GameObject("Checkpoint Manager").AddComponent<CheckpointManager>();

            return instance;
        }
    }

    // Statics survive play sessions when the domain reload is skipped.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);

            return;
        }

        instance = this;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // After every Awake and Start of the first frame, so props and mechanics have set themselves
    // up before the level-start snapshot is taken.
    void Start()
    {
        player = FindAnyObjectByType<Movement>();

        if (player != null)
        {
            startPosition = FeetOf(player);
            startYaw = player.transform.eulerAngles.y;
        }

        foreach (Checkpoint checkpoint in FindObjectsByType<Checkpoint>(FindObjectsSortMode.None))
        {
            Register(checkpoint);

            if (checkpoint.IsLevelStart && Current == null) Current = checkpoint;
        }

        TakeSnapshot();
        started = true;
    }

    public void Register(Checkpoint checkpoint)
    {
        if (checkpoint != null && !checkpoints.Contains(checkpoint)) checkpoints.Add(checkpoint);
    }

    public void Reach(Checkpoint checkpoint)
    {
        if (checkpoint == null || checkpoint == Current) return;

        Current = checkpoint;

        // A checkpoint touched in the very first frame is the level start; the Start
        // snapshot will cover it.
        if (started) TakeSnapshot();

        Debug.Log($"Checkpoint reached: {checkpoint.RoomName}", checkpoint);
        Reached?.Invoke(checkpoint);
    }

    public void ResetToCheckpoint()
    {
        if (player == null) player = FindAnyObjectByType<Movement>();

        // Drop whatever is carried first, or the grab would drag the prop away from its spot.
        GrabInteract grab = player != null ? player.GetComponentInChildren<GrabInteract>() : FindAnyObjectByType<GrabInteract>();

        if (grab == null) grab = FindAnyObjectByType<GrabInteract>();
        if (grab != null) grab.Release();

        // Mechanics first: a vanished crate has to be solid and unfrozen again before its pose
        // and velocity are set, or it would stay frozen where it was.
        foreach (IResettable resettable in resettables)
        {
            if (resettable is UnityEngine.Object unityObject && unityObject == null) continue;

            resettable.RestoreState();
        }

        foreach (PropState state in props)
        {
            Rigidbody body = state.Body;

            if (body == null) continue;

            body.position = state.Position;
            body.rotation = state.Rotation;
            body.transform.SetPositionAndRotation(state.Position, state.Rotation);

            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        Physics.SyncTransforms();

        if (player != null)
        {
            if (Current != null) player.Teleport(Current.SpawnPosition, Current.SpawnYaw);
            else player.Teleport(startPosition, startYaw);
        }

        Debug.Log($"Reset to checkpoint: {(Current != null ? Current.RoomName : "level start")}");
        ResetPerformed?.Invoke();
    }

    private void TakeSnapshot()
    {
        props.Clear();

        foreach (ObjectPhysics prop in FindObjectsByType<ObjectPhysics>(FindObjectsSortMode.None))
        {
            Rigidbody body = prop.GetComponent<Rigidbody>();

            if (body == null) continue;

            props.Add(new PropState { Body = body, Position = body.position, Rotation = body.rotation });
        }

        resettables.Clear();

        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour is IResettable resettable)
            {
                resettable.CaptureState();
                resettables.Add(resettable);
            }
        }
    }

    private static Vector3 FeetOf(Movement mover)
    {
        CharacterController controller = mover.GetComponent<CharacterController>();

        if (controller == null) return mover.transform.position;

        return mover.transform.TransformPoint(controller.center) - Vector3.up * controller.height * 0.5f;
    }
}
