using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class Movement : MonoBehaviour
{
    public enum MovementMode
    {
        /// <summary>Tight and predictable. Velocity is pulled straight to the target speed and clamped there.</summary>
        Simple,

        /// <summary>Half-Life 2 / Portal / CS style. Momentum carries, friction decays it, and speed is never clamped.</summary>
        Source
    }

    public enum BodyTurnMode
    {
        /// <summary>Body is always glued to the look direction. Fine for a capsule, stiff for a humanoid.</summary>
        FaceLook,

        /// <summary>Body turns to the look direction while walking, and stays put while standing still.</summary>
        FaceLookWhenMoving,

        /// <summary>Body is never turned here. Leave it to an animator or a turn-in-place system.</summary>
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

    /// <summary>
    /// Signed degrees the look direction sits away from the body's facing. Once there is a
    /// humanoid, feed this to the head and chest bones in LateUpdate (or to a Multi-Aim
    /// Constraint) so the character looks where the camera looks without the body following.
    /// </summary>
    public float LookOffset { get; private set; }

    /// <summary>Ground speed in metres per second. Bind a readout to this when tuning bunny hops.</summary>
    public float Speed => horizontalVelocity.magnitude;

    /// <summary>Full velocity including the vertical component.</summary>
    public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;

    public bool IsGrounded { get; private set; }

    public bool IsSourceMode => mode == MovementMode.Source;

    /// <summary>
    /// Scales walk speed. Carrying something heavy drives this down, which with a rigid
    /// hold is the main way the player feels an object's weight. 1 is unencumbered.
    /// </summary>
    public float SpeedMultiplier { get; set; } = 1f;

    /// <summary>Scales jump height the same way. 1 is unencumbered.</summary>
    public float JumpMultiplier { get; set; } = 1f;

    /// <summary>
    /// Set false to stop the mouse wheel triggering jumps while something else has claimed
    /// it, such as pushing a carried object in and out. Space is unaffected.
    /// </summary>
    public bool ScrollJumpEnabled { get; set; } = true;

    /// <summary>
    /// Current top speed under your own power, so a HUD can mark where gained speed begins.
    /// Includes the carry penalty, so the marker drops when you pick something up.
    /// </summary>
    public float BaseSpeed => moveSpeed * SpeedMultiplier;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<PlayerCamera>();
        }
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (JumpPressedThisFrame(keyboard))
        {
            lastJumpPressedTime = Time.time;
        }

        Vector2 input = ReadMoveInput(keyboard);

        IsGrounded = controller.isGrounded;

        if (IsGrounded)
        {
            lastGroundedTime = Time.time;
        }

        // Jump is resolved before the horizontal step on purpose. Source clears the ground
        // entity inside CheckJumpButton so that the same tick routes to AirMove and friction
        // never runs. Doing it the other way around bleeds speed on every hop no matter how
        // well timed, which is exactly what kills bunny hopping.
        bool jumped = TryJump();
        bool groundedForMovement = IsGrounded && !jumped;

        UpdateBodyRotation(input.sqrMagnitude > 0.01f);
        UpdateHorizontal(input, groundedForMovement);
        ApplyGravity(groundedForMovement);

        controller.Move(Velocity * Time.deltaTime);
    }

    // WASD (and arrow keys)
    private Vector2 ReadMoveInput(Keyboard keyboard)
    {
        Vector2 input = Vector2.zero;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;

        // Keeps diagonals from being faster than straight lines.
        return Vector2.ClampMagnitude(input, 1f);
    }

    private void UpdateBodyRotation(bool isMoving)
    {
        if (playerCamera == null || bodyTurn == BodyTurnMode.Free)
        {
            return;
        }

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

            // Standing still, the head does the work until it runs out of travel,
            // and only then does the body get dragged around to keep up.
            targetYaw = Mathf.Abs(offset) > maxLookOffset
                ? lookYaw - Mathf.Sign(offset) * maxLookOffset
                : bodyYaw;
        }

        transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
        LookOffset = Mathf.DeltaAngle(targetYaw, lookYaw);
    }

    private void UpdateHorizontal(Vector2 input, bool grounded)
    {
        // Relative to the look direction, not the body, so strafing stays correct
        // even while the body is lagging behind or standing still. This doubles as
        // Source's wishdir, which is why turning the mouse can steer momentum in mid air.
        Vector3 forward = playerCamera != null ? playerCamera.PlanarForward : transform.forward;
        Vector3 right = playerCamera != null ? playerCamera.PlanarRight : transform.right;

        Vector3 wish = right * input.x + forward * input.y;

        // Carrying something heavy lowers the ceiling on self-powered speed. It never
        // touches existing momentum, so a heavy object cannot brake a throw in progress.
        float carrySpeed = moveSpeed * SpeedMultiplier;

        if (mode == MovementMode.Simple)
        {
            Vector3 target = wish * carrySpeed;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, acceleration * Time.deltaTime);
            return;
        }

        // Input is already clamped to length 1 and the basis vectors are perpendicular
        // unit vectors, so this reads straight off as a 0..carrySpeed request.
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

    /// <summary>
    /// Multiplicative decay, applied on the ground only. Leaving the ground before this runs
    /// is what lets a chained jump keep its speed.
    /// </summary>
    private void ApplyFriction()
    {
        float speed = horizontalVelocity.magnitude;

        if (speed < 0.01f)
        {
            horizontalVelocity = Vector3.zero;
            return;
        }

        // stopSpeed is a floor on the drop, or exponential decay would approach zero
        // without ever arriving and the capsule would drift forever.
        float control = Mathf.Max(speed, stopSpeed);
        float drop = control * friction * Time.deltaTime;

        horizontalVelocity *= Mathf.Max(0f, speed - drop) / speed;
    }

    /// <summary>
    /// The heart of Source movement. The cap is applied to how much of the current velocity
    /// already points along wishDir, never to the magnitude of the velocity itself. Move
    /// sideways relative to your momentum and the dot product is near zero, so you receive
    /// full acceleration and the resulting vector comes out longer than it went in.
    /// </summary>
    private void Accelerate(Vector3 wishDir, float wishSpeed, float accel)
    {
        float currentSpeed = Vector3.Dot(horizontalVelocity, wishDir);
        float addSpeed = wishSpeed - currentSpeed;

        // Also the no-input case: wishDir is zero, so both terms are zero.
        if (addSpeed <= 0f)
        {
            return;
        }

        float accelSpeed = Mathf.Min(accel * wishSpeed * Time.deltaTime, addSpeed);
        horizontalVelocity += wishDir * accelSpeed;
    }

    /// <summary>
    /// Same projection, with the target clamped to a very small value. That clamp is what
    /// keeps air control subtle, while leaving the total speed completely unbounded.
    /// </summary>
    private void AirAccelerate(Vector3 wishDir, float wishSpeed, float accel)
    {
        float cappedWishSpeed = Mathf.Min(wishSpeed, airSpeedCap);
        float currentSpeed = Vector3.Dot(horizontalVelocity, wishDir);
        float addSpeed = cappedWishSpeed - currentSpeed;

        if (addSpeed <= 0f)
        {
            return;
        }

        // Scaled by the uncapped wish speed, so air control stays proportional to how fast
        // the player is asking to go even though the gain target itself is tiny.
        float accelSpeed = Mathf.Min(accel * wishSpeed * Time.deltaTime, addSpeed);
        horizontalVelocity += wishDir * accelSpeed;
    }

    /// <summary>
    /// Space, plus the optional wheel binds. The wheel matters because bunny hopping is on
    /// manual timing: scrolling emits a burst of presses across consecutive frames, so one
    /// of them almost always falls inside the jump buffer as you touch down.
    /// </summary>
    private bool JumpPressedThisFrame(Keyboard keyboard)
    {
        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            return true;
        }

        if (!ScrollJumpEnabled || (!jumpOnScrollDown && !jumpOnScrollUp))
        {
            return false;
        }

        Mouse mouse = Mouse.current;

        if (mouse == null)
        {
            return false;
        }

        // Scroll is a per-frame delta whose magnitude varies by platform and driver
        // (120 per notch on Windows, 1 elsewhere), so only the sign is worth testing.
        float scroll = mouse.scroll.ReadValue().y;

        if (jumpOnScrollDown && scroll < -0.01f)
        {
            return true;
        }

        return jumpOnScrollUp && scroll > 0.01f;
    }

    private bool TryJump()
    {
        bool hasGround = Time.time - lastGroundedTime <= coyoteTime;
        bool wantsJump = Time.time - lastJumpPressedTime <= jumpBufferTime;

        if (!hasGround || !wantsJump)
        {
            return false;
        }

        // Horizontal velocity is deliberately untouched: momentum carries through a jump.
        verticalVelocity = Mathf.Sqrt(jumpHeight * JumpMultiplier * -2f * gravity);

        // Consume both windows so one press cannot produce two jumps.
        lastGroundedTime = float.NegativeInfinity;
        lastJumpPressedTime = float.NegativeInfinity;

        return true;
    }

    private void ApplyGravity(bool grounded)
    {
        if (grounded && verticalVelocity < 0f)
        {
            // Small downward bias keeps the controller pinned to ramps and steps. Skipped on
            // a jump frame, since grounded is already false by then.
            verticalVelocity = -2f;
        }
        else if (verticalVelocity > 0f && (controller.collisionFlags & CollisionFlags.Above) != 0)
        {
            // Hit a ceiling
            verticalVelocity = 0f;
        }

        verticalVelocity += gravity * Time.deltaTime;
    }

    /// <summary>
    /// CharacterController resolves collisions internally but never writes a clipped velocity
    /// back to us, so without this the velocity field keeps claiming full speed into a wall
    /// and hands all of it back the instant the player turns away.
    /// </summary>
    /// <summary>
    /// A CharacterController walks straight through loose props without disturbing them,
    /// which is most of why Unity movement feels unphysical out of the box. This shoves
    /// what the player walks into, with mass deciding how far it goes.
    /// </summary>
    private void PushRigidbody(ControllerColliderHit hit)
    {
        Rigidbody body = hit.rigidbody;

        if (pushForce <= 0f || body == null || body.isKinematic)
        {
            return;
        }

        // Horizontal only. Pushing down on whatever you are stood on makes the solver
        // fight itself and can jitter the player.
        Vector3 push = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);

        if (push.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // Scaled by deltaTime because this fires once per frame, not once per physics step,
        // so an unscaled impulse would shove harder at higher framerates. Impulse rather
        // than Acceleration so that mass still decides how much the prop actually moves.
        float strength = Speed * pushForce * Time.deltaTime;

        body.AddForceAtPosition(push.normalized * strength, hit.point, ForceMode.Impulse);
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        PushRigidbody(hit);

        // Flattened first, which makes ordinary floors a no-op by construction and avoids
        // bleeding speed while simply walking about.
        Vector3 normal = new Vector3(hit.normal.x, 0f, hit.normal.z);

        if (normal.sqrMagnitude < 0.0001f)
        {
            return;
        }

        normal.Normalize();

        float into = Vector3.Dot(horizontalVelocity, normal);

        if (into < 0f)
        {
            horizontalVelocity -= normal * (into * clipOverbounce);
        }
    }
}
