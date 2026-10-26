using UnityEngine;

public class PortalRequirement : MonoBehaviour
{
    [SerializeField] private BoxPressurePlate[] requiredPlates;
    [SerializeField] private Portal portal;

    private void Update()
    {
        foreach (BoxPressurePlate plate in requiredPlates)
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