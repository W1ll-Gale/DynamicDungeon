using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Tests
{
    public class CoreGenerationTests
    {
        private TilemapGenerator CreateGeneratorWithMockTiles()
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();
            generator.InitializeGrid();

            Texture2D texture = new Texture2D(1, 1);
            Sprite dummySprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);

            Tile mockTile = ScriptableObject.CreateInstance<Tile>();
            mockTile.sprite = dummySprite;

            TileData floorData = ScriptableObject.CreateInstance<TileData>();
            floorData.tileVisual = mockTile;
            TileData wallData = ScriptableObject.CreateInstance<TileData>();
            wallData.tileVisual = mockTile;

            generator.floorTile = floorData;
            generator.wallTile = wallData;

            return generator;
        }

        [Test]
        public void Generator_Creates_100x100_Map_Success()
        {
            TilemapGenerator generator = CreateGeneratorWithMockTiles();
            generator.width = 100;
            generator.height = 100;
            generator.randomFillPercent = 50;

            generator.GenerateTilemap();

            Tilemap map = generator.GetComponentInChildren<Tilemap>();
            map.RefreshAllTiles();
            map.CompressBounds();

            Assert.AreEqual(100, map.size.x, "Map width should be 100");
            Assert.AreEqual(100, map.size.y, "Map height should be 100");

            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void Generation_Fails_Gracefully_With_Invalid_Dimensions()
        {
            TilemapGenerator generator = CreateGeneratorWithMockTiles();

            generator.width = 0;
            generator.height = 100;

            LogAssert.Expect(LogType.Error, "Cannot generate map: Width and Height must be positive.");
            generator.GenerateTilemap();

            Tilemap map = generator.GetComponentInChildren<Tilemap>();
            map.CompressBounds();
            Assert.AreEqual(0, map.size.x, "Map should not generate with width 0");

            generator.width = 100;
            generator.height = -50;

            LogAssert.Expect(LogType.Error, "Cannot generate map: Width and Height must be positive.");
            generator.GenerateTilemap();

            Assert.AreEqual(0, map.size.y, "Map should not generate with negative height");

            Object.DestroyImmediate(generator.gameObject);
        }

        [Test]
        public void Generation_Does_Not_Crash_When_TileData_Is_Missing()
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();
            generator.InitializeGrid();

            generator.floorTile = null;
            generator.wallTile = null;
            generator.width = 10;
            generator.height = 10;

            generator.GenerateTilemap();

            Tilemap map = generator.GetComponentInChildren<Tilemap>();
            map.CompressBounds();

            Assert.Pass("Generator handled missing TileData without crashing.");

            Object.DestroyImmediate(go);
        }
    }
}