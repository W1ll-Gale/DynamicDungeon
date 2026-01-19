using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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
        int sitesCount = settings.voronoiNumSites;
        Vector2Int[] sites = new Vector2Int[sitesCount];
        int[] siteBiomeIndices = new int[sitesCount];

        for (int i = 0; i < sitesCount; i++)
        {
            sites[i] = new Vector2Int(prng.Next(0, w), prng.Next(0, h));
            siteBiomeIndices[i] = GetWeightedRandomIndex(settings.biomes, prng);
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

    private static int GetWeightedRandomIndex(List<WeightedBiome> biomes, System.Random prng)
    {
        int totalWeight = 0;
        foreach (WeightedBiome biome in biomes) totalWeight += biome.weight;

        int randomValue = prng.Next(0, totalWeight);
        int currentSum = 0;

        for (int i = 0; i < biomes.Count; i++)
        {
            currentSum += biomes[i].weight;
            if (randomValue < currentSum) return i;
        }
        return biomes.Count - 1;
    }

    private static int[,] GeneratePerlin(int w, int h, RegionSettings settings, string seed)
    {
        int[,] map = new int[w, h];
        int biomeCount = settings.biomes.Count;

        float totalWeight = 0;
        foreach (WeightedBiome biome in settings.biomes) totalWeight += biome.weight;

        float[] thresholds = new float[biomeCount];
        float currentSum = 0;
        for (int i = 0; i < biomeCount; i++)
        {
            currentSum += settings.biomes[i].weight;
            thresholds[i] = currentSum / totalWeight;
        }

        System.Random prng = new System.Random(seed.GetHashCode());

        float offsetX = prng.Next(-1000, 1000);
        float offsetY = prng.Next(-1000, 1000);

        int iterations = Mathf.Max(1, settings.octaves);

        float maxPotentialHeight = 0;
        float amp = 1;
        for (int i = 0; i < iterations; i++)
        {
            maxPotentialHeight += amp;
            amp *= settings.persistence;
        }

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                float amplitude = 1;
                float frequency = 1;
                float noiseHeight = 0;

                for (int i = 0; i < iterations; i++)
                {
                    float xCoord = (float)x * settings.perlinScale * frequency + offsetX;
                    float yCoord = (float)y * settings.perlinScale * frequency + offsetY;

                    float perlinValue = Mathf.PerlinNoise(xCoord, yCoord) * 2 - 1;
                    noiseHeight += perlinValue * amplitude;

                    amplitude *= settings.persistence;
                    frequency *= settings.lacunarity;
                }

                float normalizedHeight = (noiseHeight + maxPotentialHeight) / (maxPotentialHeight * 2f);
                float sample = Mathf.Clamp01(normalizedHeight);

                int selectedIndex = 0;
                for (int i = 0; i < biomeCount; i++)
                {
                    if (sample <= thresholds[i])
                    {
                        selectedIndex = i;
                        break;
                    }
                }
                map[x, y] = selectedIndex;
            }
        }
        return map;
    }
}