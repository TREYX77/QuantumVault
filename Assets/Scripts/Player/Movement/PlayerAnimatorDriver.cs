using UnityEngine;

// Feeds Movement state to the Animator.
[RequireComponent(typeof(Movement))]
public class PlayerAnimatorDriver : MonoBehaviour
{
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");

    [SerializeField] private Animator animator;

    private Movement movement;

    void Awake()
    {
        movement = GetComponent<Movement>();

        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (animator == null) return;

        animator.SetBool(IsGroundedHash, movement.IsGrounded);

        bool isRunning = movement.IsGrounded && movement.HasMovementInput;
        animator.SetBool(IsRunningHash, isRunning);
        
        animator.SetFloat("VerticalVelocity", movement.VerticalVelocity);
    }
}
