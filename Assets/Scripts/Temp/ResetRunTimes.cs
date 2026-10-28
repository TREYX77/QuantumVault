
using UnityEngine;

public class ResetRunTimes : MonoBehaviour
{
    private void Awake()
    {
        ZoneTimeManager.ResetTimes();
        Debug.Log("All zone times have been reset!");
    }
}

