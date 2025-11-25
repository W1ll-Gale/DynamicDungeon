using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways] 
public class TilemapGenerator : MonoBehaviour
{
    [Header("References")]
    public Tilemap tilemap;
    public TileData floorTile; 
    public TileData wallTile;

    [HideInInspector] public TileData defaultTile;

    [Header("Settings")]
    public int width = 100;
    public int height = 100;

    [Range(0, 100)]
    public int randomFillPercent = 45;

    public string seed;
    public bool useRandomSeed = true;

    private int[,] map;

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

    public void GenerateEmptyMap(int w, int h)
    {
        if (w <= 0 || h <= 0)
        {
            Debug.LogWarning("Width and height must be positive values!");
            return;
        }

        InitializeGrid();
        tilemap.ClearAllTiles();

        if (defaultTile == null || defaultTile.tileVisual == null)
        {
            Debug.LogWarning("No Default Tile assigned!");
            return;
        }

        const int batchSize = 10000; 
        int totalTiles = w * h;
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
                positions[i] = new Vector3Int(x + xOffset, y + yOffset, 0);
                tileArray[i] = defaultTile.tileVisual;
            }

            tilemap.SetTiles(positions, tileArray);
            index += currentBatchSize;
        }

        if (totalTiles > batchSize * 10)
        {
            Debug.Log("Large map generated in batches for performance. Total tiles: " + totalTiles);
        }
    }

    public void GenerateTilemap()
    {
        InitializeGrid();

        if (useRandomSeed)
        {
            seed = System.DateTime.Now.Ticks.ToString();
        }

        map = GenerateMapData(width, height, seed, randomFillPercent);

        RenderMap(map, tilemap);
    }

    public int[,] GenerateMapData(int w, int h, string currentSeed, int fillPercent)
    {
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
}
