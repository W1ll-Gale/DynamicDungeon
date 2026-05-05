using System;
using System.Collections.Generic;
using DynamicDungeon.Editor.Inspectors;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Semantic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class SemanticAssetEditorTests
    {
        [Test]
        public void EnsureBuiltInEntriesCreatesMissingReservedEntries()
        {
            TileSemanticRegistry registry = ScriptableObject.CreateInstance<TileSemanticRegistry>();

            try
            {
                bool changed = TileSemanticRegistryEditor.EnsureBuiltInEntries(registry);

                Assert.That(changed, Is.True);
                Assert.That(registry.TryGetEntry(0, out TileEntry voidEntry), Is.True);
                Assert.That(registry.TryGetEntry(1, out TileEntry floorEntry), Is.True);
                Assert.That(registry.TryGetEntry(2, out TileEntry wallEntry), Is.True);
                Assert.That(registry.TryGetEntry(3, out TileEntry liquidEntry), Is.True);
                Assert.That(registry.TryGetEntry(4, out TileEntry accessEntry), Is.True);
                Assert.That(voidEntry.DisplayName, Is.EqualTo("Void"));
                Assert.That(floorEntry.Tags, Contains.Item("Walkable"));
                Assert.That(wallEntry.Tags, Contains.Item("Solid"));
                Assert.That(liquidEntry.Tags, Contains.Item("Liquid"));
                Assert.That(accessEntry.Tags, Contains.Item("Trigger"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(registry);
            }
        }

        [Test]
        public void RenameTagCascadesToRegistryEntriesAndLayerDefinitions()
        {
            string tempFolder = CreateTempFolder();
            TileSemanticRegistry registry = ScriptableObject.CreateInstance<TileSemanticRegistry>();
            TilemapLayerDefinition layerDefinition = ScriptableObject.CreateInstance<TilemapLayerDefinition>();

            try
            {
                TileEntry entry = new TileEntry();
                entry.LogicalId = 42;
                entry.DisplayName = "Test";
                entry.Tags.Add("Solid");
                registry.Entries.Add(entry);
                registry.AllTags.Add("Solid");

                layerDefinition.RoutingTags.Add("Solid");
                AssetDatabase.CreateAsset(layerDefinition, tempFolder + "/SolidLayer.asset");
                AssetDatabase.SaveAssets();

                bool renamed = TileSemanticRegistryEditor.TryRenameTag(registry, "Solid", "Blocking", new[] { tempFolder }, out string errorMessage);

                Assert.That(renamed, Is.True);
                Assert.That(errorMessage, Is.Empty);
                Assert.That(registry.AllTags[0], Is.EqualTo("Blocking"));
                Assert.That(entry.Tags[0], Is.EqualTo("Blocking"));
                Assert.That(layerDefinition.RoutingTags[0], Is.EqualTo("Blocking"));
            }
            finally
            {
                CleanupTempFolder(tempFolder);
                UnityEngine.Object.DestroyImmediate(registry);
            }
        }

        [Test]
        public void CountLogicalIdReferencesCountsBiomeAndLayerAssets()
        {
            string tempFolder = CreateTempFolder();
            BiomeAsset biome = ScriptableObject.CreateInstance<BiomeAsset>();
            TilemapLayerDefinition layerDefinition = ScriptableObject.CreateInstance<TilemapLayerDefinition>();

            try
            {
                BiomeTileMapping mapping = new BiomeTileMapping();
                mapping.LogicalId = LogicalTileId.Wall;
                biome.TileMappings.Add(mapping);
                layerDefinition.RoutingTags.Add("Solid");

                AssetDatabase.CreateAsset(biome, tempFolder + "/Biome.asset");
                AssetDatabase.CreateAsset(layerDefinition, tempFolder + "/Layer.asset");
                AssetDatabase.SaveAssets();

                TileSemanticRegistryEditor.LogicalIdReferenceInfo referenceInfo = TileSemanticRegistryEditor.CountLogicalIdReferences(LogicalTileId.Wall, new List<string> { "Solid" }, new[] { tempFolder });

                Assert.That(referenceInfo.BiomeAssetCount, Is.EqualTo(1));
                Assert.That(referenceInfo.LayerDefinitionCount, Is.EqualTo(1));
            }
            finally
            {
                CleanupTempFolder(tempFolder);
            }
        }

        [Test]
        public void ComponentTypePickerReturnsValidTypesAndFlagsMissingOnResolve()
        {
            List<string> typeNames = TilemapLayerDefinitionEditor.GetAvailableComponentTypeNames();

            Assert.That(typeNames, Contains.Item("UnityEngine.Tilemaps.TilemapCollider2D"));
            Assert.That(TilemapLayerDefinitionEditor.ResolveComponentType("UnityEngine.Tilemaps.TilemapCollider2D"), Is.Not.Null);
            Assert.That(TilemapLayerDefinitionEditor.ResolveComponentType("Missing.Namespace.Type"), Is.Null);
        }

        [Test]
        public void MatchedEntriesHelperReturnsLogicalIdsForRoutingTags()
        {
            TileSemanticRegistry registry = ScriptableObject.CreateInstance<TileSemanticRegistry>();

            try
            {
                TileEntry floorEntry = new TileEntry();
                floorEntry.LogicalId = LogicalTileId.Floor;
                floorEntry.DisplayName = "Floor";
                floorEntry.Tags.Add("Walkable");

                TileEntry wallEntry = new TileEntry();
                wallEntry.LogicalId = LogicalTileId.Wall;
                wallEntry.DisplayName = "Wall";
                wallEntry.Tags.Add("Solid");

                registry.Entries.Add(floorEntry);
                registry.Entries.Add(wallEntry);

                List<TileEntry> matchedEntries = TilemapLayerDefinitionEditor.GetMatchedEntries(registry, new List<string> { "Walkable" });

                Assert.That(matchedEntries.Count, Is.EqualTo(1));
                Assert.That((LogicalTileId)matchedEntries[0].LogicalId, Is.EqualTo(LogicalTileId.Floor));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(registry);
            }
        }

        private static string CreateTempFolder()
        {
            string parentFolder = "Assets/DynamicDungeon";
            string folderName = "SemanticEditorTests_" + Guid.NewGuid().ToString("N");
            string guid = AssetDatabase.CreateFolder(parentFolder, folderName);
            Assert.That(guid, Is.Not.Empty);
            return AssetDatabase.GUIDToAssetPath(guid);
        }

        private static void CleanupTempFolder(string tempFolder)
        {
            if (!string.IsNullOrWhiteSpace(tempFolder))
            {
                AssetDatabase.DeleteAsset(tempFolder);
                AssetDatabase.Refresh();
            }
        }
    }
}
