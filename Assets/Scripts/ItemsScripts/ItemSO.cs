using UnityEngine;

[CreateAssetMenu(fileName = "Item", menuName = "Items")]
public class ItemSO : MonoBehaviour
{
    [Header("Properties")]
    public itemType item_Type;
    public Sprite item_sprite;
}

public enum itemType
{
    Flashlight,
    Entanglement,
    WaveParticle
};