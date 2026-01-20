using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

namespace Tests
{
    public class ResourceGenerationTests
    {
        private DungeonContext CreateMockContext(int[,] map, BiomeData.BiomeResource resource)
        {
            int w = map.GetLength(0);
            int h = map.GetLength(1);
            DungeonContext ctx = new DungeonContext(w, h, "seed");
            ctx.MapData = map;
            ctx.RegionMap = new int[w, h];

            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
            biome.resources = new List<BiomeData.BiomeResource> { resource };

            RegionSettings settings = ScriptableObject.CreateInstance<RegionSettings>();
            settings.biomes = new List<WeightedBiome> { new WeightedBiome { biome = biome, weight = 1 } };

            ctx.GlobalRegionSettings = settings;
            return ctx;
        }

        [Test]
        public void Resources_Spawn_According_To_Rules()
        {
            TileData oreTile = ScriptableObject.CreateInstance<TileData>();

            BiomeData.BiomeResource resource = new BiomeData.BiomeResource
            {
                resourceTile = oreTile,
                spawnChance = 1.0f,
                spawnsInWalls = true
            };

            int[,] map = new int[10, 10];
            for (int x = 0; x < 10; x++) for (int y = 0; y < 10; y++) map[x, y] = 1;

            DungeonContext ctx = CreateMockContext(map, resource);
            ResourcePass pass = ScriptableObject.CreateInstance<ResourcePass>();

            pass.Execute(ctx);

            Assert.Greater(ctx.Resources.Count, 0);
            foreach (var kvp in ctx.Resources)
            {
                Assert.AreEqual(oreTile, kvp.Value);
            }
        }
    }
}