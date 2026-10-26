
using UnityEngine;
using UnityEngine.SceneManagement;

public class PortalSceneTransition : MonoBehaviour
{
    [SerializeField] private string currentZone;
    [SerializeField] private string destinationScene;

    private bool transitioning;

    private void OnTriggerEnter(Collider other)
    {
        if (transitioning || !other.CompareTag("Player"))
            return;

        transitioning = true;

        ZoneTimeManager.SaveCurrentTime(currentZone);

        SceneManager.LoadScene(destinationScene);
    }
}

