using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class ItemPickup : MonoBehaviour
{
   public ItemSO itemScriptableObj;

    public void GrabItem()
    {
        Destroy(gameObject);
    }
}
