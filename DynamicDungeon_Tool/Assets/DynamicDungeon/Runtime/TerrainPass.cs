using UnityEngine;

[CreateAssetMenu(fileName = "NewTerrainPass", menuName = "DynamicDungeon/Passes/Terrain Pass")]
public class TerrainPass : GenerationPass
{
    [Header("Settings")]
    public bool useBorderWalls = true;

    public override void Execute(DungeonContext context)
    {
        if (context.RegionMap == null)
        {
            Debug.LogError("TerrainPass: RegionMap is null. Run RegionPass first.");
            return;
        }

        if (context.GlobalRegionSettings == null)
        {
            Debug.LogError("TerrainPass: No RegionSettings found in context.");
            return;
        }

        System.Random pseudoRandom = new System.Random(context.Seed.GetHashCode());
        int w = context.Width;
        int h = context.Height;
        var biomes = context.GlobalRegionSettings.biomes;

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (useBorderWalls && (x == 0 || x == w - 1 || y == 0 || y == h - 1))
                {
                    context.MapData[x, y] = 1;
                    continue;
                }

                int biomeIdx = context.RegionMap[x, y];

                if (biomeIdx >= biomes.Count || biomeIdx < 0)
                {
                    context.MapData[x, y] = 1;
                    continue;
                }

                BiomeData biome = biomes[biomeIdx].biome;
                if (biome != null)
                {
                    context.MapData[x, y] = (pseudoRandom.Next(0, 100) < biome.randomFillPercent) ? 1 : 0;
                }
                else
                {
                    context.MapData[x, y] = 1;
                }
            }
        }

        Debug.Log("[TerrainPass] Generated Base Terrain.");
    }
}