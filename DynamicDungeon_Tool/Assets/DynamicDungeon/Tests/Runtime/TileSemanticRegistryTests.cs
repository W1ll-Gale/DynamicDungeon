using System.Reflection;
using DynamicDungeon.Runtime.Semantic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
            ResetRegistryCache();

            LogAssert.Expect(LogType.Warning, "TileSemanticRegistry could not be loaded from 'Assets/DynamicDungeon/TileSemanticRegistry.asset'.");

            TileSemanticRegistry registry = null;

            Assert.DoesNotThrow(() => registry = TileSemanticRegistry.GetOrLoad());
            Assert.That(registry, Is.Null);
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
