using UnityEngine;
using UnityEngine.InputSystem;

public class Portal : MonoBehaviour
{
    [SerializeField] private Transform destination;
    [SerializeField] private bool unlocked = false;

    private bool playerNearby;

    private void Update()
    {
        if (unlocked && playerNearby && Keyboard.current.eKey.wasPressedThisFrame)
        {
            TeleportPlayer();
        }
    }

    public void SetUnlocked(bool value)
    {
        unlocked = value;
    }

    private void TeleportPlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");

        if (player != null && destination != null)
        {
            player.transform.position = destination.position;
            player.transform.rotation = destination.rotation;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerNearby = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerNearby = false;
        }
    }
}