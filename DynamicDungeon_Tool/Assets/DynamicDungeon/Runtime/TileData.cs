using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Represents data for a single tile in the dungeon, including its visual, walkability, and movement cost.
/// </summary>
[CreateAssetMenu(fileName = "NewTileData", menuName = "DynamicDungeon/Tile Data")]
public class TileData : ScriptableObject
{
    /// <summary>
    /// Unique identifier for the tile type.
    /// </summary>
    public string tileID;

    /// <summary>
    /// The visual representation of the tile, used by the Tilemap.
    /// </summary>
    public TileBase tileVisual; 

    /// <summary>
    /// Indicates whether the tile can be walked on.
    /// </summary>
    public bool isWalkable = true;

    /// <summary>
    /// The movement cost for traversing this tile. Higher values indicate more difficult terrain.
    /// </summary>
    public int movementCost = 1;
}
