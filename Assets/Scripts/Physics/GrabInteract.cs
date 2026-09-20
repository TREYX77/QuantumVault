using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Picks up, carries, rotates and throws <see cref="ObjectPhysics"/> props.
///
/// The hold is rigid, so a carried object sits dead still relative to the camera no matter
/// what it weighs. Weight is communicated instead through the walk speed penalty handed to
/// <see cref="Movement"/>, and through how far the prop travels when thrown.
///
/// Goes on the player, alongside Movement.
/// </summary>
[AddComponentMenu("Physics/Grab Interact")]
public class GrabInteract : MonoBehaviour
{
    [Header("References")]
    [Tooltip("All optional. Left empty they are resolved from this object and its children.")]
    [SerializeField] private PlayerCamera playerCamera;
    [SerializeField] private Movement movement;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private CharacterController playerCollider;

    [Header("Input")]
    [SerializeField] private Key grabKey = Key.E;

    [Tooltip("Held down to spin the carried object with the mouse. Look is suspended while held.")]
    [SerializeField] private Key rotateKey = Key.R;

    [Header("Reach")]
    [Tooltip("How far the player can reach to pick something up.")]
    [SerializeField] private float grabRange = 3f;

    [Tooltip("How far in front of the camera a carried object floats when first picked up.")]
    [SerializeField] private float holdDistance = 2f;

    [Tooltip("Closest the wheel can pull a held object.")]
    [SerializeField] private float minHoldDistance = 1f;

    [Tooltip("Furthest the wheel can push a held object.")]
    [SerializeField] private float maxHoldDistance = 4f;

    [Tooltip("How far one notch of the wheel moves a held object.")]
    [SerializeField] private float scrollDistanceStep = 0.25f;

    [Tooltip("Thickness of the grab probe. A fat probe is far more forgiving to aim than a " +
             "hairline ray. 0 makes it pixel exact.")]
    [SerializeField] private float grabRadius = 0.15f;

    [SerializeField] private LayerMask grabbableLayers = ~0;

    [Header("Carry")]
    [Tooltip("Drop the object once geometry has held it this far behind the hold point, " +
             "rather than letting the player shove props through walls.")]
    [SerializeField] private float breakDistance = 1.5f;

    [SerializeField] private float rotateSensitivity = 3f;

    [Header("Weight")]
    [Tooltip("Orientation used for props that do not override it themselves.")]
    [SerializeField] private HoldOrientation defaultOrientation = HoldOrientation.Rigid;

    [Tooltip("How fast a weightless prop catches the hold point.")]
    [SerializeField] private float followSpeed = 40f;

    [Tooltip("How much mass slows the follow. 0 makes every prop snap rigidly, which is " +
             "the behaviour before weight lag existed.")]
    [SerializeField] private float massInfluence = 0.06f;

    [Range(0f, 0.95f)]
    [Tooltip("Smoothing on the approach. Higher is heavier and floatier; too high overshoots.")]
    [SerializeField] private float followDamping = 0.25f;

    [Tooltip("Angular speed cap applied while carrying. Unity defaults rigidbodies to 7 rad/s, " +
             "which is far below what following a mouse turn needs, and the clamp is silent.")]
    [SerializeField] private float carryMaxAngularVelocity = 50f;

    [Tooltip("Dangle mode only. Spring strength pulling the grab point to the hold point.")]
    [SerializeField] private float dangleStiffness = 120f;

    [Tooltip("Dangle mode only. Damping, to stop the swing building up forever.")]
    [SerializeField] private float dangleDamping = 4f;

    [Header("Blocking")]
    [Tooltip("Distance behind the hold point at which the grip starts easing off, so a prop " +
             "pressed into a wall rests there instead of grinding.")]
    [SerializeField] private float softenDistance = 0.4f;

    [Range(0f, 1f)]
    [Tooltip("How much grip is left once fully blocked.")]
    [SerializeField] private float blockedFollowScale = 0.15f;

    [Header("Throwing")]
    [Tooltip("How long left mouse must be held for a full power throw. A quick tap still " +
             "throws, just weakly.")]
    [SerializeField] private float maxChargeTime = 1f;

