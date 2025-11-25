using UnityEngine;
using UnityEngine.Tilemaps;
using System;

[ExecuteAlways]
public class TilemapGenerator : MonoBehaviour
{
    [Header("References")]
    public Tilemap tilemap;
    public TileData floorTile;
    public TileData wallTile;

    [Header("Settings")]
    public int width = 100;
    public int height = 100;

    [Range(0, 100)]
    public int randomFillPercent = 45;

    public bool useBorderWalls = true;

    public string seed;
    public bool useRandomSeed = true;

    public int[,] CurrentMapData { get; private set; }

    private static long _autoSeedCounter = 0;

    public void GenerateTilemap()
    {
        InitializeGrid();

        if (width <= 0 || height <= 0)
        {
            Debug.LogError("Cannot generate map: Width and Height must be positive.");
            return;
        }

        if (useRandomSeed)
        {
            seed = GenerateRandomSeed();
        }

        CurrentMapData = GenerateMapData(width, height, seed, randomFillPercent);

        RenderMap(CurrentMapData, tilemap);
    }

    private string GenerateRandomSeed()
    {
        long counter = System.Threading.Interlocked.Increment(ref _autoSeedCounter);
        return $"{DateTime.UtcNow.Ticks:x}_{counter}_{Guid.NewGuid():N}";
    }

    public int[,] GenerateMapData(int w, int h, string currentSeed, int fillPercent)
    {
        if (w <= 0 || h <= 0) return new int[0, 0];

        int[,] newMap = new int[w, h];
        System.Random pseudoRandom = new System.Random(currentSeed.GetHashCode());

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (x == 0 || x == w - 1 || y == 0 || y == h - 1)
                {
                    newMap[x, y] = 1; 
                }
                else
                {
                    newMap[x, y] = (pseudoRandom.Next(0, 100) < fillPercent) ? 1 : 0;
                }
            }
        }
        return newMap;
    }

    public void RenderMap(int[,] mapData, Tilemap mapComponent)
    {
        if (mapComponent == null) return;

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
                    positions[i] = new Vector3Int(x + xOffset, y + yOffset, 0);

                    if (mapData[x, y] == 1)
                    {
                        tileArray[i] = (wallTile != null) ? wallTile.tileVisual : null;
                    }
                    else
                    {
                        tileArray[i] = (floorTile != null) ? floorTile.tileVisual : null;
                    }
                }
            }

            mapComponent.SetTiles(positions, tileArray);
            index += currentBatchSize;
        }
    }

    public void InitializeGrid()
    {
        if (tilemap == null)
        {
            if (GetComponent<Grid>() == null)
                gameObject.AddComponent<Grid>();

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
}