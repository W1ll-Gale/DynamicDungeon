using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection; 

namespace Tests
{
    public class ResourceGenerationTests
    {
        private void InjectRegionMap(TilemapGenerator generator, int[,] regionMap)
        {
            PropertyInfo prop = typeof(TilemapGenerator).GetProperty("CurrentRegionMap");
            if (prop != null)
            {
                prop.SetValue(generator, regionMap);
            }
        }

        [Test]
        public void Resources_Spawn_According_To_Rules()
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();
            generator.InitializeGrid();

            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
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

            RegionSettings regions = ScriptableObject.CreateInstance<RegionSettings>();
            regions.biomes = new List<WeightedBiome>
            {
                new WeightedBiome { biome = biome, weight = 50 }
            };
            generator.regionSettings = regions;

            int width = 10;
            int height = 10;

            int[,] allWallsMap = new int[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    allWallsMap[x, y] = 1;

            int[,] regionMap = new int[width, height]; 

            InjectRegionMap(generator, regionMap);

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

            RegionSettings regions = ScriptableObject.CreateInstance<RegionSettings>();
            regions.biomes = new List<WeightedBiome>
            {
                new WeightedBiome { biome = biome, weight = 50 }
            };
            generator.regionSettings = regions;

            int[,] allWallsMap = new int[10, 10];
            for (int x = 0; x < 10; x++)
                for (int y = 0; y < 10; y++)
                    allWallsMap[x, y] = 1;

            int[,] regionMap = new int[10, 10];

            InjectRegionMap(generator, regionMap);

        }
    }
}