    [Range(0.05f, 2f)]
    [Tooltip("Throw strength at a tap, as a fraction of the object's own Throw Impulse.")]
    [SerializeField] private float minThrowScale = 0.25f;

    [Range(0.1f, 4f)]
    [Tooltip("Throw strength at full charge.")]
    [SerializeField] private float maxThrowScale = 1.75f;

    [Tooltip("Shape of the charge ramp. Ease-in makes the last part of the hold feel like " +
             "it is worth waiting for.")]
    [SerializeField] private AnimationCurve chargeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private ObjectPhysics held;
    private Rigidbody heldBody;
    private Quaternion holdRotationOffset;
    private bool rotating;
    private float nextResolveTime;
    private float currentHoldDistance;
    private float chargeTime;
    private bool charging;
    private Vector3 aimedPoint;
    private Vector3 grabLocalPoint;

    // Rigidbody settings overridden during a carry, restored on release.
    private float cachedMaxAngularVelocity;

    /// <summary>The prop's own orientation mode if it has one, otherwise the grabber's default.</summary>
    private HoldOrientation ActiveOrientation =>
        held != null && held.OverridesOrientation ? held.Orientation : defaultOrientation;

    /// <summary>Heavier props follow more slowly. Shared by the position and rotation drives.</summary>
    private float MassResponse => 1f / (1f + (heldBody != null ? heldBody.mass : 0f) * massInfluence);

    // Rigidbody settings overridden during a carry, restored on release.
    private bool cachedUseGravity;
    private RigidbodyInterpolation cachedInterpolation;
    private CollisionDetectionMode cachedCollisionMode;

    public bool IsHolding => held != null;
    public ObjectPhysics Held => held;

    /// <summary>What the player is currently aiming at and could pick up, or null.</summary>
    public ObjectPhysics Aimed { get; private set; }

    /// <summary>Throw charge from 0 to 1, or 0 when not charging. Drive a HUD meter from this.</summary>
    public float ChargeNormalized => charging ? NormalizedCharge : 0f;

    /// <summary>Current carry distance, so a HUD can show how far the object has been pushed.</summary>
    public float HoldDistance => currentHoldDistance;

    private float NormalizedCharge =>
        maxChargeTime <= 0f ? 1f : Mathf.Clamp01(chargeTime / maxChargeTime);

    /// <summary>
    /// Why the current aim can or cannot be grabbed, in plain words. Shown by the debug
    /// overlay, because a grab that silently does nothing is otherwise impossible to
    /// diagnose from inside the game.
    /// </summary>
    public string AimStatus { get; private set; } = "nothing in range";

    /// <summary>
    /// The direction the player is looking. Prefers PlayerCamera's authoritative angles,
    /// but falls back to the camera transform, so a missing PlayerCamera degrades the
    /// rotate-while-holding feature rather than breaking grabbing outright.
    /// </summary>
    private Quaternion ViewRotation
    {
        get
        {
            if (playerCamera != null)
            {
                return playerCamera.LookRotation;
            }

            return viewCamera != null ? viewCamera.transform.rotation : Quaternion.identity;
        }
    }

    void Awake()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Walks every plausible rig layout rather than assuming one. PlayerCamera may sit on
    /// the Camera itself, on a rig object above it, or be absent entirely, and this
    /// component may be on the capsule or a child of it.
    /// </summary>
    private void ResolveReferences()
    {
        if (movement == null)
        {
            movement = GetComponentInParent<Movement>();
        }

        if (playerCollider == null)
        {
            playerCollider = GetComponentInParent<CharacterController>();
        }

        if (playerCamera == null)
        {
            // Searching inactive objects too, since a rig may start disabled.
            playerCamera = GetComponentInChildren<PlayerCamera>(true);
        }

        if (playerCamera == null)
        {
            playerCamera = FindAnyObjectByType<PlayerCamera>();
        }

        if (viewCamera != null)
        {
            return;
        }

        if (playerCamera != null)
        {
            // PlayerCamera is normally on the Camera object, but may be on a rig above it.
            viewCamera = playerCamera.GetComponent<Camera>();

            if (viewCamera == null)
            {
                viewCamera = playerCamera.GetComponentInChildren<Camera>(true);
            }
        }

        if (viewCamera == null)
        {
            viewCamera = GetComponentInChildren<Camera>(true);
        }

        // Camera.main only finds cameras tagged MainCamera, so the untagged case needs
        // one last sweep before giving up.
        if (viewCamera == null)
        {
            viewCamera = Camera.main;
        }

        if (viewCamera == null)
        {
            viewCamera = FindAnyObjectByType<Camera>();
        }
    }

