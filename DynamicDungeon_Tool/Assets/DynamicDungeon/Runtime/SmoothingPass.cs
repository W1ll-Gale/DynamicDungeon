using UnityEngine;

[CreateAssetMenu(fileName = "NewSmoothingPass", menuName = "DynamicDungeon/Passes/Smoothing Pass")]
public class SmoothingPass : GenerationPass
{
    [Tooltip("Maximum iterations any biome might require.")]
    public int maxGlobalIterations = 5;

    public bool useBorderWalls = true;

    public override void Execute(DungeonContext context)
    {
        if (context.MapData == null || context.GlobalRegionSettings == null) return;

        for (int i = 0; i < maxGlobalIterations; i++)
        {
            context.MapData = SmoothStep(context, i);
        }
        Debug.Log($"[SmoothingPass] Completed {maxGlobalIterations} smoothing iterations.");
    }

    private int[,] SmoothStep(DungeonContext context, int currentIteration)
    {
        int w = context.Width;
        int h = context.Height;
        int[,] oldMap = context.MapData;
        int[,] newMap = new int[w, h];
        var biomes = context.GlobalRegionSettings.biomes;

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (x == 0 || x == w - 1 || y == 0 || y == h - 1)
                {
                    newMap[x, y] = useBorderWalls ? 1 : oldMap[x, y];
                    continue;
                }

                int biomeIdx = context.RegionMap[x, y];
                if (biomeIdx >= biomes.Count)
                {
                    newMap[x, y] = oldMap[x, y];
                    continue;
                }

                BiomeData biome = biomes[biomeIdx].biome;

                if (biome == null || currentIteration >= biome.smoothIterations)
                {
                    newMap[x, y] = oldMap[x, y];
                    continue;
                }

                int walls = GetSurroundingWallCount(x, y, oldMap, w, h);

                if (walls > 4) newMap[x, y] = 1;
                else if (walls < 4) newMap[x, y] = 0;
                else newMap[x, y] = oldMap[x, y];
            }
        }
        return newMap;
    }

    private int GetSurroundingWallCount(int gridX, int gridY, int[,] map, int w, int h)
    {
        int count = 0;
        for (int nx = gridX - 1; nx <= gridX + 1; nx++)
        {
            for (int ny = gridY - 1; ny <= gridY + 1; ny++)
            {
                if (nx == gridX && ny == gridY) continue;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                    count += map[nx, ny];
                else
                    count++; 
            }
        }
        return count;
    }
}