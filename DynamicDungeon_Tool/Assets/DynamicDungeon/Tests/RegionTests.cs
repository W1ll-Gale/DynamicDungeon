using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

namespace Tests
{
    public class RegionTests
    {
        private RegionSettings CreateMockSettings(int biomeCount)
        {
            RegionSettings settings = ScriptableObject.CreateInstance<RegionSettings>();
            settings.biomes = new List<WeightedBiome>();
            for (int i = 0; i < biomeCount; i++)
            {
                settings.biomes.Add(new WeightedBiome
                {
                    biome = ScriptableObject.CreateInstance<BiomeData>(),
                    weight = 10
                });
            }
            return settings;
        }

        [Test]
        public void Voronoi_Generates_ValidIndices()
        {
            RegionSettings settings = CreateMockSettings(2); 
            settings.algorithm = RegionAlgorithm.Voronoi;
            settings.voronoiNumSites = 5;

            int w = 50;
            int h = 50;
            int[,] regions = RegionGenerator.GenerateRegionMap(w, h, settings, "testSeed");

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    Assert.GreaterOrEqual(regions[x, y], 0);
                    Assert.Less(regions[x, y], 2); 
                }
            }
        }

        [Test]
        public void Voronoi_Seed_Is_Deterministic()
        {
            RegionSettings settings = CreateMockSettings(3);
            settings.algorithm = RegionAlgorithm.Voronoi;
            settings.voronoiNumSites = 10;
            int w = 20;
            int h = 20;

            int[,] map1 = RegionGenerator.GenerateRegionMap(w, h, settings, "FixedSeed");
            int[,] map2 = RegionGenerator.GenerateRegionMap(w, h, settings, "FixedSeed");

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    Assert.AreEqual(map1[x, y], map2[x, y], "Maps should be identical for same seed.");
                }
            }
        }

        [Test]
        public void Perlin_Generates_Map_Within_Bounds()
        {
            RegionSettings settings = CreateMockSettings(5);
            settings.algorithm = RegionAlgorithm.PerlinNoise;
            settings.perlinScale = 0.2f;

            int w = 50;
            int h = 50;
            int[,] regions = RegionGenerator.GenerateRegionMap(w, h, settings, "noiseSeed");

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    Assert.GreaterOrEqual(regions[x, y], 0);
                    Assert.Less(regions[x, y], 5);
                }
            }
        }

        [Test]
        public void Weighted_Biomes_Respect_Rarity()
        {
            RegionSettings settings = ScriptableObject.CreateInstance<RegionSettings>();
            settings.biomes = new List<WeightedBiome>();

            settings.biomes.Add(new WeightedBiome
            {
                biome = ScriptableObject.CreateInstance<BiomeData>(),
                weight = 100
            });

            settings.biomes.Add(new WeightedBiome
            {
                biome = ScriptableObject.CreateInstance<BiomeData>(),
                weight = 1
            });

            settings.algorithm = RegionAlgorithm.Voronoi;
            settings.voronoiNumSites = 1000; 

            int w = 100;
            int h = 100;

            int[,] map = RegionGenerator.GenerateRegionMap(w, h, settings, "SeedForWeights");

            int commonCount = 0;
            int rareCount = 0;

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (map[x, y] == 0) commonCount++;
                    else rareCount++;
                }
            }

            Assert.Greater(commonCount, rareCount, "Common biome should appear more often than rare biome.");
        }
    }
}