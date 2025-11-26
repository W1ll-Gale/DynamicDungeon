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
            return generator;
        }

        private static bool MapsDiffer(int[,] a, int[,] b)
        {
            if (a == null || b == null) return true;
            if (a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1)) return true;
            int w = a.GetLength(0);
            int h = a.GetLength(1);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (a[x, y] != b[x, y]) return true;
            return false;
        }

        [Test]
        public void RandomSeed_ProducesDifferentResults_ContentDiffers()
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

            Assert.AreNotEqual(seed1, seed2, "Seeds should differ when random seeding is enabled.");
            Assert.IsTrue(MapsDiffer(first, second), "Maps should differ for different seeds.");
            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void FixedSeed_Is_Deterministic()
        {
            TilemapGenerator generator = CreateGenerator();
            generator.width = 42;
            generator.height = 37;
            generator.useRandomSeed = false;
            generator.seed = "FIXED_SEED_1";

            generator.GenerateTilemap();
            int[,] m1 = generator.CurrentMapData;

            generator.GenerateTilemap();
            int[,] m2 = generator.CurrentMapData;

            Assert.IsFalse(MapsDiffer(m1, m2), "Maps must be identical with fixed seed.");
            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void BorderToggle_Enabled_ForcesCorners_Walls()
        {
            TilemapGenerator generator = CreateGenerator();
            generator.width = 20;
            generator.height = 20;
            generator.useBorderWalls = true;
            generator.randomFillPercent = 0;

            generator.GenerateTilemap();
            int[,] map = generator.CurrentMapData;

            Assert.AreEqual(1, map[0, 0]);
            Assert.AreEqual(1, map[19, 0]);
            Assert.AreEqual(1, map[0, 19]);
            Assert.AreEqual(1, map[19, 19]);
            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void BorderToggle_Disabled_AllowsFloorAtEdge()
        {
            TilemapGenerator generator = CreateGenerator();
            generator.width = 12;
            generator.height = 12;
            generator.useBorderWalls = false;
            generator.randomFillPercent = 0;
            generator.useRandomSeed = false;
            generator.seed = "BORDER_OFF";

            generator.GenerateTilemap();
            int[,] map = generator.CurrentMapData;

            Assert.AreEqual(0, map[0, 0]);
            Assert.AreEqual(0, map[11, 11]);
            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void FillPercentZero_NoInteriorWalls()
        {
            TilemapGenerator generator = CreateGenerator();
            generator.width = 30;
            generator.height = 25;
            generator.randomFillPercent = 0;
            generator.useRandomSeed = false;
            generator.useBorderWalls = false;
            generator.seed = "ZERO_INTERIOR";
            generator.GenerateTilemap();
            int[,] map = generator.CurrentMapData;

            int interiorWalls = 0;
            for (int x = 1; x < generator.width - 1; x++)
                for (int y = 1; y < generator.height - 1; y++)
                    if (map[x, y] == 1) interiorWalls++;

            Assert.AreEqual(0, interiorWalls, "Interior should have zero walls when fillPercent=0.");
            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void FillPercentHundred_AllInteriorWalls()
        {
            TilemapGenerator generator = CreateGenerator();
            generator.width = 18;
            generator.height = 22;
            generator.randomFillPercent = 100;
            generator.useRandomSeed = false;
            generator.seed = "FULL_INTERIOR";
            generator.GenerateTilemap();
            int[,] map = generator.CurrentMapData;

            int interiorWalls = 0;
            int interiorTotal = (generator.width - 2) * (generator.height - 2);
            for (int x = 1; x < generator.width - 1; x++)
            {
                for (int y = 1; y < generator.height - 1; y++)
                {
                    if (map[x, y] == 1) interiorWalls++;
                }
            }

            Assert.AreEqual(interiorTotal, interiorWalls, "Interior should be all walls when fillPercent=100.");
            Object.DestroyImmediate(generator.gameObject);
        }
    }
}