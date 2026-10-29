using System;
using UnityEngine;

// Plays a voiceline when the player walks into the trigger. Dying to a Reset Volume does not
// touch it, so it plays to the end. Resetting from the pause menu restarts it via RestartPlaying.
[AddComponentMenu("Puzzle/Activate Voice Instance")]
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class ActivateVoiceInstance : MonoBehaviour
{
    [Tooltip("The line to play when the player enters the trigger.")]
    [SerializeField] private AudioClip voiceline;

    [Tooltip("When on, walking back in after the line has finished does not play it again.")]
    [SerializeField] private bool playOnlyOnce = true;

    private AudioSource source;
    private bool hasPlayed;

    // Called by the pause menu's "Reset to checkpoint". Only a line that is playing restarts,
    // so a line that already finished does not start up again in a room the player has left.
    public static void RestartPlaying()
    {
        foreach (ActivateVoiceInstance voice in FindObjectsByType<ActivateVoiceInstance>(FindObjectsSortMode.None))
        {
            voice.Restart();
        }
    }

    void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(4f, 3f, 1f);

        // A voiceline is heard the same everywhere, not from a point in the room.
        AudioSource audio = GetComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 0f;
    }

    void Awake()
    {
        BoxCollider box = GetComponent<BoxCollider>();

        if (!box.isTrigger)
        {
            Debug.LogWarning($"ActivateVoiceInstance '{name}' needs its collider set to Is Trigger. Fixing it for this run.", this);
            box.isTrigger = true;
        }

        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.clip = voiceline;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other) || voiceline == null) return;

        // A respawn can put the player back inside the trigger. That must not cut off
        // or restart a line that is still playing.
        if (source.isPlaying) return;

        if (playOnlyOnce && hasPlayed) return;

        hasPlayed = true;
        source.Play();
    }

    private void Restart()
    {
        if (!source.isPlaying) return;

        source.Stop();
        source.Play();
    }

    // Same test as Checkpoint and ResetVolume, so anything that counts as the player there counts here.
    private static bool IsPlayer(Collider other)
    {
        return other.GetComponentInParent<CharacterController>() != null || other.CompareTag("Player");
    }
}

