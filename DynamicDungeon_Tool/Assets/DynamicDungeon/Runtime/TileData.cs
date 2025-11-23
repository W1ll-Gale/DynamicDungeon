using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "NewTileData", menuName = "DynamicDungeon/Tile Data")]
public class TileData : ScriptableObject
{
    public string tileID;
    public TileBase tileVisual; 
    public bool isWalkable = true;
    public int movementCost = 1;
}
