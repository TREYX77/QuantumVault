using UnityEngine;

// A flashlight lying in the level. Using it gives the player a PlayerFlashlight, switched on, puts
// this flashlight's model in their hand, and takes the pickup away. The player is given the
// component if it does not have one yet.
[AddComponentMenu("Items/Flashlight Pickup")]
[DisallowMultipleComponent]
public class FlashlightPickup : MonoBehaviour, IPickup
{
    [SerializeField] private string prompt = "Pick up flashlight";

    [Tooltip("Optional. What goes into the player's hand; its spot light marks the lens. Left " +
             "empty, a child named Model is used, and with none the player gets the beam alone.")]
    [SerializeField] private Transform model;

    public string Prompt => prompt;
    public bool CanInteract => true;

    public void Interact(PlayerInteractor interactor)
    {
        PlayerFlashlight flashlight = interactor.GetComponent<PlayerFlashlight>();

        if (flashlight == null) flashlight = interactor.gameObject.AddComponent<PlayerFlashlight>();

        if (model == null) model = transform.Find("Model");

        // Handed over before the pickup switches off, or the model would vanish with it.
        flashlight.Equip(true, model);
        gameObject.SetActive(false);
    }
}
