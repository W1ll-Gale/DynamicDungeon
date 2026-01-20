using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class TilemapGenerator : MonoBehaviour
{
    [Header("References")]
    public Tilemap tilemap;

    [Header("Pipeline")]
    public List<GenerationPass> generationPipeline = new List<GenerationPass>();

    [Header("Map Settings")]
    public int width = 100;
    public int height = 100;

    [Header("Seed Settings")]
    public string seed;
    public bool useRandomSeed = true;

    public DungeonContext Context { get; private set; }
    public int[,] CurrentMapData => Context?.MapData;
    public int[,] CurrentRegionMap => Context?.RegionMap;
    public Dictionary<Vector2Int, TileData> CurrentResourceData => Context?.Resources;

    private static long _autoSeedCounter = 0;

    public void GenerateTilemap(bool preserveSeed = false)
    {
        InitializeGrid();

        if (width <= 0 || height <= 0)
        {
            Debug.LogError("Invalid dimensions.");
            return;
        }

        if (string.IsNullOrEmpty(seed) || (useRandomSeed && !preserveSeed))
        {
            seed = GenerateRandomSeed();
        }

        Context = new DungeonContext(width, height, seed);

        foreach (GenerationPass pass in generationPipeline)
        {
            if (pass != null && pass.enabled)
            {
                pass.Execute(Context);
            }
        }

        if (Context.MapData != null)
        {
            RenderMap();
        }
    }

    public void InitializeGrid()
    {
        if (tilemap == null)
        {
            if (GetComponent<Grid>() == null) gameObject.AddComponent<Grid>();
            TilemapRenderer rend = GetComponentInChildren<TilemapRenderer>();
            tilemap = rend != null ? rend.GetComponent<Tilemap>() : new GameObject("Tilemap").AddComponent<Tilemap>();
            if (tilemap.transform.parent == null) tilemap.transform.SetParent(transform);
            if (tilemap.GetComponent<TilemapRenderer>() == null) tilemap.gameObject.AddComponent<TilemapRenderer>();
        }
    }

    private void RenderMap()
    {
        tilemap.ClearAllTiles();

        RegionSettings settings = Context.GlobalRegionSettings;
        if (settings == null) return;

        int w = width;
        int h = height;

        TileBase[] tileArray = new TileBase[w * h];
        Vector3Int[] positions = new Vector3Int[w * h];
        int idx = 0;
        int xOffset = -(w / 2);
        int yOffset = -(h / 2);

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                positions[idx] = new Vector3Int(x + xOffset, y + yOffset, 0);

                int biomeIdx = Context.RegionMap[x, y];

                if (biomeIdx < settings.biomes.Count)
                {
                    BiomeData biome = settings.biomes[biomeIdx].biome;
                    if (biome != null)
                    {
                        if (Context.Resources.TryGetValue(new Vector2Int(x, y), out TileData res))
                        {
                            tileArray[idx] = res.GetTileBase();
                        }
                        else
                        {
                            bool isWall = Context.MapData[x, y] == 1;
                            tileArray[idx] = isWall ? biome.wallTile?.GetTileBase() : biome.floorTile?.GetTileBase();
                        }
                    }
                }
                idx++;
            }
        }
        tilemap.SetTiles(positions, tileArray);
    }

    public void ClearGeneratedMap()
    {
        if (tilemap != null) tilemap.ClearAllTiles();
        Context = null;
    }

    private static string GenerateRandomSeed()
    {
        long counter = System.Threading.Interlocked.Increment(ref _autoSeedCounter);
        return $"{DateTime.UtcNow.Ticks:x}_{counter}_{Guid.NewGuid():N}";
    }

    private void OnDrawGizmos()
    {
        if (Context?.RegionMap == null || Context.GlobalRegionSettings == null) return;
        if (Application.isPlaying || !Application.isEditor) return;

        RegionSettings settings = Context.GlobalRegionSettings;
        int w = Context.RegionMap.GetLength(0);
        int h = Context.RegionMap.GetLength(1);
        int xOffset = -(w / 2);
        int yOffset = -(h / 2);

        for (int x = 0; x < w; x += 4)
        {
            for (int y = 0; y < h; y += 4)
            {
                int idx = Context.RegionMap[x, y];
                if (idx < settings.biomes.Count && settings.biomes[idx].biome != null)
                {
                    Gizmos.color = settings.biomes[idx].biome.debugColor;
                    Gizmos.DrawCube(new Vector3(x + xOffset + 0.5f, y + yOffset + 0.5f, 0), new Vector3(4, 4, 0.1f));
                }
            }
        }
    }
}