using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

namespace Tests
{
    public class SmoothingTests
    {
        private DungeonContext CreateMockContext(int[,] map, int smoothIterations)
        {
            int w = map.GetLength(0);
            int h = map.GetLength(1);

            DungeonContext ctx = new DungeonContext(w, h, "seed");
            ctx.MapData = map;
            ctx.RegionMap = new int[w, h];

            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
            biome.name = "TestBiome";

            biome.smoothIterations = smoothIterations;

            RegionSettings settings = ScriptableObject.CreateInstance<RegionSettings>();
            settings.biomes = new List<WeightedBiome>
            {
                new WeightedBiome { biome = biome, weight = 1 }
            };

            ctx.GlobalRegionSettings = settings;
            return ctx;
        }

        [Test]
        public void SmoothMap_FillsGaps()
        {
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

            DungeonContext ctx = CreateMockContext(rawMap, 5);

            SmoothingPass pass = ScriptableObject.CreateInstance<SmoothingPass>();
            pass.maxGlobalIterations = 5;

            pass.Execute(ctx);

            Assert.AreEqual(1, ctx.MapData[2, 2], "Hole should be filled (Neighbors=8 > 4).");
        }
    }
}