using UnityEngine;
using UnityEngine.InputSystem;

// Player movement. Simple mode is tight; Source mode carries momentum, like Half-Life 2.
[RequireComponent(typeof(CharacterController))]
public class Movement : MonoBehaviour
{
    public enum MovementMode
    {
        Simple,
        Source
    }

    // FaceLook: body follows the view. FaceLookWhenMoving: only while walking. Free: animator.
    public enum BodyTurnMode
    {
        FaceLook,
        FaceLookWhenMoving,
        Free
    }

    [Header("Mode")]
    [SerializeField] private MovementMode mode = MovementMode.Source;

    [Header("Walking")]
    [Tooltip("Top speed under your own power. In Source mode this is the wish speed, which " +
             "caps how fast you can push yourself, not how fast you can end up going.")]
    [SerializeField] private float moveSpeed = 5f;

    [Tooltip("Simple mode only. How quickly velocity is dragged to the target speed.")]
    [SerializeField] private float acceleration = 60f;

    [Header("Source Movement")]
    [Tooltip("sv_accelerate. Unitless multiplier scaled by wish speed.")]
    [SerializeField] private float groundAccelerate = 10f;

    [Tooltip("sv_airaccelerate. Drives how sharply air strafing builds speed.")]
    [SerializeField] private float airAccelerate = 10f;

    [Tooltip("sv_friction. Only ever applied while standing on the ground.")]
    [SerializeField] private float friction = 4f;

    [Tooltip("sv_stopspeed. A floor on how hard friction bites, so you come to a full stop " +
             "instead of creeping towards zero forever.")]
    [SerializeField] private float stopSpeed = 1.5f;

    [Tooltip("AIR_SPEED_CAP. Caps the air acceleration target, not your actual speed. " +
             "Small is correct: this is what makes air strafing a slow, skilful gain.")]
    [SerializeField] private float airSpeedCap = 0.5f;

    [Tooltip("How much speed is removed when scraping a wall. 1 is a clean slide.")]
    [SerializeField] private float clipOverbounce = 1f;

    [Tooltip("How hard the player shoves loose rigidbodies they walk into. Mass still " +
             "decides the result, so heavy props barely move. 0 disables pushing.")]
    [SerializeField] private float pushForce = 8f;

    [Header("Jumping")]
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -22f;

    [Tooltip("Temporary. Mouse wheel down also jumps. This is the standard bunny hop bind: " +
             "one flick of the wheel fires several jump presses, so one of them is far more " +
             "likely to land inside the buffer window on touchdown.")]
    [SerializeField] private bool jumpOnScrollDown = true;

    [Tooltip("Temporary. Mouse wheel up also jumps. Bind both to spam in either direction.")]
    [SerializeField] private bool jumpOnScrollUp;

    [Tooltip("Grace period after walking off a ledge where a jump still counts.")]
    [SerializeField] private float coyoteTime = 0.12f;

    [Tooltip("How early a jump press is remembered before landing. This is the bunny hop " +
             "timing window: land inside it and no friction is applied.")]
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Body")]
    [SerializeField] private PlayerCamera playerCamera;
    [SerializeField] private BodyTurnMode bodyTurn = BodyTurnMode.FaceLookWhenMoving;

    [Tooltip("Degrees per second the body turns to catch up with the look direction.")]
    [SerializeField] private float turnSpeed = 540f;

    [Tooltip("How far the look direction may stray from the body before the body is dragged around. " +
             "This is the angle a head and spine are expected to cover on their own.")]
    [SerializeField] private float maxLookOffset = 70f;

    private CharacterController controller;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float lastGroundedTime = float.NegativeInfinity;
    private float lastJumpPressedTime = float.NegativeInfinity;

    public float LookOffset { get; private set; }

    public float Speed => horizontalVelocity.magnitude;
    public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;
    public bool IsGrounded { get; private set; }
    public bool IsSourceMode => mode == MovementMode.Source;
    public float SpeedMultiplier { get; set; } = 1f;
    public float JumpMultiplier { get; set; } = 1f;
    public bool ScrollJumpEnabled { get; set; } = true;
    public float BaseSpeed => moveSpeed * SpeedMultiplier;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (playerCamera == null) playerCamera = GetComponentInChildren<PlayerCamera>();
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (JumpPressedThisFrame(keyboard)) lastJumpPressedTime = Time.time;

        Vector2 input = ReadMoveInput(keyboard);

        IsGrounded = controller.isGrounded;

        if (IsGrounded) lastGroundedTime = Time.time;

        // Jump before friction, or every hop bleeds speed and bunny hopping dies.
        bool jumped = TryJump();
        bool groundedForMovement = IsGrounded && !jumped;

        UpdateBodyRotation(input.sqrMagnitude > 0.01f);
        UpdateHorizontal(input, groundedForMovement);
        ApplyGravity(groundedForMovement);

