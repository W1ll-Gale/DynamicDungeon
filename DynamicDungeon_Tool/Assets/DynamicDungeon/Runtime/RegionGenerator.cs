using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Static helper to generate biome region maps (integers representing indices in a biome list).
/// </summary>
public static class RegionGenerator
{
    public static int[,] GenerateRegionMap(int width, int height, RegionSettings settings, string seed)
    {
        if (settings == null || settings.biomes == null || settings.biomes.Count == 0)
        {
            Debug.LogError("RegionGenerator: No settings or biomes provided.");
            return new int[width, height]; 
        }

        System.Random prng = new System.Random(seed.GetHashCode());

        switch (settings.algorithm)
        {
            case RegionAlgorithm.Voronoi:
                return GenerateVoronoi(width, height, settings, prng);
            case RegionAlgorithm.PerlinNoise:
                return GeneratePerlin(width, height, settings, seed);
            default:
                return new int[width, height];
        }
    }

    private static int[,] GenerateVoronoi(int w, int h, RegionSettings settings, System.Random prng)
    {
        int[,] map = new int[w, h];
        int biomeCount = settings.biomes.Count;
        int sitesCount = settings.voronoiNumSites;

        Vector2Int[] sites = new Vector2Int[sitesCount];
        int[] siteBiomeIndices = new int[sitesCount];

        for (int i = 0; i < sitesCount; i++)
        {
            sites[i] = new Vector2Int(prng.Next(0, w), prng.Next(0, h));
            siteBiomeIndices[i] = prng.Next(0, biomeCount);
        }

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                int closestSiteIndex = 0;
                float minDst = float.MaxValue;

                for (int i = 0; i < sitesCount; i++)
                {
                    float dst = Vector2Int.Distance(new Vector2Int(x, y), sites[i]);
                    if (dst < minDst)
                    {
                        minDst = dst;
                        closestSiteIndex = i;
                    }
                }

                map[x, y] = siteBiomeIndices[closestSiteIndex];
            }
        }

        return map;
    }

    private static int[,] GeneratePerlin(int w, int h, RegionSettings settings, string seed)
    {
        int[,] map = new int[w, h];
        int biomeCount = settings.biomes.Count;

        System.Random prng = new System.Random(seed.GetHashCode());
        float offsetX = prng.Next(-10000, 10000);
        float offsetY = prng.Next(-10000, 10000);

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                float xCoord = (float)x * settings.perlinScale + offsetX;
                float yCoord = (float)y * settings.perlinScale + offsetY;

                float sample = Mathf.Clamp01(Mathf.PerlinNoise(xCoord, yCoord));

                int biomeIndex = Mathf.FloorToInt(sample * biomeCount);
                if (biomeIndex >= biomeCount) biomeIndex = biomeCount - 1;

                map[x, y] = biomeIndex;
            }
        }

        return map;
    }
}