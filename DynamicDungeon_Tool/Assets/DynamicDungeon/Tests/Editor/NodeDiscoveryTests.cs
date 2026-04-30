using System;
using System.Collections.Generic;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using NUnit.Framework;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class NodeDiscoveryTests
    {
        private static readonly string[] CanonicalCategories =
        {
            "Biome",
            "Blend",
            "Filter",
            "Generator",
            "Growth",
            "Math",
            "Noise",
            "Output",
            "Placement",
            "Point Generation",
            "Query"
        };

        [SetUp]
        public void SetUp()
        {
            NodeDiscovery.ClearCache();
        }

        [Test]
        public void DiscoverNodeTypes_ReturnsOnlyPublicTopLevelRuntimeNodesInCanonicalCategories()
        {
            IReadOnlyList<Type> nodeTypes = NodeDiscovery.DiscoverNodeTypes();
            HashSet<string> canonicalCategories = new HashSet<string>(CanonicalCategories, StringComparer.Ordinal);

            Assert.That(nodeTypes.Count, Is.GreaterThan(0));

            int index;
            for (index = 0; index < nodeTypes.Count; index++)
            {
                Type nodeType = nodeTypes[index];

                Assert.That(nodeType.IsPublic, Is.True, nodeType.FullName);
                Assert.That(nodeType.IsNested, Is.False, nodeType.FullName);
                Assert.That(nodeType.IsAbstract, Is.False, nodeType.FullName);
                Assert.That(nodeType.Namespace, Is.EqualTo("DynamicDungeon.Runtime.Nodes"), nodeType.FullName);
                Assert.That(typeof(IGenNode).IsAssignableFrom(nodeType), Is.True, nodeType.FullName);
                Assert.That(Attribute.IsDefined(nodeType, typeof(HideInNodeSearchAttribute), false), Is.False, nodeType.FullName);

                string category = NodeDiscovery.GetNodeCategory(nodeType);
                Assert.That(category, Is.Not.EqualTo("Uncategorised"), nodeType.FullName);
                Assert.That(canonicalCategories.Contains(category), Is.True, nodeType.FullName + " uses unexpected category '" + category + "'.");
            }

            AssertTypeAbsent(nodeTypes, "ImplicitCastNode");
            AssertTypeAbsent(nodeTypes, "FloatInputTestNode");
            AssertTypeAbsent(nodeTypes, "GraphCompilerCopyNode");
            AssertTypeAbsent(nodeTypes, "BlockingNode");
        }

        [Test]
        public void SearchTree_ExcludesInternalAndTestNodesWithoutCreatingUncategorisedGroup()
        {
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            NodeSearchWindow searchWindow = ScriptableObject.CreateInstance<NodeSearchWindow>();

            try
            {
                graph.SchemaVersion = GraphSchemaVersion.Current;
                graphView.LoadGraph(graph);
                searchWindow.Initialise(graphView);

                List<SearchTreeEntry> entries = searchWindow.CreateSearchTree(new SearchWindowContext(Vector2.zero));

                Assert.That(ContainsGroup(entries, "Uncategorised"), Is.False);
                Assert.That(ContainsEntry(entries, "Implicit Cast"), Is.False);
                Assert.That(ContainsEntry(entries, "Float Consumer"), Is.False);
                Assert.That(ContainsEntry(entries, "GraphCompilerCopyNode"), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(searchWindow);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        private static void AssertTypeAbsent(IReadOnlyList<Type> nodeTypes, string typeName)
        {
            int index;
            for (index = 0; index < nodeTypes.Count; index++)
            {
                Type nodeType = nodeTypes[index];
                if (nodeType != null && string.Equals(nodeType.Name, typeName, StringComparison.Ordinal))
                {
                    Assert.Fail("Unexpected node type discovered: " + nodeType.FullName);
                }
            }
        }

        private static bool ContainsEntry(IReadOnlyList<SearchTreeEntry> entries, string label)
        {
            int index;
            for (index = 0; index < entries.Count; index++)
            {
                SearchTreeEntry entry = entries[index];
                if (entry is SearchTreeGroupEntry)
                {
                    continue;
                }

                if (entry.content != null && string.Equals(entry.content.text, label, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsGroup(IReadOnlyList<SearchTreeEntry> entries, string label)
        {
            int index;
            for (index = 0; index < entries.Count; index++)
            {
                SearchTreeGroupEntry entry = entries[index] as SearchTreeGroupEntry;
                if (entry != null && entry.content != null && string.Equals(entry.content.text, label, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
