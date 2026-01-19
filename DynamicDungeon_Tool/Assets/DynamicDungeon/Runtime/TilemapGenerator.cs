using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class TilemapGenerator : MonoBehaviour
{
    [Header("References")]
    public Tilemap tilemap;

    [Header("Configuration")]
    public RegionSettings regionSettings; 
    public GenerationProfile generationProfile;

    [Header("Map Settings")]
    public int width = 100;
    public int height = 100;
    public bool useBorderWalls = true;

    [Header("Seed Settings")]
    public string seed;
    public bool useRandomSeed = true;

    public int[,] CurrentMapData { get; private set; }
    public int[,] CurrentRegionMap { get; private set; } 
    public Dictionary<Vector2Int, TileData> CurrentResourceData { get; private set; }

    private static long _autoSeedCounter = 0;

    public void GenerateTilemap()
    {
        InitializeGrid();

        if (width <= 0 || height <= 0 || regionSettings == null || regionSettings.biomes.Count == 0)
        {
            Debug.LogError("Cannot generate map: Check Width, Height, and RegionSettings.");
            return;
        }

        if (useRandomSeed) seed = GenerateRandomSeed();

        CurrentRegionMap = RegionGenerator.GenerateRegionMap(width, height, regionSettings, seed);

        CurrentMapData = GenerateBiomeAwareMapData(width, height, seed, useBorderWalls);

        int maxIterations = 0;
        foreach (var b in regionSettings.biomes)
        {
            if (b.smoothIterations > maxIterations) maxIterations = b.smoothIterations;
        }

        for (int i = 0; i < maxIterations; i++)
        {
            CurrentMapData = SmoothMapBiomeAware(CurrentMapData, i);
        }

        CurrentResourceData = GenerateBiomeAwareResources(CurrentMapData, seed);

        RenderMap(CurrentMapData, CurrentRegionMap, CurrentResourceData, tilemap);
    }

    public void InitializeGrid()
    {
        if (tilemap == null)
        {
            if (GetComponent<Grid>() == null) gameObject.AddComponent<Grid>();

            TilemapRenderer rend = GetComponentInChildren<TilemapRenderer>();
            if (rend == null)
            {
                GameObject child = new GameObject("Tilemap");
                child.transform.SetParent(transform);
                tilemap = child.AddComponent<Tilemap>();
                child.AddComponent<TilemapRenderer>();
            }
            else
            {
                tilemap = rend.GetComponent<Tilemap>();
            }
        }
    }

    private static string GenerateRandomSeed()
    {
        long counter = System.Threading.Interlocked.Increment(ref _autoSeedCounter);
        return $"{DateTime.UtcNow.Ticks:x}_{counter}_{Guid.NewGuid():N}";
    }

    public int[,] GenerateBiomeAwareMapData(int w, int h, string currentSeed, bool edgesAreWalls)
    {
        int[,] newMap = new int[w, h];
        System.Random pseudoRandom = new System.Random(currentSeed.GetHashCode());

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (edgesAreWalls && (x == 0 || x == w - 1 || y == 0 || y == h - 1))
                {
                    newMap[x, y] = 1;
                }
                else
                {
                    int biomeIdx = CurrentRegionMap[x, y];
                    BiomeData biome = regionSettings.biomes[biomeIdx];

                    newMap[x, y] = (pseudoRandom.Next(0, 100) < biome.randomFillPercent) ? 1 : 0;
                }
            }
        }
        return newMap;
    }

    public int[,] SmoothMapBiomeAware(int[,] mapData, int currentIterationIndex)
    {
        int w = mapData.GetLength(0);
        int h = mapData.GetLength(1);
        int[,] newMap = new int[w, h];

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (x == 0 || x == w - 1 || y == 0 || y == h - 1)
                {
                    newMap[x, y] = useBorderWalls ? 1 : mapData[x, y];
                    continue;
                }

                int biomeIdx = CurrentRegionMap[x, y];
                BiomeData biome = regionSettings.biomes[biomeIdx];

                if (currentIterationIndex >= biome.smoothIterations)
                {
                    newMap[x, y] = mapData[x, y];
                    continue;
                }

                int neighborWallCount = GetSurroundingWallCount(x, y, mapData);

                if (neighborWallCount > 4) newMap[x, y] = 1;
                else if (neighborWallCount < 4) newMap[x, y] = 0;
                else newMap[x, y] = mapData[x, y];
            }
        }
        return newMap;
    }

    public int GetSurroundingWallCount(int gridX, int gridY, int[,] mapData)
    {
        int wallCount = 0;
        int w = mapData.GetLength(0);
        int h = mapData.GetLength(1);

        for (int neighborX = gridX - 1; neighborX <= gridX + 1; neighborX++)
        {
            for (int neighborY = gridY - 1; neighborY <= gridY + 1; neighborY++)
            {
                if (neighborX == gridX && neighborY == gridY) continue;
                if (neighborX >= 0 && neighborX < w && neighborY >= 0 && neighborY < h)
                    wallCount += mapData[neighborX, neighborY];
                else
                    wallCount++;
            }
        }
        return wallCount;
    }

    public Dictionary<Vector2Int, TileData> GenerateBiomeAwareResources(int[,] mapData, string seed)
    {
        Dictionary<Vector2Int, TileData> resources = new Dictionary<Vector2Int, TileData>();
        System.Random prng = new System.Random(seed.GetHashCode() + 1);

        int w = mapData.GetLength(0);
        int h = mapData.GetLength(1);

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                int biomeIdx = CurrentRegionMap[x, y];
                BiomeData biome = regionSettings.biomes[biomeIdx];

                if (biome.resources == null) continue;

                bool isWall = (mapData[x, y] == 1);

                foreach (BiomeData.BiomeResource resource in biome.resources)
                {
                    if (resource.resourceTile == null) continue;

                    if (isWall == resource.spawnsInWalls)
                    {
                        if (prng.NextDouble() < resource.spawnChance)
                        {
                            resources[new Vector2Int(x, y)] = resource.resourceTile;
                            break; 
                        }
                    }
                }
            }
        }
        return resources;
    }

    public void RenderMap(int[,] mapData, int[,] regionData, Dictionary<Vector2Int, TileData> resourceData, Tilemap mapComponent)
    {
        if (mapComponent == null || regionSettings == null) return;

        mapComponent.ClearAllTiles();

        int w = mapData.GetLength(0);
        int h = mapData.GetLength(1);
        int totalTiles = w * h;
        const int batchSize = 10000;
        int index = 0;
        int xOffset = -(w / 2);
        int yOffset = -(h / 2);

        while (index < totalTiles)
        {
            int currentBatchSize = Mathf.Min(batchSize, totalTiles - index);
            Vector3Int[] positions = new Vector3Int[currentBatchSize];
            TileBase[] tileArray = new TileBase[currentBatchSize];

            for (int i = 0; i < currentBatchSize; i++)
            {
                int flat = index + i;
                int x = flat / h;
                int y = flat % h;

                if (x < w && y < h)
                {
                    Vector2Int gridPos = new Vector2Int(x, y);
                    positions[i] = new Vector3Int(x + xOffset, y + yOffset, 0);

                    int biomeIdx = regionData[x, y];
                    BiomeData biome = regionSettings.biomes[biomeIdx];

                    if (resourceData != null && resourceData.TryGetValue(gridPos, out TileData resTile))
                    {
                        tileArray[i] = resTile.GetTileBase();
                    }
                    else
                    {
                        if (mapData[x, y] == 1)
                            tileArray[i] = (biome.wallTile != null) ? biome.wallTile.GetTileBase() : null;
                        else
                            tileArray[i] = (biome.floorTile != null) ? biome.floorTile.GetTileBase() : null;
                    }
                }
            }

            mapComponent.SetTiles(positions, tileArray);
            index += currentBatchSize;
        }
    }

    public void ClearGeneratedMap()
    {
        if (tilemap != null) tilemap.ClearAllTiles();
        CurrentMapData = null;
        CurrentRegionMap = null;
        CurrentResourceData = null;
    }

    private void OnDrawGizmos()
    {
        if (CurrentRegionMap == null || regionSettings == null) return;

        if (Application.isPlaying || !Application.isEditor) return;

        int w = CurrentRegionMap.GetLength(0);
        int h = CurrentRegionMap.GetLength(1);
        int xOffset = -(w / 2);
        int yOffset = -(h / 2);

        for (int x = 0; x < w; x += 2)
        {
            for (int y = 0; y < h; y += 2)
            {
                int biomeIdx = CurrentRegionMap[x, y];
                if (biomeIdx < regionSettings.biomes.Count)
                {
                    Gizmos.color = regionSettings.biomes[biomeIdx].debugColor;
                    Vector3 pos = new Vector3(x + xOffset + 0.5f, y + yOffset + 0.5f, 0);
                    Gizmos.DrawCube(pos, new Vector3(2, 2, 0.1f));
                }
            }
        }
    }
}