        controller.Move(Velocity * Time.deltaTime);
    }

    private Vector2 ReadMoveInput(Keyboard keyboard)
    {
        Vector2 input = Vector2.zero;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;

        return Vector2.ClampMagnitude(input, 1f);
    }

    private void UpdateBodyRotation(bool isMoving)
    {
        if (playerCamera == null || bodyTurn == BodyTurnMode.Free) return;

        float lookYaw = playerCamera.Yaw;
        float bodyYaw = transform.eulerAngles.y;
        float targetYaw;

        if (bodyTurn == BodyTurnMode.FaceLook)
        {
            targetYaw = lookYaw;
        }
        else if (isMoving)
        {
            targetYaw = Mathf.MoveTowardsAngle(bodyYaw, lookYaw, turnSpeed * Time.deltaTime);
        }
        else
        {
            float offset = Mathf.DeltaAngle(bodyYaw, lookYaw);

            targetYaw = Mathf.Abs(offset) > maxLookOffset
                ? lookYaw - Mathf.Sign(offset) * maxLookOffset
                : bodyYaw;
        }

        transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
        LookOffset = Mathf.DeltaAngle(targetYaw, lookYaw);
    }

    private void UpdateHorizontal(Vector2 input, bool grounded)
    {
        // Relative to the look, not the body. This is Source wishdir.
        Vector3 forward = playerCamera != null ? playerCamera.PlanarForward : transform.forward;
        Vector3 right = playerCamera != null ? playerCamera.PlanarRight : transform.right;

        Vector3 wish = right * input.x + forward * input.y;

        float carrySpeed = moveSpeed * SpeedMultiplier;

        if (mode == MovementMode.Simple)
        {
            Vector3 target = wish * carrySpeed;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, acceleration * Time.deltaTime);
            return;
        }

        float wishSpeed = wish.magnitude * carrySpeed;
        Vector3 wishDir = wish.normalized;

        if (grounded)
        {
            ApplyFriction();
            Accelerate(wishDir, wishSpeed, groundAccelerate);
        }
        else
        {
            AirAccelerate(wishDir, wishSpeed, airAccelerate);
        }
    }

    private void ApplyFriction()
    {
        float speed = horizontalVelocity.magnitude;

        if (speed < 0.01f)
        {
            horizontalVelocity = Vector3.zero;
            return;
        }

        // stopSpeed floors the drop, or the decay never quite reaches zero.
        float control = Mathf.Max(speed, stopSpeed);
        float drop = control * friction * Time.deltaTime;

        horizontalVelocity *= Mathf.Max(0f, speed - drop) / speed;
    }

    // Caps how much velocity already points along wishDir, not the speed itself.
    private void Accelerate(Vector3 wishDir, float wishSpeed, float accel)
    {
        float currentSpeed = Vector3.Dot(horizontalVelocity, wishDir);
        float addSpeed = wishSpeed - currentSpeed;

        if (addSpeed <= 0f) return;

        float accelSpeed = Mathf.Min(accel * wishSpeed * Time.deltaTime, addSpeed);
        horizontalVelocity += wishDir * accelSpeed;
    }

    // Same, but the target is capped instead of the speed, so total speed is unbounded.
    private void AirAccelerate(Vector3 wishDir, float wishSpeed, float accel)
    {
        float cappedWishSpeed = Mathf.Min(wishSpeed, airSpeedCap);
        float currentSpeed = Vector3.Dot(horizontalVelocity, wishDir);
        float addSpeed = cappedWishSpeed - currentSpeed;

        if (addSpeed <= 0f) return;

        float accelSpeed = Mathf.Min(accel * wishSpeed * Time.deltaTime, addSpeed);
        horizontalVelocity += wishDir * accelSpeed;
    }

    private bool JumpPressedThisFrame(Keyboard keyboard)
    {
        if (keyboard.spaceKey.wasPressedThisFrame) return true;

        if (!ScrollJumpEnabled || (!jumpOnScrollDown && !jumpOnScrollUp)) return false;

        Mouse mouse = Mouse.current;

        if (mouse == null) return false;

        // Sign only: the wheel reports 120 per notch on Windows and 1 elsewhere.
        float scroll = mouse.scroll.ReadValue().y;

        if (jumpOnScrollDown && scroll < -0.01f) return true;

        return jumpOnScrollUp && scroll > 0.01f;
    }

    private bool TryJump()
    {
        bool hasGround = Time.time - lastGroundedTime <= coyoteTime;
        bool wantsJump = Time.time - lastJumpPressedTime <= jumpBufferTime;

        if (!hasGround || !wantsJump) return false;

        // Horizontal is left alone, so momentum carries through the jump.
        verticalVelocity = Mathf.Sqrt(jumpHeight * JumpMultiplier * -2f * gravity);

        lastGroundedTime = float.NegativeInfinity;
        lastJumpPressedTime = float.NegativeInfinity;

        return true;
    }

    private void ApplyGravity(bool grounded)
    {
        if (grounded && verticalVelocity < 0f)
        {
            // Downward bias keeps the controller stuck to ramps and steps.
            verticalVelocity = -2f;
        }
        else if (verticalVelocity > 0f && (controller.collisionFlags & CollisionFlags.Above) != 0)
        {
            verticalVelocity = 0f;
        }

        verticalVelocity += gravity * Time.deltaTime;
    }

    // A CharacterController walks through loose props without moving them.
    private void PushRigidbody(ControllerColliderHit hit)
    {
        Rigidbody body = hit.rigidbody;

        if (pushForce <= 0f || body == null || body.isKinematic) return;

        // Horizontal only. Pushing down on the floor makes the solver fight itself.
        Vector3 push = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);

        if (push.sqrMagnitude < 0.0001f) return;

        // deltaTime scaled: this fires per frame, not per physics step.
        float strength = Speed * pushForce * Time.deltaTime;

        body.AddForceAtPosition(push.normalized * strength, hit.point, ForceMode.Impulse);
    }

    // CharacterController never reports clipped velocity, so we would keep full speed.
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        PushRigidbody(hit);

        // Flattened, so ordinary floors are a no-op.
        Vector3 normal = new Vector3(hit.normal.x, 0f, hit.normal.z);

        if (normal.sqrMagnitude < 0.0001f) return;

        normal.Normalize();

        float into = Vector3.Dot(horizontalVelocity, normal);

        if (into < 0f) horizontalVelocity -= normal * (into * clipOverbounce);
    }
}
