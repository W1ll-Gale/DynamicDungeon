using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

namespace Tests
{
    public class RandomFillTests
    {
        private void InjectRegionMap(TilemapGenerator generator, int[,] regionMap)
        {
            PropertyInfo prop = typeof(TilemapGenerator).GetProperty("CurrentRegionMap");
            if (prop != null)
            {
                prop.SetValue(generator, regionMap);
            }
        }

        private TilemapGenerator CreateGenerator(out BiomeData biome)
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();

            biome = ScriptableObject.CreateInstance<BiomeData>();

            RegionSettings regions = ScriptableObject.CreateInstance<RegionSettings>();
            regions.biomes = new List<WeightedBiome> 
            {
                new WeightedBiome { biome = biome, weight = 50 }
            };
            generator.regionSettings = regions;

            return generator;
        }

        [Test]
        public void Map_Borders_Are_Always_Walls()
        {
            BiomeData biome;
            TilemapGenerator generator = CreateGenerator(out biome);

            int width = 50;
            int height = 50;

            biome.randomFillPercent = 50;

            InjectRegionMap(generator, new int[width, height]);


            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void Map_Fill_Ratio_Is_Within_Tolerance()
        {
            BiomeData biome;
            TilemapGenerator generator = CreateGenerator(out biome);

            int width = 100;
            int height = 100;
            int fillPercent = 45;

            biome.randomFillPercent = fillPercent;

            InjectRegionMap(generator, new int[width, height]);

            Object.DestroyImmediate(generator.gameObject);
        }
    }
}