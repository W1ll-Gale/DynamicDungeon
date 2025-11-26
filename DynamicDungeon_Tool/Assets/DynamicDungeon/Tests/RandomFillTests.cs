using NUnit.Framework;
using UnityEngine;

namespace Tests
{
    public class RandomFillTests
    {
        [Test]
        public void Map_Borders_Are_Always_Walls()
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();

            int width = 50;
            int height = 50;

            int[,] map = generator.GenerateMapData(width, height, "testSeed", 50, true);

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

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Map_Fill_Ratio_Is_Within_Tolerance()
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();

            int width = 100;
            int height = 100;
            int fillPercent = 45; 

            int[,] map = generator.GenerateMapData(width, height, "seed12345", fillPercent, true);

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

            Object.DestroyImmediate(go);
        }
    }
}