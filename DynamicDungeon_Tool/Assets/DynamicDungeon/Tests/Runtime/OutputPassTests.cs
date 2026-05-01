using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Output;
using DynamicDungeon.Runtime.Semantic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class OutputPassTests
    {
        private const string LogicalChannelName = "LogicalIds";

        [Test]
        public void ExecuteRoutesTilesToExpectedLayersAndSkipsVoidAndMissingMappings()
        {
            Grid grid = null;
            TileSemanticRegistry registry = null;
            BiomeAsset biome = null;
            TilemapLayerDefinition solidLayer = null;
            TilemapLayerDefinition floorLayer = null;
            TilemapLayerDefinition defaultLayer = null;
            Tile wallTile = null;
            Tile floorTile = null;

            try
            {
                grid = CreateGrid();
                registry = CreateRegistry();
                biome = CreateBiome();
                solidLayer = CreateLayerDefinition("Solid", false, "Solid");
                floorLayer = CreateLayerDefinition("Floor", false, "Walkable");
                defaultLayer = CreateLayerDefinition("Default", true);
                wallTile = CreateTile(Color.red);
                floorTile = CreateTile(Color.green);

                AddRegistryEntry(registry, LogicalTileId.Wall, "Wall", "Solid");
                AddRegistryEntry(registry, LogicalTileId.Floor, "Floor", "Walkable");

                AddBiomeMapping(biome, LogicalTileId.Wall, wallTile);
                AddBiomeMapping(biome, LogicalTileId.Floor, floorTile);

                WorldSnapshot snapshot = CreateSnapshot(2, 2, new[] { (int)LogicalTileId.Wall, (int)LogicalTileId.Floor, (int)LogicalTileId.Void, 999 });
                TilemapLayerWriter writer = new TilemapLayerWriter();
                TilemapOutputPass outputPass = new TilemapOutputPass();
                TilemapLayerDefinition[] layers = new[] { solidLayer, floorLayer, defaultLayer };

                writer.EnsureTimelapsCreated(grid, layers);

                Assert.DoesNotThrow(() => outputPass.Execute(snapshot, LogicalChannelName, biome, registry, writer, layers, Vector3Int.zero));

                Tilemap solidTilemap = GetLayerTilemap(grid, "Solid");
                Tilemap floorTilemap = GetLayerTilemap(grid, "Floor");
                Tilemap defaultTilemap = GetLayerTilemap(grid, "Default");
                Vector3Int wallPosition = new Vector3Int(0, 0, 0);
                Vector3Int floorPosition = new Vector3Int(1, 0, 0);
                Vector3Int voidPosition = new Vector3Int(0, 1, 0);
                Vector3Int unmappedPosition = new Vector3Int(1, 1, 0);

                Assert.That(solidTilemap.GetTile(wallPosition), Is.SameAs(wallTile));
                Assert.That(floorTilemap.GetTile(floorPosition), Is.SameAs(floorTile));
                Assert.That(floorTilemap.GetTile(wallPosition), Is.Null);
                Assert.That(solidTilemap.GetTile(floorPosition), Is.Null);
                AssertNoTileOnAnyLayer(new[] { solidTilemap, floorTilemap, defaultTilemap }, voidPosition);
                AssertNoTileOnAnyLayer(new[] { solidTilemap, floorTilemap, defaultTilemap }, unmappedPosition);
            }
            finally
            {
                DestroyImmediateIfNotNull(wallTile);
                DestroyImmediateIfNotNull(floorTile);
                DestroyImmediateIfNotNull(solidLayer);
                DestroyImmediateIfNotNull(floorLayer);
                DestroyImmediateIfNotNull(defaultLayer);
                DestroyImmediateIfNotNull(biome);
                DestroyImmediateIfNotNull(registry);
                DestroyImmediateIfNotNull(grid != null ? grid.gameObject : null);
            }
        }

        [Test]
        public void ExecuteUsesPositionAwareBiomeResolutionForWeightedMappings()
        {
            Grid grid = null;
            TileSemanticRegistry registry = null;
            BiomeAsset biome = null;
            TilemapLayerDefinition solidLayer = null;
            TilemapLayerDefinition defaultLayer = null;
            Tile firstWeightedTile = null;
            Tile secondWeightedTile = null;

            try
            {
                grid = CreateGrid();
                registry = CreateRegistry();
                biome = CreateBiome();
                solidLayer = CreateLayerDefinition("Solid", false, "Solid");
                defaultLayer = CreateLayerDefinition("Default", true);
                firstWeightedTile = CreateTile(Color.cyan);
                secondWeightedTile = CreateTile(Color.magenta);

                AddRegistryEntry(registry, 42, "Weighted Wall", "Solid");
                AddWeightedBiomeMapping(biome, 42, firstWeightedTile, 1.0f, secondWeightedTile, 1.0f);

                WorldSnapshot snapshot = CreateSnapshot(2, 1, new[] { 42, 42 });
                TilemapLayerWriter writer = new TilemapLayerWriter();
                TilemapOutputPass outputPass = new TilemapOutputPass();
                TilemapLayerDefinition[] layers = new[] { solidLayer, defaultLayer };

                writer.EnsureTimelapsCreated(grid, layers);
                outputPass.Execute(snapshot, LogicalChannelName, biome, registry, writer, layers, Vector3Int.zero);

                Tilemap solidTilemap = GetLayerTilemap(grid, "Solid");
                TileBase expectedFirstTile;
                TileBase expectedSecondTile;
                bool firstResolved = biome.TryGetTile(42, new Vector2Int(0, 0), out expectedFirstTile);
                bool secondResolved = biome.TryGetTile(42, new Vector2Int(1, 0), out expectedSecondTile);

                Assert.That(firstResolved, Is.True);
                Assert.That(secondResolved, Is.True);
                Assert.That(solidTilemap.GetTile(new Vector3Int(0, 0, 0)), Is.SameAs(expectedFirstTile));
                Assert.That(solidTilemap.GetTile(new Vector3Int(1, 0, 0)), Is.SameAs(expectedSecondTile));
            }
            finally
            {
                DestroyImmediateIfNotNull(firstWeightedTile);
                DestroyImmediateIfNotNull(secondWeightedTile);
                DestroyImmediateIfNotNull(solidLayer);
                DestroyImmediateIfNotNull(defaultLayer);
                DestroyImmediateIfNotNull(biome);
                DestroyImmediateIfNotNull(registry);
                DestroyImmediateIfNotNull(grid != null ? grid.gameObject : null);
            }
        }

        [Test]
        public void ExecuteResolvesSemanticMaterialIdsThroughActiveBiomeTiles()
        {
            Grid grid = null;
            TileSemanticRegistry registry = null;
            BiomeAsset forestBiome = null;
            BiomeAsset desertBiome = null;
            BiomeAsset fallbackBiome = null;
            TilemapLayerDefinition floorLayer = null;
            TilemapLayerDefinition solidLayer = null;
            TilemapLayerDefinition defaultLayer = null;
            Tile forestSurfaceTile = null;
            Tile forestCaveTile = null;
            Tile desertSurfaceTile = null;
            Tile desertCaveTile = null;

            try
            {
                grid = CreateGrid();
                registry = CreateRegistry();
                forestBiome = CreateBiome();
                desertBiome = CreateBiome();
                fallbackBiome = CreateBiome();
                floorLayer = CreateLayerDefinition("Floor", false, "Walkable");
                solidLayer = CreateLayerDefinition("Solid", false, "Solid");
                defaultLayer = CreateLayerDefinition("Default", true);
                forestSurfaceTile = CreateTile(Color.green);
                forestCaveTile = CreateTile(Color.gray);
                desertSurfaceTile = CreateTile(Color.yellow);
                desertCaveTile = CreateTile(new Color(0.65f, 0.45f, 0.25f, 1.0f));

                AddRegistryEntry(registry, 10, "Surface Floor", "Walkable");
                AddRegistryEntry(registry, 16, "Cave Wall", "Solid");
                AddBiomeMapping(forestBiome, 10, forestSurfaceTile);
                AddBiomeMapping(forestBiome, 16, forestCaveTile);
                AddBiomeMapping(desertBiome, 10, desertSurfaceTile);
                AddBiomeMapping(desertBiome, 16, desertCaveTile);

                WorldSnapshot snapshot = new WorldSnapshot
                {
                    Width = 2,
                    Height = 2,
                    IntChannels = new[]
                    {
                        new WorldSnapshot.IntChannelSnapshot
                        {
                            Name = LogicalChannelName,
                            Data = new[] { 10, 16, 10, 16 }
                        },
                        new WorldSnapshot.IntChannelSnapshot
                        {
                            Name = BiomeChannelUtility.ChannelName,
                            Data = new[] { 0, 0, 1, 1 }
                        }
                    },
                    BiomeChannelBiomes = new[] { forestBiome, desertBiome }
                };
                TilemapLayerWriter writer = new TilemapLayerWriter();
                TilemapOutputPass outputPass = new TilemapOutputPass();
                TilemapLayerDefinition[] layers = new[] { floorLayer, solidLayer, defaultLayer };

                writer.EnsureTimelapsCreated(grid, layers);
                outputPass.Execute(snapshot, LogicalChannelName, fallbackBiome, registry, writer, layers, Vector3Int.zero);

                Tilemap floorTilemap = GetLayerTilemap(grid, "Floor");
                Tilemap solidTilemap = GetLayerTilemap(grid, "Solid");

                Assert.That(floorTilemap.GetTile(new Vector3Int(0, 0, 0)), Is.SameAs(forestSurfaceTile));
                Assert.That(solidTilemap.GetTile(new Vector3Int(1, 0, 0)), Is.SameAs(forestCaveTile));
                Assert.That(floorTilemap.GetTile(new Vector3Int(0, 1, 0)), Is.SameAs(desertSurfaceTile));
                Assert.That(solidTilemap.GetTile(new Vector3Int(1, 1, 0)), Is.SameAs(desertCaveTile));
            }
            finally
            {
                DestroyImmediateIfNotNull(forestSurfaceTile);
                DestroyImmediateIfNotNull(forestCaveTile);
                DestroyImmediateIfNotNull(desertSurfaceTile);
                DestroyImmediateIfNotNull(desertCaveTile);
                DestroyImmediateIfNotNull(floorLayer);
                DestroyImmediateIfNotNull(solidLayer);
                DestroyImmediateIfNotNull(defaultLayer);
                DestroyImmediateIfNotNull(forestBiome);
                DestroyImmediateIfNotNull(desertBiome);
                DestroyImmediateIfNotNull(fallbackBiome);
                DestroyImmediateIfNotNull(registry);
                DestroyImmediateIfNotNull(grid != null ? grid.gameObject : null);
            }
        }

        [Test]
        public void ExecuteWritesGeneratedTileForSpriteMappings()
        {
            Grid grid = null;
            TileSemanticRegistry registry = null;
            BiomeAsset biome = null;
            TilemapLayerDefinition floorLayer = null;
            TilemapLayerDefinition defaultLayer = null;
            Texture2D spriteTexture = null;
            Sprite sprite = null;

            try
            {
                grid = CreateGrid();
                registry = CreateRegistry();
                biome = CreateBiome();
                floorLayer = CreateLayerDefinition("Floor", false, "Walkable");
                defaultLayer = CreateLayerDefinition("Default", true);
                spriteTexture = CreateTexture(Color.yellow);
                sprite = CreateSprite(spriteTexture);

                AddRegistryEntry(registry, LogicalTileId.Floor, "Floor", "Walkable");
                biome.TileMappings.Add(new BiomeTileMapping
                {
                    LogicalId = LogicalTileId.Floor,
                    TileType = TileMappingType.Sprite,
                    SpriteAsset = sprite
                });

                WorldSnapshot snapshot = CreateSnapshot(1, 1, new[] { (int)LogicalTileId.Floor });
                TilemapLayerWriter writer = new TilemapLayerWriter();
                TilemapOutputPass outputPass = new TilemapOutputPass();
                TilemapLayerDefinition[] layers = new[] { floorLayer, defaultLayer };

                writer.EnsureTimelapsCreated(grid, layers);
                outputPass.Execute(snapshot, LogicalChannelName, biome, registry, writer, layers, Vector3Int.zero);

                Tilemap floorTilemap = GetLayerTilemap(grid, "Floor");
                TileBase placedTile = floorTilemap.GetTile(Vector3Int.zero);

                Assert.That(placedTile, Is.Not.Null);
                Assert.That(placedTile, Is.TypeOf<Tile>());
                Assert.That(floorTilemap.GetSprite(Vector3Int.zero), Is.SameAs(sprite));
            }
            finally
            {
                DestroyImmediateIfNotNull(sprite);
                DestroyImmediateIfNotNull(spriteTexture);
                DestroyImmediateIfNotNull(floorLayer);
                DestroyImmediateIfNotNull(defaultLayer);
                DestroyImmediateIfNotNull(biome);
                DestroyImmediateIfNotNull(registry);
                DestroyImmediateIfNotNull(grid != null ? grid.gameObject : null);
            }
        }

        private static void AddBiomeMapping(BiomeAsset biome, ushort logicalId, TileBase tile)
        {
            BiomeTileMapping mapping = new BiomeTileMapping();
            mapping.LogicalId = logicalId;
            mapping.Tile = tile;
            biome.TileMappings.Add(mapping);
        }

        private static void AddWeightedBiomeMapping(BiomeAsset biome, ushort logicalId, TileBase firstTile, float firstWeight, TileBase secondTile, float secondWeight)
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
            biome.TileMappings.Add(mapping);
        }

        private static void AddRegistryEntry(TileSemanticRegistry registry, ushort logicalId, string displayName, params string[] tags)
        {
            TileEntry entry = new TileEntry();
            entry.LogicalId = logicalId;
            entry.DisplayName = displayName;

            int index;
            for (index = 0; index < tags.Length; index++)
            {
                string tag = tags[index];
                entry.Tags.Add(tag);
                if (!registry.AllTags.Contains(tag))
                {
                    registry.AllTags.Add(tag);
                }
            }

            registry.Entries.Add(entry);
        }

        private static void AssertNoTileOnAnyLayer(Tilemap[] tilemaps, Vector3Int position)
        {
            int index;
            for (index = 0; index < tilemaps.Length; index++)
            {
                Assert.That(tilemaps[index].GetTile(position), Is.Null);
            }
        }

        private static BiomeAsset CreateBiome()
        {
            return ScriptableObject.CreateInstance<BiomeAsset>();
        }

        private static Grid CreateGrid()
        {
            GameObject gridObject = new GameObject("TestGrid");
            return gridObject.AddComponent<Grid>();
        }

        private static TilemapLayerDefinition CreateLayerDefinition(string layerName, bool isCatchAll, params string[] routingTags)
        {
            TilemapLayerDefinition layerDefinition = ScriptableObject.CreateInstance<TilemapLayerDefinition>();
            layerDefinition.LayerName = layerName;
            layerDefinition.IsCatchAll = isCatchAll;

            int index;
            for (index = 0; index < routingTags.Length; index++)
            {
                layerDefinition.RoutingTags.Add(routingTags[index]);
            }

            return layerDefinition;
        }

        private static TileSemanticRegistry CreateRegistry()
        {
            return ScriptableObject.CreateInstance<TileSemanticRegistry>();
        }

        private static WorldSnapshot CreateSnapshot(int width, int height, int[] logicalIds)
        {
            WorldSnapshot snapshot = new WorldSnapshot();
            snapshot.Width = width;
            snapshot.Height = height;
            snapshot.Seed = 0;
            snapshot.IntChannels = new[]
            {
                new WorldSnapshot.IntChannelSnapshot
                {
                    Name = LogicalChannelName,
                    Data = logicalIds
                }
            };

            return snapshot;
        }

        private static Tile CreateTile(Color colour)
        {
            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.color = colour;
            return tile;
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

        private static void DestroyImmediateIfNotNull(UnityEngine.Object unityObject)
        {
            if (unityObject != null)
            {
                UnityEngine.Object.DestroyImmediate(unityObject);
            }
        }

        private static Tilemap GetLayerTilemap(Grid grid, string layerName)
        {
            Transform child = grid.transform.Find("Tilemap_" + layerName);

            Assert.That(child, Is.Not.Null);

            Tilemap tilemap = child.GetComponent<Tilemap>();
            Assert.That(tilemap, Is.Not.Null);
            return tilemap;
        }
    }
}
