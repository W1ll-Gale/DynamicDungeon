using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

namespace Tests
{
    public class CoreGenerationTests
    {
        private TilemapGenerator CreateGeneratorWithMockPipeline()
        {
            GameObject gameObject = new GameObject("Generator");
            TilemapGenerator generator = gameObject.AddComponent<TilemapGenerator>();
            generator.InitializeGrid();

            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
            biome.randomFillPercent = 50;
            biome.smoothIterations = 0;
            biome.wallTile = ScriptableObject.CreateInstance<TileData>();
            biome.floorTile = ScriptableObject.CreateInstance<TileData>();

            Sprite dummy = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            biome.wallTile.tileSprite = dummy;
            biome.floorTile.tileSprite = dummy;

            RegionSettings regions = ScriptableObject.CreateInstance<RegionSettings>();
            regions.biomes = new List<WeightedBiome>
            {
                new WeightedBiome { biome = biome, weight = 50 }
            };
            regions.algorithm = RegionAlgorithm.Voronoi;
            regions.voronoiNumSites = 1;

            RegionPass regionPass = ScriptableObject.CreateInstance<RegionPass>();
            regionPass.regionSettings = regions;

            NoiseFillPass terrainPass = ScriptableObject.CreateInstance<NoiseFillPass>();
            terrainPass.useBorderWalls = true;

            generator.generationPipeline = new List<GenerationPass> { regionPass, terrainPass };

            return generator;
        }

        [Test]
        public void Generator_Creates_100x100_Map_Success()
        {
            TilemapGenerator generator = CreateGeneratorWithMockPipeline();
            generator.width = 100;
            generator.height = 100;

            generator.GenerateTilemap();

            Tilemap map = generator.GetComponentInChildren<Tilemap>();
            map.CompressBounds();

            Assert.AreEqual(100, map.size.x);
            Assert.AreEqual(100, map.size.y);

            Object.DestroyImmediate(generator.gameObject);
        }
    }
}