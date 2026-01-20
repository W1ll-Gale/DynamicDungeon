using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewResourcePass", menuName = "DynamicDungeon/Passes/Resource Pass")]
public class ResourcePass : GenerationPass
{
    public override void Execute(DungeonContext context)
    {
        if (context.MapData == null || context.GlobalRegionSettings == null) return;

        System.Random prng = new System.Random(context.Seed.GetHashCode() + 1);
        int w = context.Width;
        int h = context.Height;
        List<WeightedBiome> biomes = context.GlobalRegionSettings.biomes;

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                int biomeIdx = context.RegionMap[x, y];
                if (biomeIdx >= biomes.Count) continue;

                BiomeData biome = biomes[biomeIdx].biome;
                if (biome == null || biome.resources == null) continue;

                bool isWall = (context.MapData[x, y] == 1);

                foreach (BiomeData.BiomeResource resource in biome.resources)
                {
                    if (resource.resourceTile == null) continue;

                    if (isWall == resource.spawnsInWalls)
                    {
                        if (prng.NextDouble() < resource.spawnChance)
                        {
                            context.Resources[new Vector2Int(x, y)] = resource.resourceTile;
                            break; 
                        }
                    }
                }
            }
        }
        Debug.Log("[ResourcePass] Spawning Resources.");
    }
}