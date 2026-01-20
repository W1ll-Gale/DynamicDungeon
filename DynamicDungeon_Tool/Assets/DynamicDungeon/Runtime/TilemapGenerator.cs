using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class TilemapGenerator : MonoBehaviour
{
    [Header("References")]
    public Tilemap tilemap;

    [Header("Pipeline Configuration")]
    [Tooltip("The ordered list of passes to execute.")]
    public List<GenerationPass> generationPipeline = new List<GenerationPass>();

    [HideInInspector, Obsolete("Moved to RegionPass")]
    public RegionSettings regionSettings;
    [HideInInspector, Obsolete("Moved to TerrainPass (TBD)")]
    public GenerationProfile generationProfile;

    [Header("Map Settings")]
    public int width = 100;
    public int height = 100;
    public bool useBorderWalls = true;

    [Header("Seed Settings")]
    public string seed;
    public bool useRandomSeed = true;

    public int[,] CurrentMapData => _context?.MapData;
    public int[,] CurrentRegionMap => _context?.RegionMap;
    public Dictionary<Vector2Int, TileData> CurrentResourceData => _context?.Resources;

    private DungeonContext _context;
    private static long _autoSeedCounter = 0;

    public void GenerateTilemap(bool preserveSeed = false)
    {
        InitializeGrid();

        if (string.IsNullOrEmpty(seed) || (useRandomSeed && !preserveSeed))
        {
            seed = GenerateRandomSeed();
        }

        _context = new DungeonContext(width, height, seed);

        if (generationPipeline == null || generationPipeline.Count == 0)
        {
            Debug.LogWarning("TilemapGenerator: Pipeline is empty!");
        }
        else
        {
            foreach (var pass in generationPipeline)
            {
                if (pass != null && pass.enabled)
                {
                    pass.Execute(_context);
                }
            }
        }

        if (_context.RegionMap != null && IsMapEmpty(_context.MapData))
        {
            RegionSettings activeSettings = GetRegionSettingsFromPipeline();

            if (activeSettings != null)
            {
                _context.MapData = GenerateBiomeAwareMapData(width, height, seed, useBorderWalls, activeSettings);

                for (int i = 0; i < 5; i++)
                {
                    _context.MapData = SmoothMapBiomeAware(_context.MapData, i, activeSettings);
                }

                _context.Resources = GenerateBiomeAwareResources(_context.MapData, seed, activeSettings);
            }
        }

        if (_context != null && _context.MapData != null)
        {
            RegionSettings renderSettings = GetRegionSettingsFromPipeline();
            RenderMap(_context.MapData, _context.RegionMap, _context.Resources, tilemap, renderSettings);
        }
    }

    private RegionSettings GetRegionSettingsFromPipeline()
    {
        foreach (var pass in generationPipeline)
        {
            if (pass is RegionPass rp) return rp.regionSettings;
        }
        return this.regionSettings;
    }

    private bool IsMapEmpty(int[,] map)
    {
        return map[0, 0] == 0 && map[width / 2, height / 2] == 0;
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

    private static string GenerateRandomSeed()
    {
        long counter = System.Threading.Interlocked.Increment(ref _autoSeedCounter);
        return $"{DateTime.UtcNow.Ticks:x}_{counter}_{Guid.NewGuid():N}";
    }

    public int[,] GenerateBiomeAwareMapData(int w, int h, string currentSeed, bool edgesAreWalls, RegionSettings settings)
    {
        if (settings == null) return new int[w, h];
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
                    int biomeIdx = _context.RegionMap[x, y];
                    if (biomeIdx < settings.biomes.Count)
                    {
                        BiomeData biome = settings.biomes[biomeIdx].biome;
                        newMap[x, y] = (pseudoRandom.Next(0, 100) < biome.randomFillPercent) ? 1 : 0;
                    }
                    else newMap[x, y] = 1;
                }
            }
        }
        return newMap;
    }

    public int[,] SmoothMapBiomeAware(int[,] mapData, int currentIterationIndex, RegionSettings settings)
    {
        if (settings == null) return mapData;
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

                int biomeIdx = _context.RegionMap[x, y];
                if (biomeIdx >= settings.biomes.Count) { newMap[x, y] = mapData[x, y]; continue; }

                BiomeData biome = settings.biomes[biomeIdx].biome;
                if (currentIterationIndex >= biome.smoothIterations) { newMap[x, y] = mapData[x, y]; continue; }

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
                else wallCount++;
            }
        }
        return wallCount;
    }

    public Dictionary<Vector2Int, TileData> GenerateBiomeAwareResources(int[,] mapData, string seed, RegionSettings settings)
    {
        Dictionary<Vector2Int, TileData> resources = new Dictionary<Vector2Int, TileData>();
        if (settings == null) return resources;
        System.Random prng = new System.Random(seed.GetHashCode() + 1);
        int w = mapData.GetLength(0);
        int h = mapData.GetLength(1);

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                int biomeIdx = _context.RegionMap[x, y];
                if (biomeIdx >= settings.biomes.Count) continue;
                BiomeData biome = settings.biomes[biomeIdx].biome;
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

    public void RenderMap(int[,] mapData, int[,] regionData, Dictionary<Vector2Int, TileData> resourceData, Tilemap mapComponent, RegionSettings settings)
    {
        if (mapComponent == null || settings == null) return;
        mapComponent.ClearAllTiles();
        int w = mapData.GetLength(0);
        int h = mapData.GetLength(1);

        Vector3Int[] positions = new Vector3Int[w * h];
        TileBase[] tileArray = new TileBase[w * h];
        int idx = 0;
        int xOffset = -(w / 2);
        int yOffset = -(h / 2);

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                positions[idx] = new Vector3Int(x + xOffset, y + yOffset, 0);
                int biomeIdx = regionData[x, y];
                if (biomeIdx < settings.biomes.Count)
                {
                    BiomeData biome = settings.biomes[biomeIdx].biome;
                    if (resourceData != null && resourceData.TryGetValue(new Vector2Int(x, y), out TileData resTile))
                        tileArray[idx] = resTile.GetTileBase();
                    else
                        tileArray[idx] = (mapData[x, y] == 1) ? (biome.wallTile?.GetTileBase()) : (biome.floorTile?.GetTileBase());
                }
                idx++;
            }
        }
        mapComponent.SetTiles(positions, tileArray);
    }

    public void ClearGeneratedMap()
    {
        if (tilemap != null) tilemap.ClearAllTiles();
        _context = null;
    }

    private void OnDrawGizmos()
    {
        if (_context?.RegionMap == null) return;
        RegionSettings settings = GetRegionSettingsFromPipeline();
        if (settings == null) return;

        if (Application.isPlaying || !Application.isEditor) return;

        int w = _context.RegionMap.GetLength(0);
        int h = _context.RegionMap.GetLength(1);
        int xOffset = -(w / 2);
        int yOffset = -(h / 2);

        for (int x = 0; x < w; x += 4)
        {
            for (int y = 0; y < h; y += 4)
            {
                int biomeIdx = _context.RegionMap[x, y];
                if (biomeIdx < settings.biomes.Count)
                {
                    Gizmos.color = settings.biomes[biomeIdx].biome.debugColor;
                    Vector3 pos = new Vector3(x + xOffset + 0.5f, y + yOffset + 0.5f, 0);
                    Gizmos.DrawCube(pos, new Vector3(4, 4, 0.1f));
                }
            }
        }
    }
}