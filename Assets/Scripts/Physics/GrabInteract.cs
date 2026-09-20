using UnityEngine;
using UnityEngine.InputSystem;

// Picks up, carries, rotates and throws ObjectPhysics props. Goes on the player.
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

    // Saved on grab, put back on release.
    private bool cachedUseGravity;
    private RigidbodyInterpolation cachedInterpolation;
    private CollisionDetectionMode cachedCollisionMode;
    private float cachedMaxAngularVelocity;

    public bool IsHolding => held != null;
    public ObjectPhysics Held => held;
    public ObjectPhysics Aimed { get; private set; }
    public float ChargeNormalized => charging ? NormalizedCharge : 0f;
    public float HoldDistance => currentHoldDistance;

    public string AimStatus { get; private set; } = "nothing in range";

    private float NormalizedCharge =>
        maxChargeTime <= 0f ? 1f : Mathf.Clamp01(chargeTime / maxChargeTime);

    private HoldOrientation ActiveOrientation =>
        held != null && held.OverridesOrientation ? held.Orientation : defaultOrientation;

    private float MassResponse => 1f / (1f + (heldBody != null ? heldBody.mass : 0f) * massInfluence);

    private Quaternion ViewRotation
    {
        get
        {
            if (playerCamera != null) return playerCamera.LookRotation;

            return viewCamera != null ? viewCamera.transform.rotation : Quaternion.identity;
        }
    }

    void Awake()
    {
        ResolveReferences();
    }

    // Tries every rig layout, since PlayerCamera may sit anywhere or be missing.
    private void ResolveReferences()
    {
        if (movement == null) movement = GetComponentInParent<Movement>();
        if (playerCollider == null) playerCollider = GetComponentInParent<CharacterController>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<PlayerCamera>(true);
        if (playerCamera == null) playerCamera = FindAnyObjectByType<PlayerCamera>();

        if (viewCamera != null) return;

        if (playerCamera != null)
        {
            viewCamera = playerCamera.GetComponent<Camera>();

            if (viewCamera == null) viewCamera = playerCamera.GetComponentInChildren<Camera>(true);
        }

        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>(true);

        // Camera.main only finds cameras tagged MainCamera, so sweep for any as a fallback.
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) viewCamera = FindAnyObjectByType<Camera>();
    }

    void OnDisable()
    {
        Release();
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null) return;

        Mouse mouse = Mouse.current;

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
        if (!IsHolding) return;

        if (heldBody == null || viewCamera == null)
        {
            ClearHeldState();
            return;
        }

        Transform view = viewCamera.transform;
        Vector3 target = view.position + view.forward * currentHoldDistance;

        // Follows the grabbed point, not the pivot
        Vector3 grabWorldPoint = heldBody.transform.TransformPoint(grabLocalPoint);
        Vector3 delta = target - grabWorldPoint;
        float distance = delta.magnitude;

        if (distance > breakDistance)
        {
            Release();
            return;
        }

        // Eases off when blocked, so a prop pressed into a wall rests instead of grinding.
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

    // Velocity, not position, so walls still stop it. The mass clamp makes it drag.
    private void ApplyRigidHold(Vector3 delta, float grip)
    {
        float maxFollow = followSpeed * MassResponse;

        Vector3 desired = Vector3.ClampMagnitude(delta / Time.fixedDeltaTime, maxFollow) * grip;

        heldBody.linearVelocity = Vector3.Lerp(heldBody.linearVelocity, desired, 1f - followDamping);
    }

    // Off-centre force makes torque, so a ladder held by one end hangs and swings.
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

        // ToAngleAxis gives a bad axis near zero rotation, which would NaN the rigidbody.
        if (axis.sqrMagnitude < 0.0001f || float.IsNaN(axis.x) || float.IsInfinity(axis.x))
        {
            heldBody.angularVelocity = Vector3.zero;
            return;
        }

        if (angle > 180f) angle -= 360f;

        if (Mathf.Abs(angle) < 0.01f)
        {
            heldBody.angularVelocity = Vector3.zero;
            return;
        }

        Vector3 angular = axis.normalized * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime);

        if (ActiveOrientation == HoldOrientation.RigidWithSag)
        {
            angular = Vector3.ClampMagnitude(angular, carryMaxAngularVelocity * MassResponse);
            angular = Vector3.Lerp(heldBody.angularVelocity, angular, 1f - followDamping);
        }

        heldBody.angularVelocity = angular;
    }

    private void UpdateHoldDistance(Mouse mouse)
    {
        if (mouse == null || !IsHolding) return;

        float scroll = mouse.scroll.ReadValue().y;

        if (Mathf.Abs(scroll) < 0.01f) return;

        // Sign only: the wheel reports 120 per notch on Windows and 1 elsewhere.
        currentHoldDistance = Mathf.Clamp(
            currentHoldDistance + Mathf.Sign(scroll) * scrollDistanceStep,
            minHoldDistance,
            maxHoldDistance);
    }

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

        if (!charging) return;

        if (mouse.leftButton.isPressed)
        {
            chargeTime = Mathf.Min(chargeTime + Time.deltaTime, Mathf.Max(0f, maxChargeTime));
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            // Read before Throw clears it.
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

            if (playerCamera != null) playerCamera.LookEnabled = !rotating;
        }

        if (!rotating || mouse == null) return;

        Vector2 delta = mouse.delta.ReadValue() * (rotateSensitivity * 0.1f);

        // Applied in camera space, so a rotated prop keeps its angle when you look away.
        holdRotationOffset = Quaternion.AngleAxis(delta.x, Vector3.up)
                           * Quaternion.AngleAxis(-delta.y, Vector3.right)
                           * holdRotationOffset;
    }

    private void RefreshAim()
    {
        Aimed = null;

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
            // The camera sits inside the player capsule, so skip anything on the player.
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;

            ObjectPhysics candidate = hit.collider.GetComponentInParent<ObjectPhysics>();

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

            // Bounds distance, because overlapping hits all report 0 and would tie.
            float distance = Vector3.Distance(
                view.position, hit.collider.bounds.ClosestPoint(view.position));

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;

                // This becomes the grip. Overlapping hits report (0,0,0), hence the fallback.
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

        if (Aimed != null) Grab(Aimed);
    }

    private void Grab(ObjectPhysics target)
    {
        held = target;
        heldBody = target.Body;

        cachedUseGravity = heldBody.useGravity;
        cachedInterpolation = heldBody.interpolation;
        cachedCollisionMode = heldBody.collisionDetectionMode;
        cachedMaxAngularVelocity = heldBody.maxAngularVelocity;

        grabLocalPoint = heldBody.transform.InverseTransformPoint(aimedPoint);

        // Unity caps this at 7 rad/s, too slow to follow a mouse turn, and clamps silently.
        heldBody.maxAngularVelocity = carryMaxAngularVelocity;

        // Dangle needs gravity to hang; other modes drive velocity instead.
        heldBody.useGravity = ActiveOrientation == HoldOrientation.Dangle;

        // Physics runs slower than the camera, so without this the prop steps behind.
        heldBody.interpolation = RigidbodyInterpolation.Interpolate;

        // A fast carry can cross a thin wall in one step.
        heldBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        SetPlayerCollision(false);

        holdRotationOffset = Quaternion.Inverse(ViewRotation) * heldBody.rotation;

        currentHoldDistance = Mathf.Clamp(holdDistance, minHoldDistance, maxHoldDistance);

        if (movement != null)
        {
            movement.SpeedMultiplier = held.CarrySpeedMultiplier;
            movement.JumpMultiplier = held.CarryJumpMultiplier;

            // The wheel now moves the prop, so it must stop jumping.
            movement.ScrollJumpEnabled = false;
        }
    }

    public void Release()
    {
        if (!IsHolding) return;

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

    // Multiplies the props own impulse, so mass still decides how far it goes.
    public void Throw(float chargeScale = 1f)
    {
        if (!IsHolding || viewCamera == null) return;

        Rigidbody body = heldBody;
        float impulse = held.ThrowImpulse * Mathf.Max(0f, chargeScale);
        Vector3 direction = viewCamera.transform.forward;

        Release();

        if (movement != null)
        {
            Vector3 inherited = movement.Velocity;

            // Grounded velocity has a downward bias, which would fire the throw into the floor.
            inherited.y = Mathf.Max(0f, inherited.y);

            body.linearVelocity = inherited;
        }

        // Impulse divides by mass, so heavy props leave slowly. VelocityChange would not.
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

            if (playerCamera != null) playerCamera.LookEnabled = true;
        }

        if (movement != null)
        {
            movement.SpeedMultiplier = 1f;
            movement.JumpMultiplier = 1f;
            movement.ScrollJumpEnabled = true;
        }
    }

    // Without this the prop grinds against the player capsule.
    private void SetPlayerCollision(bool enabled)
    {
        if (playerCollider == null || heldBody == null) return;

        Collider[] colliders = heldBody.GetComponentsInChildren<Collider>();

        foreach (Collider collider in colliders)
        {
            if (!collider.isTrigger) Physics.IgnoreCollision(collider, playerCollider, !enabled);
        }
    }
}
