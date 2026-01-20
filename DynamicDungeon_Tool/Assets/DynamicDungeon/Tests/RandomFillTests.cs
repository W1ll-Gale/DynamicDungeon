using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

namespace Tests
{
    public class RandomFillTests
    {
        private DungeonContext CreateMockContext(int width, int height, int fillPercent)
        {
            DungeonContext ctx = new DungeonContext(width, height, "seed");

            ctx.RegionMap = new int[width, height];

            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
            biome.randomFillPercent = fillPercent;

            RegionSettings settings = ScriptableObject.CreateInstance<RegionSettings>();
            settings.biomes = new List<WeightedBiome>
            {
                new WeightedBiome { biome = biome, weight = 100 }
            };

            ctx.GlobalRegionSettings = settings;

            return ctx;
        }

        [Test]
        public void Map_Borders_Are_Always_Walls()
        {
            int width = 50;
            int height = 50;

            DungeonContext ctx = CreateMockContext(width, height, 50);

            TerrainPass pass = ScriptableObject.CreateInstance<TerrainPass>();
            pass.useBorderWalls = true;

            pass.Execute(ctx);

            int[,] map = ctx.MapData;
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
        }

        [Test]
        public void Map_Fill_Ratio_Is_Within_Tolerance()
        {
            int width = 100;
            int height = 100;
            int fillPercent = 45;

            DungeonContext ctx = CreateMockContext(width, height, fillPercent);

            TerrainPass pass = ScriptableObject.CreateInstance<TerrainPass>();
            pass.useBorderWalls = false; 

            pass.Execute(ctx);

            int[,] map = ctx.MapData;
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
        }
    }
}