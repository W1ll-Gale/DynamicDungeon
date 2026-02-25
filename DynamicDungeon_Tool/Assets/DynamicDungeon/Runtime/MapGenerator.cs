using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class MapGenerator : MonoBehaviour
{
    [Header("Configuration")]
    public int width = 128;
    public int height = 128;
    public string seed;
    public bool randomSeed = true;

    [Header("Output")]
    public Tilemap targetTilemap;

    public ThemeProfile themeProfile;

    [Header("Pipeline")]
    public List<GenModule> modules = new List<GenModule>();

    public void Generate()
    {
        if (randomSeed) seed = System.DateTime.Now.Ticks.ToString();

        GenMap map = new GenMap(width, height, seed);

        foreach (GenModule module in modules)
        {
            if (module != null && module.enabled)
            {
                module.Execute(map);
            }
        }

        RenderMap(map);
    }

    private void RenderMap(GenMap map)
    {
        if (targetTilemap == null || themeProfile == null) return;

        targetTilemap.ClearAllTiles();
        int[,] grid = map.GetIntLayer("Main");

        TileBase[] tileArray = new TileBase[width * height];
        Vector3Int[] positions = new Vector3Int[width * height];

        int i = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                positions[i] = new Vector3Int(x, y, 0);
                int tileID = grid[x, y];

                tileArray[i] = themeProfile.GetTile(tileID);
                i++;
            }
        }

        targetTilemap.SetTiles(positions, tileArray);
    }
}
