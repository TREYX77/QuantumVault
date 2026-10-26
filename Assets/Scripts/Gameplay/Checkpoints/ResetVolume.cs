using System;
using UnityEngine;

// What a reset volume reacts to. Each entry except AnyProp is the Unity tag of the same name,
// so a box needs the "Box" tag and a ball the "Ball" tag. Add an entry here, and the matching
// tag in Project Settings > Tags and Layers, to catch a new kind of object.
[Flags]
public enum ResetTargets
{
    None = 0,
    Player = 1 << 0,
    Box = 1 << 1,
    Ball = 1 << 2,

    // Anything the player can grab, whatever its tag, so an untagged prop is never lost.
    AnyProp = 1 << 3
}

// A pit or out-of-bounds volume. The player falling in puts the player and every prop back the
// way they were at the last checkpoint. A prop falling in returns on its own, so knocking a box
// into a chasm costs the box, not the player's progress.
[AddComponentMenu("Puzzle/Reset Volume")]
[RequireComponent(typeof(BoxCollider))]
[DisallowMultipleComponent]
public class ResetVolume : MonoBehaviour
{
    [Tooltip("What this volume catches. Player resets the whole level to the last checkpoint. " +
             "The others return only the object that fell in. Pick several at once.")]
    [SerializeField] private ResetTargets targets = ResetTargets.Player | ResetTargets.Box | ResetTargets.Ball;

    [Tooltip("Seconds before it can reset the level again, so one fall never triggers two resets.")]
    [SerializeField] private float cooldown = 0.5f;

    private float readyTime;

    void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(6f, 2f, 10f);
    }

    void Awake()
    {
        BoxCollider box = GetComponent<BoxCollider>();

        if (!box.isTrigger)
        {
            Debug.LogWarning($"Reset Volume '{name}' needs its collider set to Is Trigger. Fixing it for this run.", this);
            box.isTrigger = true;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other))
        {
            if (!Has(ResetTargets.Player) || Time.time < readyTime) return;

            readyTime = Time.time + cooldown;
            CheckpointManager.Instance.ResetToCheckpoint();
            return;
        }

        // The body, not the collider: a crate's collider is often a child mesh with no tag.
        Rigidbody body = other.attachedRigidbody;

        if (body == null || !Catches(body)) return;

        if (!CheckpointManager.Instance.ResetProp(body))
        {
            Debug.LogWarning($"Reset Volume '{name}' caught '{body.name}', but it has no checkpoint " +
                             "position to return to. Only props with ObjectPhysics are remembered.", body);
        }
    }

    private bool Catches(Rigidbody body)
    {
        if (Has(ResetTargets.AnyProp) && body.GetComponent<ObjectPhysics>() != null) return true;

        // String compare rather than CompareTag, which logs an error for a tag missing from the project.
        string tag = body.tag;

        foreach (ResetTargets target in TaggedTargets)
        {
            if (Has(target) && tag == target.ToString()) return true;
        }

        return false;
    }

    private static readonly ResetTargets[] TaggedTargets = { ResetTargets.Box, ResetTargets.Ball };

    private bool Has(ResetTargets target) => (targets & target) != 0;

    // Same test as Checkpoint, so anything that counts as the player there counts here.
    private static bool IsPlayer(Collider other)
    {
        return other.GetComponentInParent<CharacterController>() != null || other.CompareTag("Player");
    }

    // Always drawn, so a pit's safety net is visible while building the room.
    void OnDrawGizmos()
    {
        BoxCollider box = GetComponent<BoxCollider>();

        if (box == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.12f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
