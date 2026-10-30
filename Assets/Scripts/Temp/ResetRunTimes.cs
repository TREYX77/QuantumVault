
using UnityEngine;

public class ResetRunTimes : MonoBehaviour
{
    // Times are only wiped where a run begins. This component also sits in the zone scenes,
    // and resetting there would erase the zones already finished before the end screen.
    [SerializeField] private string runStartScene = "StartScene";

    private void Awake()
    {
        if (gameObject.scene.name != runStartScene) return;

        ZoneTimeManager.ResetTimes();
        Debug.Log("All zone times have been reset!");
    }
}

