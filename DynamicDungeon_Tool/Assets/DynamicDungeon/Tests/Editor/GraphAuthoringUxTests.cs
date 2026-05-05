using System;
using System.Collections.Generic;
using System.Linq;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class GraphAuthoringUxTests
    {
        [Test]
        public void MultiInputEdgesAreRetainedWhenGraphViewRestoresConnections()
        {
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                graph.SchemaVersion = GraphSchemaVersion.Current;
                AddThresholdNode(graph, "mask-a", "Mask A", "MaskA", new Vector2(0.0f, 0.0f));
                AddThresholdNode(graph, "mask-b", "Mask B", "MaskB", new Vector2(0.0f, 160.0f));
                AddMaskStackNode(graph, "stack", "Stack", "Combined", new Vector2(320.0f, 80.0f));
                graph.Connections.Add(new GenConnectionData("mask-a", "MaskA", "stack", "Masks"));
                graph.Connections.Add(new GenConnectionData("mask-b", "MaskB", "stack", "Masks"));

                graphView.LoadGraph(graph);

                Assert.That(CountEdges(graphView), Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void NodeSearchCanFilterByCompatibleChannelType()
        {
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            NodeSearchWindow searchWindow = ScriptableObject.CreateInstance<NodeSearchWindow>();

            try
            {
                graph.SchemaVersion = GraphSchemaVersion.Current;
                graphView.LoadGraph(graph);
                searchWindow.Initialise(graphView);
                searchWindow.SetChannelTypeFilter(ChannelType.BoolMask, PortDirection.Input);

                List<SearchTreeEntry> entries = searchWindow.CreateSearchTree(new SearchWindowContext(Vector2.zero));

                Assert.That(ContainsEntry(entries, "Mask Stack"), Is.True);
                Assert.That(ContainsEntry(entries, "Surface Noise"), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(searchWindow);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void GroupNavigatorListsNodeCountsAndFramesGroups()
        {
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                graph.SchemaVersion = GraphSchemaVersion.Current;
                AddThresholdNode(graph, "mask-a", "Mask A", "MaskA", new Vector2(0.0f, 0.0f));
                GenGroupData group = graph.AddGroup("Terrain Shape", new Rect(-40.0f, -40.0f, 300.0f, 220.0f));
                group.ContainedNodeIds.Add("mask-a");
                graphView.LoadGraph(graph);

                IReadOnlyList<GroupNavigationItem> groups = graphView.GetGroupNavigationItems();

                Assert.That(groups.Count, Is.EqualTo(1));
                Assert.That(groups[0].Title, Is.EqualTo("Terrain Shape"));
                Assert.That(groups[0].NodeCount, Is.EqualTo(1));
                Assert.That(graphView.SelectAndFrameGroup(group.GroupId), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void AutoLayoutSelectionOrdersNodesByDependencies()
        {
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                graph.SchemaVersion = GraphSchemaVersion.Current;
                AddConstantNode(graph, "source", "Source", "Source", new Vector2(600.0f, 0.0f));
                AddThresholdNode(graph, "mask", "Mask", "Mask", new Vector2(0.0f, 240.0f));
                AddMaskStackNode(graph, "stack", "Stack", "Combined", new Vector2(240.0f, -80.0f));
                graph.Connections.Add(new GenConnectionData("source", "Source", "mask", "Input"));
                graph.Connections.Add(new GenConnectionData("mask", "Mask", "stack", "Masks"));
                graphView.LoadGraph(graph);

                foreach (GenNodeView nodeView in graphView.graphElements.OfType<GenNodeView>())
                {
                    graphView.AddToSelection(nodeView);
                }

                graphView.AutoLayoutSelection();

                Assert.That(graph.GetNode("source").Position.x, Is.LessThan(graph.GetNode("mask").Position.x));
                Assert.That(graph.GetNode("mask").Position.x, Is.LessThan(graph.GetNode("stack").Position.x));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void StackRuleValidationReportsMissingAssets()
        {
            BiomeOverrideStackRuleSet biomeRules = new BiomeOverrideStackRuleSet
            {
                Rules = new[]
                {
                    new BiomeOverrideStackRule { Enabled = true, OverrideBiome = "missing-guid", MaskSlot = 1 }
                }
            };
            PlacementSetRuleSet placementRules = new PlacementSetRuleSet
            {
                Rules = new[]
                {
                    new PlacementSetRule { Enabled = true, Prefab = "missing-guid", WeightSlot = 1 }
                }
            };

            Assert.That(StackRuleParameterControls.HasMissingBiomeAssets(biomeRules), Is.True);
            Assert.That(StackRuleParameterControls.HasMissingPlacementPrefabs(placementRules), Is.True);
        }

        [Test]
        public void MaskExpressionNodeAppearsInCompatibleNodeSearch()
        {
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            NodeSearchWindow searchWindow = ScriptableObject.CreateInstance<NodeSearchWindow>();

            try
            {
                graph.SchemaVersion = GraphSchemaVersion.Current;
                graphView.LoadGraph(graph);
                searchWindow.Initialise(graphView);
                searchWindow.SetChannelTypeFilter(ChannelType.BoolMask, PortDirection.Input);

                List<SearchTreeEntry> entries = searchWindow.CreateSearchTree(new SearchWindowContext(Vector2.zero));

                Assert.That(ContainsEntry(entries, "Mask Expression"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(searchWindow);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void MaskExpressionRuleValidationReportsInvalidSlots()
        {
            MaskExpressionRuleSet rules = new MaskExpressionRuleSet
            {
                Rules = new[]
                {
                    new MaskExpressionRule { Enabled = true, MaskSlot = 0, Operation = MaskExpressionOperation.Replace }
                }
            };

            Assert.That(StackRuleParameterControls.HasInvalidMaskExpressionSlots(rules), Is.True);
        }

        private static void AddConstantNode(GenGraph graph, string nodeId, string nodeName, string outputChannelName, Vector2 position)
        {
            GenNodeData node = new GenNodeData(nodeId, typeof(ConstantNode).FullName, nodeName, position);
            node.Ports.Add(new GenPortData(outputChannelName, PortDirection.Output, ChannelType.Float));
            node.Parameters.Add(new SerializedParameter("outputChannelName", outputChannelName));
            node.Parameters.Add(new SerializedParameter("outputType", ChannelType.Float.ToString()));
            graph.Nodes.Add(node);
        }

        private static void AddThresholdNode(GenGraph graph, string nodeId, string nodeName, string outputChannelName, Vector2 position)
        {
            GenNodeData node = new GenNodeData(nodeId, typeof(ThresholdNode).FullName, nodeName, position);
            node.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.Float));
            node.Ports.Add(new GenPortData(outputChannelName, PortDirection.Output, ChannelType.BoolMask));
            node.Parameters.Add(new SerializedParameter("outputChannelName", outputChannelName));
            node.Parameters.Add(new SerializedParameter("threshold", "0.5"));
            graph.Nodes.Add(node);
        }

        private static void AddMaskStackNode(GenGraph graph, string nodeId, string nodeName, string outputChannelName, Vector2 position)
        {
            GenNodeData node = new GenNodeData(nodeId, typeof(MaskStackNode).FullName, nodeName, position);
            node.Ports.Add(new GenPortData("Masks", PortDirection.Input, ChannelType.BoolMask));
            node.Ports.Add(new GenPortData(outputChannelName, PortDirection.Output, ChannelType.BoolMask));
            node.Parameters.Add(new SerializedParameter("outputChannelName", outputChannelName));
            node.Parameters.Add(new SerializedParameter("operation", MaskOperation.OR.ToString()));
            graph.Nodes.Add(node);
        }

        private static int CountEdges(GraphView graphView)
        {
            return graphView.graphElements.OfType<Edge>().Count();
        }

        private static bool ContainsEntry(IReadOnlyList<SearchTreeEntry> entries, string label)
        {
            int index;
            for (index = 0; index < entries.Count; index++)
            {
                SearchTreeEntry entry = entries[index];
                if (!(entry is SearchTreeGroupEntry) &&
                    entry.content != null &&
                    string.Equals(entry.content.text, label, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
