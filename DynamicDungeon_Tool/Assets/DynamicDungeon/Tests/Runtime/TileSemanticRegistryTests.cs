using System.Reflection;
using DynamicDungeon.Runtime.Semantic;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class TileSemanticRegistryTests
    {
        [Test]
        public void TryGetEntryReturnsExpectedEntryData()
        {
            TileSemanticRegistry registry = ScriptableObject.CreateInstance<TileSemanticRegistry>();

            try
            {
                TileEntry wallEntry = new TileEntry();
                wallEntry.LogicalId = LogicalTileId.Wall;
                wallEntry.DisplayName = "Wall";
                wallEntry.Tags.Add("Solid");
                wallEntry.Tags.Add("Opaque");

                registry.Entries.Add(wallEntry);

                TileEntry resolvedEntry;
                bool found = registry.TryGetEntry(LogicalTileId.Wall, out resolvedEntry);

                Assert.That(found, Is.True);
                Assert.That(resolvedEntry, Is.SameAs(wallEntry));
                Assert.That(resolvedEntry.DisplayName, Is.EqualTo("Wall"));
                CollectionAssert.AreEqual(new[] { "Solid", "Opaque" }, resolvedEntry.Tags);
            }
            finally
            {
                Object.DestroyImmediate(registry);
            }
        }

        [Test]
        public void GetTagIdsReturnsIndicesFromAllTags()
        {
            TileSemanticRegistry registry = ScriptableObject.CreateInstance<TileSemanticRegistry>();

            try
            {
                registry.AllTags.Add("Walkable");
                registry.AllTags.Add("Solid");
                registry.AllTags.Add("Decorative");

                TileEntry floorEntry = new TileEntry();
                floorEntry.LogicalId = LogicalTileId.Floor;
                floorEntry.DisplayName = "Floor";
                floorEntry.Tags.Add("Walkable");
                floorEntry.Tags.Add("Decorative");

                registry.Entries.Add(floorEntry);

                int[] tagIds = registry.GetTagIds(LogicalTileId.Floor);

                CollectionAssert.AreEqual(new[] { 0, 2 }, tagIds);
            }
            finally
            {
                Object.DestroyImmediate(registry);
            }
        }

        [Test]
        public void UnknownLogicalIdReturnsEmptyTagArray()
        {
            TileSemanticRegistry registry = ScriptableObject.CreateInstance<TileSemanticRegistry>();

            try
            {
                registry.AllTags.Add("Solid");

                int[] tagIds = registry.GetTagIds(999);

                Assert.That(tagIds, Is.Not.Null);
                Assert.That(tagIds.Length, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(registry);
            }
        }

        [Test]
        public void GetOrLoadReturnsNullGracefullyWhenAssetIsMissing()
        {
            const string registryAssetPath = "Assets/DynamicDungeon/TileSemanticRegistry.asset";
            const string temporaryFolderPath = "Assets/DynamicDungeon/TestTempRegistryBackup";
            const string temporaryAssetPath = "Assets/DynamicDungeon/TestTempRegistryBackup/TileSemanticRegistry.asset";

            TileSemanticRegistry existingRegistry = AssetDatabase.LoadAssetAtPath<TileSemanticRegistry>(registryAssetPath);
            string moveToTemporaryError = string.Empty;

            if (existingRegistry != null)
            {
                if (!AssetDatabase.IsValidFolder(temporaryFolderPath))
                {
                    string createFolderResult = AssetDatabase.CreateFolder("Assets/DynamicDungeon", "TestTempRegistryBackup");
                    Assert.That(createFolderResult, Is.Not.Empty);
                }

                moveToTemporaryError = AssetDatabase.MoveAsset(registryAssetPath, temporaryAssetPath);
                Assert.That(moveToTemporaryError, Is.Empty);
                AssetDatabase.Refresh();
            }

            try
            {
                ResetRegistryCache();

                bool originalLogEnabled = Debug.unityLogger.logEnabled;

                try
                {
                    Debug.unityLogger.logEnabled = false;

                    TileSemanticRegistry registry = null;

                    Assert.DoesNotThrow(() => registry = TileSemanticRegistry.GetOrLoad());
                    Assert.That(registry, Is.Null);
                }
                finally
                {
                    Debug.unityLogger.logEnabled = originalLogEnabled;
                }
            }
            finally
            {
                if (existingRegistry != null)
                {
                    string restoreError = AssetDatabase.MoveAsset(temporaryAssetPath, registryAssetPath);
                    Assert.That(restoreError, Is.Empty);
                    AssetDatabase.DeleteAsset(temporaryFolderPath);
                    AssetDatabase.Refresh();
                    ResetRegistryCache();
                }
            }
        }

        private static void ResetRegistryCache()
        {
            FieldInfo cachedRegistryField = typeof(TileSemanticRegistry).GetField("_cachedRegistry", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo hasAttemptedLoadField = typeof(TileSemanticRegistry).GetField("_hasAttemptedLoad", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(cachedRegistryField, Is.Not.Null);
            Assert.That(hasAttemptedLoadField, Is.Not.Null);

            cachedRegistryField.SetValue(null, null);
            hasAttemptedLoadField.SetValue(null, false);
        }
    }
}
