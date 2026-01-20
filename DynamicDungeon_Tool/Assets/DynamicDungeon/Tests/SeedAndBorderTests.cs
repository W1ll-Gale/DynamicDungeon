using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

namespace Tests
{
    public class SeedAndBorderTests
    {
        private TilemapGenerator CreateGenerator(bool borders)
        {
            GameObject gameObject = new GameObject("Generator");
            TilemapGenerator generator = gameObject.AddComponent<TilemapGenerator>();
            generator.InitializeGrid();

            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
            biome.randomFillPercent = 50;
            biome.wallTile = ScriptableObject.CreateInstance<TileData>();
            biome.floorTile = ScriptableObject.CreateInstance<TileData>();

            RegionSettings regions = ScriptableObject.CreateInstance<RegionSettings>();
            regions.biomes = new List<WeightedBiome> { new WeightedBiome { biome = biome, weight = 50 } };
            regions.algorithm = RegionAlgorithm.Voronoi;
            regions.voronoiNumSites = 1;

            RegionPass pass1 = ScriptableObject.CreateInstance<RegionPass>();
            pass1.regionSettings = regions;

            NoiseFillPass pass2 = ScriptableObject.CreateInstance<NoiseFillPass>();
            pass2.useBorderWalls = borders;

            generator.generationPipeline = new List<GenerationPass> { pass1, pass2 };

            return generator;
        }

        [Test]
        public void RandomSeed_ProducesDifferentResults()
        {
            TilemapGenerator generator = CreateGenerator(true);
            generator.width = 50;
            generator.height = 50;
            generator.useRandomSeed = true;

            generator.GenerateTilemap();
            int[,] first = generator.CurrentMapData;
            string seed1 = generator.Context.Seed;

            generator.GenerateTilemap();
            int[,] second = generator.CurrentMapData;
            string seed2 = generator.Context.Seed;

            Assert.AreNotEqual(seed1, seed2);
            bool different = false;
            for (int i = 0; i < 50; i++) if (first[i, i] != second[i, i]) different = true;
            Assert.IsTrue(different);

            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void BorderToggle_Enabled_ForcesCorners_Walls()
        {
            TilemapGenerator generator = CreateGenerator(true);
            generator.width = 20;
            generator.height = 20;

            ((RegionPass)generator.generationPipeline[0]).regionSettings.biomes[0].biome.randomFillPercent = 0;

            generator.GenerateTilemap();
            int[,] map = generator.CurrentMapData;

            Assert.AreEqual(1, map[0, 0]);
            Assert.AreEqual(1, map[19, 19]);
            Object.DestroyImmediate(generator.gameObject);
        }
    }
}