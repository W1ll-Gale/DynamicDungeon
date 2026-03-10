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

        private static void AddBiomeMapping(BiomeAsset biome, ushort logicalId, TileBase tile)
        {
            BiomeTileMapping mapping = new BiomeTileMapping();
            mapping.LogicalId = logicalId;
            mapping.Tile = tile;
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
