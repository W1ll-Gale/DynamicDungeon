using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Represents data for a single tile in the dungeon, including its visual, walkability, 
/// movement cost, and simulation flags.
/// </summary>
[CreateAssetMenu(fileName = "NewTileData", menuName = "DynamicDungeon/Tile Data")]
public class TileData : ScriptableObject
{
    [Header("Identity")]
    /// <summary>
    /// Unique identifier for the tile type.
    /// </summary>
    public string tileID;

    /// <summary>
    /// The visual representation of the tile, used by the Tilemap.
    /// </summary>
    public TileBase tileVisual;

    [Header("Gameplay")]
    /// <summary>
    /// Indicates whether the tile can be walked on.
    /// </summary>
    public bool isWalkable = true;

    /// <summary>
    /// The movement cost for traversing this tile. Higher values indicate more difficult terrain.
    /// </summary>
    public int movementCost = 1;

    [Header("Simulation")]
    /// <summary>
    /// If true, this tile is considered solid during collision checks (e.g., Walls, Ground).
    /// </summary>
    public bool isSolid = true;

    /// <summary>
    /// If true, this tile acts like a fluid (e.g., Water, Lava) for simulation purposes.
    /// </summary>
    public bool isLiquid = false;

    /// <summary>
    /// If true, this tile will fall if there is empty space below it (e.g., Sand, Gravel).
    /// </summary>
    public bool affectedByGravity = false;
}