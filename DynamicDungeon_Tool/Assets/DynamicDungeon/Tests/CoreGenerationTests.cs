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
            DungeonGenerator generator = gameObject.AddComponent<DungeonGenerator>();

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
    }
}