    void OnDisable()
    {
        // Never leave a prop weightless or the camera stuck with look suspended.
        Release();
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        Mouse mouse = Mouse.current;

        // Refreshed every frame while empty handed so the crosshair and the debug readout
        // can show what is under the aim before the player commits to grabbing it.
        if (!IsHolding)
        {
            RefreshAim();
        }
        else
        {
            Aimed = null;
            AimStatus = "holding " + held.name + "  " + currentHoldDistance.ToString("0.0") + "m";
        }

        if (keyboard[grabKey].wasPressedThisFrame)
        {
            if (IsHolding)
            {
                Release();
            }
            else
            {
                TryGrab();
            }
        }

        UpdateHoldDistance(mouse);
        UpdateThrowCharge(mouse);
        UpdateRotation(keyboard, mouse);
    }

    void FixedUpdate()
    {
        if (!IsHolding)
        {
            return;
        }

        // The prop may have been destroyed by something else mid carry.
        if (heldBody == null || viewCamera == null)
        {
            ClearHeldState();
            return;
        }

        Transform view = viewCamera.transform;
        Vector3 target = view.position + view.forward * currentHoldDistance;

        // Driven by the point that was actually grabbed rather than the pivot, recomputed
        // from the live transform each step so it self-corrects as the prop rotates.
        Vector3 grabWorldPoint = heldBody.transform.TransformPoint(grabLocalPoint);
        Vector3 delta = target - grabWorldPoint;
        float distance = delta.magnitude;

        if (distance > breakDistance)
        {
            // Wedged against geometry and falling badly behind. Letting go beats forcing it.
            Release();
            return;
        }

        // Grip eases off the further the prop has been left behind, so one pressed into a
        // wall settles against it rather than fighting the solver every step.
        float blocked = Mathf.InverseLerp(softenDistance, breakDistance, distance);
        float grip = Mathf.Lerp(1f, blockedFollowScale, blocked);

        if (ActiveOrientation == HoldOrientation.Dangle)
        {
            ApplyDangleHold(delta, grabWorldPoint, grip);
            return;
        }

        ApplyRigidHold(delta, grip);
        ApplyHoldRotation();
    }

    /// <summary>
    /// Velocity driven, so the prop still collides rather than teleporting through walls.
    /// The mass clamp is what turns a snap into a drag, and damping stops the correction
    /// ringing when it meets resistance.
    /// </summary>
    private void ApplyRigidHold(Vector3 delta, float grip)
    {
        float maxFollow = followSpeed * MassResponse;

        Vector3 desired = Vector3.ClampMagnitude(delta / Time.fixedDeltaTime, maxFollow) * grip;

        // First order low pass. FixedUpdate runs at a fixed rate, so a plain Lerp is stable.
        heldBody.linearVelocity = Vector3.Lerp(heldBody.linearVelocity, desired, 1f - followDamping);
    }

    /// <summary>
    /// A spring applied at the grab point, with gravity left on. Because the force acts off
    /// the centre of mass it produces torque for free, which is what makes a ladder taken by
    /// one end hang and swing instead of floating level.
    /// </summary>
    private void ApplyDangleHold(Vector3 delta, Vector3 grabWorldPoint, float grip)
    {
        float stiffness = dangleStiffness * MassResponse * grip;

        Vector3 force = delta * stiffness - heldBody.linearVelocity * dangleDamping;

        heldBody.AddForceAtPosition(force, grabWorldPoint, ForceMode.Acceleration);
    }

