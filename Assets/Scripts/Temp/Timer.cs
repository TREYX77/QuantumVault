using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Text))]
public class Timer : MonoBehaviour
{
    private Text timerText;
    private float elapsed;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        timerText = GetComponent<Text>();
        elapsed = 0f;
    }

    // Update is called once per frame
    void Update()
    {
        elapsed += Time.deltaTime;
        if (timerText != null)
        {
            int minutes = (int)(elapsed / 60f);
            float seconds = elapsed % 60f;
            timerText.text = string.Format("{0:00}:{1:00.00}", minutes, seconds);
        }
    }
}
