using NUnit.Framework;
using UnityEngine;

namespace Tests
{
    public class SmoothingTests
    {
        private TilemapGenerator CreateGenerator()
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();

            return generator;
        }

        [Test]
        public void Helper_CountsNeighborsCorrectly()
        {
            TilemapGenerator generator = CreateGenerator();

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
            TilemapGenerator generator = CreateGenerator();

            int[,] rawMap = new int[5, 5]; 
            rawMap[2, 2] = 1; 
            int[,] smoothedMap = generator.SmoothMap(rawMap);

            Assert.AreEqual(0, smoothedMap[2, 2], "Isolated wall should be removed (neighbors < 4).");

            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void SmoothMap_FillsGaps()
        {
            TilemapGenerator generator = CreateGenerator();

            int[,] rawMap = new int[5, 5];
            for (int x = 0; x < 5; x++)
            {
                for (int y = 0; y < 5; y++)
                {
                    rawMap[x, y] = 1;

                }
            }

            rawMap[2, 2] = 0; 

            int[,] smoothedMap = generator.SmoothMap(rawMap);

            Assert.AreEqual(1, smoothedMap[2, 2], "Hole surrounded by walls should be filled (neighbors > 4).");

            Object.DestroyImmediate(generator.gameObject);
        }
    }
}