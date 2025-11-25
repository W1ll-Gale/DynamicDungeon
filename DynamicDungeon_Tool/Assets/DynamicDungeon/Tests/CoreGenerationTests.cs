using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Tests
{
    public class CoreGenerationTests
    {
        [Test]
        public void Generator_Creates_100x100_Map()
        {
            GameObject gameObject = new GameObject("Generator");
            TilemapGenerator generator = gameObject.AddComponent<TilemapGenerator>();

            generator.InitializeGrid();

            Texture2D texture = new Texture2D(16, 16);
            Sprite dummySprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.zero);

            Tile mockTile = ScriptableObject.CreateInstance<Tile>();
            mockTile.sprite = dummySprite; 

            TileData mockTileData = ScriptableObject.CreateInstance<TileData>();
            mockTileData.tileVisual = mockTile;

            generator.defaultTile = mockTileData;

            int width = 100;
            int height = 100;
            generator.GenerateEmptyMap(width, height);

            Tilemap map = gameObject.GetComponentInChildren<Tilemap>();

            map.RefreshAllTiles();
            map.CompressBounds();

            Assert.AreEqual(width, map.size.x, $"Expected width {width} (Got {map.size.x})");
            Assert.AreEqual(height, map.size.y, $"Expected height {height} (Got {map.size.y})");

            Object.DestroyImmediate(gameObject);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(mockTile);
            Object.DestroyImmediate(mockTileData);
        }

        [Test]
        public void GenerateEmptyMap_WithPositiveDimensions_Succeeds()
        {
            GameObject gameObject = new GameObject("Generator");
            TilemapGenerator generator = gameObject.AddComponent<TilemapGenerator>();

            generator.InitializeGrid();

            Texture2D texture = new Texture2D(16, 16);
            Sprite dummySprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.zero);

            Tile mockTile = ScriptableObject.CreateInstance<Tile>();
            mockTile.sprite = dummySprite;

            TileData mockTileData = ScriptableObject.CreateInstance<TileData>();
            mockTileData.tileVisual = mockTile;

            generator.defaultTile = mockTileData;

            int width = 10;
            int height = 10;
            generator.GenerateEmptyMap(width, height);

            Tilemap map = gameObject.GetComponentInChildren<Tilemap>();
            map.RefreshAllTiles();
            map.CompressBounds();

            Assert.AreEqual(width, map.size.x, $"Expected width {width} (Got {map.size.x})");
            Assert.AreEqual(height, map.size.y, $"Expected height {height} (Got {map.size.y})");

            Object.DestroyImmediate(gameObject);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(mockTile);
            Object.DestroyImmediate(mockTileData);
        }

        [Test]
        public void GenerateEmptyMap_WithNonPositiveDimensions_FailsGracefully()
        {
            GameObject gameObject = new GameObject("Generator");
            TilemapGenerator generator = gameObject.AddComponent<TilemapGenerator>();

            generator.InitializeGrid();

            Texture2D texture = new Texture2D(16, 16);
            Sprite dummySprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.zero);

            Tile mockTile = ScriptableObject.CreateInstance<Tile>();
            mockTile.sprite = dummySprite;

            TileData mockTileData = ScriptableObject.CreateInstance<TileData>();
            mockTileData.tileVisual = mockTile;

            generator.defaultTile = mockTileData;

            generator.GenerateEmptyMap(0, 10);
            Tilemap map = gameObject.GetComponentInChildren<Tilemap>();
            map.RefreshAllTiles();
            map.CompressBounds();
            Assert.AreEqual(0, map.size.x, "Expected width 0 for zero width input");

            generator.GenerateEmptyMap(10, -5);
            map.RefreshAllTiles();
            map.CompressBounds();
            Assert.AreEqual(0, map.size.y, "Expected height 0 for negative height input");

            Object.DestroyImmediate(gameObject);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(mockTile);
            Object.DestroyImmediate(mockTileData);
        }
    }
}