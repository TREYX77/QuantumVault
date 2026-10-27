using UnityEngine;

// A flashlight lying in the level. Using it gives the player a PlayerFlashlight, switched on, and
// takes the pickup away. The player is given the component if it does not have one yet.
[AddComponentMenu("Items/Flashlight Pickup")]
[DisallowMultipleComponent]
public class FlashlightPickup : MonoBehaviour, IPickup
{
    [SerializeField] private string prompt = "Pick up flashlight";

    public string Prompt => prompt;
    public bool CanInteract => true;

    public void Interact(PlayerInteractor interactor)
    {
        PlayerFlashlight flashlight = interactor.GetComponent<PlayerFlashlight>();

        if (flashlight == null) flashlight = interactor.gameObject.AddComponent<PlayerFlashlight>();

        flashlight.Equip(true);
        gameObject.SetActive(false);
    }
}
