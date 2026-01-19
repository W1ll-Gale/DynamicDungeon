using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

namespace Tests
{
    public class SmoothingTests
    {
        private void InjectRegionMap(TilemapGenerator generator, int[,] regionMap)
        {
            PropertyInfo prop = typeof(TilemapGenerator).GetProperty("CurrentRegionMap");
            if (prop != null)
            {
                prop.SetValue(generator, regionMap);
            }
        }

        private TilemapGenerator CreateGenerator(out BiomeData biome)
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();

            biome = ScriptableObject.CreateInstance<BiomeData>();

            biome.smoothIterations = 5;

            RegionSettings regions = ScriptableObject.CreateInstance<RegionSettings>();
            regions.biomes = new List<WeightedBiome>
            {
                new WeightedBiome { biome = biome, weight = 50 }
            };
            generator.regionSettings = regions;

            return generator;
        }

        [Test]
        public void Helper_CountsNeighborsCorrectly()
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();

            int[,] testMap = new int[3, 3] {
                { 1, 1, 1 },
                { 0, 0, 1 },
                { 1, 0, 1 }
            };

            int count = generator.GetSurroundingWallCount(1, 1, testMap);

            Assert.AreEqual(6, count, "Should count 6 walls around the center tile.");

            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void SmoothMap_RemovesIsolatedWalls()
        {
            BiomeData biome;
            TilemapGenerator generator = CreateGenerator(out biome);

            int w = 5;
            int h = 5;
            int[,] rawMap = new int[w, h];
            rawMap[2, 2] = 1;

            InjectRegionMap(generator, new int[w, h]);

            int[,] smoothedMap = generator.SmoothMapBiomeAware(rawMap, 0);

            Assert.AreEqual(0, smoothedMap[2, 2], "Isolated wall should be removed (neighbors < 4).");

            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void SmoothMap_FillsGaps()
        {
            BiomeData biome;
            TilemapGenerator generator = CreateGenerator(out biome);

            int w = 5;
            int h = 5;
            int[,] rawMap = new int[w, h];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    rawMap[x, y] = 1;
                }
            }
            rawMap[2, 2] = 0;

            InjectRegionMap(generator, new int[w, h]);

            int[,] smoothedMap = generator.SmoothMapBiomeAware(rawMap, 0);

            Assert.AreEqual(1, smoothedMap[2, 2], "Hole surrounded by walls should be filled (neighbors > 4).");

            Object.DestroyImmediate(generator.gameObject);
        }
    }
}