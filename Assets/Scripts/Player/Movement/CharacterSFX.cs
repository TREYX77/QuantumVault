using UnityEngine;

// Plays the player's sound effects. Lives on the same GameObject as the Animator, so animation
// events can reach it. State sounds come from PlaySfxOnStateEnter. Footsteps come from the
// running state's progress, or from run clip events when Auto Footsteps is off.
[RequireComponent(typeof(AudioSource))]
public class CharacterSFX : MonoBehaviour
{
    public enum Sfx
    {
        RunStart,
        Jump
    }

    [Header("Clips")]
    [SerializeField] private AudioClip runStart;
    [SerializeField] private AudioClip jump;

    [Tooltip("One is picked at random per step, never the same twice in a row.")]
    [SerializeField] private AudioClip[] footsteps;

    [Header("Footsteps")]
    [Tooltip("Plays steps from the running state's progress. Turn off to use Footstep " +
             "animation events on the run clip instead.")]
    [SerializeField] private bool autoFootsteps = true;

    [Tooltip("Name of the animator state that plays the running clip.")]
    [SerializeField] private string runningState = "Running";

    [Tooltip("Steps per loop of the running clip. A run cycle is normally two, one per foot.")]
    [SerializeField] private int stepsPerCycle = 2;

    [Header("Mix")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Tooltip("Random pitch spread around 1, so repeated steps do not sound identical.")]
    [SerializeField] private float pitchVariation = 0.05f;

    private Animator animator;
    private AudioSource source;
    private int lastFootstep = -1;
    private int lastStep = -1;

    void Awake()
    {
        animator = GetComponent<Animator>();
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
    }

    void Update()
    {
        if (autoFootsteps) UpdateFootsteps();
    }

    // Called by PlaySfxOnStateEnter when the animator enters a state.
    public void Play(Sfx sfx)
    {
        switch (sfx)
        {
            case Sfx.RunStart: PlayClip(runStart); break;
            case Sfx.Jump: PlayClip(jump); break;
        }
    }

    // Animation event on the run clip. Takes the event so that, while the animator blends
    // between clips, only the clip that is mostly playing makes the sound.
    public void Footstep(AnimationEvent animationEvent)
    {
        if (autoFootsteps || animationEvent.animatorClipInfo.weight < 0.5f) return;

        PlayFootstep();
    }

    private void UpdateFootsteps()
    {
        if (animator == null || stepsPerCycle < 1) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);

        // Outside the running state, or while blending out of it, there are no steps.
        if (!state.IsName(runningState) || animator.IsInTransition(0))
        {
            lastStep = -1;
            return;
        }

        int step = Mathf.FloorToInt(state.normalizedTime * stepsPerCycle);

        // The first frame only records where the cycle is, so entering the state is not a step.
        if (lastStep < 0)
        {
            lastStep = step;
            return;
        }

        if (step == lastStep) return;

        lastStep = step;
        PlayFootstep();
    }

    private void PlayFootstep()
    {
        if (footsteps == null || footsteps.Length == 0) return;

        int index = Random.Range(0, footsteps.Length);

        if (footsteps.Length > 1 && index == lastFootstep) index = (index + 1) % footsteps.Length;

        lastFootstep = index;
        PlayClip(footsteps[index]);
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip == null) return;

        source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        source.PlayOneShot(clip, volume);
    }
}