    private void ApplyHoldRotation()
    {
        Quaternion target = ViewRotation * holdRotationOffset;
        Quaternion difference = target * Quaternion.Inverse(heldBody.rotation);

        difference.ToAngleAxis(out float angle, out Vector3 axis);

        // ToAngleAxis returns an infinite or zero axis for a near identity rotation, which
        // would put NaN into the rigidbody and freeze it permanently.
        if (axis.sqrMagnitude < 0.0001f || float.IsNaN(axis.x) || float.IsInfinity(axis.x))
        {
            heldBody.angularVelocity = Vector3.zero;
            return;
        }

        // Take the short way round rather than spinning 350 degrees to reach 10.
        if (angle > 180f)
        {
            angle -= 360f;
        }

        if (Mathf.Abs(angle) < 0.01f)
        {
            heldBody.angularVelocity = Vector3.zero;
            return;
        }

        Vector3 angular = axis.normalized * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime);

        // Sag mode lets mass slow the turn, so a heavy prop swings round behind the view
        // and settles once you stop. Rigid mode takes the correction at full strength.
        if (ActiveOrientation == HoldOrientation.RigidWithSag)
        {
            angular = Vector3.ClampMagnitude(angular, carryMaxAngularVelocity * MassResponse);
            angular = Vector3.Lerp(heldBody.angularVelocity, angular, 1f - followDamping);
        }

