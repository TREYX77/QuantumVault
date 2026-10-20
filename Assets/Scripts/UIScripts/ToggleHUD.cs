using UnityEngine;

public class ToggleHUD : MonoBehaviour
{
    [Header("Objects to Hide")]
    public GameObject HideHUD;
    public GameObject HideMap;

    [Header("Toggle Keys")]
    public KeyCode HUDKey = KeyCode.H;
    public KeyCode MapKey = KeyCode.M;

    void Update()
    {
        if (Input.GetKeyDown(HUDKey))
        {
            if (HideHUD != null)
                HideHUD.SetActive(!HideHUD.activeSelf);
        }

        if (Input.GetKeyDown(MapKey))
        {
            if (HideMap != null)
                HideMap.SetActive(!HideMap.activeSelf);
        }
    }
}