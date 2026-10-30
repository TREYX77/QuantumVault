using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// A pane of glass: solid to the player and props, clear to light. The Photon Emitter's laser, a
// Which Path Detector's line of sight and a flashlight's mirror bounce all carry on through it,
// because a slit seen through a window is still seen. Goes on the glass; every collider on it and
// its children counts.
// Rays read it through MirrorSurface.FirstHit, the one nearest-hit rule every light ray shares.
// Runs in edit mode too, so the emitter's gizmo shows the beam passing through while placing it.
[AddComponentMenu("Puzzle/Glass Pane")]
[DisallowMultipleComponent]
[ExecuteAlways]
public class GlassPane : MonoBehaviour
{
    // Colliders light passes through. A set, so the lookup on every hit of every trace stays cheap.
    private static readonly HashSet<Collider> clear = new HashSet<Collider>();

    private Collider[] own = new Collider[0];

    // Statics survive play sessions when the domain reload is skipped.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        clear.Clear();
    }

    // A solid shadow from a window would black out the room behind it.
    void Reset()
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    void OnEnable()
    {
        own = GetComponentsInChildren<Collider>(true);

        foreach (Collider collider in own) Add(collider);
    }

    void OnDisable()
    {
        foreach (Collider collider in own) Remove(collider);
    }

    public static bool IsClear(Collider collider) => collider != null && clear.Contains(collider);

    // For anything else light should ignore, such as a PropBarrier.
    public static void Add(Collider collider)
    {
        if (collider != null) clear.Add(collider);
    }

    public static void Remove(Collider collider)
    {
        clear.Remove(collider);
    }

    // Physics.Linecast as light sees it: the first thing between the two points that is not glass.
    public static bool Linecast(Vector3 from, Vector3 to, LayerMask mask, out RaycastHit hit)
    {
        Vector3 line = to - from;
        float length = line.magnitude;

        if (length < 0.0001f)
        {
            hit = default;
            return false;
        }

        return MirrorSurface.FirstHit(from, line / length, length, mask, null, out hit);
    }
}
