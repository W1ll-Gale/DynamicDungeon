using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Tests
{
    public class CoreGenerationTests
    {
        private TilemapGenerator CreateGeneratorWithMockBiome()
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();
            generator.InitializeGrid();

            BiomeData biome = ScriptableObject.CreateInstance<BiomeData>();
            biome.wallTile = ScriptableObject.CreateInstance<TileData>();
            biome.floorTile = ScriptableObject.CreateInstance<TileData>();

            Sprite dummy = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            biome.wallTile.tileSprite = dummy;
            biome.floorTile.tileSprite = dummy;

            generator.defaultBiome = biome;

            return generator;
        }

        [Test]
        public void Generator_Creates_100x100_Map_Success()
        {
            TilemapGenerator generator = CreateGeneratorWithMockBiome();
            generator.width = 100;
            generator.height = 100;

            generator.defaultBiome.randomFillPercent = 50;
            generator.defaultBiome.smoothIterations = 0;

            generator.GenerateTilemap();

            Tilemap map = generator.GetComponentInChildren<Tilemap>();
            map.CompressBounds();

            Assert.AreEqual(100, map.size.x);
            Assert.AreEqual(100, map.size.y);

            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void Generation_Fails_Without_Biome()
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();
            generator.width = 10;
            generator.height = 10;
            generator.defaultBiome = null; 

            LogAssert.Expect(LogType.Error, "Cannot generate map: No BiomeData assigned.");
            generator.GenerateTilemap();

            Object.DestroyImmediate(go);
        }
    }
}