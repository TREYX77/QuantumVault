using UnityEngine;

// An invisible wall only props bump into. The player walks straight through it, but anything
// carried is held back against it until the hold stretches past GrabInteract's break distance and
// drops it, and anything thrown bounces off. Put it over a stairway to keep the Photon Emitter on
// the floor it belongs to. Light ignores it the way it ignores glass, so it never cuts a beam or
// hides a slit.
[AddComponentMenu("Puzzle/Prop Barrier")]
[RequireComponent(typeof(BoxCollider))]
[DisallowMultipleComponent]
[ExecuteAlways]
public class PropBarrier : MonoBehaviour
{
    private BoxCollider box;
    private CharacterController player;
    private float nextPlayerSearch;

    // Leaves the size alone, so adding this to a box already fitted over a stairway keeps its fit.
    void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = false;
    }

    void Awake()
    {
        box = GetComponent<BoxCollider>();

        if (!Application.isPlaying) return;

        if (box.isTrigger)
        {
            Debug.LogWarning($"Prop Barrier '{name}' must be solid to stop props. Fixing it for this run.", this);
            box.isTrigger = false;
        }

        // Invisible in game, whatever it was built from.
        Renderer look = GetComponent<Renderer>();

        if (look != null) look.enabled = false;
    }

    void OnEnable()
    {
        if (box == null) box = GetComponent<BoxCollider>();

        GlassPane.Add(box);
    }

    void OnDisable()
    {
        GlassPane.Remove(box);
    }

    // Checked every frame rather than set once: the ignore can be lost whenever either collider
    // is switched off, which a checkpoint teleport may do to the player's. Looked up lazily and
    // retried once a second, the project's usual self-heal for a player that may load after this.
    void Update()
    {
        if (!Application.isPlaying) return;

        if (player == null)
        {
            if (Time.time < nextPlayerSearch) return;

            nextPlayerSearch = Time.time + 1f;
            player = FindAnyObjectByType<CharacterController>();

            if (player == null) return;
        }

        if (!Physics.GetIgnoreCollision(box, player)) Physics.IgnoreCollision(box, player, true);
    }

    // Always drawn, since in game it is invisible and in the scene it is easy to forget.
    void OnDrawGizmos()
    {
        BoxCollider collider = GetComponent<BoxCollider>();

        if (collider == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.35f, 0.7f, 1f, 0.1f);
        Gizmos.DrawCube(collider.center, collider.size);
        Gizmos.color = new Color(0.35f, 0.7f, 1f, 0.6f);
        Gizmos.DrawWireCube(collider.center, collider.size);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
