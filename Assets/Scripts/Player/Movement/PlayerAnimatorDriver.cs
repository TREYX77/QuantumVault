using UnityEngine;

// Feeds Movement state to the Animator. Running plays only while grounded and moving.
[RequireComponent(typeof(Movement))]
public class PlayerAnimatorDriver : MonoBehaviour
{
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");

    [SerializeField] private Animator animator;

    [Tooltip("Horizontal speed (x/z) above which the player counts as moving.")]
    [SerializeField] private float runThreshold = 0.1f;

    private Movement movement;

    void Awake()
    {
        movement = GetComponent<Movement>();

        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (animator == null) return;

        // Speed is the horizontal (x/z) magnitude, so any direction or a diagonal counts.
        bool isRunning = movement.IsGrounded && movement.Speed > runThreshold;

        animator.SetBool(IsRunningHash, isRunning);
    }
}