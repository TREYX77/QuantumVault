using UnityEngine;
using UnityEngine.InputSystem;

// Mouse look. Owns the view angles so Movement can walk relative to them.
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

    public float Yaw => yaw;
    public float Pitch => pitch;
    public Quaternion LookRotation => Quaternion.Euler(pitch, yaw, 0f);
    public Vector3 PlanarForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    public Vector3 PlanarRight => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

    // Set false to suspend look, such as while rotating a carried prop.
    public bool LookEnabled { get; set; } = true;

    void Start()
    {
        yaw = transform.eulerAngles.y;
        pitch = NormalizeAngle(transform.eulerAngles.x);

        if (lockCursor) SetCursorLocked(true);
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (lockCursor)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCursorLocked(false);

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (mouse.leftButton.wasPressedThisFrame) SetCursorLocked(true);

                return;
            }
        }

        if (!LookEnabled) return;

        // Delta is already per-frame, so do not scale it by deltaTime.
        Vector2 delta = mouse.delta.ReadValue();

        // Locking warps the OS pointer, which arrives as one huge delta.
        if (skipNextDelta)
        {
            skipNextDelta = false;
            return;
        }

        if (delta.sqrMagnitude > maxDeltaPerFrame * maxDeltaPerFrame) return;

        yaw += delta.x * sensitivity;
        pitch = Mathf.Clamp(pitch - delta.y * sensitivity, minPitch, maxPitch);
    }

    // World space, after everything moved, so the body cannot feed back in.
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

    // Euler angles come back 0..360; look angles need -180..180.
    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        return angle > 180f ? angle - 360f : angle;
    }
}
