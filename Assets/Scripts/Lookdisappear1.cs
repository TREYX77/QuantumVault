using UnityEngine;

public class LookDisappear : MonoBehaviour
{
    // Put the objects to disappear in this list.
    [SerializeField] private GameObject[] objectsToDisappear;

    // Maximum raycast distance.
    [SerializeField] private float maxDistance = 100f;

    private GameObject hiddenObject;
    private Renderer[] hiddenRenderers;


    private void Update()
    {
        // Ray starts at camera and goes forward
        Ray ray = new Ray(transform.position, transform.forward);

        // Raycast into the scene.
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
        {
            //  object the raycast hits.
            GameObject hitObject = hit.collider.gameObject;

            // Check whether object is in selected list.
            if (IsSelected(hitObject))
            {
                Debug.Log("SELECTED OBJECT: " + hitObject.name);

                // Don't repeatedly hide same object.
                if (hiddenObject != hitObject)
                {
                    // Show previous object.
                    ShowObject();

                    // Remember  object.
                    hiddenObject = hitObject;

                    // Get renderer.
                    hiddenRenderers =
                        hiddenObject.GetComponentsInChildren<Renderer>();

                    // Hide it.
                    foreach (Renderer renderer in hiddenRenderers)
                    {
                        renderer.enabled = false;
                    }

                    Debug.Log("HIDING: " + hitObject.name);
                }

                return;
            }
        }

        // If not longer looking at selected object,
        // makeprevious one visible again.
        ShowObject();
    }


    private bool IsSelected(GameObject objectHit)
    {
        foreach (GameObject selectedObject in objectsToDisappear)
        {
            if (selectedObject == objectHit)
            {
                return true;
            }
        }

        return false;
    }


    private void ShowObject()
    {
        if (hiddenRenderers == null)
        {
            return;
        }

        foreach (Renderer renderer in hiddenRenderers)
        {
            if (renderer != null)
            {
                renderer.enabled = true;
            }
        }

        hiddenObject = null;
        hiddenRenderers = null;
    }
}