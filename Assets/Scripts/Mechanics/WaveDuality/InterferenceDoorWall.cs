using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// A wall with two slits whose photons, fired while nobody watches, turn its interference fringes
// into doors. The result is decided when the emitter fires, over a short exposure: if the slits
// were seen at any point in it, the photons went through as particles and only the two slits
// light up. If not, they went through as a wave and every door appears. Either result holds
// until the beam stops, so turning round to walk to the doors does not undo them.
[AddComponentMenu("Wave Duality/Interference Door Wall")]
[DisallowMultipleComponent]
public class InterferenceDoorWall : MonoBehaviour, IResettable
{
    public enum WallState
    {
        Dark,
        Measuring,
        Wave,
        Particle
    }

    [Header("Source")]
    [Tooltip("The two slits in this wall. The emitter has to hit it, and seeing it is observing.")]
    [SerializeField] private SlitGate gate;

    [Header("Doors")]
    [Tooltip("Left to right as seen from the room.")]
    [SerializeField] private List<InterferenceDoor> doors = new List<InterferenceDoor>();

    [Tooltip("Which door leads on, counting from 1 at the left as seen from the room.")]
    [SerializeField] private int correctDoor = 3;

    [Tooltip("What waits behind the correct door. Moved behind it when the level starts.")]
    [SerializeField] private Transform exit;

    [Header("Measurement")]
    [Tooltip("Seconds after the beam starts during which seeing the slits makes the photons " +
             "particles. Long enough that a glance counts, short enough not to feel like lag.")]
    [SerializeField] private float exposureTime = 0.6f;

    [Tooltip("Seconds the beam can be gone before the pattern fades and the wall is dark again. " +
             "Rides out the player stepping through the beam.")]
    [SerializeField] private float beamLostGrace = 0.5f;

    [Header("Slits")]
    [Tooltip("Lit only when the photons came through as particles: two bright bands, in order " +
             "left slit then right. Each is moved to where a line from the emitter through its " +
             "slit meets this wall, so the bands line up with the gate wherever it stands.")]
    [SerializeField] private Renderer[] slitGlows = new Renderer[0];

    [Header("Events")]
    [SerializeField] private UnityEvent onWave;
    [SerializeField] private UnityEvent onParticle;
    [SerializeField] private UnityEvent onDark;

    // Shared by every wall, which is safe because Update never runs two at once.
    private static readonly Vector3[] slitPoints = new Vector3[6];

    private float measureStart;
    private float lastLitTime = float.NegativeInfinity;
    private bool seenWhileMeasuring;
    private Vector3[] glowHomes = new Vector3[0];

    public WallState State { get; private set; } = WallState.Dark;
    public IReadOnlyList<InterferenceDoor> Doors => doors;
    public int CorrectDoor => correctDoor;

    public string Status => State switch
    {
        WallState.Dark => "dark",
        WallState.Measuring => "measuring",
        WallState.Wave => "wave - " + doors.Count + " doors",
        _ => "particle - two slits"
    };

    void Awake()
    {
        // Where each band was placed, used when no beam source is known.
        glowHomes = new Vector3[slitGlows.Length];

        for (int i = 0; i < slitGlows.Length; i++)
        {
            if (slitGlows[i] != null) glowHomes[i] = slitGlows[i].transform.position;
        }

        PlaceExit();
        Show(WallState.Dark, true);
    }

    void Update()
    {
        bool lit = gate != null && gate.IsIlluminated;

        if (lit) lastLitTime = Time.time;

        bool beamGone = Time.time - lastLitTime > beamLostGrace;

        switch (State)
        {
            case WallState.Dark:
                if (!lit) break;

                measureStart = Time.time;
                seenWhileMeasuring = gate.IsObserved;
                Show(WallState.Measuring);

                break;

            case WallState.Measuring:
                if (beamGone)
                {
                    Show(WallState.Dark);
                    break;
                }

                if (gate.IsObserved) seenWhileMeasuring = true;

                if (Time.time - measureStart >= exposureTime) Show(seenWhileMeasuring ? WallState.Particle : WallState.Wave);

                break;

            default:
                if (beamGone) Show(WallState.Dark);

                // Followed every frame, so carrying the emitter sweeps the bands with it.
                else if (State == WallState.Particle) PlaceSlitGlows();

                break;
        }
    }

