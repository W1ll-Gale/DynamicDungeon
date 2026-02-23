using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewResourcePass", menuName = "DynamicDungeon/Passes/Resource Pass")]
public class ResourcePass : GenerationPass
{
    public override void Execute(DungeonContext context)
    {
        if (context == null) return;
        if (context.MapData == null || context.RegionMap == null || context.GlobalRegionSettings == null) return;

        int w = context.Width;
        int h = context.Height;
        List<WeightedBiome> biomes = context.GlobalRegionSettings.biomes;

        context.Resources.Clear();

        int seedHash = StableStringHash(context.Seed);

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                int biomeIdx = context.RegionMap[x, y];
                if ((uint)biomeIdx >= (uint)biomes.Count) continue;

                BiomeData biome = biomes[biomeIdx].biome;
                if (biome == null || biome.resources == null || biome.resources.Count == 0) continue;

                bool isWall = context.MapData[x, y] == 1;

                for (int r = 0; r < biome.resources.Count; r++)
                {
                    BiomeData.BiomeResource resource = biome.resources[r];
                    if (resource.resourceTile == null) continue;

                    if (isWall != resource.spawnsInWalls) continue;

                    float roll = Hash01(seedHash, x, y, biomeIdx, r, resource.spawnsInWalls ? 1 : 0);

                    if (roll < resource.spawnChance)
                    {
                        context.Resources[new Vector2Int(x, y)] = resource.resourceTile;
                        break;
                    }
                }
            }
        }

        Debug.Log("[ResourcePass] Spawning Resources.");
    }

    private static int StableStringHash(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        unchecked
        {
            int hash = 23;
            for (int i = 0; i < value.Length; i++)
                hash = (hash * 31) + value[i];
            return hash;
        }
    }

    private static float Hash01(int seedHash, int x, int y, int biomeIdx, int resourceIdx, int flags)
    {
        unchecked
        {
            uint h = 2166136261u; 
            h = (h ^ (uint)seedHash) * 16777619u;
            h = (h ^ (uint)x) * 16777619u;
            h = (h ^ (uint)y) * 16777619u;
            h = (h ^ (uint)biomeIdx) * 16777619u;
            h = (h ^ (uint)resourceIdx) * 16777619u;
            h = (h ^ (uint)flags) * 16777619u;

            uint mantissa = (h >> 8) & 0x00FFFFFFu;
            return mantissa / 16777216f; 
        }
    }
}