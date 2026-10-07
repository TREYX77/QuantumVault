using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class PortalRequirement : MonoBehaviour
{
    [SerializeField] private PressurePlate[] requiredPlates;
    [SerializeField] private Portal portal;

    private void Update()
    {
        foreach (PressurePlate plate in requiredPlates)
        {
            if (!plate.IsPressed)
            {
                portal.SetUnlocked(false);
                return;
            }
        }

        portal.SetUnlocked(true);
    }
}