    private void Show(WallState next, bool force = false)
    {
        if (next == State && !force) return;

        State = next;

        foreach (InterferenceDoor door in doors)
        {
            if (door != null) door.Materialize(next == WallState.Wave);
        }

        if (next == WallState.Particle) PlaceSlitGlows();

        foreach (Renderer glow in slitGlows)
        {
            if (glow != null) glow.enabled = next == WallState.Particle;
        }

        if (force) return;

        if (next == WallState.Wave) onWave?.Invoke();
        else if (next == WallState.Particle) onParticle?.Invoke();
        else if (next == WallState.Dark) onDark?.Invoke();
    }

    // Photons that went through as particles land in a straight line from the source through
    // their slit, so each band goes where that line meets this wall's face. Without a known
    // source, or a line that never reaches the wall, a band stays where it was placed.
    private void PlaceSlitGlows()
    {
        if (gate == null) return;

        // Three points down each slit; the middle one is its centre.
        int slits = gate.GetSlitPoints(slitPoints) / 3;
        Plane face = new Plane(-transform.forward, transform.position);

        for (int i = 0; i < slitGlows.Length; i++)
        {
            Renderer glow = slitGlows[i];

            if (glow == null) continue;

            Vector3 position = glowHomes[i];

            if (gate.HasBeamOrigin && i < slits)
            {
                Vector3 slit = slitPoints[i * 3 + 1];
                Ray path = new Ray(gate.BeamOrigin, slit - gate.BeamOrigin);

                if (face.Raycast(path, out float distance) && distance > Vector3.Distance(gate.BeamOrigin, slit))
                {
                    // Just proud of the face, and never sunk into the floor.
                    position = path.GetPoint(distance) - transform.forward * 0.02f;
                    position.y = Mathf.Max(position.y, transform.position.y + HalfHeight(glow));
                }
            }

            glow.transform.position = position;
        }
    }

    // From the mesh, because a disabled renderer's bounds are not reliable.
    private static float HalfHeight(Renderer glow)
    {
        MeshFilter filter = glow.GetComponent<MeshFilter>();

        if (filter == null || filter.sharedMesh == null) return 0f;

        return filter.sharedMesh.bounds.extents.y * Mathf.Abs(glow.transform.lossyScale.y);
    }

    // The exit lives behind whichever door the designer picked, so changing the number in the
    // inspector is all it takes to move it.
    private void PlaceExit()
    {
        InterferenceDoor door = CorrectDoorObject;

        if (exit == null || door == null) return;

        exit.SetPositionAndRotation(door.ExitAnchor.position, door.ExitAnchor.rotation);
    }

    private InterferenceDoor CorrectDoorObject
    {
        get
        {
            int index = correctDoor - 1;

            return index >= 0 && index < doors.Count ? doors[index] : null;
        }
    }

    public void CaptureState()
    {
    }

    // Dark again, with the beam reset alongside it, so the next shot is measured afresh.
    public void RestoreState()
    {
        lastLitTime = float.NegativeInfinity;
        seenWhileMeasuring = false;
        Show(WallState.Dark, true);
    }

    void OnValidate()
    {
        correctDoor = Mathf.Clamp(correctDoor, 1, Mathf.Max(1, doors.Count));
    }

    // Numbers every door and marks the correct one, since in the game they all look the same.
    void OnDrawGizmos()
    {
#if UNITY_EDITOR
        for (int i = 0; i < doors.Count; i++)
        {
            InterferenceDoor door = doors[i];

            if (door == null) continue;

            bool correct = i == correctDoor - 1;

            UnityEditor.Handles.color = correct ? new Color(0.3f, 1f, 0.45f) : Color.white;
            UnityEditor.Handles.Label(door.transform.position + Vector3.up * 2.8f, correct ? $"{i + 1} (correct)" : (i + 1).ToString());
        }
#endif

        InterferenceDoor target = CorrectDoorObject;

        if (target == null) return;

        Gizmos.color = new Color(0.3f, 1f, 0.45f, 0.8f);
        Gizmos.DrawWireCube(target.ExitAnchor.position + Vector3.up * 1.2f, new Vector3(1.2f, 2.4f, 0.8f));
    }
}
