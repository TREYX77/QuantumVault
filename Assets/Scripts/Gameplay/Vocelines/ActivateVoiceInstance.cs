using UnityEngine;

// Plays one voiceline when the player enters the trigger. Starting another voiceline stops
// the previous one so separate level voicelines cannot overlap.
[AddComponentMenu("Puzzle/Activate Voice Instance")]
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class ActivateVoiceInstance : MonoBehaviour
{
    [Tooltip("The line to play when the player enters the trigger.")]
    [SerializeField] private AudioClip voiceline;

    [Tooltip("When enabled, this trigger will not play again after its line has finished.")]
    [SerializeField] private bool playOnlyOnce = true;

    private static ActivateVoiceInstance currentlyPlaying;

    private AudioSource source;
    private bool hasPlayed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        currentlyPlaying = null;
    }

    // Called by the pause menu's "Reset to checkpoint" button.
    public static void RestartPlaying()
    {
        if (currentlyPlaying != null) currentlyPlaying.Restart();
    }

    void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(4f, 3f, 1f);

        AudioSource audio = GetComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 0f;
    }

    void Awake()
    {
        foreach (BoxCollider box in GetComponents<BoxCollider>())
        {
            if (box.isTrigger) continue;

            Debug.LogWarning(
                $"ActivateVoiceInstance '{name}' needs its collider set to Is Trigger. Fixing it for this run.",
                this);

            box.isTrigger = true;
        }

        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.clip = voiceline;
    }

    void OnDestroy()
    {
        if (currentlyPlaying == this) currentlyPlaying = null;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other) || voiceline == null) return;

        if (source.isPlaying) return;
        if (playOnlyOnce && hasPlayed) return;

        StopOtherVoiceline();

        hasPlayed = true;
        currentlyPlaying = this;
        source.Play();
    }

    private void StopOtherVoiceline()
    {
        if (currentlyPlaying == null || currentlyPlaying == this) return;

        currentlyPlaying.source.Stop();
        currentlyPlaying = null;
    }

    private void Restart()
    {
        if (!source.isPlaying) return;

        source.Stop();
        source.Play();
    }

    private static bool IsPlayer(Collider other)
    {
        return other.GetComponentInParent<CharacterController>() != null
            || other.CompareTag("Player");
    }
}

