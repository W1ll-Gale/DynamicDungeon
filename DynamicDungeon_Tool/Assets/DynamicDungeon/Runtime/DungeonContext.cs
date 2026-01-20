using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Holds the state of the dungeon generation pipeline.
/// Passed between GenerationPasses to build the map layer by layer.
/// </summary>
public class DungeonContext
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string Seed { get; private set; }

    /// <summary>
    /// 0 = Floor, 1 = Wall.
    /// </summary>
    public int[,] MapData { get; set; }

    /// <summary>
    /// Stores the Biome Index for each tile.
    /// </summary>
    public int[,] RegionMap { get; set; }

    /// <summary>
    /// Stores generated resources (Ores, Decorations).
    /// </summary>
    public Dictionary<Vector2Int, TileData> Resources { get; set; }

    public DungeonContext(int width, int height, string seed)
    {
        Width = width;
        Height = height;
        Seed = seed;

        MapData = new int[width, height];
        RegionMap = new int[width, height];
        Resources = new Dictionary<Vector2Int, TileData>();
    }
}