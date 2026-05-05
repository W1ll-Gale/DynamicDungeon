using System.Collections.Generic;
using DynamicDungeon.Runtime.Biome;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class BiomeAssetTests
    {
        [Test]
        public void TryGetTileReturnsSameWeightedTileForRepeatedCellPosition()
        {
            BiomeAsset biome = ScriptableObject.CreateInstance<BiomeAsset>();
            Tile firstTile = ScriptableObject.CreateInstance<Tile>();
            Tile secondTile = ScriptableObject.CreateInstance<Tile>();

            try
            {
                biome.TileMappings.Add(CreateWeightedMapping(7, firstTile, 1.0f, secondTile, 3.0f));

                TileBase resolvedA;
                TileBase resolvedB;
                bool firstResolved = biome.TryGetTile(7, new Vector2Int(12, 9), out resolvedA);
                bool secondResolved = biome.TryGetTile(7, new Vector2Int(12, 9), out resolvedB);

                Assert.That(firstResolved, Is.True);
                Assert.That(secondResolved, Is.True);
                Assert.That(resolvedA, Is.SameAs(resolvedB));
            }
            finally
            {
                Object.DestroyImmediate(firstTile);
                Object.DestroyImmediate(secondTile);
                Object.DestroyImmediate(biome);
            }
        }

        [Test]
        public void TryGetTileCanVaryAcrossDifferentPositions()
        {
            BiomeAsset biome = ScriptableObject.CreateInstance<BiomeAsset>();
            Tile firstTile = ScriptableObject.CreateInstance<Tile>();
            Tile secondTile = ScriptableObject.CreateInstance<Tile>();

            try
            {
                biome.TileMappings.Add(CreateWeightedMapping(7, firstTile, 1.0f, secondTile, 1.0f));

                HashSet<TileBase> resolvedTiles = new HashSet<TileBase>();
                int x;
                for (x = 0; x < 64; x++)
                {
                    TileBase tile;
                    bool resolved = biome.TryGetTile(7, new Vector2Int(x, x / 2), out tile);
                    Assert.That(resolved, Is.True);
                    resolvedTiles.Add(tile);
                }

                Assert.That(resolvedTiles.Contains(firstTile), Is.True);
                Assert.That(resolvedTiles.Contains(secondTile), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(firstTile);
                Object.DestroyImmediate(secondTile);
                Object.DestroyImmediate(biome);
            }
        }

        [Test]
        public void TryGetTileReturnsFalseWhenWeightedEntriesHaveNoValidPositiveWeights()
        {
            BiomeAsset biome = ScriptableObject.CreateInstance<BiomeAsset>();

            try
            {
                BiomeTileMapping mapping = new BiomeTileMapping();
                mapping.LogicalId = 12;
                mapping.TileType = TileMappingType.WeightedRandom;
                mapping.WeightedTiles.Add(new WeightedTileEntry
                {
                    Tile = null,
                    Weight = 2.0f
                });
                mapping.WeightedTiles.Add(new WeightedTileEntry
                {
                    Tile = ScriptableObject.CreateInstance<Tile>(),
                    Weight = 0.0f
                });

                biome.TileMappings.Add(mapping);

                TileBase resolvedTile;
                bool resolved = biome.TryGetTile(12, new Vector2Int(1, 1), out resolvedTile);

                Assert.That(resolved, Is.False);
                Assert.That(resolvedTile, Is.Null);

                Object.DestroyImmediate(mapping.WeightedTiles[1].Tile);
            }
            finally
            {
                Object.DestroyImmediate(biome);
            }
        }

        [Test]
        public void TryGetTileCreatesReusableTileFromSpriteMapping()
        {
            BiomeAsset biome = ScriptableObject.CreateInstance<BiomeAsset>();
            Texture2D spriteTexture = CreateTexture(Color.yellow);
            Sprite sprite = CreateSprite(spriteTexture);

            try
            {
                biome.TileMappings.Add(new BiomeTileMapping
                {
                    LogicalId = 21,
                    TileType = TileMappingType.Sprite,
                    SpriteAsset = sprite
                });

                TileBase resolvedA;
                TileBase resolvedB;
                bool firstResolved = biome.TryGetTile(21, new Vector2Int(3, 4), out resolvedA);
                bool secondResolved = biome.TryGetTile(21, new Vector2Int(9, 2), out resolvedB);

                Assert.That(firstResolved, Is.True);
                Assert.That(secondResolved, Is.True);
                Assert.That(resolvedA, Is.SameAs(resolvedB));

                Tile generatedTile = resolvedA as Tile;
                Assert.That(generatedTile, Is.Not.Null);
                Assert.That(generatedTile.sprite, Is.SameAs(sprite));
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(spriteTexture);
                Object.DestroyImmediate(biome);
            }
        }

        private static BiomeTileMapping CreateWeightedMapping(ushort logicalId, TileBase firstTile, float firstWeight, TileBase secondTile, float secondWeight)
        {
            BiomeTileMapping mapping = new BiomeTileMapping();
            mapping.LogicalId = logicalId;
            mapping.TileType = TileMappingType.WeightedRandom;
            mapping.WeightedTiles.Add(new WeightedTileEntry
            {
                Tile = firstTile,
                Weight = firstWeight
            });
            mapping.WeightedTiles.Add(new WeightedTileEntry
            {
                Tile = secondTile,
                Weight = secondWeight
            });

            return mapping;
        }

        private static Sprite CreateSprite(Texture2D texture)
        {
            return Sprite.Create(texture, new Rect(0.0f, 0.0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 16.0f);
        }

        private static Texture2D CreateTexture(Color colour)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, colour);
            texture.Apply();
            return texture;
        }
    }
}
