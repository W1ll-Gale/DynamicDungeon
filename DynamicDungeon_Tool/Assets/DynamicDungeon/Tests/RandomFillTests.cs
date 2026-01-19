using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

namespace Tests
{
    public class RandomFillTests
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

            RegionSettings regions = ScriptableObject.CreateInstance<RegionSettings>();
            regions.biomes = new List<BiomeData> { biome };
            generator.regionSettings = regions;

            return generator;
        }

        [Test]
        public void Map_Borders_Are_Always_Walls()
        {
            BiomeData biome;
            TilemapGenerator generator = CreateGenerator(out biome);

            int width = 50;
            int height = 50;

            biome.randomFillPercent = 50;

            InjectRegionMap(generator, new int[width, height]);

            int[,] map = generator.GenerateBiomeAwareMapData(width, height, "testSeed", true);

            for (int x = 0; x < width; x++)
            {
                Assert.AreEqual(1, map[x, 0], $"Bottom border at {x} must be Wall");
                Assert.AreEqual(1, map[x, height - 1], $"Top border at {x} must be Wall");
            }

            for (int y = 0; y < height; y++)
            {
                Assert.AreEqual(1, map[0, y], $"Left border at {y} must be Wall");
                Assert.AreEqual(1, map[width - 1, y], $"Right border at {y} must be Wall");
            }

            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void Map_Fill_Ratio_Is_Within_Tolerance()
        {
            BiomeData biome;
            TilemapGenerator generator = CreateGenerator(out biome);

            int width = 100;
            int height = 100;
            int fillPercent = 45;

            biome.randomFillPercent = fillPercent;

            InjectRegionMap(generator, new int[width, height]);

            int[,] map = generator.GenerateBiomeAwareMapData(width, height, "seed12345", true);

            int wallCount = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (map[x, y] == 1) wallCount++;
                }
            }

            float actualPercent = ((float)wallCount / ((float)width * height)) * 100f;

            Assert.IsTrue(Mathf.Abs(actualPercent - fillPercent) < 5.0f,
                $"Expected ~{fillPercent}% walls, but got {actualPercent}%");

            Object.DestroyImmediate(generator.gameObject);
        }
    }
}