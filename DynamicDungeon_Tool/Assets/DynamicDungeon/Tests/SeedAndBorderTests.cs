using NUnit.Framework;
using UnityEngine;

namespace Tests
{
    public class SeedAndBorderTests
    {
        private TilemapGenerator CreateGenerator()
        {
            GameObject gameObject = new GameObject("Generator");
            TilemapGenerator generator = gameObject.AddComponent<TilemapGenerator>();
            generator.InitializeGrid();

            generator.defaultBiome = ScriptableObject.CreateInstance<BiomeData>();
            generator.defaultBiome.randomFillPercent = 50;
            generator.defaultBiome.smoothIterations = 0;

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
            generator.defaultBiome.randomFillPercent = 0; 

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

            generator.defaultBiome.randomFillPercent = 0;

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