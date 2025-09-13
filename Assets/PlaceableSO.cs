using UnityEngine;

[CreateAssetMenu(menuName = "Builder/Placeable")]
public class PlaceableSO : ScriptableObject
{
    [Header("Identity")]
    public string id = "Item";
    public string displayName = "Item";
    public Sprite icon;               // shown in the catalog

    [Header("Prefab")]
    public GameObject prefab;

    [Header("Placement")]
    public bool alignToSurfaceNormal = false;
    public float yOffset = 0.02f;
    public float footprintRadius = 0.6f;
    public float rotateStepDegrees = 15f;
}
