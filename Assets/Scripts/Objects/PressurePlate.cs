using UnityEngine;

public class PressurePlate : MonoBehaviour
{
    public bool IsPressed { get; private set; }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Plate detected: " + other.name);

        if (other.CompareTag("Box"))
        {
            IsPressed = true;
            Debug.Log("PLATE PRESSED");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Box"))
        {
            IsPressed = false;
            Debug.Log("PLATE RELEASED");
        }
    }
}