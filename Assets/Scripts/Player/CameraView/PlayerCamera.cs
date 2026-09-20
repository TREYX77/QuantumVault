using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCamera : MonoBehaviour
{
    [Header("Look")]
    [SerializeField] private float sensitivity = 0.08f;
    [SerializeField] private float minPitch = -85f;
    [SerializeField] private float maxPitch = 85f;

    [Tooltip("Mouse movement larger than this in a single frame is ignored as a pointer warp.")]
    [SerializeField] private float maxDeltaPerFrame = 400f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursor = true;

    private float yaw;
    private float pitch;
    private bool skipNextDelta;

    /// <summary>Where the player is looking, in degrees. Independent of which way the body faces.</summary>
    public float Yaw => yaw;

    /// <summary>Look elevation in degrees. Negative is up. Drive a spine bend from this later.</summary>
    public float Pitch => pitch;

    public Quaternion LookRotation => Quaternion.Euler(pitch, yaw, 0f);

    /// <summary>Look direction flattened onto the ground plane, for movement.</summary>
    public Vector3 PlanarForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

    public Vector3 PlanarRight => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

    /// <summary>
    /// Set false to suspend mouse look without disabling the component, for example while
    /// rotating a carried object. LateUpdate keeps writing the rotation, so the view holds
    /// steady rather than drifting.
    /// </summary>
    public bool LookEnabled { get; set; } = true;

    void Start()
    {
        yaw = transform.eulerAngles.y;
        pitch = NormalizeAngle(transform.eulerAngles.x);

        if (lockCursor)
        {
            SetCursorLocked(true);
        }
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        if (lockCursor)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetCursorLocked(false);
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                // Click back into the game window to resume looking around.
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    SetCursorLocked(true);
                }

                return;
            }
        }

        // Suspended while something else is claiming the mouse, such as rotating a held
        // object. Checked after the cursor handling above so Escape still works.
        if (!LookEnabled)
        {
            return;
        }

        // Mouse delta is already a per-frame value, so it must not be scaled by deltaTime.
        Vector2 delta = mouse.delta.ReadValue();

        // Changing the lock state warps the OS pointer, and that warp arrives as one
        // huge delta. Feeding it into the rotation is what sent the camera spinning.
        if (skipNextDelta)
        {
            skipNextDelta = false;
            return;
        }

        if (delta.sqrMagnitude > maxDeltaPerFrame * maxDeltaPerFrame)
        {
            return;
        }

        yaw += delta.x * sensitivity;
        pitch = Mathf.Clamp(pitch - delta.y * sensitivity, minPitch, maxPitch);
    }

    // Written in world space and after everything else has moved, so the body's own
    // rotation never feeds into the camera. The body is free to lag or stay put.
    void LateUpdate()
    {
        transform.rotation = LookRotation;
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
        skipNextDelta = true;
    }

    // Euler angles come back as 0..360; look angles need -180..180.
    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        return angle > 180f ? angle - 360f : angle;
    }
}
