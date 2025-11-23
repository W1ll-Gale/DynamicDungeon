using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways] 
public class DungeonGenerator : MonoBehaviour
{
    [Header("References")]
    public Tilemap tilemap;
    public TileData defaultTile; 

    [Header("Settings")]
    public int width = 100;
    public int height = 100;

    public void InitializeGrid()
    {
        if (tilemap == null)
        {
            Grid grid = GetComponent<Grid>();
            if (grid == null) grid = gameObject.AddComponent<Grid>();

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
        InitializeGrid();
        tilemap.ClearAllTiles();

        if (defaultTile == null || defaultTile.tileVisual == null)
        {
            Debug.LogWarning("No Default Tile assigned!");
            return;
        }

        Vector3Int[] positions = new Vector3Int[w * h];
        TileBase[] tileArray = new TileBase[w * h];

        int index = 0;
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                positions[index] = new Vector3Int(x, y, 0);
                tileArray[index] = defaultTile.tileVisual;
                index++;
            }
        }

        tilemap.SetTiles(positions, tileArray);
    }
}
