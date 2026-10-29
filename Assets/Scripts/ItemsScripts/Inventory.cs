using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Inventory : MonoBehaviour
{
    public List<itemType> inventoryList;
    public int selectedItem;

    [Header("Item Prefabs")]
    [SerializeField] GameObject flashlightItem;
    [SerializeField] GameObject entanglementItem;
    [SerializeField] GameObject waveParticleItem;

    private Dictionary<itemType, GameObject> itemSetActive = new Dictionary<itemType, GameObject>();

    private GameObject puzzleObject;

    private void Start()
    {
        itemSetActive.Add(itemType.Flashlight, flashlightItem);
        itemSetActive.Add(itemType.Entanglement, entanglementItem);
        itemSetActive.Add(itemType.WaveParticle, waveParticleItem);

        puzzleObject = GameObject.FindGameObjectWithTag("Puzzle");

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
        // A slot can be empty, or lose its object when the player prefab is rebuilt.
        foreach (GameObject item in itemSetActive.Values)
        {
            if (item != null) item.SetActive(false);
        }

        if (selectedItem < 0 || selectedItem >= inventoryList.Count) return;

        GameObject selectedItemObject = itemSetActive[inventoryList[selectedItem]];
        selectedItemObject.SetActive(true);

        if (puzzleObject != null)
        {
            puzzleObject.SetActive(
                inventoryList[selectedItem] == itemType.Entanglement
            );
        }
    }
}
