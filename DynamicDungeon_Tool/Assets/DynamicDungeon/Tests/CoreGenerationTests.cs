using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Tests
{
    public class CoreGenerationTests
    {
        // Setup Helper to reduce code duplication
        private TilemapGenerator CreateGeneratorWithMockTiles()
        {
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();
            generator.InitializeGrid();

            // Create a dummy 1x1 white texture for the tile
            Texture2D texture = new Texture2D(1, 1);
            Sprite dummySprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);

            Tile mockTile = ScriptableObject.CreateInstance<Tile>();
            mockTile.sprite = dummySprite;

            // Setup valid TileData
            TileData floorData = ScriptableObject.CreateInstance<TileData>();
            floorData.tileVisual = mockTile;
            TileData wallData = ScriptableObject.CreateInstance<TileData>();
            wallData.tileVisual = mockTile;

            generator.floorTile = floorData;
            generator.wallTile = wallData;

            return generator;
        }

        // TEST 1: The "Happy Path" (Objective P1)
        [Test]
        public void Generator_Creates_100x100_Map_Success()
        {
            // Arrange
            TilemapGenerator generator = CreateGeneratorWithMockTiles();
            generator.width = 100;
            generator.height = 100;
            generator.randomFillPercent = 50;

            // Act
            generator.GenerateTilemap();

            // Assert
            Tilemap map = generator.GetComponentInChildren<Tilemap>();
            map.RefreshAllTiles();
            map.CompressBounds();

            Assert.AreEqual(100, map.size.x, "Map width should be 100");
            Assert.AreEqual(100, map.size.y, "Map height should be 100");

            // Cleanup
            Object.DestroyImmediate(generator.gameObject);
        }

        // TEST 2: The "Negative Size" Test (Input Validation)
        [Test]
        public void Generation_Fails_Gracefully_With_Invalid_Dimensions()
        {
            // Arrange
            TilemapGenerator generator = CreateGeneratorWithMockTiles();

            // Sub-Test A: Zero Width
            generator.width = 0;
            generator.height = 100;

            // We use LogAssert because we expect an error log
            LogAssert.Expect(LogType.Error, "Cannot generate map: Width and Height must be positive.");
            generator.GenerateTilemap();

            // Verify map is empty
            Tilemap map = generator.GetComponentInChildren<Tilemap>();
            map.CompressBounds();
            Assert.AreEqual(0, map.size.x, "Map should not generate with width 0");

            // Sub-Test B: Negative Height
            generator.width = 100;
            generator.height = -50;

            LogAssert.Expect(LogType.Error, "Cannot generate map: Width and Height must be positive.");
            generator.GenerateTilemap();

            Assert.AreEqual(0, map.size.y, "Map should not generate with negative height");

            Object.DestroyImmediate(generator.gameObject);
        }

        // TEST 3: Missing Tile Data (Null Reference Safety)
        [Test]
        public void Generation_Does_Not_Crash_When_TileData_Is_Missing()
        {
            // Arrange
            GameObject go = new GameObject("Generator");
            TilemapGenerator generator = go.AddComponent<TilemapGenerator>();
            generator.InitializeGrid();

            // Explicitly set tiles to null
            generator.floorTile = null;
            generator.wallTile = null;
            generator.width = 10;
            generator.height = 10;

            // Act
            // If the code is bad, this will throw a NullReferenceException and fail the test
            generator.GenerateTilemap();

            // Assert
            Tilemap map = generator.GetComponentInChildren<Tilemap>();
            map.CompressBounds();

            // The map should exist, but be empty (or full of nulls which compress to 0)
            // The key is that we reached this line without crashing
            Assert.Pass("Generator handled missing TileData without crashing.");

            Object.DestroyImmediate(go);
        }
    }
}