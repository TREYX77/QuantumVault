// Anything the player uses by looking at it and pressing the interact key: doors, pickups, the
// photon emitter. The PlayerInteractor finds it under the crosshair and calls Interact.
public interface IInteractable
{
    // What the on-screen prompt says while aimed at, such as "Open door". Null shows nothing,
    // so a door still disguised as wall gives nothing away.
    string Prompt { get; }

    // False shows the prompt without the key, for things that are seen but locked.
    bool CanInteract { get; }

    void Interact(PlayerInteractor interactor);
}
