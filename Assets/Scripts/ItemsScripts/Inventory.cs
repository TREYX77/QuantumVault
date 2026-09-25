using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    public List<itemType> inventoryList;
    public int selectedItem;

    [Header("Keys")]

    [SerializeField] KeyCode throwItemKey;
    [SerializeField] KeyCode grabItemKey;

    [Header("Item Prefabs")]

    [SerializeField] GameObject flashlightItem;
    [SerializeField] GameObject entanglementItem;
    [SerializeField] GameObject waveParticleItem;

    private Dictionary<itemType, GameObject> itemSetActive = new Dictionary<itemType, GameObject>();

    private void Start()
    {
        itemSetActive.Add(itemType.Flashlight, flashlightItem);
        itemSetActive.Add(itemType.Entanglement, entanglementItem);
        itemSetActive.Add(itemType.WaveParticle, waveParticleItem);

        NewItemSelected();
    }


    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) && inventoryList.Count > 0)
        {
            selectedItem = 0;
            NewItemSelected();
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2) && inventoryList.Count > 1)
        {
            selectedItem = 1;
            NewItemSelected();
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3) && inventoryList.Count > 2)
        {
            selectedItem = 2;
            NewItemSelected();
        }
    }

    private void NewItemSelected()
    {
        flashlightItem.SetActive(false);
        entanglementItem.SetActive(false);
        waveParticleItem.SetActive(false);

        GameObject selectedItemObject = itemSetActive[inventoryList[selectedItem]];
        selectedItemObject.SetActive(true);
    }
}
