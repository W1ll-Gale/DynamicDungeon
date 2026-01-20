using UnityEngine;
using System.Collections.Generic;

public class DungeonContext
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string Seed { get; private set; }

    public int[,] MapData { get; set; }
    public int[,] RegionMap { get; set; }
    public Dictionary<Vector2Int, TileData> Resources { get; set; }

    public RegionSettings GlobalRegionSettings { get; set; }

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