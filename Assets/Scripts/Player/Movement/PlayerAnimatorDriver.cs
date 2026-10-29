using UnityEngine;

// Feeds Movement state to the Animator.
// The head and upper spine follow the view, so the body itself only ever turns at the hips.
[RequireComponent(typeof(Movement))]
public class PlayerAnimatorDriver : MonoBehaviour
{
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int VerticalVelocityHash = Animator.StringToHash("VerticalVelocity");

    // How far along the view the head aims. Close targets make the eyes visibly cross.
    private const float LookDistance = 10f;

    [SerializeField] private Animator animator;
    [SerializeField] private PlayerCamera playerCamera;

    [Header("Look")]
    [Tooltip("How much of the look the spine takes on.")]
    [SerializeField, Range(0f, 1f)] private float lookBodyWeight = 0.3f;

    [Tooltip("How much of the look the head takes on.")]
    [SerializeField, Range(0f, 1f)] private float lookHeadWeight = 0.8f;

    [Tooltip("0 lets the head turn without limit, 1 stops it turning. 0.5 caps it near 90 degrees, " +
             "which covers Movement's max look offset before the hips are dragged around.")]
    [SerializeField, Range(0f, 1f)] private float lookClamp = 0.5f;

    private Movement movement;

    void Awake()
    {
        movement = GetComponent<Movement>();

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<PlayerCamera>();
    }

    void Update()
    {
        if (animator == null) return;

        animator.SetBool(IsGroundedHash, movement.IsGrounded);

        bool isRunning = movement.IsGrounded && movement.HasMovementInput;
        animator.SetBool(IsRunningHash, isRunning);

        animator.SetFloat(VerticalVelocityHash, movement.VerticalVelocity);
    }

    // Needs IK Pass on the controller layer, and only reaches scripts on the Animator's own object.
    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || playerCamera == null) return;

        Vector3 eye = playerCamera.transform.position;

        animator.SetLookAtWeight(1f, lookBodyWeight, lookHeadWeight, 1f, lookClamp);
        animator.SetLookAtPosition(eye + playerCamera.LookRotation * Vector3.forward * LookDistance);
    }
}
