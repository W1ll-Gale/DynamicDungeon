using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

namespace Tests
{
    public class ResourceGenerationTests
    {
        [Test]
        public void Resources_Spawn_According_To_Rules()
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();
            generator.InitializeGrid();

            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
            generator.defaultBiome = biome;

            TileData oreTile = ScriptableObject.CreateInstance<TileData>();
            oreTile.tileID = "GoldOre";

            biome.resources = new List<BiomeData.BiomeResource>
            {
                new BiomeData.BiomeResource
                {
                    resourceTile = oreTile,
                    spawnChance = 1.0f,
                    spawnsInWalls = true
                }
            };

            int width = 10;
            int height = 10;
            int[,] allWallsMap = new int[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    allWallsMap[x, y] = 1;
                }
            }
 
            Dictionary<Vector2Int, TileData> resources = generator.GenerateResources(allWallsMap, biome, "seed");

            Assert.Greater(resources.Count, 0, "Resources should spawn given 100% chance.");

            foreach (KeyValuePair<Vector2Int, TileData> kvp in resources)
            {
                Assert.AreEqual(oreTile, kvp.Value);
            }

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Resources_Do_Not_Spawn_In_Wrong_Context()
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();
            generator.InitializeGrid();

            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();

            TileData grassTile = ScriptableObject.CreateInstance<TileData>();
            biome.resources = new List<BiomeData.BiomeResource>
            {
                new BiomeData.BiomeResource
                {
                    resourceTile = grassTile,
                    spawnChance = 1.0f,
                    spawnsInWalls = false 
                }
            };

            int[,] allWallsMap = new int[10, 10];
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    allWallsMap[x, y] = 1;
                }
            }

            Dictionary<Vector2Int, TileData> resources = generator.GenerateResources(allWallsMap, biome, "seed");

            Assert.AreEqual(0, resources.Count, "Floor decorations should not spawn inside Walls.");

            Object.DestroyImmediate(go);
        }
    }
}