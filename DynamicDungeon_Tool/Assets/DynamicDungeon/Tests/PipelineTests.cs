using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

namespace Tests
{
    public class PipelineTests
    {
        private class MockPass : GenerationPass
        {
            public bool executed = false;
            public override void Execute(DungeonContext context)
            {
                executed = true;
                context.MapData[0, 0] = 99;
            }
        }

        [Test]
        public void Context_Initializes_With_Correct_Dimensions()
        {
            DungeonContext context = new DungeonContext(10, 20, "testSeed");

            Assert.AreEqual(10, context.Width);
            Assert.AreEqual(20, context.Height);
            Assert.AreEqual("testSeed", context.Seed);
            Assert.IsNotNull(context.MapData);
            Assert.IsNotNull(context.RegionMap);
            Assert.AreEqual(10, context.MapData.GetLength(0));
            Assert.AreEqual(20, context.MapData.GetLength(1));
        }

        [Test]
        public void RegionPass_Generates_RegionMap()
        {
            RegionPass pass = ScriptableObject.CreateInstance<RegionPass>();
            RegionSettings settings = ScriptableObject.CreateInstance<RegionSettings>();

            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
            settings.biomes = new List<WeightedBiome> { new WeightedBiome { biome = biome, weight = 1 } };
            settings.algorithm = RegionAlgorithm.Voronoi;
            settings.voronoiNumSites = 1;

            pass.regionSettings = settings;

            DungeonContext context = new DungeonContext(10, 10, "seed");

            pass.Execute(context);

            Assert.IsNotNull(context.RegionMap, "RegionMap should be initialized.");
            Assert.AreEqual(0, context.RegionMap[5, 5]);
        }
    }
}