        heldBody.angularVelocity = angular;
    }

    /// <summary>
    /// Wheel pushes the held object away and pulls it closer. Movement.ScrollJumpEnabled is
    /// switched off during a carry, so the same wheel does not also fire a jump.
    /// </summary>
    private void UpdateHoldDistance(Mouse mouse)
    {
        if (mouse == null || !IsHolding)
        {
            return;
        }

        float scroll = mouse.scroll.ReadValue().y;

        if (Mathf.Abs(scroll) < 0.01f)
        {
            return;
        }

        // Only the sign is used. Wheel magnitude is 120 per notch on Windows and 1
        // elsewhere, so reading the value directly would move wildly different amounts.
        currentHoldDistance = Mathf.Clamp(
            currentHoldDistance + Mathf.Sign(scroll) * scrollDistanceStep,
            minHoldDistance,
            maxHoldDistance);
    }

    /// <summary>
    /// Left mouse charges while held and throws on release. A tap still throws, at
    /// minThrowScale, so the control never feels unresponsive.
    /// </summary>
    private void UpdateThrowCharge(Mouse mouse)
    {
        if (mouse == null || !IsHolding)
        {
            charging = false;
            chargeTime = 0f;
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            charging = true;
            chargeTime = 0f;
        }

        if (!charging)
        {
            return;
        }

        if (mouse.leftButton.isPressed)
        {
            chargeTime = Mathf.Min(chargeTime + Time.deltaTime, Mathf.Max(0f, maxChargeTime));
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            // Read the scale before Throw clears the charge state.
            float scale = Mathf.Lerp(minThrowScale, maxThrowScale, chargeCurve.Evaluate(NormalizedCharge));

            charging = false;
            chargeTime = 0f;

            Throw(scale);
        }
    }

    private void UpdateRotation(Keyboard keyboard, Mouse mouse)
    {
        bool wantsRotate = IsHolding && keyboard[rotateKey].isPressed;

        if (wantsRotate != rotating)
        {
            rotating = wantsRotate;

            // Hand the mouse to the object, and give it back on release.
            if (playerCamera != null)
            {
                playerCamera.LookEnabled = !rotating;
            }
        }

        if (!rotating || mouse == null)
        {
            return;
        }

        Vector2 delta = mouse.delta.ReadValue() * (rotateSensitivity * 0.1f);

        // Pre-multiplied, so the spin happens in camera space. The offset is stored relative
        // to the view, which is what lets a rotated prop keep its new orientation when you
        // then look somewhere else.
        holdRotationOffset = Quaternion.AngleAxis(delta.x, Vector3.up)
                           * Quaternion.AngleAxis(-delta.y, Vector3.right)
                           * holdRotationOffset;
    }

    /// <summary>
    /// Works out what is being aimed at and why it can or cannot be taken.
    ///
    /// A plain Physics.Raycast is the obvious implementation and it does not work here: the
    /// camera sits inside the player's own CharacterController capsule, so the very first
    /// thing any probe from the eye position hits is the player. Everything on the player
    /// is filtered out explicitly rather than relying on layers, so this keeps working
    /// whatever the layer setup ends up being.
    /// </summary>
    private void RefreshAim()
    {
        Aimed = null;

        // Self-heals if the camera rig is spawned or enabled after this component woke up.
        if (viewCamera == null && Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 1f;
            ResolveReferences();
        }

        if (viewCamera == null)
        {
            AimStatus = playerCamera == null
                ? "no Camera found anywhere in the scene"
                : "PlayerCamera has no Camera on it or under it";
            return;
        }

        Transform view = viewCamera.transform;

        // A sphere rather than a line, so aiming at a small prop does not demand pixel
        // accuracy. Overlapping hits come back with distance 0, which is why the sort
        // below measures against collider bounds instead of hit.distance.
        RaycastHit[] hits = Physics.SphereCastAll(
            view.position, grabRadius, view.forward, grabRange,
            grabbableLayers, QueryTriggerInteraction.Ignore);

        if (hits.Length == 0)
        {
            AimStatus = "nothing in range";
            return;
        }

        ObjectPhysics best = null;
        Vector3 bestPoint = Vector3.zero;
        float bestDistance = float.MaxValue;
        string rejection = null;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
            {
                // The player themselves, including the capsule the camera lives inside.
                continue;
            }

            ObjectPhysics candidate = hit.collider.GetComponentInParent<ObjectPhysics>();

            // Each rejection is recorded so the overlay can say which step failed, rather
            // than just reporting that nothing happened.
            if (candidate == null)
            {
                rejection ??= hit.collider.name + ": no ObjectPhysics";
                continue;
            }

            if (candidate.Body == null)
            {
                rejection = candidate.name + ": no Rigidbody";
                continue;
            }

            if (!candidate.Grabbable)
            {
                rejection = candidate.name + ": not grabbable";
                continue;
            }

            if (candidate.Body.isKinematic)
            {
                rejection = candidate.name + ": is kinematic";
                continue;
            }

            float distance = Vector3.Distance(
                view.position, hit.collider.bounds.ClosestPoint(view.position));

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;

                // Where on the surface the probe landed. This becomes the grip, so a long
                // prop taken by one end stays held by that end instead of its pivot.
                // SphereCastAll reports (0,0,0) for an already-overlapping hit, in which
                // case fall back to the nearest point on the collider.
                bestPoint = hit.point.sqrMagnitude > 0.0001f
                    ? hit.point
                    : hit.collider.ClosestPoint(view.position);
            }
        }

        if (best != null)
        {
            Aimed = best;
            aimedPoint = bestPoint;
            AimStatus = best.name + "  " + best.Mass.ToString("0.#") + "kg";
            return;
        }

        AimStatus = rejection ?? "nothing grabbable in range";
    }

    private void TryGrab()
    {
        RefreshAim();

        if (Aimed != null)
        {
            Grab(Aimed);
        }
    }

    private void Grab(ObjectPhysics target)
    {
        held = target;
        heldBody = target.Body;

        cachedUseGravity = heldBody.useGravity;
        cachedInterpolation = heldBody.interpolation;
        cachedCollisionMode = heldBody.collisionDetectionMode;
        cachedMaxAngularVelocity = heldBody.maxAngularVelocity;

        // The grip, in the prop's own space, so it survives the prop moving and rotating.
        grabLocalPoint = heldBody.transform.InverseTransformPoint(aimedPoint);

        // Unity defaults this to 7 rad/s. Following a quick mouse turn needs several times
        // that, and PhysX clamps it silently, so the prop falls behind, demands a bigger
        // correction, and clamps again. That loop is what made carrying look broken.
        heldBody.maxAngularVelocity = carryMaxAngularVelocity;

        // Dangle keeps gravity so the far end can hang; the other modes drive velocity
        // outright, where gravity would only fight the grip.
        heldBody.useGravity = ActiveOrientation == HoldOrientation.Dangle;

        // Physics runs at 50Hz while the camera runs at frame rate. Without interpolation
        // a held prop visibly steps along behind the view.
        heldBody.interpolation = RigidbodyInterpolation.Interpolate;

        // A fast carry can cover enough ground in one step to tunnel a thin wall.
        heldBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        SetPlayerCollision(false);

        // Stored relative to the camera, so looking around carries the prop with you while
        // any rotation the player dials in with R survives.
        holdRotationOffset = Quaternion.Inverse(ViewRotation) * heldBody.rotation;

        // Starts at the inspector default each time, so a previous carry cannot leave the
        // next object floating at arm's length.
        currentHoldDistance = Mathf.Clamp(holdDistance, minHoldDistance, maxHoldDistance);

        if (movement != null)
        {
            movement.SpeedMultiplier = held.CarrySpeedMultiplier;
            movement.JumpMultiplier = held.CarryJumpMultiplier;

            // The wheel now belongs to hold distance, so it must stop firing jumps.
            movement.ScrollJumpEnabled = false;
        }
    }

    /// <summary>Drops whatever is held, restoring it to ordinary physics.</summary>
    public void Release()
    {
        if (!IsHolding)
        {
            return;
        }

        if (heldBody != null)
        {
            SetPlayerCollision(true);

            heldBody.useGravity = cachedUseGravity;
            heldBody.interpolation = cachedInterpolation;
            heldBody.collisionDetectionMode = cachedCollisionMode;
            heldBody.maxAngularVelocity = cachedMaxAngularVelocity;
        }

        ClearHeldState();
    }

    /// <summary>
    /// Throws the held object along the view direction. The scale comes from how long the
    /// throw was charged, so it multiplies the object's own mass-derived impulse rather
    /// than replacing it: a fully charged heavy crate still goes less far than a tapped
    /// light one.
    /// </summary>
    public void Throw(float chargeScale = 1f)
    {
        if (!IsHolding || viewCamera == null)
        {
            return;
        }

        Rigidbody body = heldBody;
        float impulse = held.ThrowImpulse * Mathf.Max(0f, chargeScale);
        Vector3 direction = viewCamera.transform.forward;

        Release();

        if (movement != null)
        {
            Vector3 inherited = movement.Velocity;

            // The controller holds a small downward bias while grounded to stay on ramps;
            // passing that on would fire every standing throw slightly into the floor.
            inherited.y = Mathf.Max(0f, inherited.y);

            body.linearVelocity = inherited;
        }

        // Impulse is a change in momentum, so the resulting speed is impulse divided by
        // mass. One value therefore gives a 2kg cube a fast exit and a 50kg crate a short
        // lob, with no curve needed. VelocityChange here would make them identical, which
        // is the usual reason thrown props feel weightless.
        body.AddForce(direction * impulse, ForceMode.Impulse);
    }

    private void ClearHeldState()
    {
        held = null;
        heldBody = null;
        charging = false;
        chargeTime = 0f;

        if (rotating)
        {
            rotating = false;

            if (playerCamera != null)
            {
                playerCamera.LookEnabled = true;
            }
        }

        if (movement != null)
        {
            movement.SpeedMultiplier = 1f;
            movement.JumpMultiplier = 1f;

            // Hand the wheel back to jumping.
            movement.ScrollJumpEnabled = true;
        }
    }

    // Without this the held prop grinds against the player capsule, which reads as a
    // constant vibration and can shove the player sideways.
    private void SetPlayerCollision(bool enabled)
    {
        if (playerCollider == null || heldBody == null)
        {
            return;
        }

        Collider[] colliders = heldBody.GetComponentsInChildren<Collider>();

        foreach (Collider collider in colliders)
        {
            if (!collider.isTrigger)
            {
                Physics.IgnoreCollision(collider, playerCollider, !enabled);
            }
        }
    }
}
