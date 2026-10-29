
using UnityEngine;
using UnityEngine.UI;

public static class ZoneTimeManager
{
    private const string KeyPrefix = "ZoneTime_";

    public static void SaveCurrentTime(string zoneName)
    {
        Timer timer = Object.FindAnyObjectByType<Timer>();

        if (timer == null)
        {
            Debug.LogWarning("No Timer found in the current scene.");
            return;
        }

        Text timerText = timer.GetComponent<Text>();

        if (timerText == null)
        {
            Debug.LogWarning("The Timer object has no Text component.");
            return;
        }

        PlayerPrefs.SetString(KeyPrefix + zoneName, timerText.text);
        PlayerPrefs.Save();

        Debug.Log(zoneName + " time saved: " + timerText.text);
    }

    public static string GetTime(string zoneName)
    {
        return PlayerPrefs.GetString(
            KeyPrefix + zoneName,
            "--:--.--"
        );
    }

    public static void ResetTimes()
    {
        PlayerPrefs.DeleteKey(KeyPrefix + "Z1L1");
        PlayerPrefs.DeleteKey(KeyPrefix + "Z2L1");
        PlayerPrefs.DeleteKey(KeyPrefix + "Z3L1");
        PlayerPrefs.Save();
    }
}
