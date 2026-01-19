using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

namespace Tests
{
    public class SeedAndBorderTests
    {
        private TilemapGenerator CreateGenerator()
        {
            GameObject gameObject = new GameObject("Generator");
            TilemapGenerator generator = gameObject.AddComponent<TilemapGenerator>();
            generator.InitializeGrid();

            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
            biome.randomFillPercent = 50;
            biome.smoothIterations = 0;
            biome.wallTile = ScriptableObject.CreateInstance<TileData>(); 
            biome.floorTile = ScriptableObject.CreateInstance<TileData>();

            RegionSettings regions = ScriptableObject.CreateInstance<RegionSettings>();
            regions.biomes = new List<WeightedBiome>
            {
                new WeightedBiome { biome = biome, weight = 50 }
            };
            regions.algorithm = RegionAlgorithm.Voronoi;
            regions.voronoiNumSites = 1; 

            generator.regionSettings = regions;

            return generator;
        }

        private static bool MapsDiffer(int[,] a, int[,] b)
        {
            if (a == null || b == null) return true;
            if (a.GetLength(0) != b.GetLength(0)) return true;

            for (int x = 0; x < a.GetLength(0); x++)
            {
                for (int y = 0; y < a.GetLength(1); y++)
                {
                    if (a[x, y] != b[x, y]) return true;
                }
            }

            return false;
        }

        [Test]
        public void RandomSeed_ProducesDifferentResults()
        {
            TilemapGenerator generator = CreateGenerator();
            generator.width = 50;
            generator.height = 50;
            generator.useRandomSeed = true;

            generator.GenerateTilemap();
            int[,] first = generator.CurrentMapData;
            string seed1 = generator.seed;

            generator.GenerateTilemap();
            int[,] second = generator.CurrentMapData;
            string seed2 = generator.seed;

            Assert.AreNotEqual(seed1, seed2);
            Assert.IsTrue(MapsDiffer(first, second));
            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void BorderToggle_Enabled_ForcesCorners_Walls()
        {
            TilemapGenerator generator = CreateGenerator();
            generator.width = 20;
            generator.height = 20;
            generator.useBorderWalls = true;

            generator.regionSettings.biomes[0].biome.randomFillPercent = 0;

            generator.GenerateTilemap();
            int[,] map = generator.CurrentMapData;

            Assert.AreEqual(1, map[0, 0]);
            Assert.AreEqual(1, map[19, 19]);
            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void FillPercentZero_NoInteriorWalls()
        {
            TilemapGenerator generator = CreateGenerator();
            generator.width = 30;
            generator.height = 30;
            generator.useBorderWalls = false;

            generator.regionSettings.biomes[0].biome.randomFillPercent = 0;

            generator.GenerateTilemap();
            int[,] map = generator.CurrentMapData;

            int interiorWalls = 0;
            for (int x = 1; x < generator.width - 1; x++)
            {
                for (int y = 1; y < generator.height - 1; y++)
                {
                    if (map[x, y] == 1) interiorWalls++;
                }
            }

            Assert.AreEqual(0, interiorWalls);
            Object.DestroyImmediate(generator.gameObject);
        }
    }
}