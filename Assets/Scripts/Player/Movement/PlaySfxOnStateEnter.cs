using UnityEngine;

// Add to an Animator state to play a CharacterSFX sound each time that state is entered.
public class PlaySfxOnStateEnter : StateMachineBehaviour
{
    private static readonly int VerticalVelocityHash = Animator.StringToHash("VerticalVelocity");

    [SerializeField] private CharacterSFX.Sfx sound;

    [Tooltip("Only play while the player is moving upward. Tick it on the Jump state, so " +
             "walking off a ledge does not sound like a jump.")]
    [SerializeField] private bool onlyWhenRising;

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (onlyWhenRising && animator.GetFloat(VerticalVelocityHash) <= 0.1f) return;

        CharacterSFX sfx = animator.GetComponent<CharacterSFX>();

        if (sfx != null) sfx.Play(sound);
    }
}