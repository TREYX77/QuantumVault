
using UnityEngine;
using TMPro;

public class EndScreenTimes : MonoBehaviour
{
    [SerializeField] private TMP_Text zone1TimeText;
    [SerializeField] private TMP_Text zone2TimeText;
    [SerializeField] private TMP_Text zone3TimeText;

    private void Start()
    {
        zone1TimeText.text = "Zone 1: " +
            ZoneTimeManager.GetTime("Z1L1");

        zone2TimeText.text = "Zone 2: " +
            ZoneTimeManager.GetTime("Z2L1");

        zone3TimeText.text = "Zone 3: " +
            ZoneTimeManager.GetTime("Z3L1");
    }
}
