// Anything with state a checkpoint reset must put back. The CheckpointManager calls
// CaptureState when a checkpoint is reached and RestoreState when the player resets, so each
// mechanic owns what "how it was" means for itself and the manager never needs to know.
public interface IResettable
{
    void CaptureState();
    void RestoreState();
}
